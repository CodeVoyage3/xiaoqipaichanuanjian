using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace StoreExpiryInspector.Infrastructure.Backups;

public static class PreImportSnapshotCodes
{
    public const string Verified = "verified";

    public const string Success = Verified;

    public const string SourceMissing = "source_missing";

    public const string SourceUnavailable = "source_unavailable";

    public const string DestinationUnavailable = "destination_unavailable";

    public const string SnapshotFailed = "snapshot_failed";

    public const string VerificationFailed = "verification_failed";
}

public sealed class PreImportSnapshotService
{
    private static readonly string[] RequiredTables =
    {
        "app_state",
        "backups",
        "batches",
        "draft_items",
        "drafts",
        "import_issues",
        "import_workbooks",
        "imports",
        "inspection_item_revisions",
        "inspection_items",
        "inspections",
        "inventory_adjustments",
        "lifecycle_events",
        "products",
        "settings",
        "task_items",
        "tasks",
        "__EFMigrationsHistory"
    };

    private static readonly string[] RequiredMigrationIds =
    {
        "20260826123739_InitialCreate",
        "20260826130822_AddTasksAndDrafts",
        "20260826135612_AddInspectionHistory",
        "20260826142429_AddInventoryAdjustments",
        "20260826152131_AddImportPersistence",
        "20260826155455_AddBackupMetadata",
        "20260826162033_AddSettingsAndAppState",
        "20260826170403_AddLifecycleEvents"
    };

    private const string EfMigrationsLockTable = "__EFMigrationsLock";

    public PreImportSnapshotResult Create(string sourceDatabasePath, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabasePath))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.SourceUnavailable,
                "源数据库路径为空，无法创建导入前快照。");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.DestinationUnavailable,
                "快照目标目录为空，无法创建导入前快照。");
        }

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(sourceDatabasePath);
        }
        catch (Exception)
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.SourceUnavailable,
                "源数据库路径不可用，无法创建导入前快照。");
        }
        string destinationPath;
        try
        {
            destinationPath = Path.GetFullPath(destinationDirectory);
        }
        catch (Exception)
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.DestinationUnavailable,
                "快照目标目录不可用，无法创建导入前快照。");
        }

        if (Directory.Exists(sourcePath))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.SourceUnavailable,
                "源数据库当前不可读取，无法创建导入前快照。");
        }

        if (!File.Exists(sourcePath))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.SourceMissing,
                "源数据库文件不存在，无法创建导入前快照。");
        }

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.DestinationUnavailable,
                "快照目标目录不可用，无法创建导入前快照。");
        }

        try
        {
            Directory.CreateDirectory(destinationPath);
            if (!Directory.Exists(destinationPath))
            {
                return PreImportSnapshotResult.Failure(
                    PreImportSnapshotCodes.DestinationUnavailable,
                    "快照目标目录不可用，无法创建导入前快照。");
            }
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.DestinationUnavailable,
                "快照目标目录不可用，无法创建导入前快照。");
        }

        DatabaseSchema sourceSchema;
        try
        {
            using var source = OpenReadOnly(sourcePath);
            sourceSchema = ReadSchema(source);
        }
        catch (Exception)
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.SourceUnavailable,
                "源数据库当前不可读取，无法创建导入前快照。");
        }

        string temporaryPath;
        string finalPath;
        try
        {
            (temporaryPath, finalPath) = ReserveSnapshotPath(destinationPath, sourcePath);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return PreImportSnapshotResult.Failure(
                PreImportSnapshotCodes.DestinationUnavailable,
                "快照目标目录不可用，无法创建导入前快照。");
        }

        var published = false;
        var succeeded = false;
        try
        {
            try
            {
                using var source = OpenReadOnly(sourcePath);
                using var destination = OpenWritableSnapshot(temporaryPath);
                source.BackupDatabase(destination);
            }
            catch (Exception)
            {
                return PreImportSnapshotResult.Failure(
                    PreImportSnapshotCodes.SnapshotFailed,
                    "SQLite 在线快照创建失败，导入已阻断。");
            }

            DatabaseSchema snapshotSchema;
            try
            {
                using var snapshot = OpenReadOnly(temporaryPath);
                snapshotSchema = ReadSchema(snapshot);
                if (!IsVerified(sourceSchema, snapshotSchema))
                {
                    return PreImportSnapshotResult.Failure(
                        PreImportSnapshotCodes.VerificationFailed,
                        "快照完整性或结构验证失败，导入已阻断。");
                }
            }
            catch (Exception)
            {
                return PreImportSnapshotResult.Failure(
                    PreImportSnapshotCodes.VerificationFailed,
                    "快照完整性或结构验证失败，导入已阻断。");
            }

            string sha256;
            long fileSize;
            try
            {
                var fileInfo = new FileInfo(temporaryPath);
                fileSize = fileInfo.Length;
                if (fileSize <= 0)
                {
                    return PreImportSnapshotResult.Failure(
                        PreImportSnapshotCodes.VerificationFailed,
                        "快照完整性或结构验证失败，导入已阻断。");
                }

                sha256 = ComputeSha256(temporaryPath);
                File.Move(temporaryPath, finalPath);
                published = true;

                var finalInfo = new FileInfo(finalPath);
                if (!finalInfo.Exists || finalInfo.Length != fileSize ||
                    !string.Equals(ComputeSha256(finalPath), sha256, StringComparison.Ordinal))
                {
                    return PreImportSnapshotResult.Failure(
                        PreImportSnapshotCodes.SnapshotFailed,
                        "快照最终落位校验失败，导入已阻断。");
                }
            }
            catch (Exception)
            {
                return PreImportSnapshotResult.Failure(
                    PreImportSnapshotCodes.SnapshotFailed,
                    "快照最终落位失败，导入已阻断。");
            }

            succeeded = true;
            var metadata = new PreImportSnapshotMetadata(
                sourcePath,
                finalPath,
                sha256,
                DateTime.UtcNow,
                fileSize,
                snapshotSchema.MigrationIds);
            return PreImportSnapshotResult.Success(metadata);
        }
        finally
        {
            if (!succeeded)
            {
                TryDelete(temporaryPath);
                if (published)
                {
                    TryDelete(finalPath);
                }
            }
        }
    }

    public PreImportSnapshotResult CreateSnapshot(string sourceDatabasePath, string destinationDirectory) =>
        Create(sourceDatabasePath, destinationDirectory);

    public bool ValidateSnapshot(PreImportSnapshotMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            var snapshotPath = Path.GetFullPath(metadata.SnapshotPath);
            if (!File.Exists(snapshotPath))
            {
                return false;
            }

            var snapshotInfo = new FileInfo(snapshotPath);
            if (snapshotInfo.Length <= 0 || snapshotInfo.Length != metadata.FileSize ||
                !string.Equals(ComputeSha256(snapshotPath), metadata.Sha256, StringComparison.Ordinal))
            {
                return false;
            }

            using var snapshot = OpenReadOnly(snapshotPath);
            return IsSelfVerified(ReadSchema(snapshot), metadata.MigrationIds);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool ValidateSavedSnapshot(string snapshotPath, string sha256)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath) || string.IsNullOrWhiteSpace(sha256))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(snapshotPath);
            var fileSize = new FileInfo(fullPath).Length;
            if (fileSize <= 0)
            {
                return false;
            }

            return ValidateSnapshot(new PreImportSnapshotMetadata(
                fullPath,
                fullPath,
                sha256,
                DateTime.UtcNow,
                fileSize,
                RequiredMigrationIds));
        }
        catch (Exception)
        {
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

    private static SqliteConnection OpenWritableSnapshot(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static (string TemporaryPath, string FinalPath) ReserveSnapshotPath(
        string destinationDirectory,
        string sourcePath)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
            var finalPath = Path.Combine(destinationDirectory, $"pre-import-{stamp}-{Guid.NewGuid():N}.db");
            var temporaryPath = finalPath + ".tmp";
            if (string.Equals(finalPath, sourcePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(temporaryPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using (new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.SequentialScan))
                {
                }

                return (temporaryPath, finalPath);
            }
            catch (IOException) when (File.Exists(temporaryPath))
            {
            }
        }

        throw new IOException("Unable to reserve a unique snapshot path.");
    }

    private static DatabaseSchema ReadSchema(SqliteConnection connection)
    {
        var entries = new List<string>();
        var tableNames = new List<string>();
        var columns = new Dictionary<string, string[]>(StringComparer.Ordinal);

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT type, name, tbl_name, sql FROM sqlite_master " +
                "WHERE name NOT LIKE 'sqlite_%' ORDER BY type COLLATE BINARY, name COLLATE BINARY;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                var tableName = reader.GetString(2);
                var sql = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                if (type == "table")
                {
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        throw new SqliteException("A required table definition is unreadable.", 1);
                    }

                    tableNames.Add(name);
                }

                entries.Add($"{type}\u001f{name}\u001f{tableName}\u001f{sql}");
            }
        }

        tableNames.Sort(StringComparer.Ordinal);
        foreach (var tableName in tableNames)
        {
            var tableColumns = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var defaultValue = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                tableColumns.Add(string.Join(
                    '\u001f',
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3).ToString(CultureInfo.InvariantCulture),
                    defaultValue,
                    reader.GetInt32(5).ToString(CultureInfo.InvariantCulture)));
            }

            if (tableColumns.Count == 0)
            {
                throw new SqliteException("A required table has no readable columns.", 1);
            }

            columns.Add(tableName, tableColumns.ToArray());
        }

        var migrationIds = Array.Empty<string>();
        if (tableNames.Contains("__EFMigrationsHistory", StringComparer.Ordinal))
        {
            var migrations = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT MigrationId FROM \"__EFMigrationsHistory\" " +
                "ORDER BY MigrationId COLLATE BINARY;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                migrations.Add(reader.GetString(0));
            }

            migrationIds = migrations.ToArray();
        }

        return new DatabaseSchema(
            tableNames.ToArray(),
            entries.ToArray(),
            columns,
            migrationIds,
            ReadQuickCheck(connection),
            ReadForeignKeyCheckIsEmpty(connection),
            ReadPageCount(connection));
    }

    private static bool IsVerified(DatabaseSchema source, DatabaseSchema snapshot)
    {
        return snapshot.QuickCheckOk &&
               snapshot.ForeignKeyCheckEmpty &&
               snapshot.PageCount > 0 &&
               source.TableNames.SequenceEqual(snapshot.TableNames, StringComparer.Ordinal) &&
               source.SchemaEntries.SequenceEqual(snapshot.SchemaEntries, StringComparer.Ordinal) &&
               AreColumnsEqual(source.Columns, snapshot.Columns) &&
               source.MigrationIds.SequenceEqual(snapshot.MigrationIds, StringComparer.Ordinal) &&
               RequiredTables.All(snapshot.TableNames.Contains) &&
               snapshot.TableNames.All(static name =>
                   name == EfMigrationsLockTable || RequiredTables.Contains(name, StringComparer.Ordinal)) &&
               RequiredMigrationIds.SequenceEqual(snapshot.MigrationIds, StringComparer.Ordinal);
    }

    private static bool IsSelfVerified(DatabaseSchema snapshot, IReadOnlyList<string> metadataMigrationIds)
    {
        return snapshot.QuickCheckOk &&
               snapshot.ForeignKeyCheckEmpty &&
               snapshot.PageCount > 0 &&
               RequiredTables.All(snapshot.TableNames.Contains) &&
               snapshot.TableNames.All(static name =>
                   name == EfMigrationsLockTable || RequiredTables.Contains(name, StringComparer.Ordinal)) &&
               RequiredMigrationIds.SequenceEqual(snapshot.MigrationIds, StringComparer.Ordinal) &&
               metadataMigrationIds.SequenceEqual(snapshot.MigrationIds, StringComparer.Ordinal);
    }

    private static bool AreColumnsEqual(
        IReadOnlyDictionary<string, string[]> left,
        IReadOnlyDictionary<string, string[]> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var rightColumns) ||
                !pair.Value.SequenceEqual(rightColumns, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReadQuickCheck(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        using var reader = command.ExecuteReader();
        var rowCount = 0;
        var isOk = false;
        while (reader.Read())
        {
            rowCount++;
            isOk = reader.GetString(0) == "ok";
        }

        return rowCount == 1 && isOk;
    }

    private static bool ReadForeignKeyCheckIsEmpty(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        return !reader.Read();
    }

    private static long ReadPageCount(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_count;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
        }
    }

    private static bool IsFileSystemFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or SecurityException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private sealed class DatabaseSchema
    {
        public DatabaseSchema(
            string[] tableNames,
            string[] schemaEntries,
            Dictionary<string, string[]> columns,
            string[] migrationIds,
            bool quickCheckOk,
            bool foreignKeyCheckEmpty,
            long pageCount)
        {
            TableNames = tableNames;
            SchemaEntries = schemaEntries;
            Columns = columns;
            MigrationIds = migrationIds;
            QuickCheckOk = quickCheckOk;
            ForeignKeyCheckEmpty = foreignKeyCheckEmpty;
            PageCount = pageCount;
        }

        public string[] TableNames { get; }

        public string[] SchemaEntries { get; }

        public Dictionary<string, string[]> Columns { get; }

        public string[] MigrationIds { get; }

        public bool QuickCheckOk { get; }

        public bool ForeignKeyCheckEmpty { get; }

        public long PageCount { get; }
    }
}

public sealed class PreImportSnapshotResult
{
    private PreImportSnapshotResult(
        bool canProceed,
        string code,
        string safeSummary,
        PreImportSnapshotMetadata? metadata)
    {
        CanProceed = canProceed;
        Code = code;
        SafeSummary = safeSummary;
        Metadata = metadata;
    }

    public bool CanProceed { get; }

    public string Code { get; }

    public string SafeSummary { get; }

    public string SafeUserMessage => SafeSummary;

    public PreImportSnapshotMetadata? Metadata { get; }

    internal static PreImportSnapshotResult Success(PreImportSnapshotMetadata metadata) => new(
        true,
        PreImportSnapshotCodes.Verified,
        "导入前 SQLite 快照已验证。",
        metadata);

    internal static PreImportSnapshotResult Failure(string code, string safeSummary) => new(
        false,
        code,
        safeSummary,
        null);
}

public sealed class PreImportSnapshotMetadata
{
    internal PreImportSnapshotMetadata(
        string sourceDatabasePath,
        string snapshotPath,
        string sha256,
        DateTime createdAtUtc,
        long fileSize,
        IEnumerable<string> migrationIds)
    {
        SourceDatabasePath = sourceDatabasePath;
        SnapshotPath = snapshotPath;
        Sha256 = sha256;
        CreatedAtUtc = createdAtUtc;
        FileSize = fileSize;
        MigrationIds = new ReadOnlyCollection<string>(migrationIds.ToArray());
    }

    public string BackupType => "pre_import";

    public string SourceDatabasePath { get; }

    public string SnapshotPath { get; }

    public string FilePath => SnapshotPath;

    public string Sha256 { get; }

    public DateTime CreatedAtUtc { get; }

    public string VerificationStatus => "verified";

    public long FileSize { get; }

    public IReadOnlyList<string> MigrationIds { get; }
}
