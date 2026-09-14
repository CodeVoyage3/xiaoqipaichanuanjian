using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure.Excel;
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
        context.Imports.AddRange(baseline, undone);
        context.SaveChanges();

        var present = Product("P", 5, 99, baseline.Id);
        var missing = Product("Q", 7, 7, baseline.Id);
        context.Products.AddRange(present, missing);
        context.SaveChanges();
        var presentBatch = Batch(present.Id, new DateOnly(2026, 1, 1), baseline.Id);
        var missingBatch = Batch(missing.Id, new DateOnly(2026, 1, 2), baseline.Id);
        context.Batches.AddRange(presentBatch, missingBatch);
        context.SaveChanges();
        var task = new ProductTask { ProductId = missing.Id, Status = "open" };
        context.Tasks.Add(task);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = missing.Id, BatchId = missingBatch.Id });
        var current = Import("2026-09-03");
        context.Imports.Add(current);
        context.SaveChanges();
        present.LastSeenImportId = current.Id;
        presentBatch.LastSeenImportId = current.Id;
        context.ImportIssues.Add(new ImportIssue { ImportId = current.Id, IssueType = "row_issue", SafeSummary = "test" });
        context.SaveChanges();

        var plan = new ExcelImportPlanner().Plan(context, Classify(
            Row(2, "P", "新名称", "新条码", "2026-01-01", "2026-12-31", "0"),
            Row(3, "N", "新增", "N条码", null, "2027-12-31", "3")));
        var summary = new ImportDifferenceSummaryQuery().Read(context, plan, current.Id);

        Assert.Equal(1, summary.NewProductCount);
        Assert.Equal(1, summary.NewBatchCount);
        Assert.Equal(0, summary.StockIncreaseCount);
        Assert.Equal(1, summary.StockDecreaseCount);
        Assert.Equal(1, summary.StockBecameZeroCount);
        Assert.Equal(1, summary.MissingBatchCount);
        Assert.Equal(1, summary.MissingProductCount);
        Assert.Equal(1, summary.MissingOpenTaskBatchCount);
        Assert.Equal(1, summary.MissingOpenTaskProductCount);
        Assert.Equal(1, summary.IssueCount);
        Assert.Contains(plan.UpdatedProducts.Single().FieldChanges, change => change.FieldName == "CurrentName");
        Assert.Contains(plan.UpdatedProducts.Single().FieldChanges, change => change.FieldName == "CurrentBarcode");
        Assert.Contains(plan.NewBatches, batch => batch.BatchKey.ProductionDate is null);
        Assert.Equal(7, missing.ExcelStockQty);
        Assert.Equal("open", task.Status);
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

    private static ImportRecord Import(string confirmedAtUtc, bool undone = false) => new()
    {
        SourceFileName = "test.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.Parse(confirmedAtUtc + "T00:00:00Z"),
        ConfirmedAtUtc = DateTime.Parse(confirmedAtUtc + "T00:00:00Z"), Status = undone ? ImportStatuses.Undone : ImportStatuses.Succeeded,
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
}
