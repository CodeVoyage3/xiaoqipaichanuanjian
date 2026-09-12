using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StoreExpiryInspector.Application.Tasks;

// Database ids are resolved from current facts later; the workbook carries only business fields.
public sealed record InspectionPlanRow(int RowNumber, long? TaskId, long? TaskItemId, long? ProductId, long? BatchId, int? AttentionVersion, DateTime? TaskUpdatedAtUtc, int? TaskItemCount, string? TrackingStatus, string? Stage, int? CurrentArrivalQty, int? MaxArrivalQty, int? EffectiveStockQty, int? CheckedQty, string? ProductCode, string? ProductName, string? BatchDisplay, IReadOnlyList<string> Errors, string? ProductBarcode = null, string? ProductionDate = null, string? ExpiryDate = null);
public sealed record InspectionPlanReadResult(IReadOnlyList<InspectionPlanRow> Rows) { public int ErrorCount => Rows.Sum(row => row.Errors.Count); }

public sealed class InspectionPlanResultReader
{
    private static readonly string[] Headers = ["序号", "商品编码", "条码", "商品名称", "大类", "生产日期", "有效日期", "当前阶段", "当前批次累计到货", "历史累计到货最大值", "总库存", "本次排查数量"];
    public InspectionPlanReadResult Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("Inspection plan must be an existing absolute .xlsx path.", path);
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Inspection plan must be a .xlsx file.", nameof(path));
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("Workbook is required.");
        var workbookDocument = workbook.Workbook ?? throw new InvalidDataException("Workbook is required.");
        var sheet = workbookDocument.Sheets?.Elements<Sheet>().SingleOrDefault(value => value.Name?.Value == "今日排查计划") ?? throw new InvalidDataException("Worksheet 今日排查计划 is required.");
        var worksheet = (WorksheetPart)workbook.GetPartById(sheet.Id?.Value ?? throw new InvalidDataException("Worksheet relationship is required."));
        var rows = (worksheet.Worksheet ?? throw new InvalidDataException("Worksheet is required.")).GetFirstChild<SheetData>()?.Elements<Row>().ToArray() ?? [];
        if (rows.Length < 2) throw new InvalidDataException("Inspection plan contains no data rows.");
        ValidateHeaders(rows[0], workbook);
        var result = rows.Skip(1).Select(row => ParseRow(row, workbook)).ToArray(); MarkDuplicates(result); return new(result);
    }
    private static void ValidateHeaders(Row row, WorkbookPart workbook)
    {
        var values = Cells(row, workbook);
        if (Headers.Where((header, index) => !string.Equals(values[index], header, StringComparison.Ordinal)).Any()) throw new InvalidDataException("Inspection plan A:L headers do not match the current format.");
    }
    private static InspectionPlanRow ParseRow(Row row, WorkbookPart workbook)
    {
        var values = Cells(row, workbook); var errors = new List<string>(); var quantity = Quantity(values[11], errors);
        var production = Date(values[5], "生产日期", true, errors); var expiry = Date(values[6], "有效日期", false, errors);
        if (string.IsNullOrWhiteSpace(values[1])) errors.Add("商品编码不能为空。");
        return new((int)(row.RowIndex?.Value ?? 0), null, null, null, null, null, null, null, null, values[7], Number(values[8]), Number(values[9]), Number(values[10]), quantity, values[1], values[3], expiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), errors, values[2], production?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), expiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
    private static void MarkDuplicates(IReadOnlyList<InspectionPlanRow> rows)
    {
        foreach (var group in rows.Where(row => row.CheckedQty is not null && row.Errors.Count == 0).GroupBy(row => $"{row.ProductCode}\u001f{row.ProductionDate}\u001f{row.ExpiryDate}").Where(group => group.Count() > 1)) foreach (var row in group) ((List<string>)row.Errors).Add("同一商品批次在文件中重复。");
    }
    private static string[] Cells(Row row, WorkbookPart workbook)
    {
        var values = new string[12]; foreach (var cell in row.Elements<Cell>()) { var index = Column(cell.CellReference?.Value); if (index is >= 0 and < 12) values[index] = cell.CellFormula is null ? Text(cell, workbook) : "#FORMULA#"; } return values;
    }
    private static int? Quantity(string value, List<string> errors) { if (string.IsNullOrWhiteSpace(value)) return null; if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 0) errors.Add("本次排查数量必须是 0 或正整数。"); return errors.Any(error => error.StartsWith("本次排查数量", StringComparison.Ordinal)) ? null : quantity; }
    private static DateOnly? Date(string value, string name, bool optional, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) { if (!optional) errors.Add($"{name}不能为空。"); return null; }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) { try { return DateOnly.FromDateTime(DateTime.FromOADate(serial)); } catch (ArgumentException) { } }
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date; errors.Add($"{name}格式不正确。"); return null;
    }
    private static int? Number(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static string Text(Cell cell, WorkbookPart workbook) => cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.Text?.Text ?? string.Empty : cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var index) ? workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText ?? string.Empty : cell.CellValue?.Text ?? string.Empty;
    private static int Column(string? reference) => string.IsNullOrEmpty(reference) ? -1 : reference.TakeWhile(char.IsLetter).Aggregate(0, (value, c) => value * 26 + char.ToUpperInvariant(c) - 'A' + 1) - 1;
}
