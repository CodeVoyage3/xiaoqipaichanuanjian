using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using StoreExpiryInspector.UI;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record TodayInspectionPlanExportRequest(string OutputPath, IReadOnlyCollection<long>? TaskIds = null);

public sealed record TodayInspectionPlanExportResult(string OutputPath, int TaskCount, int RowCount);

public sealed class TodayInspectionPlanExportUseCase
{
    private const string FormatVersion = "inspection_plan_v1";
    private static readonly string[] Headers =
    [
        "序号", "商品编码", "条码", "商品名称", "大类", "生产日期", "有效日期", "当前阶段", "当前批次累计到货", "历史累计到货最大值", "总库存", "本次排查数量",
        "格式版本", "TaskId", "TaskItemId", "ProductId", "BatchId", "AttentionVersion", "Task更新时间UTC", "TaskItem总数", "Batch当前状态", "Stage快照", "当前批次累计到货快照", "历史累计到货最大值快照", "商品当前库存快照"
    ];

    public TodayInspectionPlanExportResult Execute(StoreDbContext context, TodayInspectionPlanExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var taskIds = ValidateRequest(request);
        var rows = QueryRows(context, taskIds);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No legal open tasks are available for export.");
        }

        var temporaryPath = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, $".{Path.GetFileName(request.OutputPath)}.{Guid.NewGuid():N}.tmp.xlsx");
        try
        {
            WriteWorkbook(temporaryPath, rows);
            File.Move(temporaryPath, request.OutputPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new(request.OutputPath, rows.Select(row => row.TaskId).Distinct().Count(), rows.Count);
    }

    private static HashSet<long>? ValidateRequest(TodayInspectionPlanExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputPath) || !Path.IsPathFullyQualified(request.OutputPath) ||
            !string.Equals(Path.GetExtension(request.OutputPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("OutputPath must be an absolute .xlsx path.", nameof(request));
        }

        var directory = Path.GetDirectoryName(request.OutputPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The output directory must already exist.");
        }

        if (File.Exists(request.OutputPath))
        {
            throw new IOException("The output file already exists and will not be overwritten.");
        }

        if (request.TaskIds is null)
        {
            return null;
        }

        if (request.TaskIds.Count == 0 || request.TaskIds.Any(id => id <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "TaskIds must contain unique positive IDs.");
        }

        var taskIds = request.TaskIds.ToHashSet();
        if (taskIds.Count != request.TaskIds.Count)
        {
            throw new ArgumentException("TaskIds must not contain duplicates.", nameof(request));
        }

        return taskIds;
    }

    private static IReadOnlyList<PlanRow> QueryRows(StoreDbContext context, HashSet<long>? requestedTaskIds)
    {
        var tasks = context.Tasks.AsNoTracking()
            .Where(task => requestedTaskIds == null || requestedTaskIds.Contains(task.Id))
            .Select(task => new TaskRow(task.Id, task.ProductId, task.Status, task.UpdatedAtUtc))
            .ToArray();
        if (requestedTaskIds is not null && tasks.Length != requestedTaskIds.Count)
        {
            throw new InvalidOperationException("Every selected task must exist.");
        }

        if (requestedTaskIds is null)
        {
            tasks = tasks.Where(task => task.Status == "open").ToArray();
        }
        else if (tasks.Any(task => task.Status != "open"))
        {
            throw new InvalidOperationException("Every selected task must be open.");
        }

        var products = context.Products.AsNoTracking()
            .Select(product => new ProductRow(product.Id, product.ProductCode, product.CurrentName, product.CurrentBarcode, product.CategoryCode, product.PolicyCode, product.PolicyVersion, product.ExpiryManagementStatus, product.EffectiveStockQty))
            .ToDictionary(product => product.Id);
        var baselines = context.ScopeBaselines.AsNoTracking()
            .Where(baseline => baseline.IsCompleted)
            .Select(baseline => new BaselineRow(baseline.ScopeKey, baseline.PolicyCode, baseline.PolicyVersion))
            .ToHashSet();
        var eligibleTasks = new List<TaskRow>();
        foreach (var task in tasks)
        {
            if (!products.TryGetValue(task.ProductId, out var product))
            {
                throw new InvalidOperationException("Task product relationship is invalid.");
            }

            var eligible = product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                product.PolicyVersion == ExpiryPolicies.Version1 &&
                product.PolicyCode is ExpiryPolicies.Food or ExpiryPolicies.Pet or ExpiryPolicies.GeneralLong &&
                baselines.Contains(new(product.CategoryCode, product.PolicyCode, product.PolicyVersion.Value));
            if (!eligible)
            {
                if (requestedTaskIds is not null)
                {
                    throw new InvalidOperationException("Every selected task must be legally exportable.");
                }

                continue;
            }

            if (product.EffectiveStockQty < 0)
            {
                throw new InvalidOperationException("Product stock contract is invalid.");
            }

            _ = ProductCategoryScopes.DisplayNameForCategoryCode(product.CategoryCode);
            eligibleTasks.Add(task);
        }

        if (eligibleTasks.Count == 0)
        {
            return Array.Empty<PlanRow>();
        }

        var taskIds = eligibleTasks.Select(task => task.Id).ToArray();
        var items = context.TaskItems.AsNoTracking()
            .Where(item => taskIds.Contains(item.TaskId))
            .Select(item => new ItemRow(item.Id, item.TaskId, item.ProductId, item.BatchId, item.Stage, item.AttentionVersion))
            .ToArray();
        var batches = context.Batches.AsNoTracking()
            .Select(batch => new BatchRow(batch.Id, batch.ProductId, batch.ProductionDate, batch.ExpiryDate, batch.CurrentArrivalQty, batch.MaxArrivalQty, batch.TrackingStatus, batch.CurrentStage, batch.AttentionVersion, batch.HandledAttentionVersion))
            .ToDictionary(batch => batch.Id);
        var itemCounts = items.GroupBy(item => item.TaskId).ToDictionary(group => group.Key, group => group.Count());
        var taskById = eligibleTasks.ToDictionary(task => task.Id);
        var result = new List<PlanRow>();
        foreach (var item in items)
        {
            var task = taskById[item.TaskId];
            if (!itemCounts.TryGetValue(task.Id, out var itemCount) || itemCount == 0 || item.ProductId != task.ProductId ||
                !products.TryGetValue(item.ProductId, out var product) || !batches.TryGetValue(item.BatchId, out var batch) ||
                batch.ProductId != item.ProductId || batch.TrackingStatus != "active" || item.AttentionVersion < 0 ||
                batch.AttentionVersion < 0 || batch.HandledAttentionVersion < 0 || batch.CurrentArrivalQty < 0 || batch.MaxArrivalQty < 0 ||
                item.AttentionVersion != batch.AttentionVersion || item.Stage != batch.CurrentStage || !IsTrackableStage(item.Stage))
            {
                throw new InvalidOperationException("Task item export contract is invalid.");
            }

            result.Add(new(task.Id, item.Id, product.Id, batch.Id, product.ProductCode, product.Barcode, product.ProductName,
                ProductCategoryScopes.DisplayNameForCategoryCode(product.CategoryCode), batch.ProductionDate, batch.ExpiryDate, item.Stage,
                batch.CurrentArrivalQty, batch.MaxArrivalQty, product.EffectiveStockQty, item.AttentionVersion, task.UpdatedAtUtc, itemCount, batch.TrackingStatus));
        }

        if (eligibleTasks.Any(task => !itemCounts.ContainsKey(task.Id)))
        {
            throw new InvalidOperationException("Every task must contain at least one item.");
        }

        return result.OrderBy(row => row.ProductCode, StringComparer.Ordinal).ThenBy(row => row.TaskId).ThenBy(row => row.BatchId).ThenBy(row => row.TaskItemId).ToArray();
    }

    private static bool IsTrackableStage(string stage)
    {
        try { return ExpiryStageCalculator.GetStagePriority(stage) > 0; }
        catch (ArgumentException) { return false; }
    }

    private static void WriteWorkbook(string path, IReadOnlyList<PlanRow> rows)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = new Stylesheet(
            new NumberingFormats(new NumberingFormat { NumberFormatId = 164U, FormatCode = "yyyy-mm-dd" }) { Count = 1U },
            new Fonts(new Font()) { Count = 1U }, new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2U },
            new Borders(new Border()) { Count = 1U }, new CellStyleFormats(new CellFormat()) { Count = 1U },
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 164U, ApplyNumberFormat = true }) { Count = 2U });
        var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        sheetData.Append(new Row(Headers.Select((header, index) => TextCell(index + 1, 1, header))));
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowIndex = (uint)(index + 2);
            sheetData.Append(new Row(
                NumberCell(1, rowIndex, index + 1), TextCell(2, rowIndex, row.ProductCode), TextCell(3, rowIndex, row.Barcode), TextCell(4, rowIndex, row.ProductName), TextCell(5, rowIndex, row.CategoryName),
                DateCell(6, rowIndex, row.ProductionDate), DateCell(7, rowIndex, row.ExpiryDate), TextCell(8, rowIndex, StageLabels.ToDisplay(row.Stage)), NumberCell(9, rowIndex, row.CurrentArrivalQty), NumberCell(10, rowIndex, row.MaxArrivalQty), NumberCell(11, rowIndex, row.EffectiveStockQty), BlankCell(12, rowIndex),
                TextCell(13, rowIndex, FormatVersion), TextCell(14, rowIndex, row.TaskId.ToString(CultureInfo.InvariantCulture)), TextCell(15, rowIndex, row.TaskItemId.ToString(CultureInfo.InvariantCulture)), TextCell(16, rowIndex, row.ProductId.ToString(CultureInfo.InvariantCulture)), TextCell(17, rowIndex, row.BatchId.ToString(CultureInfo.InvariantCulture)), NumberCell(18, rowIndex, row.AttentionVersion), TextCell(19, rowIndex, NormalizeUtc(row.TaskUpdatedAtUtc).ToString("O", CultureInfo.InvariantCulture)), NumberCell(20, rowIndex, row.TaskItemCount), TextCell(21, rowIndex, row.TrackingStatus), TextCell(22, rowIndex, row.Stage), NumberCell(23, rowIndex, row.CurrentArrivalQty), NumberCell(24, rowIndex, row.MaxArrivalQty), NumberCell(25, rowIndex, row.EffectiveStockQty)));
        }

        sheetPart.Worksheet = new Worksheet(
            new SheetProperties(new PageSetupProperties { FitToPage = true }),
            new SheetViews(new SheetView(new Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0U }),
            new Columns(Enumerable.Range(1, 12).Select(index => new Column { Min = (uint)index, Max = (uint)index, Width = index is 3 or 4 ? 20D : 14D, CustomWidth = true }).Append(new Column { Min = 13U, Max = 25U, Hidden = true })),
            sheetData,
            new AutoFilter { Reference = $"A1:L{rows.Count + 1}" },
            new PageMargins { Left = 0.25D, Right = 0.25D, Top = 0.5D, Bottom = 0.5D, Header = 0.2D, Footer = 0.2D },
            new PageSetup { Orientation = OrientationValues.Landscape, FitToWidth = 1U, FitToHeight = 0U });
        workbookPart.Workbook.Append(new Sheets(new Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1U, Name = "今日排查计划" }));
        workbookPart.Workbook.DefinedNames = new DefinedNames(new DefinedName { Name = "_xlnm.Print_Titles", LocalSheetId = 0U, Text = "'今日排查计划'!$1:$1" });
        workbookPart.Workbook.Save();
    }

    private static Cell TextCell(int column, uint row, string? value) => new() { CellReference = CellReference(column, row), DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? string.Empty)) };
    private static Cell NumberCell(int column, uint row, int value) => new() { CellReference = CellReference(column, row), DataType = CellValues.Number, CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)) };
    private static Cell BlankCell(int column, uint row) => new() { CellReference = CellReference(column, row) };
    private static Cell DateCell(int column, uint row, DateOnly? value) => value is null ? BlankCell(column, row) : new Cell { CellReference = CellReference(column, row), StyleIndex = 1U, DataType = CellValues.Number, CellValue = new CellValue((value.Value.DayNumber - new DateOnly(1899, 12, 30).DayNumber).ToString(CultureInfo.InvariantCulture)) };
    private static string CellReference(int column, uint row) => $"{(char)('A' + column - 1)}{row}";
    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch { DateTimeKind.Local => value.ToUniversalTime(), DateTimeKind.Utc => value, _ => DateTime.SpecifyKind(value, DateTimeKind.Utc) };

    private sealed record TaskRow(long Id, long ProductId, string Status, DateTime UpdatedAtUtc);
    private sealed record ProductRow(long Id, string ProductCode, string? ProductName, string? Barcode, string CategoryCode, string? PolicyCode, int? PolicyVersion, ExpiryManagementStatus ExpiryManagementStatus, int EffectiveStockQty);
    private sealed record BaselineRow(string ScopeKey, string PolicyCode, int PolicyVersion);
    private sealed record ItemRow(long Id, long TaskId, long ProductId, long BatchId, string Stage, int AttentionVersion);
    private sealed record BatchRow(long Id, long ProductId, DateOnly? ProductionDate, DateOnly ExpiryDate, int CurrentArrivalQty, int MaxArrivalQty, string TrackingStatus, string CurrentStage, int AttentionVersion, int HandledAttentionVersion);
    private sealed record PlanRow(long TaskId, long TaskItemId, long ProductId, long BatchId, string ProductCode, string? Barcode, string? ProductName, string CategoryName, DateOnly? ProductionDate, DateOnly ExpiryDate, string Stage, int CurrentArrivalQty, int MaxArrivalQty, int EffectiveStockQty, int AttentionVersion, DateTime TaskUpdatedAtUtc, int TaskItemCount, string TrackingStatus);
}
