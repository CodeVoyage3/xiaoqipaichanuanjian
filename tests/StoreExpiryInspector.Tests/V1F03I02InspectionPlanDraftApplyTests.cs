using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F03I02InspectionPlanDraftApplyTests
{
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
}
