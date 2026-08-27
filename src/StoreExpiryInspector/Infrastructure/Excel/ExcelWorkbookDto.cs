namespace StoreExpiryInspector.Infrastructure.Excel;

public sealed class ExcelWorkbookDto
{
    internal ExcelWorkbookDto(
        string sourceFileName,
        string sourceFileSha256,
        string worksheetName,
        IReadOnlyList<string> normalizedHeaders,
        IReadOnlyList<ExcelRowDto> rows)
    {
        SourceFileName = sourceFileName;
        SourceFileSha256 = sourceFileSha256;
        WorksheetName = worksheetName;
        NormalizedHeaders = normalizedHeaders;
        Rows = rows;
    }

    public string SourceFileName { get; }

    public string SourceFileSha256 { get; }

    public string WorksheetName { get; }

    public IReadOnlyList<string> NormalizedHeaders { get; }

    public IReadOnlyList<ExcelRowDto> Rows { get; }
}

public sealed class ExcelRowDto
{
    internal ExcelRowDto(
        int excelRowNumber,
        string? productCategory,
        string? productCode,
        string? productBarcode,
        string? productName,
        string? productionDate,
        string? expiryDate,
        string? shelfLife,
        string? shelfLifeUnit,
        string? isNearExpiryDiscountRequired,
        string? cumulativeArrivalQuantity,
        string? storeStockQuantity)
    {
        ExcelRowNumber = excelRowNumber;
        ProductCategory = productCategory;
        ProductCode = productCode;
        ProductBarcode = productBarcode;
        ProductName = productName;
        ProductionDate = productionDate;
        ExpiryDate = expiryDate;
        ShelfLife = shelfLife;
        ShelfLifeUnit = shelfLifeUnit;
        IsNearExpiryDiscountRequired = isNearExpiryDiscountRequired;
        CumulativeArrivalQuantity = cumulativeArrivalQuantity;
        StoreStockQuantity = storeStockQuantity;
    }

    public int ExcelRowNumber { get; }

    public string? ProductCategory { get; }

    public string? ProductCode { get; }

    public string? ProductBarcode { get; }

    public string? ProductName { get; }

    public string? ProductionDate { get; }

    public string? ExpiryDate { get; }

    public string? ShelfLife { get; }

    public string? ShelfLifeUnit { get; }

    public string? IsNearExpiryDiscountRequired { get; }

    public string? CumulativeArrivalQuantity { get; }

    public string? StoreStockQuantity { get; }
}
