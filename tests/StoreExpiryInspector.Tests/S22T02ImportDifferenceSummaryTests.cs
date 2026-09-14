using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using StoreExpiryInspector.UI;
using System.Security.Cryptography;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S22T02ImportDifferenceSummaryTests
{
    [Fact]
    public void UsesPlanExcelStockAndPreviousEffectiveImportWithoutWritingBusinessData()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var baseline = Import("2026-09-01");
        var undone = Import("2026-09-02", undone: true);
        var failed = Import("2026-09-02", status: "Failed");
        var unconfirmed = Import("2026-09-02", status: ImportStatuses.Succeeded, confirmed: false);
        var future = Import("2026-09-04");
        context.Imports.AddRange(baseline, undone, failed, unconfirmed, future);
        context.SaveChanges();

        var present = Product("P", 5, 99, baseline.Id);
        var increase = Product("I", 1, 99, baseline.Id);
        var missing = Product("Q", 7, 7, baseline.Id);
        context.Products.AddRange(present, increase, missing);
        context.SaveChanges();
        var presentBatch = Batch(present.Id, new DateOnly(2026, 1, 1), baseline.Id);
        var increaseBatch = Batch(increase.Id, new DateOnly(2026, 1, 3), baseline.Id);
        var missingBatch = Batch(missing.Id, new DateOnly(2026, 1, 2), baseline.Id);
        context.Batches.AddRange(presentBatch, increaseBatch, missingBatch);
        context.SaveChanges();
        var task = new ProductTask { ProductId = missing.Id, Status = "open" };
        context.Tasks.Add(task);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = missing.Id, BatchId = missingBatch.Id });
        var closedTask = new ProductTask { ProductId = missing.Id, Status = "completed", ClosedAtUtc = DateTime.UtcNow, CloseReason = "test" };
        context.Tasks.Add(closedTask);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = closedTask.Id, ProductId = missing.Id, BatchId = missingBatch.Id });
        var current = Import("2026-09-03");
        context.Imports.Add(current);
        context.SaveChanges();
        present.LastSeenImportId = current.Id;
        presentBatch.LastSeenImportId = current.Id;
        increase.LastSeenImportId = current.Id;
        increaseBatch.LastSeenImportId = current.Id;
        context.ImportIssues.Add(new ImportIssue { ImportId = current.Id, IssueType = "row_issue", SafeSummary = "test" });
        context.SaveChanges();

        var plan = new ExcelImportPlanner().Plan(context, Classify(
            Row(2, "P", "新名称", "新条码", "2026-01-01", "2026-12-31", "0"),
            Row(3, "I", "增加", "I条码", "2026-01-03", "2027-01-03", "3"),
            Row(4, "N", "新增", "N条码", null, "2027-12-31", "3")));
        var summary = new ImportDifferenceSummaryQuery().Read(context, plan, current.Id);

        Assert.Equal(1, summary.NewProductCount);
        Assert.Equal(1, summary.NewBatchCount);
        Assert.Equal(1, summary.StockIncreaseCount);
        Assert.Equal(1, summary.StockDecreaseCount);
        Assert.Equal(1, summary.StockBecameZeroCount);
        Assert.Equal(1, summary.MissingBatchCount);
        Assert.Equal(1, summary.MissingProductCount);
        Assert.Equal(1, summary.MissingOpenTaskBatchCount);
        Assert.Equal(1, summary.MissingOpenTaskProductCount);
        Assert.Equal(1, summary.IssueCount);
        var renamed = plan.UpdatedProducts.Single(product => product.ProductCode == "P");
        Assert.Contains(renamed.FieldChanges, change => change.FieldName == "CurrentName");
        Assert.Contains(renamed.FieldChanges, change => change.FieldName == "CurrentBarcode");
        Assert.Contains(plan.NewBatches, batch => batch.BatchKey.ProductionDate is null);
        Assert.Equal(7, missing.ExcelStockQty);
        Assert.Equal("open", task.Status);
        Assert.Equal("completed", closedTask.Status);
    }

    [Fact]
    public void FirstEffectiveImportLeavesHistoricalIndicatorsUnavailable()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var current = Import("2026-09-03");
        context.Imports.Add(current);
        context.SaveChanges();
        var plan = new ExcelImportPlanner().Plan(context, Classify(Row(2, "N", "新增", "N条码", null, "2027-12-31", "3")));

        var summary = new ImportDifferenceSummaryQuery().Read(context, plan, current.Id);

        Assert.Null(summary.StockIncreaseCount);
        Assert.Null(summary.StockDecreaseCount);
        Assert.Null(summary.StockBecameZeroCount);
        Assert.Null(summary.MissingBatchCount);
        Assert.Null(summary.MissingOpenTaskBatchCount);
    }

    [Fact]
    public void CountsMoreMissingBatchesThanSqliteParameterLimitWithoutArrayParameters()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var baseline = Import("2026-09-01");
        var current = Import("2026-09-02");
        context.Imports.AddRange(baseline, current);
        context.SaveChanges();
        var product = Product("P", 1, 1, baseline.Id);
        context.Products.Add(product);
        context.SaveChanges();
        context.Batches.AddRange(Enumerable.Range(0, 1_100).Select(index => new Batch
        {
            ProductId = product.Id, ProductionDate = null, ExpiryDate = new DateOnly(2027, 1, 1).AddDays(index), ShelfLifeValue = 12,
            ShelfLifeUnit = "M", CurrentArrivalQty = 1, MaxArrivalQty = 1, LastSeenImportId = baseline.Id
        }));
        context.SaveChanges();
        var plan = new ExcelImportPlanner().Plan(context, Classify(Row(2, "N", "新增", "N条码", null, "2027-12-31", "3")));

        var summary = new ImportDifferenceSummaryQuery().Read(context, plan, current.Id);

        Assert.Equal(1_100, summary.MissingBatchCount);
        Assert.Equal(1, summary.MissingProductCount);
        Assert.Equal(0, summary.MissingOpenTaskBatchCount);
    }

    [Fact]
    public async Task PreservesSucceededImportWhenImmediateSummaryReadFails()
    {
        using var fixture = SummaryVmFixture.Create();
        var executions = 0;
        var vm = new ImportViewModel(
            parsePreview: fixture.Parse,
            confirmPreview: fixture.Coordinator.Confirm,
            executeImport: (contract, parsedAtUtc) => { executions++; return fixture.Coordinator.Execute(contract, parsedAtUtc); },
            summarizeImport: (_, _) => throw new InvalidOperationException("summary unavailable"),
            utcNow: () => new DateTime(2026, 9, 13, 23, 59, 0, DateTimeKind.Utc));

        await vm.SelectFileAsync(fixture.Path);
        await vm.ConfirmAsync();

        Assert.True(vm.State == ImportPageState.Succeeded, $"{vm.LastCode}: {vm.StatusMessage}");
        Assert.True(vm.HasDifferenceSummaryFailure);
        Assert.False(vm.CanRetry);
        Assert.Contains("摘要读取失败", vm.StatusMessage);
        await vm.RetryAsync();
        Assert.Equal(1, executions);
    }

    [Fact]
    public void BatchIdentityKeepsProductionDateAndNoProductionDateSeparate()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(context, Classify(
            Row(2, "P", "商品", "条码", null, "2027-12-31", "3"),
            Row(3, "P", "商品", "条码", "2026-01-01", "2027-12-31", "3")));

        Assert.Single(plan.NewProducts);
        Assert.Equal(2, plan.NewBatches.Count);
        Assert.Contains(plan.NewBatches, batch => batch.BatchKey.ProductionDate is null);
        Assert.Contains(plan.NewBatches, batch => batch.BatchKey.ProductionDate == new DateOnly(2026, 1, 1));
    }

    private static ImportRecord Import(string confirmedAtUtc, bool undone = false, string? status = null, bool confirmed = true) => new()
    {
        SourceFileName = "test.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.Parse(confirmedAtUtc + "T00:00:00Z"),
        ConfirmedAtUtc = confirmed ? DateTime.Parse(confirmedAtUtc + "T00:00:00Z") : null, Status = status ?? (undone ? ImportStatuses.Undone : ImportStatuses.Succeeded),
        IsUndone = undone, UndoneAtUtc = undone ? DateTime.Parse(confirmedAtUtc + "T01:00:00Z") : null
    };

    private static Product Product(string code, int excel, int effective, long importId) => new()
    {
        ProductCode = code, CurrentName = code, CurrentBarcode = code + "B", ExcelStockQty = excel, EffectiveStockQty = effective,
        EffectiveStockSource = "manual", LastSeenImportId = importId
    };

    private static Batch Batch(long productId, DateOnly production, long importId) => new()
    {
        ProductId = productId, ProductionDate = production, ExpiryDate = production == new DateOnly(2026, 1, 1) ? new DateOnly(2026, 12, 31) : production.AddYears(1), ShelfLifeValue = 12, ShelfLifeUnit = "M",
        CurrentArrivalQty = 1, MaxArrivalQty = 1, LastSeenImportId = importId
    };

    private static ExcelClassificationResult Classify(params ExcelRowDto[] rows) => new ExcelFileClassifier().Classify(new ExcelWorkbookDto("test.xlsx", string.Empty, "Sheet1", Array.Empty<string>(), rows));

    private static ExcelRowDto Row(int number, string code, string name, string barcode, string? production, string expiry, string stock) => new(
        number, "食品", code, barcode, name, production, expiry, "12", "M", "否", "1", stock);

    private sealed class SummaryVmFixture : IDisposable
    {
        private SummaryVmFixture(string directory, string path, SqliteTestDatabase database, DataImportCoordinator coordinator, ImportPreviewLoadResult loadResult)
        {
            Directory = directory; Path = path; Database = database; Coordinator = coordinator; _loadResult = loadResult;
        }

        private readonly ImportPreviewLoadResult _loadResult;
        public string Directory { get; }
        public string Path { get; }
        public SqliteTestDatabase Database { get; }
        public DataImportCoordinator Coordinator { get; }
        public ImportPreviewLoadResult Parse(string _) => _loadResult;

        public static SummaryVmFixture Create()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StoreExpiryInspectorS22T02", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "source.xlsx");
            var bytes = new byte[] { 1, 2, 3 };
            System.IO.File.WriteAllBytes(path, bytes);
            var database = SqliteTestDatabase.Create();
            using var context = database.Open();
            var plan = new ExcelImportPlanner().Plan(context, Classify(Row(2, "N", "新增", "N条码", null, "2027-12-31", "3")));
            var workbook = new ExcelWorkbookDto("source.xlsx", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Sheet1", Array.Empty<string>(), Array.Empty<ExcelRowDto>());
            var loadResult = new ImportPreviewLoadResult(workbook, plan, new ImportConfirmationGuard().BindPreview(path, workbook, plan));
            return new(directory, path, database, new DataImportCoordinator(database.Open, System.IO.Path.Combine(directory, "snapshots"), () => new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc)), loadResult);
        }

        public void Dispose()
        {
            Database.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
