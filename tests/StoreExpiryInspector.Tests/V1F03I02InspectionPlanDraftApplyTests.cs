using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F03I02InspectionPlanDraftApplyTests
{
    [Theory]
    [InlineData("O2")]
    [InlineData("P2")]
    [InlineData("Q2")]
    [InlineData("R2")]
    [InlineData("T2")]
    [InlineData("U2")]
    [InlineData("V2")]
    [InlineData("W2")]
    [InlineData("X2")]
    [InlineData("Y2")]
    public void HiddenSnapshotMutationMakesTaskInapplicable(string cellReference)
    {
        using var fixture = Fixture.Create();
        Mutate(fixture.Path, cellReference, cellReference == "S2" ? "2026-09-02T08:00:01.0000000Z" : "999");
        var preview = new InspectionPlanDraftApplyUseCase().Preview(fixture.Context, fixture.Path);
        Assert.False(preview.Tasks.Single().IsApplicable);
        Assert.Empty(fixture.Context.Drafts);
    }

    [Fact]
    public void ReconfirmationOnDeletedExcelRowStillBlocksPreviewWithoutWrites()
    {
        using var fixture = Fixture.Create(2);
        DeleteSecondRow(fixture.Path);
        fixture.Context.TaskItems.OrderBy(item => item.Id).Last().RequiresReconfirmation = true;
        fixture.Context.SaveChanges();
        var preview = new InspectionPlanDraftApplyUseCase().Preview(fixture.Context, fixture.Path);
        Assert.False(preview.Tasks.Single().IsApplicable);
        Assert.Empty(fixture.Context.Drafts); Assert.Empty(fixture.Context.Inspections);
    }

    [Fact]
    public void CompletedTaskAndInspectionMakePreviewInapplicableWithoutBusinessWrites()
    {
        using var fixture = Fixture.Create();
        fixture.Context.Tasks.Single().Status = "completed"; fixture.Context.Tasks.Single().ClosedAtUtc = DateTime.UtcNow; fixture.Context.SaveChanges();
        var before = (fixture.Context.Drafts.Count(), fixture.Context.Inspections.Count(), fixture.Context.TaskItems.Count(), fixture.Context.Batches.Count(), fixture.Context.Products.Count(), fixture.Context.Imports.Count());
        var preview = new InspectionPlanDraftApplyUseCase().Preview(fixture.Context, fixture.Path);
        Assert.False(preview.Tasks.Single().IsApplicable);
        Assert.Equal(before, (fixture.Context.Drafts.Count(), fixture.Context.Inspections.Count(), fixture.Context.TaskItems.Count(), fixture.Context.Batches.Count(), fixture.Context.Products.Count(), fixture.Context.Imports.Count()));
    }

    [Fact]
    public void ApplyRejectsInvalidInputsAndSecondReadConflictWritesNothing()
    {
        using var fixture = Fixture.Create(); var useCase = new InspectionPlanDraftApplyUseCase(); var preview = useCase.Preview(fixture.Context, fixture.Path); var taskId = preview.ApplicableTaskIds.Single(); var date = new DateOnly(2026, 9, 2); var utc = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => useCase.Apply(fixture.Context, new(preview, [], "x", date, date, utc)));
        Assert.Throws<ArgumentException>(() => useCase.Apply(fixture.Context, new(preview, [taskId, taskId], "x", date, date, utc)));
        Assert.Throws<ArgumentException>(() => useCase.Apply(fixture.Context, new(preview, [taskId], " ", date, date, utc)));
        Assert.Throws<ArgumentException>(() => useCase.Apply(fixture.Context, new(preview, [taskId], new string('x', 201), date, date, utc)));
        Assert.Throws<ArgumentException>(() => useCase.Apply(fixture.Context, new(preview, [taskId], "x", date.AddDays(1), date, utc)));
        Assert.Throws<ArgumentException>(() => useCase.Apply(fixture.Context, new(preview, [taskId], "x", date, date, DateTime.SpecifyKind(utc, DateTimeKind.Local))));
        fixture.Context.Batches.Single().AttentionVersion++; fixture.Context.SaveChanges();
        Assert.Throws<InvalidOperationException>(() => useCase.Apply(fixture.Context, new(preview, [taskId], "x", date, date, utc)));
        Assert.Empty(fixture.Context.Drafts); Assert.Empty(fixture.Context.Inspections);
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("inspection")]
    [InlineData("invalid-draft")]
    [InlineData("excluded")]
    [InlineData("unresolved")]
    [InlineData("baseline")]
    public void LifecycleDefencesMakePreviewInapplicable(string mutation)
    {
        using var fixture = Fixture.Create();
        switch (mutation)
        {
            case "stopped": fixture.Context.Batches.Single().TrackingStatus = "stopped"; break;
            case "inspection": fixture.Context.Inspections.Add(new Inspection { TaskId = fixture.Context.Tasks.Single().Id, ProductId = fixture.Context.Products.Single().Id, ProductCodeSnapshot = "x", InspectorName = "x", CheckDate = new(2026, 9, 2), SubmittedAtUtc = DateTime.UtcNow }); break;
            case "invalid-draft": fixture.Context.Drafts.Add(new InspectionDraft { TaskId = fixture.Context.Tasks.Single().Id, IsInvalid = true }); break;
            case "excluded": fixture.Context.Products.Single().ExpiryManagementStatus = ExpiryManagementStatus.Excluded; fixture.Context.Products.Single().PolicyCode = null; fixture.Context.Products.Single().PolicyVersion = null; break;
            case "unresolved": fixture.Context.Products.Single().ExpiryManagementStatus = ExpiryManagementStatus.Unresolved; fixture.Context.Products.Single().PolicyCode = null; fixture.Context.Products.Single().PolicyVersion = null; break;
            case "baseline": fixture.Context.ScopeBaselines.Single().IsCompleted = false; break;
        }
        fixture.Context.SaveChanges();
        Assert.False(new InspectionPlanDraftApplyUseCase().Preview(fixture.Context, fixture.Path).Tasks.Single().IsApplicable);
    }

    [Fact]
    public void BlankClearsIncludedItemAndDeletedRowLeavesExistingDraftUntouched()
    {
        using var fixture = Fixture.Create(2); var task = fixture.Context.Tasks.Single(); var items = fixture.Context.TaskItems.OrderBy(item => item.Id).ToArray(); var draft = new InspectionDraft { TaskId = task.Id, InspectorName = "x", CheckDate = new(2026, 9, 2), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        fixture.Context.Drafts.Add(draft); fixture.Context.SaveChanges(); fixture.Context.DraftItems.AddRange(new InspectionDraftItem { DraftId = draft.Id, TaskId = task.Id, TaskItemId = items[0].Id, CheckedQty = 5, ConfirmedAttentionVersion = 2 }, new InspectionDraftItem { DraftId = draft.Id, TaskId = task.Id, TaskItemId = items[1].Id, CheckedQty = 6, ConfirmedAttentionVersion = 2 }); fixture.Context.SaveChanges();
        DeleteSecondRow(fixture.Path); var useCase = new InspectionPlanDraftApplyUseCase(); var preview = useCase.Preview(fixture.Context, fixture.Path); useCase.Apply(fixture.Context, new(preview, preview.ApplicableTaskIds, "x", new(2026, 9, 2), new(2026, 9, 2), new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc)));
        Assert.Null(fixture.Context.DraftItems.Single(item => item.TaskItemId == items[0].Id).CheckedQty); Assert.Equal(6, fixture.Context.DraftItems.Single(item => item.TaskItemId == items[1].Id).CheckedQty);
    }

    [Fact]
    public void RepeatedApplyOfSamePreviewIsNoChange()
    {
        using var fixture = Fixture.Create(); var useCase = new InspectionPlanDraftApplyUseCase(); var preview = useCase.Preview(fixture.Context, fixture.Path); var request = new ApplyInspectionPlanDraftRequest(preview, preview.ApplicableTaskIds, "检查员", new(2026, 9, 2), new(2026, 9, 2), new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));
        Assert.True(useCase.Apply(fixture.Context, request).Changed);
        var replay = useCase.Apply(fixture.Context, request);
        Assert.False(replay.Changed); Assert.All(replay.Tasks, task => Assert.False(task.Changed));
    }
    [Theory]
    [InlineData("0", true)]
    [InlineData("7", true)]
    [InlineData("", true)]
    [InlineData("not-a-number", false)]
    [InlineData("1.5", false)]
    [InlineData("-1", false)]
    [InlineData("2147483648", false)]
    [InlineData("FORMULA", false)]
    public void ReaderQuantityVariantsArePreviewedAndOnlyValidRowsApply(string quantity, bool applicable)
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var now = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        var import = new ImportRecord { SourceFileName = "source.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = now, Status = "confirmed" };
        context.Imports.Add(import); context.SaveChanges();
        var product = new Product { ProductCode = "SKU-I02", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = 4 };
        context.Products.Add(product); context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = new DateOnly(2026, 9, 2), IsCompleted = true, CompletedAtUtc = now });
        var batch = new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 10, 1), CurrentArrivalQty = 4, MaxArrivalQty = 4, TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount50, AttentionVersion = 2 };
        context.Batches.Add(batch); context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Discount50, CreatedAtUtc = now, UpdatedAtUtc = now };
        context.Tasks.Add(task); context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = product.Id, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 2, CreatedAtUtc = now, UpdatedAtUtc = now });
        context.SaveChanges();
        var path = Path.Combine(Path.GetTempPath(), $"i02-{Guid.NewGuid():N}.xlsx");
        try
        {
            new TodayInspectionPlanExportUseCase().Execute(context, new(path, [task.Id]));
            using (var document = SpreadsheetDocument.Open(path, true))
            {
                var workbook = document.WorkbookPart!;
                var worksheet = workbook.WorksheetParts.Single().Worksheet ?? throw new InvalidOperationException();
                var sheetData = worksheet.GetFirstChild<SheetData>() ?? throw new InvalidOperationException();
                var strings = workbook.SharedStringTablePart ?? workbook.AddNewPart<SharedStringTablePart>();
                strings.SharedStringTable ??= new SharedStringTable();
                strings.SharedStringTable.AppendChild(new SharedStringItem(new Text("序号")));
                var header = sheetData.Elements<Row>().First().Elements<Cell>().First();
                header.DataType = CellValues.SharedString; header.CellValue = new CellValue("0"); header.InlineString = null;
                var row = sheetData.Elements<Row>().Skip(1).Single();
                var quantityCell = row.Elements<Cell>().Single(cell => cell.CellReference == "L2");
                if (quantity == "FORMULA") quantityCell.CellFormula = new CellFormula("1+1");
                else quantityCell.CellValue = new CellValue(quantity);
                strings.SharedStringTable.Save(); (workbook.Workbook ?? throw new InvalidOperationException()).Save(); worksheet.Save();
            }
            var useCase = new InspectionPlanDraftApplyUseCase();
            var preview = useCase.Preview(context, path);
            Assert.Equal(applicable, preview.ApplicableTaskIds.Contains(task.Id));
            Assert.Empty(context.Drafts);
            Assert.Equal(1, preview.Summary.TaskCount);
            Assert.Equal(applicable ? 0 : 1, preview.Summary.ErrorCount);
            if (!applicable)
            {
                Assert.NotEmpty(preview.File.Rows.Single().Errors);
                return;
            }
            var result = useCase.Apply(context, new(preview, [task.Id], "  检查员  ", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 2), now));
            Assert.True(result.Changed);
            Assert.Equal(string.IsNullOrEmpty(quantity) ? null : int.Parse(quantity), context.DraftItems.Single().CheckedQty);
            Assert.Equal("检查员", context.Drafts.Single().InspectorName);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Mutate(string path, string reference, string value)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var cell = document.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1).Single().Elements<Cell>().Single(cell => cell.CellReference == reference);
        cell.CellValue = null; cell.DataType = CellValues.InlineString; cell.InlineString = new InlineString(new Text(value)); document.WorkbookPart.Workbook.Save(); document.WorkbookPart.WorksheetParts.Single().Worksheet.Save();
    }

    private static void DeleteSecondRow(string path)
    {
        using var document = SpreadsheetDocument.Open(path, true); var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet; sheet.GetFirstChild<SheetData>()!.Elements<Row>().Last().Remove(); document.WorkbookPart.Workbook.Save(); sheet.Save();
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(SqliteTestDatabase database, StoreDbContext context, string path) { Database = database; Context = context; Path = path; }
        public SqliteTestDatabase Database { get; } public StoreDbContext Context { get; } public string Path { get; }
        public static Fixture Create(int items = 1)
        {
            var database = SqliteTestDatabase.Create(); var context = database.Open(); var now = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
            var import = new ImportRecord { SourceFileName = "source.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = now, Status = "confirmed" }; context.Imports.Add(import); context.SaveChanges();
            var product = new Product { ProductCode = "SKU-FIXTURE", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = 4 }; context.Products.Add(product); context.SaveChanges();
            context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = new(2026, 9, 2), IsCompleted = true, CompletedAtUtc = now });
            var task = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Discount50, CreatedAtUtc = now, UpdatedAtUtc = now }; context.Tasks.Add(task); context.SaveChanges();
            for (var i = 0; i < items; i++) { var batch = new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 10, 1).AddDays(i), CurrentArrivalQty = 4, MaxArrivalQty = 4, TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount50, AttentionVersion = 2 }; context.Batches.Add(batch); context.SaveChanges(); context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = product.Id, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 2, CreatedAtUtc = now, UpdatedAtUtc = now }); context.SaveChanges(); }
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"i02-{Guid.NewGuid():N}.xlsx"); new TodayInspectionPlanExportUseCase().Execute(context, new(path, [task.Id])); return new(database, context, path);
        }
        public void Dispose() { Context.Dispose(); Database.Dispose(); if (File.Exists(Path)) File.Delete(Path); }
    }
}
