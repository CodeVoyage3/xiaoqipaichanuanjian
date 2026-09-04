using System.IO;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Imports;

public static class ConfirmedImportCodes
{
    public const string Succeeded = "succeeded";

    public const string InvalidContract = "invalid_contract";

    public const string FileChanged = "file_changed";

    public const string FileMissing = "file_missing";

    public const string FileUnavailable = "file_unavailable";

    public const string SnapshotFailed = "snapshot_failed";

    public const string StalePlan = "stale_plan";

    public const string TransactionFailed = "transaction_failed";
}

public sealed class ConfirmedImportResult
{
    private ConfirmedImportResult(
        bool succeeded,
        string code,
        string safeSummary,
        long? importId,
        string? snapshotPath,
        PreImportSnapshotMetadata? snapshotMetadata)
    {
        Succeeded = succeeded;
        Code = code;
        SafeSummary = safeSummary;
        ImportId = importId;
        SnapshotPath = snapshotPath;
        SnapshotMetadata = snapshotMetadata;
    }

    public bool Succeeded { get; }

    public bool Success => Succeeded;

    public string Code { get; }

    public string SafeSummary { get; }

    public string SafeUserMessage => SafeSummary;

    public long? ImportId { get; }

    public long? ImportRecordId => ImportId;

    public string? SnapshotPath { get; }

    public PreImportSnapshotMetadata? SnapshotMetadata { get; }

    internal static ConfirmedImportResult Succeed(long importId, PreImportSnapshotMetadata metadata) => new(
        true,
        ConfirmedImportCodes.Succeeded,
        "确认导入已成功提交。",
        importId,
        metadata.SnapshotPath,
        metadata);

    internal static ConfirmedImportResult Fail(
        string code,
        string summary,
        string? snapshotPath = null,
        PreImportSnapshotMetadata? snapshotMetadata = null) => new(
        false,
        code,
        summary,
        null,
        snapshotPath,
        snapshotMetadata);
}

public sealed class ConfirmedImportExecutor
{
    private readonly PreImportSnapshotService _snapshotService;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string, TimeSpan>? _measure;

    public ConfirmedImportExecutor(
        PreImportSnapshotService? snapshotService = null,
        Func<DateTime>? utcNow = null,
        Action<string, TimeSpan>? measure = null)
    {
        _snapshotService = snapshotService ?? new PreImportSnapshotService();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _measure = measure;
    }

    public ConfirmedImportExecutor(Func<DateTime> utcNow)
        : this(null, utcNow)
    {
    }

    public ConfirmedImportResult Execute(
        ImportConfirmationContract? contract,
        StoreDbContext? context,
        string? snapshotDirectory,
        DateTime parsedAtUtc)
    {
        if (contract is null || context is null || string.IsNullOrWhiteSpace(snapshotDirectory))
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.InvalidContract,
                "确认导入契约或快照目录无效。");
        }

        if (context.ChangeTracker.Entries().Any())
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.InvalidContract,
                "执行数据库上下文必须没有已跟踪实体，请重新打开上下文后重试。");
        }

        if (!TryValidateContract(contract, parsedAtUtc, out var frozenBytes, out var confirmedAtUtc))
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.InvalidContract,
                "确认导入契约无效，请重新解析并预览。");
        }

        try
        {
            var (currentBytes, currentSha256) = ImportConfirmationGuard.ReadFileBytes(contract.SourceFilePath);
            if (!string.Equals(currentSha256, contract.SourceFileSha256, StringComparison.Ordinal) ||
                !currentBytes.AsSpan().SequenceEqual(frozenBytes))
            {
                return ConfirmedImportResult.Fail(
                    ConfirmedImportCodes.FileChanged,
                    "确认前源文件内容已变化，请重新解析并预览。");
            }
        }
        catch (FileNotFoundException)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.FileMissing,
                "确认前源文件已不存在，请重新解析并预览。");
        }
        catch (DirectoryNotFoundException)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.FileUnavailable,
                "确认前无法读取源文件，请重新解析并预览。");
        }
        catch (UnauthorizedAccessException)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.FileUnavailable,
                "确认前无法读取源文件，请重新解析并预览。");
        }
        catch (SecurityException)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.FileUnavailable,
                "确认前无法读取源文件，请重新解析并预览。");
        }
        catch (IOException)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.FileUnavailable,
                "确认前无法读取源文件，请重新解析并预览。");
        }

        if (!TryGetSqliteDatabasePath(context, out var databasePath))
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.InvalidContract,
                "当前数据库不是可写的 SQLite 文件数据库。");
        }

        PreImportSnapshotResult snapshot;
        try
        {
            snapshot = Measure("snapshot", () => _snapshotService.Create(databasePath, snapshotDirectory));
        }
        catch (Exception)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.SnapshotFailed,
                "导入前 SQLite 快照失败，导入已阻断。");
        }

        if (!snapshot.CanProceed || snapshot.Metadata is null ||
            !IsValidSnapshotMetadata(snapshot.Metadata, databasePath))
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.SnapshotFailed,
                "导入前 SQLite 快照未通过验证，导入已阻断。",
                snapshot.Metadata?.SnapshotPath);
        }

        var metadata = snapshot.Metadata;
        var plan = contract.Plan;
        var pendingIssues = BuildIssues(plan.Preview);
        var ownsTransaction = context.Database.CurrentTransaction is null;
        var transaction = (IDbContextTransaction?)null;
        var committed = false;
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            if (!TryValidateCurrentPlan(context, plan, out var currentPlan))
            {
                transaction?.Rollback();
                context.ChangeTracker.Clear();
                return ConfirmedImportResult.Fail(
                    ConfirmedImportCodes.StalePlan,
                    "确认前数据库内容已变化，请重新解析并预览。",
                    metadata.SnapshotPath,
                    metadata);
            }

            var import = new ImportRecord
            {
                SourceFileName = contract.SourceFileName,
                SourceFileSha256 = contract.SourceFileSha256,
                ParsedAtUtc = parsedAtUtc,
                ConfirmedAtUtc = confirmedAtUtc,
                Status = ImportStatuses.Succeeded,
                ProductCount = plan.Preview.InvolvedProductCount,
                BatchCount = plan.Preview.NormalBatchKeyCount,
                NewProductCount = plan.NewProducts.Count,
                NewBatchCount = plan.NewBatches.Count,
                UpdatedBatchCount = plan.UpdatedBatches.Count,
                IssueCount = pendingIssues.Count,
                UnsupportedCategoryCount = plan.Preview.SkippedRowCount,
                NewTaskProductCount = contract.NewTaskProductCountSchemaPlaceholder,
                PreImportSnapshotPath = metadata.SnapshotPath,
                IsUndone = false,
                UndoneAtUtc = null
            };
            context.Imports.Add(import);
            Measure("import_record_write", context.SaveChanges);

            var productsByCode = ApplyProducts(context, plan, currentPlan, import.Id, confirmedAtUtc);
            Measure("product_write", context.SaveChanges);

            ApplyBatches(context, plan, currentPlan, productsByCode, import.Id, confirmedAtUtc);
            Measure("batch_write", context.SaveChanges);

            context.ImportIssues.AddRange(pendingIssues.Select(issue => new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = issue.RowNumber,
                IssueType = issue.IssueType,
                FieldName = issue.FieldName,
                SafeSummary = issue.SafeSummary
            }));
            Measure("issue_write", context.SaveChanges);

            var workbookContent = frozenBytes.ToArray();
            if (workbookContent.Length == 0 ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(workbookContent)).ToLowerInvariant(),
                    contract.SourceFileSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The frozen workbook bytes do not match the confirmation contract.");
            }

            context.ImportWorkbooks.Add(new ImportWorkbook
            {
                ImportId = import.Id,
                OriginalFileName = contract.SourceFileName,
                Content = workbookContent,
                Sha256 = contract.SourceFileSha256,
                SavedAtUtc = confirmedAtUtc
            });
            context.BackupRecords.Add(new BackupRecord
            {
                BackupType = metadata.BackupType,
                FilePath = metadata.SnapshotPath,
                Sha256 = metadata.Sha256,
                CreatedAtUtc = metadata.CreatedAtUtc,
                VerificationStatus = metadata.VerificationStatus
            });
            Measure("workbook_backup_write", context.SaveChanges);

            RetainRecentImportWorkbooks(context);
            Measure("workbook_retention", context.SaveChanges);

            if (context.ImportIssues.Count(issue => issue.ImportId == import.Id) != pendingIssues.Count)
            {
                throw new InvalidDataException("The persisted import issue count does not match the import record.");
            }

            context.ChangeTracker.Clear();
            transaction?.Commit();
            committed = true;
            return ConfirmedImportResult.Succeed(import.Id, metadata);
        }
        catch (Exception)
        {
            if (ownsTransaction && !committed)
            {
                try
                {
                    transaction?.Rollback();
                }
                catch (Exception)
                {
                }
            }

            context.ChangeTracker.Clear();
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.TransactionFailed,
                "确认导入事务失败，数据库写入已回滚。",
                metadata.SnapshotPath,
                metadata);
        }
        finally
        {
            try
            {
                transaction?.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    public ConfirmedImportResult Execute(
        StoreDbContext context,
        ImportConfirmationContract contract,
        string snapshotDirectory,
        DateTime parsedAtUtc) => Execute(contract, context, snapshotDirectory, parsedAtUtc);

    private static void RetainRecentImportWorkbooks(StoreDbContext context)
    {
        var staleWorkbooks = context.ImportWorkbooks
            .Join(
                context.Imports,
                workbook => workbook.ImportId,
                import => import.Id,
                (workbook, import) => new { Workbook = workbook, Import = import })
            .Where(item => item.Import.Status == ImportStatuses.Succeeded)
            .OrderByDescending(item => item.Import.ConfirmedAtUtc)
            .ThenByDescending(item => item.Import.Id)
            .Skip(2)
            .Select(item => item.Workbook)
            .ToArray();

        context.ImportWorkbooks.RemoveRange(staleWorkbooks);
    }

    private T Measure<T>(string stage, Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        try { return action(); }
        finally { watch.Stop(); _measure?.Invoke(stage, watch.Elapsed); }
    }

    private void Measure(string stage, Action action) => Measure(stage, () => { action(); return 0; });

    private bool TryValidateContract(
        ImportConfirmationContract contract,
        DateTime parsedAtUtc,
        out byte[] frozenBytes,
        out DateTime confirmedAtUtc)
    {
        frozenBytes = Array.Empty<byte>();
        confirmedAtUtc = default;
        if (parsedAtUtc.Kind != DateTimeKind.Utc ||
            !string.Equals(contract.TargetImportStatus, ImportStatuses.Succeeded, StringComparison.Ordinal) ||
            contract.NewTaskProductCountSchemaPlaceholder != 0 ||
            !contract.Plan.HasChanges ||
            !contract.Plan.Preview.HasChanges ||
            !Path.IsPathFullyQualified(contract.SourceFilePath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(contract.SourceFilePath);
        }
        catch (Exception)
        {
            return false;
        }

        if (!string.Equals(fullPath, contract.SourceFilePath, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(fullPath), contract.SourceFileName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(contract.SourceFileName) ||
            !IsLowerSha256(contract.SourceFileSha256))
        {
            return false;
        }

        frozenBytes = contract.WorkbookBytes.ToArray();
        if (frozenBytes.Length == 0 ||
            !string.Equals(
                Convert.ToHexString(SHA256.HashData(frozenBytes)).ToLowerInvariant(),
                contract.SourceFileSha256,
                StringComparison.Ordinal) ||
            !ValidatePlanShape(contract.Plan))
        {
            return false;
        }

        try
        {
            confirmedAtUtc = _utcNow();
        }
        catch (Exception)
        {
            return false;
        }

        return confirmedAtUtc.Kind == DateTimeKind.Utc && confirmedAtUtc >= parsedAtUtc;
    }

    private static bool IsLowerSha256(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TryGetSqliteDatabasePath(StoreDbContext context, out string path)
    {
        path = string.Empty;
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection is not SqliteConnection sqliteConnection)
            {
                return false;
            }

            var connectionOptions = new SqliteConnectionStringBuilder(sqliteConnection.ConnectionString);
            if (connectionOptions.Mode == SqliteOpenMode.Memory ||
                string.IsNullOrWhiteSpace(connectionOptions.DataSource) ||
                string.Equals(connectionOptions.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
                connectionOptions.DataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = Path.GetFullPath(connectionOptions.DataSource);
            return !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path);
        }
        catch (Exception)
        {
            path = string.Empty;
            return false;
        }
    }

    private bool IsValidSnapshotMetadata(PreImportSnapshotMetadata metadata, string databasePath)
    {
        try
        {
            return string.Equals(
                    Path.GetFullPath(metadata.SourceDatabasePath),
                    Path.GetFullPath(databasePath),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(metadata.BackupType, "pre_import", StringComparison.Ordinal) &&
                string.Equals(metadata.VerificationStatus, "verified", StringComparison.Ordinal) &&
                IsLowerSha256(metadata.Sha256) &&
                metadata.CreatedAtUtc.Kind == DateTimeKind.Utc &&
                metadata.FileSize > 0 &&
                Path.IsPathFullyQualified(metadata.SnapshotPath) &&
                _snapshotService.ValidateSnapshot(metadata);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ValidatePlanShape(ImportPlan plan)
    {
        if (plan.Preview.InvolvedProductCount < 0 ||
            plan.Preview.NormalBatchKeyCount < 0 ||
            plan.Preview.SkippedRowCount < 0 ||
            plan.Preview.RowIssueCount < 0 ||
            plan.Preview.BatchConflictCount < 0 ||
            plan.Preview.StockConflictCount < 0 ||
            plan.Preview.PlanningIssueCount < 0 ||
            plan.NewProducts.Any(static item => !ValidProductCode(item.ProductCode)) ||
            plan.UpdatedProducts.Any(static item => !ValidProductCode(item.ProductCode)) ||
            plan.UnchangedProducts.Any(static item => !ValidProductCode(item.ProductCode)) ||
            plan.NewBatches.Any(static item => !ValidBatchKey(item.BatchKey) || item.ExcelRowNumber <= 0) ||
            plan.UpdatedBatches.Any(static item => !ValidBatchKey(item.BatchKey) || item.ExcelRowNumber <= 0) ||
            plan.UnchangedBatches.Any(static item => !ValidBatchKey(item.BatchKey) || item.ExcelRowNumber <= 0))
        {
            return false;
        }

        var productActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in plan.NewProducts)
        {
            if (!productActions.Add(product.ProductCode) ||
                !ValidScope(product) ||
                product.ExcelStockQty < 0 ||
                product.EffectiveStockQty < 0 ||
                product.EffectiveStockQty != product.ExcelStockQty ||
                product.EffectiveStockSource != "excel" ||
                !ValidRows(product.SourceRowNumbers))
            {
                return false;
            }
        }

        foreach (var product in plan.UpdatedProducts)
        {
            if (!productActions.Add(product.ProductCode) ||
                product.FieldChanges.Count == 0 ||
                !ValidRows(product.SourceRowNumbers) ||
                !ValidateProductChanges(product.FieldChanges))
            {
                return false;
            }
        }

        foreach (var product in plan.UnchangedProducts)
        {
            if (!productActions.Add(product.ProductCode) || !ValidRows(product.SourceRowNumbers))
            {
                return false;
            }
        }

        var batchActions = new HashSet<BatchKey>(BatchKeyComparer.Instance);
        foreach (var batch in plan.NewBatches)
        {
            if (!batchActions.Add(new BatchKey(batch.BatchKey)) ||
                batch.ShelfLifeValue <= 0 ||
                !ValidShelfLifeUnit(batch.ShelfLifeUnit) ||
                batch.CurrentArrivalQty < 0 ||
                batch.MaxArrivalQty < batch.CurrentArrivalQty ||
                !ValidRows(batch.SourceRowNumbers))
            {
                return false;
            }
        }

        foreach (var batch in plan.UpdatedBatches)
        {
            if (!batchActions.Add(new BatchKey(batch.BatchKey)) ||
                batch.FieldChanges.Count == 0 ||
                !ValidRows(batch.SourceRowNumbers) ||
                !ValidateBatchChanges(batch.FieldChanges))
            {
                return false;
            }
        }

        foreach (var batch in plan.UnchangedBatches)
        {
            if (!batchActions.Add(new BatchKey(batch.BatchKey)) || !ValidRows(batch.SourceRowNumbers))
            {
                return false;
            }
        }

        if (plan.Preview.BatchConflicts.Any(conflict =>
                !ValidBatchKey(conflict.BatchKey) ||
                conflict.RowNumbers.Any(row => row <= 0) ||
                conflict.DifferingFields.Count == 0 ||
                conflict.DifferingFields.Any(string.IsNullOrWhiteSpace) ||
                batchActions.Contains(new BatchKey(conflict.BatchKey))))
        {
            return false;
        }

        if (plan.Preview.StockConflicts.Any(stock =>
                !ValidProductCode(stock.ProductCode) ||
                !stock.IsConflict ||
                stock.Values.Count < 2 ||
                stock.Values.Any(value => value.RowNumbers.Any(row => row <= 0))))
        {
            return false;
        }

        var stockConflictCodes = plan.Preview.StockConflicts
            .Select(stock => stock.ProductCode)
            .ToHashSet(StringComparer.Ordinal);
        if (plan.UpdatedProducts.Any(product =>
                stockConflictCodes.Contains(product.ProductCode) &&
                product.FieldChanges.Any(change => change.FieldName is
                    "ExcelStockQty" or "EffectiveStockQty" or "EffectiveStockSource")))
        {
            return false;
        }

        return plan.Preview.RowIssues.All(issue =>
                issue.ExcelRowNumber > 0 &&
                !string.IsNullOrWhiteSpace(issue.Code) &&
                !string.IsNullOrWhiteSpace(issue.FieldName) &&
                !string.IsNullOrWhiteSpace(issue.Summary)) &&
            plan.Preview.PlanningIssues.All(issue =>
                (!issue.ExcelRowNumber.HasValue || issue.ExcelRowNumber.Value > 0) &&
                !string.IsNullOrWhiteSpace(issue.Code) &&
                !string.IsNullOrWhiteSpace(issue.FieldName) &&
                !string.IsNullOrWhiteSpace(issue.SafeSummary)) &&
            plan.Preview.StockConflicts
                .SelectMany(stock => stock.Values)
                .All(value => value.RowNumbers.All(row => row > 0));
    }

    private static bool ValidateProductChanges(IReadOnlyList<ImportFieldChange> changes)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!fields.Add(change.FieldName))
            {
                return false;
            }

            switch (change.FieldName)
            {
                case "CurrentName":
                case "CurrentBarcode":
                    if (!NullableString(change.Before) || !NullableString(change.After))
                    {
                        return false;
                    }

                    break;
                case "ExcelStockQty":
                case "EffectiveStockQty":
                    if (!NonNegativeInt(change.Before) || !NonNegativeInt(change.After))
                    {
                        return false;
                    }

                    break;
                case "EffectiveStockSource":
                    if (!NullableString(change.Before) || !Equals(change.After, "excel"))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool ValidScope(NewProductPlan product) =>
        product.ExpiryManagementStatus == ExpiryManagementStatus.Managed
            ? (product.PolicyCode is ExpiryPolicies.Food or ExpiryPolicies.Pet or ExpiryPolicies.GeneralLong) &&
              product.PolicyVersion == ExpiryPolicies.Version1
            : (product.ExpiryManagementStatus is ExpiryManagementStatus.Excluded or ExpiryManagementStatus.Unresolved) &&
              product.PolicyCode is null && product.PolicyVersion is null;

    private static bool ValidateBatchChanges(IReadOnlyList<ImportFieldChange> changes)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!fields.Add(change.FieldName))
            {
                return false;
            }

            switch (change.FieldName)
            {
                case "ShelfLifeValue":
                    if (!PositiveInt(change.Before) || !PositiveInt(change.After))
                    {
                        return false;
                    }

                    break;
                case "ShelfLifeUnit":
                    if (change.Before is not string beforeUnit || !ValidShelfLifeUnit(beforeUnit) ||
                        change.After is not string afterUnit || !ValidShelfLifeUnit(afterUnit))
                    {
                        return false;
                    }

                    break;
                case "CurrentArrivalQty":
                    if (!NonNegativeInt(change.Before) || !NonNegativeInt(change.After))
                    {
                        return false;
                    }

                    break;
                case "MaxArrivalQty":
                    if (change.Before is not int beforeMax || beforeMax < 0 ||
                        change.After is not int afterMax || afterMax < 0 || afterMax < beforeMax)
                    {
                        return false;
                    }

                    break;
                case "SourceDiscountReference":
                    if (!NullableString(change.Before) || !NullableString(change.After))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryValidateCurrentPlan(
        StoreDbContext context,
        ImportPlan plan,
        out CurrentPlan currentPlan)
    {
        currentPlan = new CurrentPlan();
        var productCodes = ProductCodes(plan).ToArray();
        var existingProducts = context.Products
            .Where(product => productCodes.Contains(product.ProductCode))
            .ToArray();
        var existingProductsByCode = existingProducts.ToDictionary(
            product => product.ProductCode,
            StringComparer.Ordinal);

        foreach (var product in plan.NewProducts)
        {
            if (existingProductsByCode.ContainsKey(product.ProductCode))
            {
                return false;
            }
        }

        foreach (var product in plan.UpdatedProducts)
        {
            if (!existingProductsByCode.TryGetValue(product.ProductCode, out var existing) ||
                !ValidateProductBeforeValues(existing, product.FieldChanges))
            {
                return false;
            }
        }

        foreach (var product in plan.UnchangedProducts)
        {
            if (!existingProductsByCode.TryGetValue(product.ProductCode, out var existing) ||
                !string.Equals(existing.CurrentName, product.CurrentName, StringComparison.Ordinal) ||
                !string.Equals(existing.CurrentBarcode, product.CurrentBarcode, StringComparison.Ordinal) ||
                existing.ExcelStockQty != product.ExcelStockQty ||
                existing.EffectiveStockQty != product.EffectiveStockQty ||
                !string.Equals(existing.EffectiveStockSource, product.EffectiveStockSource, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var batchesByKey = existingProducts.Length == 0
            ? new Dictionary<BatchKey, Batch>(BatchKeyComparer.Instance)
            : context.Batches
                .Where(batch => existingProducts.Select(product => product.Id).Contains(batch.ProductId))
                .ToDictionary(
                    batch => new BatchKey(
                        existingProductsByCode.Values.Single(product => product.Id == batch.ProductId).ProductCode,
                        batch.ProductionDate,
                        batch.ExpiryDate),
                    BatchKeyComparer.Instance);

        foreach (var batch in plan.NewBatches)
        {
            var key = new BatchKey(batch.BatchKey);
            if (!existingProductsByCode.ContainsKey(batch.BatchKey.ProductCode) &&
                !plan.NewProducts.Any(product => product.ProductCode == batch.BatchKey.ProductCode))
            {
                return false;
            }

            if (batchesByKey.ContainsKey(key))
            {
                return false;
            }
        }

        foreach (var batch in plan.UpdatedBatches)
        {
            if (!batchesByKey.TryGetValue(new BatchKey(batch.BatchKey), out var existing) ||
                !ValidateBatchBeforeValues(existing, batch.FieldChanges))
            {
                return false;
            }
        }

        foreach (var batch in plan.UnchangedBatches)
        {
            if (!batchesByKey.TryGetValue(new BatchKey(batch.BatchKey), out var existing) ||
                existing.ShelfLifeValue != batch.ShelfLifeValue ||
                !string.Equals(existing.ShelfLifeUnit, batch.ShelfLifeUnit, StringComparison.Ordinal) ||
                existing.CurrentArrivalQty != batch.CurrentArrivalQty ||
                existing.MaxArrivalQty != batch.MaxArrivalQty ||
                !string.Equals(existing.SourceDiscountReference, batch.SourceDiscountReference, StringComparison.Ordinal))
            {
                return false;
            }
        }

        currentPlan = new CurrentPlan(existingProductsByCode, batchesByKey);
        return true;
    }

    private static bool ValidateProductBeforeValues(Product product, IReadOnlyList<ImportFieldChange> changes)
    {
        foreach (var change in changes)
        {
            var matches = change.FieldName switch
            {
                "CurrentName" => NullableStringEquals(change.Before, product.CurrentName),
                "CurrentBarcode" => NullableStringEquals(change.Before, product.CurrentBarcode),
                "ExcelStockQty" => IntEquals(change.Before, product.ExcelStockQty),
                "EffectiveStockQty" => IntEquals(change.Before, product.EffectiveStockQty),
                "EffectiveStockSource" => NullableStringEquals(change.Before, product.EffectiveStockSource),
                _ => false
            };
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateBatchBeforeValues(Batch batch, IReadOnlyList<ImportFieldChange> changes)
    {
        foreach (var change in changes)
        {
            var matches = change.FieldName switch
            {
                "ShelfLifeValue" => IntEquals(change.Before, batch.ShelfLifeValue),
                "ShelfLifeUnit" => NullableStringEquals(change.Before, batch.ShelfLifeUnit),
                "CurrentArrivalQty" => IntEquals(change.Before, batch.CurrentArrivalQty),
                "MaxArrivalQty" => IntEquals(change.Before, batch.MaxArrivalQty),
                "SourceDiscountReference" => NullableStringEquals(change.Before, batch.SourceDiscountReference),
                _ => false
            };
            if (!matches)
            {
                return false;
            }
        }

        var maxChange = changes.SingleOrDefault(change => change.FieldName == "MaxArrivalQty");
        if (maxChange is not null &&
            maxChange.After is int maxAfter &&
            changes.SingleOrDefault(change => change.FieldName == "CurrentArrivalQty") is { After: int currentArrival } &&
            currentArrival > maxAfter)
        {
            return false;
        }

        return true;
    }

    private static Dictionary<string, Product> ApplyProducts(
        StoreDbContext context,
        ImportPlan plan,
        CurrentPlan currentPlan,
        long importId,
        DateTime updatedAtUtc)
    {
        var productsByCode = new Dictionary<string, Product>(currentPlan.ProductsByCode, StringComparer.Ordinal);
        foreach (var newProduct in plan.NewProducts)
        {
            var product = new Product
            {
                ProductCode = newProduct.ProductCode,
                CurrentName = newProduct.CurrentName,
                CurrentBarcode = newProduct.CurrentBarcode,
                CategoryCode = newProduct.CategoryCode,
                PolicyCode = newProduct.PolicyCode,
                PolicyVersion = newProduct.PolicyVersion,
                ExpiryManagementStatus = newProduct.ExpiryManagementStatus,
                ExcelStockQty = newProduct.ExcelStockQty,
                EffectiveStockQty = newProduct.EffectiveStockQty,
                EffectiveStockSource = "excel",
                LastSeenImportId = importId,
                CreatedAtUtc = updatedAtUtc,
                UpdatedAtUtc = updatedAtUtc
            };
            context.Products.Add(product);
            productsByCode.Add(newProduct.ProductCode, product);
        }

        foreach (var updatedProduct in plan.UpdatedProducts)
        {
            var product = productsByCode[updatedProduct.ProductCode];
            foreach (var change in updatedProduct.FieldChanges)
            {
                ApplyProductChange(product, change);
            }

            product.LastSeenImportId = importId;
            product.UpdatedAtUtc = updatedAtUtc;
        }

        foreach (var unchangedProduct in plan.UnchangedProducts)
        {
            var product = productsByCode[unchangedProduct.ProductCode];
            product.LastSeenImportId = importId;
            product.UpdatedAtUtc = updatedAtUtc;
        }

        var productActionCodes = ProductActionCodes(plan).ToHashSet(StringComparer.Ordinal);
        foreach (var productCode in BatchActionKeys(plan)
                     .Select(key => key.ProductCode)
                     .Distinct(StringComparer.Ordinal))
        {
            if (productActionCodes.Contains(productCode))
            {
                continue;
            }

            var product = productsByCode[productCode];
            product.LastSeenImportId = importId;
            product.UpdatedAtUtc = updatedAtUtc;
        }

        return productsByCode;
    }

    private static void ApplyProductChange(Product product, ImportFieldChange change)
    {
        switch (change.FieldName)
        {
            case "CurrentName":
                product.CurrentName = (string?)change.After;
                break;
            case "CurrentBarcode":
                product.CurrentBarcode = (string?)change.After;
                break;
            case "ExcelStockQty":
                product.ExcelStockQty = (int)change.After!;
                break;
            case "EffectiveStockQty":
                product.EffectiveStockQty = (int)change.After!;
                break;
            case "EffectiveStockSource":
                product.EffectiveStockSource = (string?)change.After;
                break;
            default:
                throw new InvalidDataException("The import plan contains an unsupported product field.");
        }
    }

    private static void ApplyBatches(
        StoreDbContext context,
        ImportPlan plan,
        CurrentPlan currentPlan,
        IReadOnlyDictionary<string, Product> productsByCode,
        long importId,
        DateTime updatedAtUtc)
    {
        foreach (var newBatch in plan.NewBatches)
        {
            var product = productsByCode[newBatch.BatchKey.ProductCode];
            context.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ProductionDate = newBatch.BatchKey.ProductionDate,
                ExpiryDate = newBatch.BatchKey.ExpiryDate,
                ShelfLifeValue = newBatch.ShelfLifeValue,
                ShelfLifeUnit = newBatch.ShelfLifeUnit,
                CurrentArrivalQty = newBatch.CurrentArrivalQty,
                MaxArrivalQty = newBatch.MaxArrivalQty,
                SourceDiscountReference = newBatch.SourceDiscountReference,
                LastSeenImportId = importId,
                CreatedAtUtc = updatedAtUtc,
                UpdatedAtUtc = updatedAtUtc
            });
        }

        foreach (var updatedBatch in plan.UpdatedBatches)
        {
            var batch = currentPlan.BatchesByKey[new BatchKey(updatedBatch.BatchKey)];
            foreach (var change in updatedBatch.FieldChanges)
            {
                ApplyBatchChange(batch, change);
            }

            batch.LastSeenImportId = importId;
            batch.UpdatedAtUtc = updatedAtUtc;
        }

        foreach (var unchangedBatch in plan.UnchangedBatches)
        {
            var batch = currentPlan.BatchesByKey[new BatchKey(unchangedBatch.BatchKey)];
            batch.LastSeenImportId = importId;
            batch.UpdatedAtUtc = updatedAtUtc;
        }
    }

    private static void ApplyBatchChange(Batch batch, ImportFieldChange change)
    {
        switch (change.FieldName)
        {
            case "ShelfLifeValue":
                batch.ShelfLifeValue = (int)change.After!;
                break;
            case "ShelfLifeUnit":
                batch.ShelfLifeUnit = (string)change.After!;
                break;
            case "CurrentArrivalQty":
                batch.CurrentArrivalQty = (int)change.After!;
                break;
            case "MaxArrivalQty":
                batch.MaxArrivalQty = (int)change.After!;
                break;
            case "SourceDiscountReference":
                batch.SourceDiscountReference = (string?)change.After;
                break;
            default:
                throw new InvalidDataException("The import plan contains an unsupported batch field.");
        }
    }

    private static IReadOnlyList<PendingIssue> BuildIssues(ImportPreview preview)
    {
        var issues = new List<PendingIssue>();
        foreach (var issue in preview.RowIssues
                     .OrderBy(issue => issue.ExcelRowNumber)
                     .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                     .ThenBy(issue => issue.FieldName, StringComparer.Ordinal))
        {
            issues.Add(new PendingIssue(issue.ExcelRowNumber, issue.Code, issue.FieldName, issue.Summary));
        }

        foreach (var conflict in preview.BatchConflicts
                     .OrderBy(conflict => conflict.BatchKey.ProductCode, StringComparer.Ordinal)
                     .ThenBy(conflict => conflict.BatchKey.ProductionDate ?? DateOnly.MinValue)
                     .ThenBy(conflict => conflict.BatchKey.ExpiryDate))
        {
            var fields = string.Join("、", conflict.DifferingFields);
            foreach (var rowNumber in conflict.RowNumbers.Distinct().OrderBy(row => row))
            {
                issues.Add(new PendingIssue(
                    rowNumber,
                    "batch_conflict",
                    fields,
                    "批次关键字段存在冲突，该批次未写入。"));
            }
        }

        foreach (var stock in preview.StockConflicts.OrderBy(stock => stock.ProductCode, StringComparer.Ordinal))
        {
            foreach (var rowNumber in stock.Values
                         .SelectMany(value => value.RowNumbers)
                         .Distinct()
                         .OrderBy(row => row))
            {
                issues.Add(new PendingIssue(
                    rowNumber,
                    "stock_conflict",
                    "该商品门店库存总数",
                    "同一商品存在多个库存总数，未选择任何库存值。"));
            }
        }

        foreach (var issue in preview.PlanningIssues
                     .OrderBy(issue => issue.ProductCode, StringComparer.Ordinal)
                     .ThenBy(issue => issue.ExcelRowNumber ?? int.MaxValue)
                     .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                     .ThenBy(issue => issue.FieldName, StringComparer.Ordinal))
        {
            issues.Add(new PendingIssue(issue.ExcelRowNumber, issue.Code, issue.FieldName, issue.SafeSummary));
        }

        return issues;
    }

    private static IEnumerable<string> ProductCodes(ImportPlan plan) => ProductActionCodes(plan)
        .Concat(BatchActionKeys(plan).Select(key => key.ProductCode))
        .Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> ProductActionCodes(ImportPlan plan) => plan.NewProducts
        .Select(product => product.ProductCode)
        .Concat(plan.UpdatedProducts.Select(product => product.ProductCode))
        .Concat(plan.UnchangedProducts.Select(product => product.ProductCode))
        .Distinct(StringComparer.Ordinal);

    private static IEnumerable<BatchKey> BatchActionKeys(ImportPlan plan) => plan.NewBatches
        .Select(batch => new BatchKey(batch.BatchKey))
        .Concat(plan.UpdatedBatches.Select(batch => new BatchKey(batch.BatchKey)))
        .Concat(plan.UnchangedBatches.Select(batch => new BatchKey(batch.BatchKey)))
        .Distinct(BatchKeyComparer.Instance);

    private static bool ValidProductCode(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();

    private static bool ValidBatchKey(ExcelBatchKey key) =>
        ValidProductCode(key.ProductCode) && key.ExpiryDate != default;

    private static bool ValidRows(IReadOnlyList<int> rows) => rows.All(row => row > 0);

    private static bool ValidShelfLifeUnit(string? value) => value is "M" or "D" or "Y";

    private static bool NullableString(object? value) => value is null or string;

    private static bool NonNegativeInt(object? value) => value is int number && number >= 0;

    private static bool PositiveInt(object? value) => value is int number && number > 0;

    private static bool NullableStringEquals(object? value, string? expected) =>
        value is null && expected is null || value is string text && string.Equals(text, expected, StringComparison.Ordinal);

    private static bool IntEquals(object? value, int expected) => value is int number && number == expected;

    private readonly record struct PendingIssue(int? RowNumber, string IssueType, string? FieldName, string SafeSummary);

    private sealed class CurrentPlan
    {
        public CurrentPlan()
        {
            ProductsByCode = new Dictionary<string, Product>(StringComparer.Ordinal);
            BatchesByKey = new Dictionary<BatchKey, Batch>(BatchKeyComparer.Instance);
        }

        public CurrentPlan(
            Dictionary<string, Product> productsByCode,
            Dictionary<BatchKey, Batch> batchesByKey)
        {
            ProductsByCode = productsByCode;
            BatchesByKey = batchesByKey;
        }

        public Dictionary<string, Product> ProductsByCode { get; }

        public Dictionary<BatchKey, Batch> BatchesByKey { get; }
    }

    private readonly record struct BatchKey(string ProductCode, DateOnly? ProductionDate, DateOnly ExpiryDate)
    {
        public BatchKey(ExcelBatchKey key)
            : this(key.ProductCode, key.ProductionDate, key.ExpiryDate)
        {
        }
    }

    private sealed class BatchKeyComparer : IEqualityComparer<BatchKey>
    {
        public static readonly BatchKeyComparer Instance = new();

        public bool Equals(BatchKey x, BatchKey y) =>
            string.Equals(x.ProductCode, y.ProductCode, StringComparison.Ordinal) &&
            x.ProductionDate == y.ProductionDate &&
            x.ExpiryDate == y.ExpiryDate;

        public int GetHashCode(BatchKey obj) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.ProductCode),
            obj.ProductionDate,
            obj.ExpiryDate);
    }
}
