namespace StoreExpiryInspector.Infrastructure.Excel;

public readonly record struct ExcelBatchKey(
    string ProductCode,
    DateOnly? ProductionDate,
    DateOnly ExpiryDate);

public sealed record ExcelNormalizedRow(
    int ExcelRowNumber,
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
    public ExcelBatchKey BatchKey => new(ProductCode, ProductionDate, ExpiryDate);
}

public sealed class ExcelRowIssue
{
    public ExcelRowIssue(int excelRowNumber, string code, string fieldName, string summary)
    {
        ExcelRowNumber = excelRowNumber;
        Code = code;
        FieldName = fieldName;
        Summary = summary;
    }

    public int ExcelRowNumber { get; }

    public string Code { get; }

    public string FieldName { get; }

    public string Summary { get; }
}

public sealed class ExcelSkippedRow
{
    public ExcelSkippedRow(int excelRowNumber, string? originalProductCategory)
    {
        ExcelRowNumber = excelRowNumber;
        OriginalProductCategory = originalProductCategory;
    }

    public int ExcelRowNumber { get; }

    public string? OriginalProductCategory { get; }
}

public sealed class ExcelDuplicateRow
{
    public ExcelDuplicateRow(int representativeRowNumber, int duplicateRowNumber)
    {
        RepresentativeRowNumber = representativeRowNumber;
        DuplicateRowNumber = duplicateRowNumber;
    }

    public int RepresentativeRowNumber { get; }

    public int DuplicateRowNumber { get; }
}

public sealed class ExcelNormalBatch
{
    public ExcelNormalBatch(
        ExcelBatchKey batchKey,
        int representativeRowNumber,
        ExcelNormalizedRow representativeRow,
        IReadOnlyList<int> sourceRowNumbers)
    {
        BatchKey = batchKey;
        RepresentativeRowNumber = representativeRowNumber;
        RepresentativeRow = representativeRow;
        SourceRowNumbers = sourceRowNumbers;
    }

    public ExcelBatchKey BatchKey { get; }

    public int RepresentativeRowNumber { get; }

    public ExcelNormalizedRow RepresentativeRow { get; }

    public IReadOnlyList<int> SourceRowNumbers { get; }
}

public sealed class ExcelBatchConflict
{
    public ExcelBatchConflict(
        ExcelBatchKey batchKey,
        IReadOnlyList<int> rowNumbers,
        IReadOnlyList<string> differingFields)
    {
        BatchKey = batchKey;
        RowNumbers = rowNumbers;
        DifferingFields = differingFields;
    }

    public ExcelBatchKey BatchKey { get; }

    public IReadOnlyList<int> RowNumbers { get; }

    public IReadOnlyList<string> DifferingFields { get; }
}

public sealed class ExcelStockValue
{
    public ExcelStockValue(string? value, IReadOnlyList<int> rowNumbers)
    {
        Value = value;
        RowNumbers = rowNumbers;
    }

    public string? Value { get; }

    public IReadOnlyList<int> RowNumbers { get; }
}

public sealed class ExcelProductStock
{
    public ExcelProductStock(
        string productCode,
        bool isConflict,
        string? stockValue,
        IReadOnlyList<ExcelStockValue> values)
    {
        ProductCode = productCode;
        IsConflict = isConflict;
        StockValue = stockValue;
        Values = values;
    }

    public string ProductCode { get; }

    public bool IsConflict { get; }

    public string? StockValue { get; }

    public IReadOnlyList<ExcelStockValue> Values { get; }
}

public sealed class ExcelClassificationResult
{
    internal ExcelClassificationResult(
        int totalRowCount,
        int foodRowCount,
        IReadOnlyList<ExcelSkippedRow> skippedRows,
        IReadOnlyList<ExcelRowIssue> rowIssues,
        int batchKeyCount,
        IReadOnlyList<ExcelNormalBatch> normalBatches,
        IReadOnlyList<ExcelDuplicateRow> duplicateRows,
        IReadOnlyList<ExcelBatchConflict> batchConflicts,
        IReadOnlyList<ExcelProductStock> productStocks)
    {
        TotalRowCount = totalRowCount;
        FoodRowCount = foodRowCount;
        SkippedRows = Array.AsReadOnly(skippedRows.ToArray());
        RowIssues = Array.AsReadOnly(rowIssues.ToArray());
        BatchKeyCount = batchKeyCount;
        NormalBatches = Array.AsReadOnly(normalBatches.ToArray());
        DuplicateRows = Array.AsReadOnly(duplicateRows.ToArray());
        BatchConflicts = Array.AsReadOnly(batchConflicts.ToArray());
        ProductStocks = Array.AsReadOnly(productStocks.ToArray());
        StockConflicts = Array.AsReadOnly(productStocks.Where(static stock => stock.IsConflict).ToArray());
    }

    public int TotalRowCount { get; }

    public int FoodRowCount { get; }

    public int SkippedRowCount => SkippedRows.Count;

    public IReadOnlyList<ExcelSkippedRow> SkippedRows { get; }

    public IReadOnlyList<ExcelRowIssue> RowIssues { get; }

    public int BatchKeyCount { get; }

    public IReadOnlyList<ExcelNormalBatch> NormalBatches { get; }

    public IReadOnlyList<ExcelDuplicateRow> DuplicateRows { get; }

    public IReadOnlyList<ExcelBatchConflict> BatchConflicts { get; }

    public IReadOnlyList<ExcelProductStock> ProductStocks { get; }

    public IReadOnlyList<ExcelProductStock> StockConflicts { get; }
}
