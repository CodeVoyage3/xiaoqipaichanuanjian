using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S25T01GuiFixtureTests
{
    [Fact]
    public async Task SeedsTheRequestedTemporaryGuiFixture()
    {
        var root = Environment.GetEnvironmentVariable("S25_T01_GUI_ROOT");
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
            var import = new ImportRecord { SourceFileName = "s25-gui.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = "succeeded" };
            context.Imports.Add(import); context.SaveChanges();
            context.ScopeBaselines.AddRange(
                new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = DateOnly.FromDateTime(DateTime.Today), IsCompleted = true, CompletedAtUtc = DateTime.UtcNow },
                new ScopeBaseline { ScopeKey = "pet", PolicyCode = ExpiryPolicies.Pet, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = DateOnly.FromDateTime(DateTime.Today), IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
            for (var index = 1; index <= 55; index++)
            {
                var pet = index % 2 == 0;
                var product = new Product { ProductCode = $"S25-GUI-{index:D3}", CurrentName = $"S25 待排查商品 {index:D3}", CurrentBarcode = $"697439695{index:D4}", ExcelStockQty = 10, EffectiveStockQty = 10, EffectiveStockSource = "fixture", CategoryCode = pet ? "pet" : "food", PolicyCode = pet ? ExpiryPolicies.Pet : ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, LastSeenImportId = import.Id };
                var batch = new Batch { Product = product, ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(5 + index), ShelfLifeValue = 30, ShelfLifeUnit = "D", CurrentArrivalQty = 10, MaxArrivalQty = 10, TrackingStatus = "active", CurrentStage = index % 3 == 0 ? "withdraw" : "discount_50", AttentionVersion = 1, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
                var task = new ProductTask { Product = product, HighestStage = batch.CurrentStage, Status = "open" };
                task.Items.Add(new ProductTaskItem { Product = product, Batch = batch, Stage = batch.CurrentStage, AttentionVersion = batch.AttentionVersion });
                context.Tasks.Add(task);
            }
            context.SaveChanges();
            Assert.Equal(55, context.Tasks.Count(task => task.Status == "open"));
            Assert.Equal(2, context.ScopeBaselines.Count(scope => scope.IsCompleted));
            Assert.All(context.Products, product => Assert.Equal(10, product.EffectiveStockQty));
            Assert.All(context.Tasks.SelectMany(task => task.Items), item => Assert.Equal(item.Batch!.AttentionVersion, item.AttentionVersion));
            var completedTaskId = context.Tasks.Single(task => task.Product.ProductCode == "S25-GUI-001").Id;
            var planPath = Path.Combine(root, "S25-pending-plan.xlsx");
            var exported = new TodayInspectionPlanExportUseCase().Execute(context, new(planPath, context.Tasks.Select(task => task.Id).ToArray()));
            Assert.Equal(55, exported.TaskCount);
            Assert.All(new InspectionPlanDraftApplyUseCase().Preview(context, planPath).File.Rows, row => Assert.Empty(row.Errors));
            SetQuantity(planPath, "11");
            var confirmations = 0;
            InspectionResultImportSessionViewModel CreateSession() => new(
                path => { using var preview = DatabaseInitializer.CreateContext(databasePath); return new InspectionPlanDraftApplyUseCase().Preview(preview, path); },
                request => { using var apply = DatabaseInitializer.CreateContext(databasePath); return new InspectionPlanDraftApplyUseCase().Apply(apply, request); },
                request => { using var submit = DatabaseInitializer.CreateContext(databasePath); return new BulkInspectionSubmissionUseCase().Submit(submit, request); },
                _ => Task.CompletedTask,
                _ => { confirmations++; return true; },
                confirmSubmission: () => true);
            var session = CreateSession();
            await session.PreviewAsync(planPath);
            var pending = new PendingTasksViewModel(request =>
            {
                using var query = DatabaseInitializer.CreateContext(databasePath);
                return new InspectionTaskQuery().SearchOpenTasks(query, request);
            }, importSession: session);
            await pending.LoadAsync();
            pending.Items[0].IsSelected = true;
            pending.ClearSelectionCommand.Execute(null);
            Assert.Equal(0, pending.SelectedCount);
            Assert.All(pending.Items, item => Assert.False(item.IsSelected));
            Assert.True(pending.CanUseImport);

            var submitted = CreateSession();
            await submitted.PreviewAsync(planPath);
            var filled = Assert.Single(submitted.PreviewRows, row => row.TaskId == completedTaskId);
            Assert.Equal("11", filled.CheckedQtyText);
            Assert.Equal("可提交", filled.ResultText);
            submitted.InspectorName = "S25 GUI";
            await submitted.SubmitAsync();
            Assert.Equal(1, confirmations);
            using (var verify = DatabaseInitializer.CreateContext(databasePath)) Assert.Single(verify.Inspections);
            var reopened = CreateSession();
            await reopened.PreviewAsync(planPath);
            Assert.Empty(reopened.CompleteTaskIds);
            var closed = Assert.Single(reopened.PreviewRows, row => row.ProductName == filled.ProductName);
            Assert.Equal("状态变化", closed.ResultText);
            Assert.Contains("状态已经变化", closed.Reason);
            using (var verify = DatabaseInitializer.CreateContext(databasePath))
                Assert.DoesNotContain(completedTaskId, new InspectionPlanDraftApplyUseCase().Preview(verify, planPath).ApplicableTaskIds);
            reopened.InspectorName = "S25 GUI 重提";
            await reopened.SubmitAsync();
            Assert.Equal(1, confirmations);
            using (var verify = DatabaseInitializer.CreateContext(databasePath)) Assert.Single(verify.Inspections);
        }
        finally
        {
            if (ownsRoot && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void SetQuantity(string path, string value)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbook = document.WorkbookPart?.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
        var sheet = workbook.Sheets?.Elements<Sheet>().Single() ?? throw new InvalidOperationException("Worksheet is missing.");
        var worksheet = (WorksheetPart)document.WorkbookPart!.GetPartById(sheet.Id!);
        var worksheetData = worksheet.Worksheet ?? throw new InvalidOperationException("Worksheet is missing.");
        var cell = (worksheetData.GetFirstChild<SheetData>() ?? throw new InvalidOperationException("Rows are missing.")).Elements<Row>().Skip(1).First().Elements<Cell>().ElementAt(11);
        cell.DataType = CellValues.InlineString; cell.CellValue = null; cell.InlineString = new InlineString(new Text(value));
        worksheetData.Save();
    }
}
