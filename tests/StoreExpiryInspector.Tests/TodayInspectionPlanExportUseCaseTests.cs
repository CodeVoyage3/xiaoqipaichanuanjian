using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class TodayInspectionPlanExportUseCaseTests
{
    [Fact]
    public void ExportsStableRowsAndCompletePrintableWorkbookWithoutWritingDatabaseFacts()
    {
        using var database = SqliteTestDatabase.Create();
        long firstTaskId;
        long secondTaskId;
        long unselectedTaskId;
        ExportExpected[] expectedRows;
        using (var seed = database.Open())
        {
            firstTaskId = AddTask(seed, "0002", "001234", "第二商品", "food", ExpiryPolicies.Food, 5, new DateOnly(2026, 10, 2), new DateOnly(2026, 12, 1));
            AddSecondItem(seed, firstTaskId, new DateOnly(2026, 10, 4));
            secondTaskId = AddTask(seed, "0001", "000987", "第一商品", "pet", ExpiryPolicies.Pet, 7, new DateOnly(2026, 10, 3));
            unselectedTaskId = AddTask(seed, "0003", "000333", "未选择", "food", ExpiryPolicies.Food, 9, new DateOnly(2026, 10, 5));
            SetStage(seed, firstTaskId, ExpiryStageCalculator.Expired);
            SetStage(seed, secondTaskId, ExpiryStageCalculator.Withdraw);
            expectedRows = (from item in seed.TaskItems
                            join task in seed.Tasks on item.TaskId equals task.Id
                            join batch in seed.Batches on item.BatchId equals batch.Id
                            join product in seed.Products on item.ProductId equals product.Id
                            where item.TaskId == firstTaskId || item.TaskId == secondTaskId
                            orderby product.ProductCode, task.Id, batch.Id, item.Id
                            select new ExportExpected(task.Id, item.Id, product.Id, batch.Id, item.AttentionVersion, task.UpdatedAtUtc, 0, batch.TrackingStatus, item.Stage, batch.CurrentArrivalQty, batch.MaxArrivalQty, product.EffectiveStockQty)).ToArray();
            expectedRows = expectedRows.Select(row => row with { TaskItemCount = expectedRows.Count(candidate => candidate.TaskId == row.TaskId) }).ToArray();
        }

        var output = Path.Combine(database.Directory, "today.xlsx");
        var reversedOutput = Path.Combine(database.Directory, "reversed.xlsx");
        using (var context = database.Open())
        {
            var before = Snapshot(context);
            var result = new TodayInspectionPlanExportUseCase().Execute(context, new(output, new[] { firstTaskId, secondTaskId }));
            Assert.Equal(2, result.TaskCount);
            Assert.Equal(3, result.RowCount);
            Assert.Equal(before, Snapshot(context));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using (var reverseContext = database.Open())
        {
            new TodayInspectionPlanExportUseCase().Execute(reverseContext, new(reversedOutput, new[] { secondTaskId, firstTaskId }));
        }

        using var document = SpreadsheetDocument.Open(output, false);
        var workbook = document.WorkbookPart?.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
        var sheets = workbook.Sheets ?? throw new InvalidOperationException("Workbook sheets are missing.");
        var sheet = Assert.Single(sheets.Elements<Sheet>());
        Assert.Equal("今日排查计划", sheet.Name!.Value);
        var worksheet = ((WorksheetPart)document.WorkbookPart!.GetPartById(sheet.Id!)).Worksheet ?? throw new InvalidOperationException("Worksheet is missing.");
        var views = worksheet.GetFirstChild<SheetViews>() ?? throw new InvalidOperationException("Sheet views are missing.");
        Assert.NotNull(views.Elements<SheetView>().Single().GetFirstChild<Pane>());
        Assert.Equal("A1:L4", worksheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
        Assert.Equal(OrientationValues.Landscape, worksheet.GetFirstChild<PageSetup>()!.Orientation!.Value);
        Assert.Equal((uint)1, worksheet.GetFirstChild<PageSetup>()!.FitToWidth!.Value);
        Assert.True(worksheet.GetFirstChild<SheetProperties>()!.GetFirstChild<PageSetupProperties>()!.FitToPage!.Value);
        Assert.Contains(worksheet.GetFirstChild<Columns>()!.Elements<Column>(), column => column.Min?.Value == 13U && column.Max?.Value == 25U && column.Hidden?.Value == true);
        Assert.Contains(workbook.DefinedNames!.Elements<DefinedName>(), name => name.Name == "_xlnm.Print_Titles" && name.Text!.Contains("$1:$1"));
        var rows = worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToArray();
        using var reversedDocument = SpreadsheetDocument.Open(reversedOutput, false);
        var reversedSheet = ((WorksheetPart)reversedDocument.WorkbookPart!.GetPartById(reversedDocument.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>().Single().Id!)).Worksheet!;
        var reversedRows = reversedSheet.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1).Select(row => Text(row.Elements<Cell>().ElementAt(14))).ToArray();
        Assert.Equal(new[] { "序号", "商品编码", "条码", "商品名称", "大类", "生产日期", "有效日期", "当前阶段", "当前批次累计到货", "历史累计到货最大值", "总库存", "本次排查数量", "格式版本", "TaskId", "TaskItemId", "ProductId", "BatchId", "AttentionVersion", "Task更新时间UTC", "TaskItem总数", "Batch当前状态", "Stage快照", "当前批次累计到货快照", "历史累计到货最大值快照", "商品当前库存快照" }, rows[0].Elements<Cell>().Select(Text).ToArray());
        Assert.Equal(new[] { "0001", "0002", "0002" }, rows.Skip(1).Select(row => Text(row.Elements<Cell>().ElementAt(1))).ToArray());
        Assert.Equal(rows.Skip(1).Select(row => Text(row.Elements<Cell>().ElementAt(14))), reversedRows);
        Assert.All(rows.Skip(1), row => Assert.Equal(CellValues.InlineString, row.Elements<Cell>().ElementAt(1).DataType!.Value));
        Assert.Equal("宠物", Text(rows[1].Elements<Cell>().ElementAt(4)));
        Assert.Equal("收仓", Text(rows[1].Elements<Cell>().ElementAt(7)));
        Assert.Equal("过期", Text(rows[2].Elements<Cell>().ElementAt(7)));
        Assert.DoesNotContain(rows.Skip(1).Select(row => Text(row.Elements<Cell>().ElementAt(7))), value => value is "expired" or "withdraw");
        Assert.Empty(new InspectionPlanResultReader().Read(output).Rows.SelectMany(row => row.Errors));
        Assert.Null(rows[1].Elements<Cell>().ElementAt(5).CellValue);
        Assert.Equal(CellValues.Number, rows[1].Elements<Cell>().ElementAt(6).DataType!.Value);
        Assert.Equal((uint)1, rows[1].Elements<Cell>().ElementAt(6).StyleIndex!.Value);
        Assert.Null(rows[1].Elements<Cell>().ElementAt(11).CellValue);
        Assert.All(rows.Skip(1), row => Assert.Equal("inspection_plan_v1", Text(row.Elements<Cell>().ElementAt(12))));
        Assert.All(rows.Skip(1), row => Assert.Equal(CellValues.InlineString, row.Elements<Cell>().ElementAt(13).DataType!.Value));
        Assert.All(rows.Skip(1), row => Assert.Equal(CellValues.Number, row.Elements<Cell>().ElementAt(17).DataType!.Value));
        foreach (var (expected, row) in expectedRows.Zip(rows.Skip(1)))
        {
            var cells = row.Elements<Cell>().ToArray();
            Assert.Equal(expected.TaskId.ToString(), Text(cells[13]));
            Assert.Equal(expected.TaskItemId.ToString(), Text(cells[14]));
            Assert.Equal(expected.ProductId.ToString(), Text(cells[15]));
            Assert.Equal(expected.BatchId.ToString(), Text(cells[16]));
            Assert.Equal(expected.AttentionVersion.ToString(), Text(cells[17]));
            Assert.Equal(Utc.ToString("O"), Text(cells[18]));
            Assert.Equal(expected.TaskItemCount.ToString(), Text(cells[19]));
            Assert.Equal(expected.TrackingStatus, Text(cells[20]));
            Assert.Equal(expected.Stage, Text(cells[21]));
            Assert.Equal(expected.CurrentArrivalQty.ToString(), Text(cells[22]));
            Assert.Equal(expected.MaxArrivalQty.ToString(), Text(cells[23]));
            Assert.Equal(expected.EffectiveStockQty.ToString(), Text(cells[24]));
        }
        Assert.DoesNotContain(rows.Skip(1), row => Text(row.Elements<Cell>().ElementAt(13)) == unselectedTaskId.ToString());
    }

    [Fact]
    public void RejectsInvalidSelectionsIllegalTasksAndOutputPathsWithoutResidue()
    {
        using var database = SqliteTestDatabase.Create();
        long legalTaskId;
        long completedTaskId;
        long excludedTaskId;
        long noBaselineTaskId;
        long stoppedTaskId;
        long mismatchedVersionTaskId;
        using (var seed = database.Open())
        {
            legalTaskId = AddTask(seed, "LEGAL", "B", "合法", "food", ExpiryPolicies.Food, 1, new DateOnly(2026, 10, 1));
            completedTaskId = AddTask(seed, "DONE", "C", "完成", "food", ExpiryPolicies.Food, 1, new DateOnly(2026, 10, 2), status: "completed");
            excludedTaskId = AddTask(seed, "EXCLUDED", "D", "排除", "seasonal_assortment", null, 1, new DateOnly(2026, 10, 3), status: "open", managementStatus: ExpiryManagementStatus.Excluded);
            noBaselineTaskId = AddTask(seed, "NO-BASELINE", "G", "无基线", "beauty", ExpiryPolicies.GeneralLong, 1, new DateOnly(2026, 10, 4), baseline: false);
            stoppedTaskId = AddTask(seed, "STOPPED", "H", "已停止", "daily_use", ExpiryPolicies.GeneralLong, 1, new DateOnly(2026, 10, 5));
            seed.Batches.Single(value => value.ProductId == seed.Tasks.Single(task => task.Id == stoppedTaskId).ProductId).TrackingStatus = "stopped";
            mismatchedVersionTaskId = AddTask(seed, "VERSION", "I", "版本异常", "home", ExpiryPolicies.GeneralLong, 1, new DateOnly(2026, 10, 6));
            seed.Batches.Single(value => value.ProductId == seed.Tasks.Single(task => task.Id == mismatchedVersionTaskId).ProductId).AttentionVersion = 3;
            seed.SaveChanges();
        }

        using var context = database.Open();
        var useCase = new TodayInspectionPlanExportUseCase();
        var output = Path.Combine(database.Directory, "invalid.xlsx");
        Assert.ThrowsAny<ArgumentException>(() => useCase.Execute(context, new(output, Array.Empty<long>())));
        Assert.ThrowsAny<ArgumentException>(() => useCase.Execute(context, new(output, new[] { legalTaskId, legalTaskId })));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => useCase.Execute(context, new(output, new[] { 0L })));
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(context, new(output, new[] { 99999L })));
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(context, new(output, new[] { completedTaskId })));
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(context, new(output, new[] { excludedTaskId })));
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(context, new(output, new[] { noBaselineTaskId })));
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(context, new(output, new[] { stoppedTaskId })));
        Assert.Throws<InvalidOperationException>(() => useCase.Execute(context, new(output, new[] { mismatchedVersionTaskId })));
        Assert.Throws<ArgumentException>(() => useCase.Execute(context, new("relative.xlsx", new[] { legalTaskId })));
        Assert.Throws<DirectoryNotFoundException>(() => useCase.Execute(context, new(Path.Combine(database.Directory, "missing", "x.xlsx"), new[] { legalTaskId })));
        File.WriteAllText(output, "keep");
        Assert.Throws<IOException>(() => useCase.Execute(context, new(output, new[] { legalTaskId })));
        Assert.Equal("keep", File.ReadAllText(output));
        Assert.Empty(Directory.GetFiles(database.Directory, ".invalid.xlsx.*.tmp.xlsx"));
    }

    [Fact]
    public void AllExportSkipsIneligibleTasksButRejectsBrokenEligibleTaskContract()
    {
        using var database = SqliteTestDatabase.Create();
        long legalTaskId;
        long malformedTaskId;
        using (var seed = database.Open())
        {
            AddTask(seed, "EXCLUDED", "E", "排除", "gift_sample", null, 1, new DateOnly(2026, 10, 1), managementStatus: ExpiryManagementStatus.Excluded);
            AddTask(seed, "UNRESOLVED", "U", "待处理", "beauty", null, 1, new DateOnly(2026, 10, 1), managementStatus: ExpiryManagementStatus.Unresolved);
            legalTaskId = AddTask(seed, "LEGAL", "L", "合法", "food", ExpiryPolicies.Food, 1, new DateOnly(2026, 10, 1));
        }

        var allOutput = Path.Combine(database.Directory, "all.xlsx");
        using (var allContext = database.Open())
        {
            var result = new TodayInspectionPlanExportUseCase().Execute(allContext, new(allOutput));
            Assert.Equal(1, result.TaskCount);
            Assert.Equal(1, result.RowCount);
        }
        using (var seed = database.Open())
        {
            malformedTaskId = AddTask(seed, "BROKEN", "F", "错误", "food", ExpiryPolicies.Food, 1, new DateOnly(2026, 10, 2));
            var item = seed.TaskItems.Single(value => value.TaskId == malformedTaskId);
            item.Stage = ExpiryStageCalculator.Withdraw;
            seed.SaveChanges();
        }

        var output = Path.Combine(database.Directory, "broken.xlsx");
        using var context = database.Open();
        Assert.Throws<InvalidOperationException>(() => new TodayInspectionPlanExportUseCase().Execute(context, new(output)));
        Assert.False(File.Exists(output));
    }

    private static long AddTask(StoreDbContext context, string code, string barcode, string name, string category, string? policy, int stock, DateOnly expiry, DateOnly? production = null, string status = "open", ExpiryManagementStatus managementStatus = ExpiryManagementStatus.Managed, bool baseline = true)
    {
        var import = new ImportRecord { SourceFileName = $"{code}.xlsx", SourceFileSha256 = new string('a', 64), Status = ImportStatuses.Succeeded, ConfirmedAtUtc = Utc };
        context.Imports.Add(import);
        context.SaveChanges();
        var product = new Product { ProductCode = code, CurrentBarcode = barcode, CurrentName = name, CategoryCode = category, PolicyCode = policy, PolicyVersion = policy is null ? null : 1, ExpiryManagementStatus = managementStatus, ExcelStockQty = stock, EffectiveStockQty = stock, EffectiveStockSource = "excel" };
        context.Products.Add(product);
        context.SaveChanges();
        if (baseline && policy is not null && !context.ScopeBaselines.Any(value => value.ScopeKey == category && value.PolicyCode == policy))
        {
            context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = category, PolicyCode = policy, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = DateOnly.FromDateTime(Utc), IsCompleted = true, CompletedAtUtc = Utc });
            context.SaveChanges();
        }

        var batch = new Batch { ProductId = product.Id, ProductionDate = production, ExpiryDate = expiry, ShelfLifeValue = 365, ShelfLifeUnit = "D", CurrentArrivalQty = 3, MaxArrivalQty = 5, CurrentStage = ExpiryStageCalculator.Discount50, AttentionVersion = 2, HandledAttentionVersion = 0 };
        context.Batches.Add(batch);
        context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id, Status = status, HighestStage = ExpiryStageCalculator.Discount50, UpdatedAtUtc = Utc, ClosedAtUtc = status == "open" ? null : Utc };
        context.Tasks.Add(task);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = product.Id, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 2 });
        context.SaveChanges();
        return task.Id;
    }

    private static void AddSecondItem(StoreDbContext context, long taskId, DateOnly expiry)
    {
        var task = context.Tasks.Single(value => value.Id == taskId);
        var batch = new Batch { ProductId = task.ProductId, ExpiryDate = expiry, ShelfLifeValue = 365, ShelfLifeUnit = "D", CurrentArrivalQty = 4, MaxArrivalQty = 6, CurrentStage = ExpiryStageCalculator.Discount50, AttentionVersion = 2 };
        context.Batches.Add(batch);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = task.ProductId, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 2 });
        context.SaveChanges();
    }

    private static void SetStage(StoreDbContext context, long taskId, string stage)
    {
        context.Tasks.Single(value => value.Id == taskId).HighestStage = stage;
        foreach (var item in context.TaskItems.Where(value => value.TaskId == taskId))
        {
            item.Stage = stage;
            context.Batches.Single(batch => batch.Id == item.BatchId).CurrentStage = stage;
        }
        context.SaveChanges();
    }

    private static string Text(Cell cell) => cell.InlineString?.Text?.Text ?? cell.CellValue?.Text ?? string.Empty;
    private static string Snapshot(StoreDbContext context) => string.Join("|", context.Products.Count(), context.Batches.Count(), context.Tasks.Count(), context.TaskItems.Count(), context.Drafts.Count(), context.Inspections.Count(), context.LifecycleEvents.Count());
    private static readonly DateTime Utc = new(2026, 9, 2, 1, 2, 3, DateTimeKind.Utc);
    private sealed record ExportExpected(long TaskId, long TaskItemId, long ProductId, long BatchId, int AttentionVersion, DateTime TaskUpdatedAtUtc, int TaskItemCount, string TrackingStatus, string Stage, int CurrentArrivalQty, int MaxArrivalQty, int EffectiveStockQty);
}
