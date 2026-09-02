using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record InspectionPlanRow(
    int RowNumber, long? TaskId, long? TaskItemId, long? ProductId, long? BatchId,
    int? AttentionVersion, DateTime? TaskUpdatedAtUtc, int? TaskItemCount,
    string? TrackingStatus, string? Stage, int? CurrentArrivalQty, int? MaxArrivalQty,
    int? EffectiveStockQty, int? CheckedQty, string? ProductCode, string? ProductName,
    string? BatchDisplay, IReadOnlyList<string> Errors,
    string? ProductBarcode = null, string? ProductionDate = null, string? ExpiryDate = null);

public sealed record InspectionPlanReadResult(IReadOnlyList<InspectionPlanRow> Rows)
{
    public int ErrorCount => Rows.Sum(row => row.Errors.Count);
}

public sealed class InspectionPlanResultReader
{
    private const string FormatVersion = "inspection_plan_v1";
    private static readonly string[] Headers =
    [
        "序号", "商品编码", "条码", "商品名称", "大类", "生产日期", "有效日期", "当前阶段", "当前批次累计到货", "历史累计到货最大值", "商品当前库存", "本次排查数量",
        "格式版本", "TaskId", "TaskItemId", "ProductId", "BatchId", "AttentionVersion", "Task更新时间UTC", "TaskItem总数", "Batch当前状态", "Stage快照", "当前批次累计到货快照", "历史累计到货最大值快照", "商品当前库存快照"
    ];

    public InspectionPlanReadResult Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException("Inspection plan must be an existing absolute .xlsx path.", path);
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Inspection plan must be a .xlsx file.", nameof(path));

        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook is required.");
        var workbook = workbookPart.Workbook ?? throw new InvalidDataException("Workbook is required.");
        var sheets = workbook.Sheets ?? throw new InvalidDataException("Workbook sheets are required.");
        var sheet = sheets.Elements<Sheet>().SingleOrDefault(s => s.Name?.Value == "今日排查计划")
            ?? throw new InvalidDataException("Worksheet 今日排查计划 is required.");
        var sheetId = sheet.Id?.Value ?? throw new InvalidDataException("Worksheet relationship is required.");
        var part = (WorksheetPart)workbookPart.GetPartById(sheetId);
        var worksheet = part.Worksheet ?? throw new InvalidDataException("Worksheet is required.");
        var rows = worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToArray() ?? [];
        if (rows.Length < 2) throw new InvalidDataException("Inspection plan contains no data rows.");
        ValidateHeaders(rows[0], workbookPart);
        var result = rows.Skip(1).Select(row => ParseRow(row, workbookPart)).ToArray();
        if (result.Any(row => row.Errors.Any(error => error.StartsWith("格式版本", StringComparison.Ordinal))))
            throw new InvalidDataException("Inspection plan format version is missing, old, or mixed; re-export it.");
        MarkDuplicates(result);
        return new(result.OrderBy(row => row.ProductCode, StringComparer.Ordinal).ThenBy(row => row.TaskId).ThenBy(row => row.BatchId).ThenBy(row => row.TaskItemId).ToArray());
    }

    private static void ValidateHeaders(Row row, WorkbookPart workbook)
    {
        var values = Cells(row, workbook);
        if (Headers.Where((header, index) => !string.Equals(values[index], header, StringComparison.Ordinal)).Any())
            throw new InvalidDataException("Inspection plan A:Y headers do not match inspection_plan_v1.");
    }

    private static void MarkDuplicates(IReadOnlyList<InspectionPlanRow> rows)
    {
        foreach (var duplicate in rows.Where(row => row.TaskItemId is > 0).GroupBy(row => row.TaskItemId).Where(group => group.Count() > 1))
            foreach (var row in duplicate) ((List<string>)row.Errors).Add("TaskItemId 在文件中重复。");
        foreach (var duplicate in rows.Where(row => row.BatchId is > 0).GroupBy(row => row.BatchId).Where(group => group.Count() > 1))
            foreach (var row in duplicate) ((List<string>)row.Errors).Add("BatchId 在文件中重复。");
    }

    private static InspectionPlanRow ParseRow(Row row, WorkbookPart workbook)
    {
        var values = Cells(row, workbook); var errors = new List<string>();
        if (!string.Equals(values[12], FormatVersion, StringComparison.Ordinal)) errors.Add("格式版本必须为 inspection_plan_v1。");
        var taskId = Positive(values[13], "TaskId", errors); var taskItemId = Positive(values[14], "TaskItemId", errors);
        var productId = Positive(values[15], "ProductId", errors); var batchId = Positive(values[16], "BatchId", errors);
        var version = NonNegative(values[17], "AttentionVersion", errors); var taskCount = NonNegative(values[19], "TaskItem总数", errors);
        var arrival = NonNegative(values[22], "当前批次累计到货快照", errors); var max = NonNegative(values[23], "历史累计到货最大值快照", errors);
        var stock = NonNegative(values[24], "商品当前库存快照", errors);
        DateTime? updated = null;
        if (!DateTime.TryParseExact(values[18], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) || parsed.Kind != DateTimeKind.Utc)
            errors.Add("Task更新时间UTC 必须为 UTC round-trip 值。"); else updated = parsed;
        if (string.IsNullOrEmpty(values[20])) errors.Add("Batch当前状态 缺失。");
        if (string.IsNullOrEmpty(values[21])) errors.Add("Stage快照 缺失。");
        var checkedQty = Quantity(values[11], row, errors);
        return new((int)(row.RowIndex?.Value ?? 0), taskId, taskItemId, productId, batchId, version, updated, taskCount, values[20], values[21], arrival, max, stock, checkedQty, values[1], values[3], values[6], errors,
            values[2], DisplayDate(values[5]), DisplayDate(values[6]));
    }

    private static string[] Cells(Row row, WorkbookPart? workbook)
    {
        var values = new string[25];
        foreach (var cell in row.Elements<Cell>())
        {
            var index = Column(cell.CellReference?.Value);
            if (index is < 0 or >= 25) continue;
            values[index] = cell.CellFormula is null ? Text(cell, workbook) : "#FORMULA#";
        }
        return values;
    }

    private static string Text(Cell cell, WorkbookPart? workbook)
    {
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.Text?.Text ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var index))
            return workbook!.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        return cell.CellValue?.Text ?? string.Empty;
    }
    private static int Column(string? reference) => string.IsNullOrEmpty(reference) ? -1 : reference.TakeWhile(char.IsLetter).Aggregate(0, (value, c) => value * 26 + char.ToUpperInvariant(c) - 'A' + 1) - 1;
    private static long? Positive(string value, string name, List<string> errors) => Parse(value, name, errors, true);
    private static int? NonNegative(string value, string name, List<string> errors)
    { var parsed = Parse(value, name, errors, false); return parsed is > int.MaxValue ? Add(name, errors) : parsed is null ? null : (int)parsed.Value; }
    private static long? Parse(string value, string name, List<string> errors, bool positive)
    { if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || (positive ? result <= 0 : result < 0)) { errors.Add($"{name} 非法。"); return null; } return result; }
    private static int? Add(string name, List<string> errors) { errors.Add($"{name} 超出 Int32 范围。"); return null; }
    private static int? Quantity(string value, Row row, List<string> errors)
    { if (string.IsNullOrEmpty(value)) return null; if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 0) errors.Add("本次排查数量必须是非负 Int32 整数。"); return errors.Any(e => e.StartsWith("本次排查数量", StringComparison.Ordinal)) ? null : quantity; }
    private static string? DisplayDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) return string.IsNullOrWhiteSpace(value) ? null : value;
        try { return DateOnly.FromDateTime(DateTime.FromOADate(serial)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        catch (ArgumentException) { return value; }
    }
}
