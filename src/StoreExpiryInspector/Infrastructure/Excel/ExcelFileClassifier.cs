namespace StoreExpiryInspector.Infrastructure.Excel;

public sealed class ExcelFileClassifier
{

    private static readonly string[] BatchConflictFields =
    [
        "商品大类",
        "商品条码",
        "商品名称",
        "保质期",
        "保质期单位",
        "是否该做临期折扣",
        "该批次累计到货数量"
    ];

    public ExcelClassificationResult Classify(ExcelWorkbookDto workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var skippedRows = new List<ExcelSkippedRow>();
        var rowIssues = new List<ExcelRowIssue>();
        var validRows = new List<ExcelNormalizedRow>();
        var stockValuesByCode = new Dictionary<string, Dictionary<StockValueKey, List<int>>>(StringComparer.Ordinal);
        var acceptedRowCount = 0;

        foreach (var row in workbook.Rows)
        {
            if (!ProductCategoryScopes.IsKnown(row.ProductCategory))
            {
                skippedRows.Add(new ExcelSkippedRow(row.ExcelRowNumber, row.ProductCategory));
                rowIssues.Add(new ExcelRowIssue(
                    row.ExcelRowNumber,
                    "unsupported_product_category",
                    "商品大类",
                    "商品大类规则未覆盖，未导入该商品。"));
                continue;
            }

            acceptedRowCount++;
            var issues = ValidateRow(row, out var normalizedProductCode, out var normalizedRow);
            rowIssues.AddRange(issues);

            if (normalizedProductCode is not null)
            {
                AddStockValue(
                    stockValuesByCode,
                    normalizedProductCode,
                    NormalizeStock(row.StoreStockQuantity),
                    row.ExcelRowNumber);
            }

            if (issues.Count == 0)
            {
                validRows.Add(normalizedRow!);
            }
        }

        var duplicateRows = new List<ExcelDuplicateRow>();
        var signatureGroups = new List<SignatureGroup>();
        foreach (var group in validRows.GroupBy(static row => CompleteSignature.From(row)))
        {
            var rows = group.OrderBy(static row => row.ExcelRowNumber).ToArray();
            var representative = rows[0];
            for (var index = 1; index < rows.Length; index++)
            {
                duplicateRows.Add(new ExcelDuplicateRow(
                    representative.ExcelRowNumber,
                    rows[index].ExcelRowNumber));
            }

            signatureGroups.Add(new SignatureGroup(rows));
        }

        var rowsByBatchKey = new Dictionary<ExcelBatchKey, List<SignatureGroup>>();
        foreach (var group in signatureGroups)
        {
            var key = group.Representative.BatchKey;
            if (!rowsByBatchKey.TryGetValue(key, out var groups))
            {
                groups = [];
                rowsByBatchKey.Add(key, groups);
            }

            groups.Add(group);
        }

        var normalBatches = new List<ExcelNormalBatch>();
        var batchConflicts = new List<ExcelBatchConflict>();
        foreach (var pair in rowsByBatchKey)
        {
            var groups = pair.Value
                .OrderBy(static group => group.Representative.ExcelRowNumber)
                .ToArray();
            var differingFields = FindDifferingFields(groups.Select(static group => group.Representative));
            var sourceRowNumbers = groups
                .SelectMany(static group => group.Rows)
                .Select(static row => row.ExcelRowNumber)
                .OrderBy(static rowNumber => rowNumber)
                .ToArray();

            if (differingFields.Count == 0)
            {
                var representative = groups[0].Representative with { StoreStockQuantity = null };
                normalBatches.Add(new ExcelNormalBatch(
                    pair.Key,
                    representative.ExcelRowNumber,
                    representative,
                    Array.AsReadOnly(sourceRowNumbers)));
            }
            else
            {
                batchConflicts.Add(new ExcelBatchConflict(
                    pair.Key,
                    Array.AsReadOnly(sourceRowNumbers),
                    Array.AsReadOnly(differingFields.ToArray())));
            }
        }

        var productStocks = BuildProductStocks(stockValuesByCode);
        return new ExcelClassificationResult(
            workbook.Rows.Count,
            acceptedRowCount,
            ReadOnly(skippedRows.OrderBy(static row => row.ExcelRowNumber)),
            ReadOnly(rowIssues
                .OrderBy(static issue => issue.ExcelRowNumber)
                .ThenBy(static issue => IssueOrder(issue.Code))),
            rowsByBatchKey.Count,
            ReadOnly(normalBatches
                .OrderBy(static batch => batch.BatchKey.ProductCode, StringComparer.Ordinal)
                .ThenBy(static batch => batch.BatchKey.ProductionDate ?? DateOnly.MinValue)
                .ThenBy(static batch => batch.BatchKey.ExpiryDate)),
            ReadOnly(duplicateRows
                .OrderBy(static duplicate => duplicate.RepresentativeRowNumber)
                .ThenBy(static duplicate => duplicate.DuplicateRowNumber)),
            ReadOnly(batchConflicts
                .OrderBy(static conflict => conflict.BatchKey.ProductCode, StringComparer.Ordinal)
                .ThenBy(static conflict => conflict.BatchKey.ProductionDate ?? DateOnly.MinValue)
                .ThenBy(static conflict => conflict.BatchKey.ExpiryDate)),
            productStocks);
    }

    private static List<ExcelRowIssue> ValidateRow(
        ExcelRowDto row,
        out string? normalizedProductCode,
        out ExcelNormalizedRow? normalizedRow)
    {
        var issues = new List<ExcelRowIssue>();
        normalizedProductCode = Trim(row.ProductCode);
        if (string.IsNullOrWhiteSpace(normalizedProductCode))
        {
            normalizedProductCode = null;
            AddIssue(issues, row, "missing_product_code", "商品编码", "商品编码为空。");
        }

        var normalizedExpiry = Trim(row.ExpiryDate);
        DateOnly expiryDate = default;
        if (string.IsNullOrWhiteSpace(normalizedExpiry))
        {
            AddIssue(issues, row, "missing_expiry_date", "有效日期", "有效日期为空。");
        }
        else if (!ExcelDateParser.TryParse(normalizedExpiry, out expiryDate))
        {
            AddIssue(issues, row, "invalid_expiry_date", "有效日期", "有效日期无法按批准格式解析。");
        }

        var normalizedProduction = Trim(row.ProductionDate);
        DateOnly? productionDate = null;
        if (!string.IsNullOrWhiteSpace(normalizedProduction))
        {
            if (ExcelDateParser.TryParse(normalizedProduction, out var parsedProductionDate))
            {
                productionDate = parsedProductionDate;
            }
            else
            {
                AddIssue(issues, row, "invalid_production_date", "生产日期", "生产日期无法按批准格式解析。");
            }
        }

        var normalizedShelfLife = Trim(row.ShelfLife);
        if (string.IsNullOrWhiteSpace(normalizedShelfLife))
        {
            AddIssue(issues, row, "missing_shelf_life", "保质期", "保质期为空。");
        }

        var normalizedShelfLifeUnit = Trim(row.ShelfLifeUnit);
        if (string.IsNullOrWhiteSpace(normalizedShelfLifeUnit))
        {
            AddIssue(issues, row, "missing_shelf_life_unit", "保质期单位", "保质期单位为空。");
        }
        else if (normalizedShelfLifeUnit is not ("M" or "D" or "Y"))
        {
            AddIssue(issues, row, "invalid_shelf_life_unit", "保质期单位", "保质期单位不是 M、D 或 Y。");
        }

        normalizedRow = issues.Count == 0
            ? new ExcelNormalizedRow(
                row.ExcelRowNumber,
                row.ProductCategory!.Trim(),
                normalizedProductCode!,
                Trim(row.ProductBarcode),
                Trim(row.ProductName),
                productionDate,
                expiryDate,
                normalizedShelfLife!,
                normalizedShelfLifeUnit!,
                Trim(row.IsNearExpiryDiscountRequired),
                Trim(row.CumulativeArrivalQuantity),
                Trim(row.StoreStockQuantity))
            : null;

        return issues;
    }

    private static void AddStockValue(
        Dictionary<string, Dictionary<StockValueKey, List<int>>> valuesByCode,
        string productCode,
        string? stockValue,
        int rowNumber)
    {
        if (!valuesByCode.TryGetValue(productCode, out var values))
        {
            values = [];
            valuesByCode.Add(productCode, values);
        }

        var key = new StockValueKey(stockValue);
        if (!values.TryGetValue(key, out var rowNumbers))
        {
            rowNumbers = [];
            values.Add(key, rowNumbers);
        }

        rowNumbers.Add(rowNumber);
    }

    private static IReadOnlyList<ExcelProductStock> BuildProductStocks(
        Dictionary<string, Dictionary<StockValueKey, List<int>>> valuesByCode)
    {
        var stocks = new List<ExcelProductStock>(valuesByCode.Count);
        foreach (var pair in valuesByCode.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var values = pair.Value
                .Select(static value => new ExcelStockValue(
                    value.Key.Value,
                    Array.AsReadOnly(value.Value.OrderBy(static rowNumber => rowNumber).ToArray())))
                .OrderBy(static value => value.Value is not null)
                .ThenBy(static value => value.Value, StringComparer.Ordinal)
                .ToArray();
            var isConflict = values.Length > 1;
            stocks.Add(new ExcelProductStock(
                pair.Key,
                isConflict,
                isConflict ? null : values[0].Value,
                Array.AsReadOnly(values)));
        }

        return Array.AsReadOnly(stocks.ToArray());
    }

    private static List<string> FindDifferingFields(IEnumerable<ExcelNormalizedRow> rows)
    {
        var values = rows.ToArray();
        var first = values[0];
        var differingFields = new List<string>();
        if (values.Any(row => !string.Equals(row.ProductCategory, first.ProductCategory, StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[0]);
        }

        if (values.Any(row => !string.Equals(row.ProductBarcode, first.ProductBarcode, StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[1]);
        }

        if (values.Any(row => !string.Equals(row.ProductName, first.ProductName, StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[2]);
        }

        if (values.Any(row => !string.Equals(row.ShelfLife, first.ShelfLife, StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[3]);
        }

        if (values.Any(row => !string.Equals(row.ShelfLifeUnit, first.ShelfLifeUnit, StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[4]);
        }

        if (values.Any(row => !string.Equals(
                row.IsNearExpiryDiscountRequired,
                first.IsNearExpiryDiscountRequired,
                StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[5]);
        }

        if (values.Any(row => !string.Equals(
                row.CumulativeArrivalQuantity,
                first.CumulativeArrivalQuantity,
                StringComparison.Ordinal)))
        {
            differingFields.Add(BatchConflictFields[6]);
        }

        return differingFields;
    }

    private static void AddIssue(
        ICollection<ExcelRowIssue> issues,
        ExcelRowDto row,
        string code,
        string fieldName,
        string summary) => issues.Add(new ExcelRowIssue(row.ExcelRowNumber, code, fieldName, summary));

    private static string? Trim(string? value) => value?.Trim();

    private static string? NormalizeStock(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int IssueOrder(string code) => code switch
    {
        "missing_product_code" => 0,
        "missing_expiry_date" => 1,
        "invalid_expiry_date" => 2,
        "invalid_production_date" => 3,
        "missing_shelf_life" => 4,
        "missing_shelf_life_unit" => 5,
        "invalid_shelf_life_unit" => 6,
        _ => int.MaxValue
    };

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());

    private readonly record struct StockValueKey(string? Value);

    private readonly record struct CompleteSignature(
        string ProductCategory,
        string ProductCode,
        string? ProductBarcode,
        string? ProductName,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate,
        string ShelfLife,
        string ShelfLifeUnit,
        string? IsNearExpiryDiscountRequired,
        string? CumulativeArrivalQuantity,
        string? StoreStockQuantity)
    {
        public static CompleteSignature From(ExcelNormalizedRow row) => new(
            row.ProductCategory,
            row.ProductCode,
            row.ProductBarcode,
            row.ProductName,
            row.ProductionDate,
            row.ExpiryDate,
            row.ShelfLife,
            row.ShelfLifeUnit,
            row.IsNearExpiryDiscountRequired,
            row.CumulativeArrivalQuantity,
            row.StoreStockQuantity);
    }

    private sealed record SignatureGroup(IReadOnlyList<ExcelNormalizedRow> Rows)
    {
        public ExcelNormalizedRow Representative => Rows[0];
    }
}
