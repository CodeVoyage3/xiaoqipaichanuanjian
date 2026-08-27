using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;

namespace StoreExpiryInspector.Application.Imports;

public static class ImportUndoEligibilityCodes
{
    public const string NoCandidate = "no_candidate";

    public const string SnapshotMissing = "snapshot_missing";

    public const string SnapshotAssociationInvalid = "snapshot_association_invalid";

    public const string SnapshotInvalid = "snapshot_invalid";

    public const string SubsequentBusinessChanges = "subsequent_business_changes";

    public const string BusinessStateUnverifiable = "business_state_unverifiable";

    public const string Eligible = "eligible";
}

public sealed class ImportUndoEligibilityResult
{
    private ImportUndoEligibilityResult(
        bool canUndo,
        string code,
        string safeSummary,
        long? candidateImportId,
        DateTime? confirmedAtUtc,
        string? snapshotPath,
        string? snapshotSha256,
        long? backupRecordId,
        IReadOnlyList<string> blockingTables)
    {
        CanUndo = canUndo;
        Code = code;
        SafeSummary = safeSummary;
        CandidateImportId = candidateImportId;
        ConfirmedAtUtc = confirmedAtUtc;
        SnapshotPath = snapshotPath;
        SnapshotSha256 = snapshotSha256;
        BackupRecordId = backupRecordId;
        BlockingTables = blockingTables;
    }

    public bool CanUndo { get; }

    public string Code { get; }

    public string SafeSummary { get; }

    public string SafeUserMessage => SafeSummary;

    public long? CandidateImportId { get; }

    public DateTime? ConfirmedAtUtc { get; }

    public string? SnapshotPath { get; }

    public string? SnapshotSha256 { get; }

    public long? BackupRecordId { get; }

    public IReadOnlyList<string> BlockingTables { get; }

    public IReadOnlyList<string> ChangedTables => BlockingTables;

    internal static ImportUndoEligibilityResult NoCandidate() => new(
        false,
        ImportUndoEligibilityCodes.NoCandidate,
        "没有符合条件的最近一次成功导入。",
        null,
        null,
        null,
        null,
        null,
        Array.Empty<string>());

    internal static ImportUndoEligibilityResult Failure(
        string code,
        string safeSummary,
        ImportRecord? candidate = null,
        string? snapshotPath = null,
        string? snapshotSha256 = null,
        long? backupRecordId = null,
        IEnumerable<string>? blockingTables = null) => new(
        false,
        code,
        safeSummary,
        candidate?.Id,
        candidate?.ConfirmedAtUtc,
        snapshotPath,
        snapshotSha256,
        backupRecordId,
        Array.AsReadOnly((blockingTables ?? Array.Empty<string>()).ToArray()));

    internal static ImportUndoEligibilityResult EligibleResult(
        ImportRecord candidate,
        string snapshotPath,
        string snapshotSha256,
        long backupRecordId) => new(
        true,
        ImportUndoEligibilityCodes.Eligible,
        "最近一次成功导入具备撤销资格。",
        candidate.Id,
        candidate.ConfirmedAtUtc,
        snapshotPath,
        snapshotSha256,
        backupRecordId,
        Array.Empty<string>());
}

public sealed class ImportUndoEligibilityService
{
    private static readonly string[] FixedBusinessTables =
    {
        "tasks",
        "task_items",
        "inspections",
        "inspection_items",
        "inspection_item_revisions",
        "inventory_adjustments",
        "lifecycle_events",
        "drafts",
        "draft_items"
    };

    private readonly PreImportSnapshotService _snapshotService;

    public ImportUndoEligibilityService(PreImportSnapshotService? snapshotService = null)
    {
        _snapshotService = snapshotService ?? new PreImportSnapshotService();
    }

    public ImportUndoEligibilityResult Check(StoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ImportRecord? candidate;
        try
        {
            candidate = context.Imports
                .AsNoTracking()
                .Where(import => import.Status == ImportStatuses.Succeeded &&
                                 !import.IsUndone &&
                                 import.ConfirmedAtUtc.HasValue)
                .OrderByDescending(import => import.ConfirmedAtUtc)
                .ThenByDescending(import => import.Id)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.BusinessStateUnverifiable,
                "当前导入记录无法可靠读取，暂不可撤销。");
        }

        if (candidate is null)
        {
            return ImportUndoEligibilityResult.NoCandidate();
        }

        var confirmedAtUtc = candidate.ConfirmedAtUtc!.Value;
        var rawSnapshotPath = candidate.PreImportSnapshotPath;
        if (string.IsNullOrWhiteSpace(rawSnapshotPath))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SnapshotMissing,
                "最近一次导入没有可用的原始导入前快照。",
                candidate);
        }

        if (!TryNormalizePath(rawSnapshotPath, out var snapshotPath))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SnapshotAssociationInvalid,
                "最近一次导入的快照路径关联无效。",
                candidate);
        }

        if (!File.Exists(snapshotPath))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SnapshotMissing,
                "最近一次导入的原始导入前快照不存在。",
                candidate,
                snapshotPath);
        }

        BackupRecord[] backups;
        try
        {
            backups = context.BackupRecords.AsNoTracking().ToArray();
        }
        catch (Exception)
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.BusinessStateUnverifiable,
                "备份记录无法可靠读取，暂不可撤销。",
                candidate,
                snapshotPath);
        }

        var relatedBackups = backups
            .Where(backup => TryNormalizePath(backup.FilePath, out var path) &&
                             PathsEqual(path, snapshotPath))
            .ToArray();
        if (relatedBackups.Length != 1)
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SnapshotAssociationInvalid,
                "最近一次导入没有唯一有效的导入前快照备份记录。",
                candidate,
                snapshotPath);
        }

        var backup = relatedBackups[0];
        if (!string.Equals(backup.BackupType, "pre_import", StringComparison.Ordinal) ||
            !string.Equals(backup.VerificationStatus, "verified", StringComparison.Ordinal) ||
            backup.CreatedAtUtc > confirmedAtUtc ||
            !IsLowerSha256(backup.Sha256))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SnapshotAssociationInvalid,
                "最近一次导入的快照备份关联信息无效。",
                candidate,
                snapshotPath,
                backup.Sha256,
                backup.Id);
        }

        if (!TryGetSqliteDatabasePath(context, out var databasePath))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.BusinessStateUnverifiable,
                "当前业务数据库无法可靠读取，暂不可撤销。",
                candidate,
                snapshotPath,
                backup.Sha256,
                backup.Id);
        }

        if (!_snapshotService.ValidateSavedSnapshot(snapshotPath, backup.Sha256, databasePath))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SnapshotInvalid,
                "最近一次导入的原始导入前快照未通过独立验证。",
                candidate,
                snapshotPath,
                backup.Sha256,
                backup.Id);
        }

        var changedTables = new List<string>();
        try
        {
            if (context.Products.AsNoTracking().Any(product => product.UpdatedAtUtc > confirmedAtUtc))
            {
                changedTables.Add("products");
            }

            if (context.Batches.AsNoTracking().Any(batch => batch.UpdatedAtUtc > confirmedAtUtc))
            {
                changedTables.Add("batches");
            }
        }
        catch (Exception)
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.BusinessStateUnverifiable,
                "商品或批次更新时间无法可靠判断，暂不可撤销。",
                candidate,
                snapshotPath,
                backup.Sha256,
                backup.Id);
        }

        if (!TryCompareFixedBusinessTables(databasePath, snapshotPath, out var fixedChanges))
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.BusinessStateUnverifiable,
                "导入后正式业务记录无法可靠比较，暂不可撤销。",
                candidate,
                snapshotPath,
                backup.Sha256,
                backup.Id);
        }

        changedTables.AddRange(fixedChanges);
        if (changedTables.Count > 0)
        {
            return ImportUndoEligibilityResult.Failure(
                ImportUndoEligibilityCodes.SubsequentBusinessChanges,
                "最近一次导入后已有正式业务变化，不能安全撤销。",
                candidate,
                snapshotPath,
                backup.Sha256,
                backup.Id,
                changedTables);
        }

        return ImportUndoEligibilityResult.EligibleResult(
            candidate,
            snapshotPath,
            backup.Sha256,
            backup.Id);
    }

    public ImportUndoEligibilityResult Evaluate(StoreDbContext context) => Check(context);

    private static bool TryGetSqliteDatabasePath(StoreDbContext context, out string path)
    {
        path = string.Empty;
        try
        {
            if (context.Database.GetDbConnection() is not SqliteConnection sqliteConnection)
            {
                return false;
            }

            var options = new SqliteConnectionStringBuilder(sqliteConnection.ConnectionString);
            if (options.Mode == SqliteOpenMode.Memory ||
                string.IsNullOrWhiteSpace(options.DataSource) ||
                string.Equals(options.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
                options.DataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = Path.GetFullPath(options.DataSource);
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception)
        {
            path = string.Empty;
            return false;
        }
    }

    private static bool TryNormalizePath(string value, out string path)
    {
        path = string.Empty;
        try
        {
            path = Path.GetFullPath(value);
            return !string.IsNullOrWhiteSpace(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsLowerSha256(string value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TryCompareFixedBusinessTables(
        string databasePath,
        string snapshotPath,
        out IReadOnlyList<string> changedTables)
    {
        var changes = new List<string>();
        try
        {
            using var current = OpenReadOnly(databasePath);
            using var snapshot = OpenReadOnly(snapshotPath);
            foreach (var tableName in FixedBusinessTables)
            {
                if (!TryReadTable(current, tableName, out var currentTable) ||
                    !TryReadTable(snapshot, tableName, out var snapshotTable))
                {
                    changedTables = Array.Empty<string>();
                    return false;
                }

                if (!SameTableShape(currentTable, snapshotTable))
                {
                    changedTables = Array.Empty<string>();
                    return false;
                }

                if (!SameRows(currentTable.Rows, snapshotTable.Rows))
                {
                    changes.Add(tableName);
                }
            }

            changedTables = changes;
            return true;
        }
        catch (Exception)
        {
            changedTables = Array.Empty<string>();
            return false;
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static bool TryReadTable(SqliteConnection connection, string tableName, out TableData table)
    {
        table = null!;
        var columns = new List<TableColumn>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new TableColumn(
                    reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString(),
                    reader.GetInt32(5)));
            }
        }

        if (columns.Count == 0 || columns.All(column => !string.Equals(column.Name, "id", StringComparison.Ordinal)))
        {
            return false;
        }

        var rows = new List<object?[]>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)))} " +
                                  $"FROM {QuoteIdentifier(tableName)} ORDER BY \"id\";";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object?[columns.Count];
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = reader.GetValue(index);
                }

                rows.Add(values);
            }
        }

        table = new TableData(columns, rows);
        return true;
    }

    private static bool SameTableShape(TableData left, TableData right)
    {
        if (left.Columns.Count != right.Columns.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Columns.Count; index++)
        {
            if (left.Columns[index] != right.Columns[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameRows(IReadOnlyList<object?[]> left, IReadOnlyList<object?[]> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var rowIndex = 0; rowIndex < left.Count; rowIndex++)
        {
            var leftRow = left[rowIndex];
            var rightRow = right[rowIndex];
            if (leftRow.Length != rightRow.Length)
            {
                return false;
            }

            for (var columnIndex = 0; columnIndex < leftRow.Length; columnIndex++)
            {
                if (!SameValue(leftRow[columnIndex], rightRow[columnIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SameValue(object? left, object? right)
    {
        if (left is byte[] leftBytes && right is byte[] rightBytes)
        {
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        }

        return Equals(left, right);
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed class TableData
    {
        public TableData(IReadOnlyList<TableColumn> columns, IReadOnlyList<object?[]> rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public IReadOnlyList<TableColumn> Columns { get; }

        public IReadOnlyList<object?[]> Rows { get; }
    }

    private sealed record TableColumn(
        string Name,
        string Type,
        int NotNull,
        string? DefaultValue,
        int PrimaryKey);
}
