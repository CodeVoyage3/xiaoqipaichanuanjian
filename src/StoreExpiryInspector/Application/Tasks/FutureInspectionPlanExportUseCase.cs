using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

/// <summary>A work-arrangement workbook, deliberately not a formal inspection-result template.</summary>
public sealed class FutureInspectionPlanExportUseCase
{
    public TodayInspectionPlanExportResult Execute(StoreDbContext context, string outputPath, DateOnly targetDate, IReadOnlyCollection<long> productIds, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(productIds);
        if (targetDate <= businessDate) throw new ArgumentException("Only future work arrangements may use this export.", nameof(targetDate));
        if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathFullyQualified(outputPath) || !string.Equals(Path.GetExtension(outputPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OutputPath must be an absolute .xlsx path.", nameof(outputPath));
        if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw new DirectoryNotFoundException();
        if (File.Exists(outputPath)) throw new IOException("The output file already exists and will not be overwritten.");
        if (productIds.Count == 0 || productIds.Any(id => id <= 0)) throw new ArgumentException("Select valid products.", nameof(productIds));
        var selected = productIds.ToHashSet();
        var rows = new InspectionPlanQuery().Search(context, targetDate, new(PageSize: int.MaxValue)).Items.Where(item => selected.Contains(item.ProductId)).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("No future plan rows remain available.");
        var temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp.xlsx");
        try
        {
            using (var document = SpreadsheetDocument.Create(temporaryPath, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
                var sheet = workbook.AddNewPart<WorksheetPart>();
                var data = new SheetData();
                data.Append(TextRow("未来排查工作安排（不能导入正式排查结果）", targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                data.Append(TextRow("商品编码", "条码", "商品名称", "大类", "预计阶段", "总库存", "预计排查日期"));
                foreach (var item in rows) data.Append(TextRow(item.ProductCode, item.ProductBarcode ?? "", item.ProductName ?? "", item.CategoryName,
                    ExpiryStageCalculator.ToDisplay(item.HighestStage), item.EffectiveStockQty.ToString(CultureInfo.InvariantCulture), targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                sheet.Worksheet = new Worksheet(new Columns(Enumerable.Range(1, 7).Select(index => new Column { Min = (uint)index, Max = (uint)index, Width = index == 3 ? 36 : 22, CustomWidth = true })), data);
                workbook.Workbook.Append(new Sheets(new Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "未来排查工作安排" }));
                workbook.Workbook.Save();
            }
            File.Move(temporaryPath, outputPath, overwrite: false);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        return new(outputPath, rows.Length, rows.Length);
    }

    private static Row TextRow(params string[] values) => new(values.Select(value => new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) }));
}
