using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using StoreExpiryInspector.UI;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void SucceededUiSeparatesSevenMetricCardsFromPreviewActions()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "ImportViewModel.cs"));

        Assert.Contains("<UniformGrid Columns=\"4\"", xaml);
        Assert.Contains("<UniformGrid Columns=\"3\"", xaml);
        foreach (var name in new[] { "新增商品指标", "新增批次指标", "库存增加指标", "库存减少指标", "库存变为零指标", "本次未出现指标", "未出现仍有待办指标" })
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"再次导入 Excel 数据\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"完成本次导入\"", xaml);
        Assert.Contains("Visibility=\"{Binding Import.ShowIssueTable", xaml);
        Assert.Contains("Visibility=\"{Binding Import.ShowNoIssueMessage", xaml);
        Assert.Contains("<Condition Binding=\"{Binding Import.IsSucceeded}\" Value=\"False\"", xaml);
        Assert.DoesNotContain("PreviewIssueSummaryTitle", xaml + viewModel);
        Assert.DoesNotContain("导入前预览提示", xaml + viewModel);
    }

    [Fact]
    public void SeedsRequestedTemporaryGuiFixture()
    {
        var root = Environment.GetEnvironmentVariable("S22_T02_GUI_ROOT");
        var ownsRoot = string.IsNullOrWhiteSpace(root);
        root ??= Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.True(Guid.TryParse(Path.GetFileName(root), out _));
        Assert.StartsWith(Path.GetTempPath(), root, StringComparison.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "backups", "pre-import"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            var databasePath = Path.Combine(root, "data", "app.db");
            DatabaseInitializer.Initialize(databasePath);
            using var context = DatabaseInitializer.CreateContext(databasePath);
            var baseline = Import(DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"));
            context.Imports.Add(baseline);
            context.SaveChanges();

            var existing = Enumerable.Range(0, 11).Select(index =>
            {
                var stock = index == 1 ? 10 : index == 10 ? 40 : index % 10 == 0 ? 0 : 30;
                return Product($"S8T03-{index:D6}", stock, index == 1 ? 99 : stock, baseline.Id);
            }).ToArray();
            var missing = Product("S22-T02-MISSING", 6, 6, baseline.Id);
            context.Products.AddRange(existing.Append(missing));
            context.SaveChanges();
            context.Batches.AddRange(existing.Select((product, index) =>
                FixtureBatch(
                    product,
                    index % 10 is 0 or 1 ? new DateOnly(2026, 9, 9) : new DateOnly(2027, 9, 4),
                    index % 10 is 0 or 1 ? 10 : index % 10 == 2 ? 1 : 12,
                    index % 10 is 0 or 1 ? "D" : index % 10 == 2 ? "Y" : "M",
                    baseline.Id)));
            var missingBatch = FixtureBatch(missing, DateOnly.FromDateTime(DateTime.Today).AddDays(14), 12, "M", baseline.Id);
            context.Batches.Add(missingBatch);
            context.SaveChanges();
            var task = new ProductTask { ProductId = missing.Id, Status = "open", HighestStage = "discount_50" };
            context.Tasks.Add(task);
            context.SaveChanges();
            context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = missing.Id, BatchId = missingBatch.Id, Stage = "discount_50" });
            context.SaveChanges();

            var workbookPath = Path.Combine(root, "S22-T02-请导入此文件.xlsx");
            S8T03ImportPerformanceTests.WriteWorkbook(workbookPath, products: 12, batchesPerProduct: 1, seed: false);
            foreach (var category in new[] { "宠物", "日用", "美妆", "家居", "香氛香水", "文具", "潮流玩具", "应季搭配", "赠品小样" })
                ReplaceWorksheetText(workbookPath, category, "食品");
            Assert.True(File.Exists(workbookPath));
            Assert.Equal(12, context.Products.Count());
            Assert.Single(context.Tasks.Where(value => value.Status == "open"));
            context.Dispose();

            if (ownsRoot)
            {
                var coordinator = new DataImportCoordinator(
                    () => DatabaseInitializer.CreateContext(databasePath),
                    Path.Combine(root, "backups", "pre-import"));
                var preview = coordinator.Parse(workbookPath);
                var confirmation = coordinator.Confirm(preview.Identity);
                Assert.True(confirmation.CanConfirm);
                var result = coordinator.Execute(confirmation.Contract!, DateTime.UtcNow);
                Assert.True(result.Succeeded, result.Code);
                var summary = coordinator.Summarize(preview.Plan, result.ImportId!.Value);
                Assert.Equal(1, summary.NewProductCount);
                Assert.Equal(1, summary.NewBatchCount);
                Assert.Equal(1, summary.StockIncreaseCount);
                Assert.Equal(1, summary.StockDecreaseCount);
                Assert.Equal(1, summary.StockBecameZeroCount);
                Assert.Equal(1, summary.MissingBatchCount);
                Assert.Equal(1, summary.MissingOpenTaskBatchCount);
            }
        }
        finally
        {
            if (ownsRoot && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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

    private static Batch FixtureBatch(Product product, DateOnly expiry, int shelfLife, string unit, long importId) => new()
    {
        ProductId = product.Id, ProductionDate = new DateOnly(2026, 1, 1), ExpiryDate = expiry,
        ShelfLifeValue = shelfLife, ShelfLifeUnit = unit, CurrentArrivalQty = 1, MaxArrivalQty = 1, LastSeenImportId = importId
    };

    private static void ReplaceWorksheetText(string path, string before, string after)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        string xml;
        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) xml = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open(), new UTF8Encoding(false));
        writer.Write(xml.Replace($">{before}<", $">{after}<", StringComparison.Ordinal));
    }

    private static ExcelClassificationResult Classify(params ExcelRowDto[] rows) => new ExcelFileClassifier().Classify(new ExcelWorkbookDto("test.xlsx", string.Empty, "Sheet1", Array.Empty<string>(), rows));

    private static ExcelRowDto Row(int number, string code, string name, string barcode, string? production, string expiry, string stock) => new(
        number, "食品", code, barcode, name, production, expiry, "12", "M", "否", "1", stock);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

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
