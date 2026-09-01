using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ConfirmedImportLifecycleOrchestratorTests
{
    private static readonly string[] Headers =
    [
        "商品大类", "商品编码", "商品条码", "商品名称", "生产日期", "有效日期", "保质期",
        "保质期单位", "是否该做临期折扣", "该批次累计到货数量", "该商品门店库存总数"
    ];

    [Fact]
    public void RealXlsxNewBatchRunsStage2AndPostImportInOneCommit()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddCompletedFoodBaseline(seed);
        }
        var sourcePath = Path.Combine(database.Directory, "source.xlsx");
        WriteWorkbook(sourcePath, [
            "食品", "P-NEW", "B-NEW", "新商品", "2026-01-01", "2026-09-20", "12", "M", "是", "3", "5"
        ]);
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var occurredAtUtc = new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(sourcePath);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            Assert.Equal([("P-NEW", 5)], plan.ExplicitProductStocks.Select(stock => (stock.ProductCode, stock.Quantity)));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        }

        ConfirmedImportResult result;
        using (var context = database.Open())
        {
            result = new ConfirmedImportLifecycleOrchestrator().Execute(
                context,
                new ConfirmedImportLifecycleRequest(
                    contract,
                    Path.Combine(database.Directory, "snapshots"),
                    parsedAtUtc,
                    new DateOnly(2026, 8, 27),
                    occurredAtUtc));
        }

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(result.ImportId, product.LastSeenImportId);
        Assert.Equal(result.ImportId, batch.LastSeenImportId);
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Equal(occurredAtUtc, batch.UpdatedAtUtc);
        Assert.Single(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void ExplicitZeroUsesStockZeroLifecycleAndSkipsPostImport()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddCompletedFoodBaseline(seed);
        }
        var sourcePath = Path.Combine(database.Directory, "zero.xlsx");
        WriteWorkbook(sourcePath, [
            "食品", "P-ZERO", "B-ZERO", "归零商品", "2026-01-01", "2026-09-20", "12", "M", "是", "3", "0"
        ]);
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var occurredAtUtc = new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(sourcePath);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            Assert.Equal([("P-ZERO", 0)], plan.ExplicitProductStocks.Select(stock => (stock.ProductCode, stock.Quantity)));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        }

        using (var context = database.Open())
        {
            var result = new ConfirmedImportLifecycleOrchestrator().Execute(
                context,
                new ConfirmedImportLifecycleRequest(
                    contract,
                    Path.Combine(database.Directory, "snapshots"),
                    parsedAtUtc,
                    new DateOnly(2026, 8, 27),
                    occurredAtUtc));
            Assert.True(result.Succeeded);
        }

        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal(1, product.LifecycleGeneration);
        Assert.Equal("stopped", batch.TrackingStatus);
        Assert.Equal("product_stock_zero", batch.StopReason);
        Assert.Null(batch.NextTriggerDate);
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Single(verify.LifecycleEvents.AsNoTracking().Where(item => item.EventType == "product_stock_zero"));
    }

    [Fact]
    public void ExistingArrivalUsesPreImportMaximumAndAttentionVersion()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-ARRIVAL",
                CurrentName = "到货商品",
                CurrentBarcode = "B-ARRIVAL",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "excel"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 9, 20),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 2,
                SourceDiscountReference = "是",
                AttentionVersion = 4
            });
            seed.SaveChanges();
            AddCompletedFoodBaseline(seed);
        }

        var sourcePath = Path.Combine(database.Directory, "arrival.xlsx");
        WriteWorkbook(sourcePath, [
            "食品", "P-ARRIVAL", "B-ARRIVAL", "到货商品", "2026-01-01", "2026-09-20", "12", "M", "是", "3", "5"
        ]);
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var occurredAtUtc = new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(sourcePath);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        }

        using (var context = database.Open())
        {
            var result = new ConfirmedImportLifecycleOrchestrator().Execute(
                context,
                new ConfirmedImportLifecycleRequest(
                    contract,
                    Path.Combine(database.Directory, "snapshots"),
                    parsedAtUtc,
                    new DateOnly(2026, 8, 27),
                    occurredAtUtc));
            Assert.True(result.Succeeded);
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(3, batch.CurrentArrivalQty);
        Assert.Equal(3, batch.MaxArrivalQty);
        Assert.Equal(5, batch.AttentionVersion);
        Assert.Single(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void ExecutorJoinsOuterTransactionWithoutCommittingIt()
    {
        using var database = SqliteTestDatabase.Create();
        var sourcePath = Path.Combine(database.Directory, "outer.xlsx");
        WriteWorkbook(sourcePath, [
            "食品", "P-OUTER", "B-OUTER", "外层事务", "2026-01-01", "2026-09-20", "12", "M", "是", "3", "5"
        ]);
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var contract = ReadContract(database, sourcePath);

        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            var result = new ConfirmedImportExecutor(
                utcNow: () => new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc))
                .Execute(contract, context, Path.Combine(database.Directory, "snapshots"), parsedAtUtc);
            Assert.True(result.Succeeded);
            Assert.True(context.Database.CurrentTransaction is not null);
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.Empty(verify.Imports.AsNoTracking());
        Assert.Empty(verify.Products.AsNoTracking());
        Assert.Empty(verify.Batches.AsNoTracking());
    }

    [Fact]
    public void PostImportConstraintFailureRollsBackStage2AndLifecycleTogether()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var seedProduct = new Product
            {
                ProductCode = "P-FAIL",
                CurrentName = "导入前商品",
                CurrentBarcode = "B-FAIL-OLD",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "excel"
            };
            seed.Products.Add(seedProduct);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = seedProduct.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 9, 20),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                SourceDiscountReference = "是",
                CurrentStage = ExpiryStageCalculator.None,
                CreatedAtUtc = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc)
            });
            seed.SaveChanges();
            AddCompletedFoodBaseline(seed);
        }
        using (var schema = database.Open())
        {
            schema.Database.ExecuteSqlRaw(
                "CREATE TRIGGER fail_task_item_insert BEFORE INSERT ON task_items BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        }

        var sourcePath = Path.Combine(database.Directory, "failure.xlsx");
        WriteWorkbook(sourcePath, [
            "食品", "P-FAIL", "B-FAIL", "失败回滚", "2026-01-01", "2026-09-20", "12", "M", "是", "3", "5"
        ]);
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var contract = ReadContract(database, sourcePath);

        using (var context = database.Open())
        {
            var result = new ConfirmedImportLifecycleOrchestrator().Execute(
                context,
                new ConfirmedImportLifecycleRequest(
                    contract,
                    Path.Combine(database.Directory, "snapshots"),
                    parsedAtUtc,
                    new DateOnly(2026, 8, 27),
                    new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc)));
            Assert.False(result.Succeeded);
            Assert.Equal(ConfirmedImportCodes.TransactionFailed, result.Code);
            Assert.NotNull(result.SnapshotPath);
        }

        using var verify = database.Open();
        Assert.Single(verify.Imports.AsNoTracking());
        var product = Assert.Single(verify.Products.AsNoTracking());
        Assert.Equal("导入前商品", product.CurrentName);
        Assert.Equal("B-FAIL-OLD", product.CurrentBarcode);
        Assert.Equal(5, product.EffectiveStockQty);
        Assert.Null(product.LastSeenImportId);
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(1, batch.CurrentArrivalQty);
        Assert.Equal(1, batch.MaxArrivalQty);
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Null(batch.NextTriggerDate);
        Assert.Null(batch.LastSeenImportId);
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Empty(verify.LifecycleEvents.AsNoTracking());
    }

    [Fact]
    public void ReplayingTheSameConfirmationIsRejectedWithoutASecondImport()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-REPLAY",
                CurrentName = "重放商品",
                CurrentBarcode = "B-REPLAY",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "excel"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 9, 20),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                TrackingStatus = "stopped",
                StopReason = "batch_checked_zero",
                StoppedAtUtc = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc),
                SourceDiscountReference = "是"
            });
            seed.SaveChanges();
            AddCompletedFoodBaseline(seed);
        }
        var sourcePath = Path.Combine(database.Directory, "replay.xlsx");
        WriteWorkbook(sourcePath, [
            "食品", "P-REPLAY", "B-REPLAY", "重放商品", "2026-01-01", "2026-09-20", "12", "M", "是", "2", "5"
        ]);
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var contract = ReadContract(database, sourcePath);
        var request = new ConfirmedImportLifecycleRequest(
            contract,
            Path.Combine(database.Directory, "snapshots"),
            parsedAtUtc,
            new DateOnly(2026, 8, 27),
            new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc));

        using (var first = database.Open())
        {
            Assert.True(new ConfirmedImportLifecycleOrchestrator().Execute(first, request).Succeeded);
        }

        using (var second = database.Open())
        {
            var result = new ConfirmedImportLifecycleOrchestrator().Execute(second, request);
            Assert.False(result.Succeeded);
            Assert.Equal(ConfirmedImportCodes.StalePlan, result.Code);
        }

        using var verify = database.Open();
        Assert.Equal(2, verify.Imports.AsNoTracking().Count());
        Assert.Single(verify.Products.AsNoTracking());
        Assert.Single(verify.Batches.AsNoTracking());
        Assert.Single(verify.Tasks.AsNoTracking());
        Assert.Single(verify.TaskItems.AsNoTracking());
        var replayBatch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(1, replayBatch.AttentionVersion);
        Assert.Equal("active", replayBatch.TrackingStatus);
        Assert.Single(verify.LifecycleEvents.AsNoTracking()
            .Where(item => item.EventType == "batch_tracking_resumed"));
    }

    private static void WriteWorkbook(string path, IReadOnlyList<string> values)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        var header = string.Join(string.Empty, Headers.Select((value, index) => InlineCell(ColumnName(index), 1, value)));
        var row = string.Join(string.Empty, values.Select((value, index) => InlineCell(ColumnName(index), 2, value)));
        AddEntry(archive, "xl/worksheets/sheet1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{header}</row><row r=\"2\">{row}</row></sheetData></worksheet>");
    }

    private static void AddCompletedFoodBaseline(StoreDbContext context)
    {
        var import = new ImportRecord { SourceFileName = "baseline.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = "succeeded" };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = new DateOnly(2026, 8, 26), IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
        context.SaveChanges();
    }

    private static ImportConfirmationContract ReadContract(SqliteTestDatabase database, string sourcePath)
    {
        using var preview = database.Open();
        var workbook = new ExcelTemplateReader().Read(sourcePath);
        var plan = new ExcelImportPlanner().Plan(
            preview,
            new ExcelFileClassifier().Classify(workbook));
        return Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
            new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
    }

    private static string InlineCell(string column, int row, string value) =>
        $"<c r=\"{column}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBased)
    {
        var value = zeroBased + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }

        return result;
    }
}
