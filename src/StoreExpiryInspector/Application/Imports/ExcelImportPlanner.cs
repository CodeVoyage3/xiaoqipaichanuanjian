using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Imports;

public sealed class ExcelImportPlanner
{
    public ImportPlan Plan(StoreDbContext context, ExcelClassificationResult classification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(classification);

        var normalBatches = classification.NormalBatches
            .OrderBy(batch => batch.BatchKey.ProductCode, StringComparer.Ordinal)
            .ThenBy(batch => batch.BatchKey.ProductionDate ?? DateOnly.MinValue)
            .ThenBy(batch => batch.BatchKey.ExpiryDate)
            .ThenBy(batch => batch.RepresentativeRowNumber)
            .ToArray();
        var productStocks = classification.ProductStocks
            .OrderBy(stock => stock.ProductCode, StringComparer.Ordinal)
            .ToArray();
        var productCodes = normalBatches
            .Select(batch => batch.BatchKey.ProductCode)
            .Concat(productStocks.Select(stock => stock.ProductCode))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        var existingProducts = productCodes.Length == 0
            ? Array.Empty<ProductSnapshot>()
            : context.Products
                .AsNoTracking()
                .Where(product => productCodes.Contains(product.ProductCode))
                .Select(product => new ProductSnapshot(
                    product.Id,
                    product.ProductCode,
                    product.CurrentName,
                    product.CurrentBarcode,
                    product.ExcelStockQty,
                    product.EffectiveStockQty,
                    product.EffectiveStockSource))
                .ToArray();
        var productsByCode = existingProducts.ToDictionary(
            product => product.ProductCode,
            StringComparer.Ordinal);

        var normalProductCodes = normalBatches
            .Select(batch => batch.BatchKey.ProductCode)
            .ToHashSet(StringComparer.Ordinal);
        var existingProductIds = existingProducts
            .Where(product => normalProductCodes.Contains(product.ProductCode))
            .Select(product => product.Id)
            .ToArray();
        var existingBatches = existingProductIds.Length == 0
            ? Array.Empty<BatchSnapshot>()
            : context.Batches
                .AsNoTracking()
                .Where(batch => existingProductIds.Contains(batch.ProductId))
                .Select(batch => new BatchSnapshot(
                    batch.ProductId,
                    batch.ProductionDate,
                    batch.ExpiryDate,
                    batch.ShelfLifeValue,
                    batch.ShelfLifeUnit,
                    batch.CurrentArrivalQty,
                    batch.MaxArrivalQty,
                    batch.SourceDiscountReference))
                .ToArray();
        var batchesByKey = existingBatches.ToDictionary(
            batch => new DatabaseBatchKey(batch.ProductId, batch.ProductionDate, batch.ExpiryDate));

        var stocksByCode = productStocks.ToDictionary(stock => stock.ProductCode, StringComparer.Ordinal);
        var normalByCode = normalBatches
            .GroupBy(batch => batch.BatchKey.ProductCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var planningIssues = new List<ImportPreviewIssue>();
        var newProducts = new List<NewProductPlan>();
        var updatedProducts = new List<ProductUpdatePlan>();
        var unchangedProducts = new List<ProductUnchangedPlan>();
        var newBatches = new List<NewBatchPlan>();
        var updatedBatches = new List<BatchUpdatePlan>();
        var unchangedBatches = new List<BatchUnchangedPlan>();
        var explicitProductStocks = new List<ExplicitProductStock>();

        foreach (var productCode in productCodes)
        {
            var hasNormalBatches = normalByCode.TryGetValue(productCode, out var productNormalBatches);
            productNormalBatches ??= Array.Empty<ExcelNormalBatch>();
            var hasExistingProduct = productsByCode.TryGetValue(productCode, out var existingProduct);
            var canCreateProduct = !hasExistingProduct && hasNormalBatches;
            var stock = stocksByCode.TryGetValue(productCode, out var productStock)
                ? productStock
                : null;
            var stockDecision = hasExistingProduct || canCreateProduct
                ? ReadStock(productCode, stock, canCreateProduct, planningIssues)
                : StockDecision.Unavailable;
            if (stockDecision.IsValid)
            {
                explicitProductStocks.Add(new ExplicitProductStock(productCode, stockDecision.Quantity!.Value));
            }
            var productValues = ReadProductValues(productCode, productNormalBatches, planningIssues);
            var sourceRows = SourceRows(productNormalBatches, stock);
            var canPlanNewProduct = canCreateProduct && stockDecision.IsValid;

            if (canPlanNewProduct)
            {
                newProducts.Add(new NewProductPlan(
                    productCode,
                    productValues.Name,
                    productValues.Barcode,
                    productValues.NameIsAmbiguous,
                    productValues.BarcodeIsAmbiguous,
                    stockDecision.Quantity!.Value,
                    sourceRows));
            }
            else if (hasExistingProduct)
            {
                var fieldChanges = CompareProduct(existingProduct!, productValues, stockDecision);
                var hasComparableFields = productValues.HasComparableField || stockDecision.IsValid;
                if (fieldChanges.Count > 0)
                {
                    updatedProducts.Add(new ProductUpdatePlan(productCode, sourceRows, fieldChanges));
                }
                else if (hasComparableFields)
                {
                    unchangedProducts.Add(new ProductUnchangedPlan(
                        productCode,
                        existingProduct!.CurrentName,
                        existingProduct.CurrentBarcode,
                        existingProduct.ExcelStockQty,
                        existingProduct.EffectiveStockQty,
                        existingProduct.EffectiveStockSource,
                        sourceRows));
                }
            }

            if (!hasNormalBatches || (!hasExistingProduct && !canPlanNewProduct))
            {
                continue;
            }

            foreach (var normalBatch in productNormalBatches)
            {
                if (!TryReadBatch(normalBatch, planningIssues, out var parsedBatch))
                {
                    continue;
                }

                var batchKey = normalBatch.BatchKey;
                if (!hasExistingProduct)
                {
                    newBatches.Add(new NewBatchPlan(
                        batchKey,
                        normalBatch.RepresentativeRowNumber,
                        normalBatch.SourceRowNumbers,
                        parsedBatch.ShelfLifeValue,
                        parsedBatch.ShelfLifeUnit,
                        parsedBatch.CurrentArrivalQty,
                        parsedBatch.CurrentArrivalQty,
                        parsedBatch.SourceDiscountReference));
                    continue;
                }

                var databaseKey = new DatabaseBatchKey(
                    existingProduct!.Id,
                    batchKey.ProductionDate,
                    batchKey.ExpiryDate);
                if (!batchesByKey.TryGetValue(databaseKey, out var existingBatch))
                {
                    newBatches.Add(new NewBatchPlan(
                        batchKey,
                        normalBatch.RepresentativeRowNumber,
                        normalBatch.SourceRowNumbers,
                        parsedBatch.ShelfLifeValue,
                        parsedBatch.ShelfLifeUnit,
                        parsedBatch.CurrentArrivalQty,
                        parsedBatch.CurrentArrivalQty,
                        parsedBatch.SourceDiscountReference));
                    continue;
                }

                var batchChanges = CompareBatch(existingBatch, parsedBatch);
                if (batchChanges.Count > 0)
                {
                    updatedBatches.Add(new BatchUpdatePlan(
                        batchKey,
                        normalBatch.RepresentativeRowNumber,
                        normalBatch.SourceRowNumbers,
                        batchChanges));
                }
                else
                {
                    unchangedBatches.Add(new BatchUnchangedPlan(
                        batchKey,
                        normalBatch.RepresentativeRowNumber,
                        normalBatch.SourceRowNumbers,
                        existingBatch.ShelfLifeValue,
                        existingBatch.ShelfLifeUnit,
                        existingBatch.CurrentArrivalQty,
                        existingBatch.MaxArrivalQty,
                        existingBatch.SourceDiscountReference));
                }
            }
        }

        var sortedNewProducts = newProducts
            .OrderBy(product => product.ProductCode, StringComparer.Ordinal)
            .ToArray();
        var sortedUpdatedProducts = updatedProducts
            .OrderBy(product => product.ProductCode, StringComparer.Ordinal)
            .ToArray();
        var sortedUnchangedProducts = unchangedProducts
            .OrderBy(product => product.ProductCode, StringComparer.Ordinal)
            .ToArray();
        var sortedNewBatches = newBatches
            .OrderBy(batch => batch.BatchKey.ProductCode, StringComparer.Ordinal)
            .ThenBy(batch => batch.BatchKey.ProductionDate ?? DateOnly.MinValue)
            .ThenBy(batch => batch.BatchKey.ExpiryDate)
            .ThenBy(batch => batch.ExcelRowNumber)
            .ToArray();
        var sortedUpdatedBatches = updatedBatches
            .OrderBy(batch => batch.BatchKey.ProductCode, StringComparer.Ordinal)
            .ThenBy(batch => batch.BatchKey.ProductionDate ?? DateOnly.MinValue)
            .ThenBy(batch => batch.BatchKey.ExpiryDate)
            .ThenBy(batch => batch.ExcelRowNumber)
            .ToArray();
        var sortedUnchangedBatches = unchangedBatches
            .OrderBy(batch => batch.BatchKey.ProductCode, StringComparer.Ordinal)
            .ThenBy(batch => batch.BatchKey.ProductionDate ?? DateOnly.MinValue)
            .ThenBy(batch => batch.BatchKey.ExpiryDate)
            .ThenBy(batch => batch.ExcelRowNumber)
            .ToArray();
        var sortedPlanningIssues = planningIssues
            .OrderBy(issue => issue.ProductCode, StringComparer.Ordinal)
            .ThenBy(issue => issue.ExcelRowNumber ?? int.MaxValue)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.FieldName, StringComparer.Ordinal)
            .ToArray();
        var sortedExplicitProductStocks = explicitProductStocks
            .OrderBy(stock => stock.ProductCode, StringComparer.Ordinal)
            .ToArray();
        var hasChanges = sortedNewProducts.Length > 0
            || sortedUpdatedProducts.Length > 0
            || sortedNewBatches.Length > 0
            || sortedUpdatedBatches.Length > 0;
        var preview = new ImportPreview(
            productCodes.Length,
            classification.NormalBatches.Count,
            classification.SkippedRows,
            classification.RowIssues,
            classification.DuplicateRows,
            classification.BatchConflicts,
            classification.StockConflicts,
            sortedPlanningIssues,
            hasChanges);
        return new ImportPlan(
            preview,
            sortedNewProducts,
            sortedUpdatedProducts,
            sortedUnchangedProducts,
            sortedNewBatches,
            sortedUpdatedBatches,
            sortedUnchangedBatches,
            sortedExplicitProductStocks);
    }

    private static IReadOnlyList<ImportFieldChange> CompareProduct(
        ProductSnapshot existing,
        ProductValues values,
        StockDecision stock)
    {
        var changes = new List<ImportFieldChange>();
        if (!values.NameIsAmbiguous && values.HasName && !string.Equals(existing.CurrentName, values.Name, StringComparison.Ordinal))
        {
            changes.Add(new ImportFieldChange("CurrentName", existing.CurrentName, values.Name));
        }

        if (!values.BarcodeIsAmbiguous && values.HasBarcode && !string.Equals(existing.CurrentBarcode, values.Barcode, StringComparison.Ordinal))
        {
            changes.Add(new ImportFieldChange("CurrentBarcode", existing.CurrentBarcode, values.Barcode));
        }

        if (stock.IsValid && existing.ExcelStockQty != stock.Quantity)
        {
            changes.Add(new ImportFieldChange("ExcelStockQty", existing.ExcelStockQty, stock.Quantity));
        }

        if (stock.IsValid && existing.EffectiveStockQty != stock.Quantity)
        {
            changes.Add(new ImportFieldChange("EffectiveStockQty", existing.EffectiveStockQty, stock.Quantity));
        }

        if (stock.IsValid && !string.Equals(existing.EffectiveStockSource, "excel", StringComparison.Ordinal))
        {
            changes.Add(new ImportFieldChange("EffectiveStockSource", existing.EffectiveStockSource, "excel"));
        }

        return changes;
    }

    private static IReadOnlyList<ImportFieldChange> CompareBatch(
        BatchSnapshot existing,
        ParsedBatch incoming)
    {
        var changes = new List<ImportFieldChange>();
        if (existing.ShelfLifeValue != incoming.ShelfLifeValue)
        {
            changes.Add(new ImportFieldChange("ShelfLifeValue", existing.ShelfLifeValue, incoming.ShelfLifeValue));
        }

        if (!string.Equals(existing.ShelfLifeUnit, incoming.ShelfLifeUnit, StringComparison.Ordinal))
        {
            changes.Add(new ImportFieldChange("ShelfLifeUnit", existing.ShelfLifeUnit, incoming.ShelfLifeUnit));
        }

        if (existing.CurrentArrivalQty != incoming.CurrentArrivalQty)
        {
            changes.Add(new ImportFieldChange("CurrentArrivalQty", existing.CurrentArrivalQty, incoming.CurrentArrivalQty));
        }

        var maxArrivalQty = Math.Max(existing.MaxArrivalQty, incoming.CurrentArrivalQty);
        if (existing.MaxArrivalQty != maxArrivalQty)
        {
            changes.Add(new ImportFieldChange("MaxArrivalQty", existing.MaxArrivalQty, maxArrivalQty));
        }

        if (!string.Equals(existing.SourceDiscountReference, incoming.SourceDiscountReference, StringComparison.Ordinal))
        {
            changes.Add(new ImportFieldChange(
                "SourceDiscountReference",
                existing.SourceDiscountReference,
                incoming.SourceDiscountReference));
        }

        return changes;
    }

    private static ProductValues ReadProductValues(
        string productCode,
        IReadOnlyList<ExcelNormalBatch> batches,
        ICollection<ImportPreviewIssue> planningIssues)
    {
        if (batches.Count == 0)
        {
            return ProductValues.None;
        }

        var rows = batches
            .Select(batch => batch.RepresentativeRow)
            .OrderBy(row => row.ExcelRowNumber)
            .ToArray();
        var names = rows
            .Select(row => row.ProductName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var barcodes = rows
            .Select(row => row.ProductBarcode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nameIsAmbiguous = names.Length > 1;
        var barcodeIsAmbiguous = barcodes.Length > 1;
        var issueRow = rows[0].ExcelRowNumber;
        if (nameIsAmbiguous)
        {
            planningIssues.Add(new ImportPreviewIssue(
                productCode,
                issueRow,
                "ambiguous_product_name",
                "商品名称",
                "同一商品的正常批次出现多个商品名称，未选择其中任何一个。"));
        }

        if (barcodeIsAmbiguous)
        {
            planningIssues.Add(new ImportPreviewIssue(
                productCode,
                issueRow,
                "ambiguous_product_barcode",
                "商品条码",
                "同一商品的正常批次出现多个商品条码，未选择其中任何一个。"));
        }

        return new ProductValues(
            nameIsAmbiguous ? null : names[0],
            barcodeIsAmbiguous ? null : barcodes[0],
            !nameIsAmbiguous,
            !barcodeIsAmbiguous,
            nameIsAmbiguous,
            barcodeIsAmbiguous);
    }

    private static StockDecision ReadStock(
        string productCode,
        ExcelProductStock? stock,
        bool isNewProduct,
        ICollection<ImportPreviewIssue> planningIssues)
    {
        if (stock is null)
        {
            if (isNewProduct)
            {
                planningIssues.Add(new ImportPreviewIssue(
                    productCode,
                    null,
                    "missing_stock_quantity",
                    "该商品门店库存总数",
                    "新商品没有可用的单一库存数量，未形成商品或批次计划。"));
            }

            return StockDecision.Unavailable;
        }

        if (stock.IsConflict)
        {
            return StockDecision.Conflict;
        }

        var rowNumber = stock.Values
            .SelectMany(value => value.RowNumbers)
            .DefaultIfEmpty()
            .Min();
        if (string.IsNullOrWhiteSpace(stock.StockValue))
        {
            if (isNewProduct)
            {
                planningIssues.Add(new ImportPreviewIssue(
                    productCode,
                    rowNumber == 0 ? null : rowNumber,
                    "missing_stock_quantity",
                    "该商品门店库存总数",
                    "新商品没有可用的单一库存数量，未形成商品或批次计划。"));
            }

            return StockDecision.Unavailable;
        }

        if (!TryParseInteger(stock.StockValue, out var quantity) || quantity < 0)
        {
            planningIssues.Add(new ImportPreviewIssue(
                productCode,
                rowNumber == 0 ? null : rowNumber,
                "invalid_stock_quantity",
                "该商品门店库存总数",
                "库存数量必须是非负十进制整数，未把无效文本转换为数量。"));
            return StockDecision.Invalid;
        }

        return new StockDecision(true, quantity);
    }

    private static bool TryReadBatch(
        ExcelNormalBatch normalBatch,
        ICollection<ImportPreviewIssue> planningIssues,
        out ParsedBatch parsed)
    {
        var row = normalBatch.RepresentativeRow;
        var isValid = true;
        var shelfLifeValue = 0;
        var arrivalQuantity = 0;
        if (string.IsNullOrWhiteSpace(row.ShelfLife))
        {
            planningIssues.Add(new ImportPreviewIssue(
                normalBatch.BatchKey.ProductCode,
                normalBatch.RepresentativeRowNumber,
                "invalid_shelf_life_value",
                "保质期",
                "保质期必须是正十进制整数，该批次未进入计划。"));
            isValid = false;
        }
        else if (!TryParseInteger(row.ShelfLife, out shelfLifeValue) || shelfLifeValue <= 0)
        {
            planningIssues.Add(new ImportPreviewIssue(
                normalBatch.BatchKey.ProductCode,
                normalBatch.RepresentativeRowNumber,
                "invalid_shelf_life_value",
                "保质期",
                "保质期必须是正十进制整数，该批次未进入计划。"));
            isValid = false;
            shelfLifeValue = 0;
        }

        if (string.IsNullOrWhiteSpace(row.CumulativeArrivalQuantity))
        {
            planningIssues.Add(new ImportPreviewIssue(
                normalBatch.BatchKey.ProductCode,
                normalBatch.RepresentativeRowNumber,
                "missing_arrival_quantity",
                "该批次累计到货数量",
                "批次缺少累计到货数量，该批次未进入计划。"));
            isValid = false;
        }
        else if (!TryParseInteger(row.CumulativeArrivalQuantity, out arrivalQuantity) || arrivalQuantity < 0)
        {
            planningIssues.Add(new ImportPreviewIssue(
                normalBatch.BatchKey.ProductCode,
                normalBatch.RepresentativeRowNumber,
                "invalid_arrival_quantity",
                "该批次累计到货数量",
                "累计到货数量必须是非负十进制整数，该批次未进入计划。"));
            isValid = false;
            arrivalQuantity = 0;
        }

        if (!isValid)
        {
            parsed = default;
            return false;
        }

        parsed = new ParsedBatch(
            shelfLifeValue,
            row.ShelfLifeUnit,
            arrivalQuantity,
            row.IsNearExpiryDiscountRequired);
        return true;
    }

    private static IReadOnlyList<int> SourceRows(
        IReadOnlyList<ExcelNormalBatch> batches,
        ExcelProductStock? stock)
    {
        return Array.AsReadOnly(batches
            .SelectMany(batch => batch.SourceRowNumbers)
            .Concat(stock?.Values.SelectMany(value => value.RowNumbers) ?? [])
            .Distinct()
            .OrderBy(rowNumber => rowNumber)
            .ToArray());
    }

    private static bool TryParseInteger(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct ProductSnapshot(
        long Id,
        string ProductCode,
        string? CurrentName,
        string? CurrentBarcode,
        int ExcelStockQty,
        int EffectiveStockQty,
        string? EffectiveStockSource);

    private readonly record struct BatchSnapshot(
        long ProductId,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate,
        int ShelfLifeValue,
        string ShelfLifeUnit,
        int CurrentArrivalQty,
        int MaxArrivalQty,
        string? SourceDiscountReference);

    private readonly record struct DatabaseBatchKey(
        long ProductId,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate);

    private readonly record struct ParsedBatch(
        int ShelfLifeValue,
        string ShelfLifeUnit,
        int CurrentArrivalQty,
        string? SourceDiscountReference);

    private readonly record struct ProductValues(
        string? Name,
        string? Barcode,
        bool HasName,
        bool HasBarcode,
        bool NameIsAmbiguous,
        bool BarcodeIsAmbiguous)
    {
        public bool HasComparableField => HasName || HasBarcode;

        public static ProductValues None => new(null, null, false, false, false, false);
    }

    private readonly record struct StockDecision(bool IsValid, int? Quantity)
    {
        public static StockDecision Unavailable => new(false, null);

        public static StockDecision Invalid => new(false, null);

        public static StockDecision Conflict => new(false, null);
    }
}

public sealed class ImportPlan
{
    internal ImportPlan(
        ImportPreview preview,
        IReadOnlyList<NewProductPlan> newProducts,
        IReadOnlyList<ProductUpdatePlan> updatedProducts,
        IReadOnlyList<ProductUnchangedPlan> unchangedProducts,
        IReadOnlyList<NewBatchPlan> newBatches,
        IReadOnlyList<BatchUpdatePlan> updatedBatches,
        IReadOnlyList<BatchUnchangedPlan> unchangedBatches,
        IReadOnlyList<ExplicitProductStock> explicitProductStocks)
    {
        Preview = preview;
        NewProducts = Array.AsReadOnly(newProducts.ToArray());
        UpdatedProducts = Array.AsReadOnly(updatedProducts.ToArray());
        UnchangedProducts = Array.AsReadOnly(unchangedProducts.ToArray());
        NewBatches = Array.AsReadOnly(newBatches.ToArray());
        UpdatedBatches = Array.AsReadOnly(updatedBatches.ToArray());
        UnchangedBatches = Array.AsReadOnly(unchangedBatches.ToArray());
        ExplicitProductStocks = Array.AsReadOnly(explicitProductStocks.ToArray());
    }

    public ImportPreview Preview { get; }

    public IReadOnlyList<NewProductPlan> NewProducts { get; }

    public IReadOnlyList<ProductUpdatePlan> UpdatedProducts { get; }

    public IReadOnlyList<ProductUnchangedPlan> UnchangedProducts { get; }

    public IReadOnlyList<NewBatchPlan> NewBatches { get; }

    public IReadOnlyList<BatchUpdatePlan> UpdatedBatches { get; }

    public IReadOnlyList<BatchUnchangedPlan> UnchangedBatches { get; }

    public IReadOnlyList<ExplicitProductStock> ExplicitProductStocks { get; }

    public int NewProductCount => NewProducts.Count;

    public int UpdatedProductCount => UpdatedProducts.Count;

    public int UnchangedProductCount => UnchangedProducts.Count;

    public int NewBatchCount => NewBatches.Count;

    public int UpdatedBatchCount => UpdatedBatches.Count;

    public int UnchangedBatchCount => UnchangedBatches.Count;

    public bool HasChanges => NewProducts.Count > 0
        || UpdatedProducts.Count > 0
        || NewBatches.Count > 0
        || UpdatedBatches.Count > 0;
}

public sealed record ExplicitProductStock(string ProductCode, int Quantity);

public sealed class ImportPreview
{
    internal ImportPreview(
        int involvedProductCount,
        int normalBatchKeyCount,
        IReadOnlyList<ExcelSkippedRow> skippedRows,
        IReadOnlyList<ExcelRowIssue> rowIssues,
        IReadOnlyList<ExcelDuplicateRow> duplicateRows,
        IReadOnlyList<ExcelBatchConflict> batchConflicts,
        IReadOnlyList<ExcelProductStock> stockConflicts,
        IReadOnlyList<ImportPreviewIssue> planningIssues,
        bool hasChanges)
    {
        InvolvedProductCount = involvedProductCount;
        NormalBatchKeyCount = normalBatchKeyCount;
        SkippedRows = skippedRows;
        RowIssues = rowIssues;
        DuplicateRows = duplicateRows;
        BatchConflicts = batchConflicts;
        StockConflicts = stockConflicts;
        PlanningIssues = Array.AsReadOnly(planningIssues.ToArray());
        HasChanges = hasChanges;
    }

    public int InvolvedProductCount { get; }

    public int NormalBatchKeyCount { get; }

    public IReadOnlyList<ExcelSkippedRow> SkippedRows { get; }

    public IReadOnlyList<ExcelRowIssue> RowIssues { get; }

    public IReadOnlyList<ExcelDuplicateRow> DuplicateRows { get; }

    public IReadOnlyList<ExcelBatchConflict> BatchConflicts { get; }

    public IReadOnlyList<ExcelProductStock> StockConflicts { get; }

    public IReadOnlyList<ImportPreviewIssue> PlanningIssues { get; }

    public int SkippedRowCount => SkippedRows.Count;

    public int RowIssueCount => RowIssues.Count;

    public int DuplicateRowCount => DuplicateRows.Count;

    public int BatchConflictCount => BatchConflicts.Count;

    public int StockConflictCount => StockConflicts.Count;

    public int PlanningIssueCount => PlanningIssues.Count;

    public bool HasChanges { get; }
}

public sealed class ImportPreviewIssue
{
    internal ImportPreviewIssue(
        string? productCode,
        int? excelRowNumber,
        string code,
        string fieldName,
        string safeSummary)
    {
        ProductCode = productCode;
        ExcelRowNumber = excelRowNumber;
        Code = code;
        FieldName = fieldName;
        SafeSummary = safeSummary;
    }

    public string? ProductCode { get; }

    public int? ExcelRowNumber { get; }

    public int? RowNumber => ExcelRowNumber;

    public string Code { get; }

    public string FieldName { get; }

    public string SafeSummary { get; }
}

public sealed class ImportFieldChange
{
    internal ImportFieldChange(string fieldName, object? before, object? after)
    {
        FieldName = fieldName;
        Before = before;
        After = after;
    }

    public string FieldName { get; }

    public object? Before { get; }

    public object? After { get; }
}

public sealed class NewProductPlan
{
    internal NewProductPlan(
        string productCode,
        string? currentName,
        string? currentBarcode,
        bool nameIsAmbiguous,
        bool barcodeIsAmbiguous,
        int stockQuantity,
        IReadOnlyList<int> sourceExcelRowNumbers)
    {
        ProductCode = productCode;
        CurrentName = currentName;
        CurrentBarcode = currentBarcode;
        NameIsAmbiguous = nameIsAmbiguous;
        BarcodeIsAmbiguous = barcodeIsAmbiguous;
        CategoryCode = "food";
        PolicyCode = ExpiryPolicies.Food;
        PolicyVersion = ExpiryPolicies.Version1;
        ExpiryManagementStatus = ExpiryManagementStatus.Managed;
        ExcelStockQty = stockQuantity;
        EffectiveStockQty = stockQuantity;
        EffectiveStockSource = "excel";
        ExcelRowNumber = sourceExcelRowNumbers.Count == 0 ? null : sourceExcelRowNumbers[0];
        SourceExcelRowNumbers = Array.AsReadOnly(sourceExcelRowNumbers.ToArray());
    }

    public string ProductCode { get; }

    public string? CurrentName { get; }

    public string? CurrentBarcode { get; }

    public bool NameIsAmbiguous { get; }

    public bool BarcodeIsAmbiguous { get; }

    public string CategoryCode { get; }

    public string PolicyCode { get; }

    public int PolicyVersion { get; }

    public ExpiryManagementStatus ExpiryManagementStatus { get; }

    public int ExcelStockQty { get; }

    public int EffectiveStockQty { get; }

    public string EffectiveStockSource { get; }

    public int? ExcelRowNumber { get; }

    public IReadOnlyList<int> SourceExcelRowNumbers { get; }

    public IReadOnlyList<int> SourceRowNumbers => SourceExcelRowNumbers;
}

public sealed class ProductUpdatePlan
{
    internal ProductUpdatePlan(
        string productCode,
        IReadOnlyList<int> sourceExcelRowNumbers,
        IReadOnlyList<ImportFieldChange> fieldChanges)
    {
        ProductCode = productCode;
        ExcelRowNumber = sourceExcelRowNumbers.Count == 0 ? null : sourceExcelRowNumbers[0];
        SourceExcelRowNumbers = Array.AsReadOnly(sourceExcelRowNumbers.ToArray());
        FieldChanges = Array.AsReadOnly(fieldChanges.ToArray());
    }

    public string ProductCode { get; }

    public int? ExcelRowNumber { get; }

    public IReadOnlyList<int> SourceExcelRowNumbers { get; }

    public IReadOnlyList<int> SourceRowNumbers => SourceExcelRowNumbers;

    public IReadOnlyList<ImportFieldChange> FieldChanges { get; }

    public IReadOnlyList<ImportFieldChange> Changes => FieldChanges;
}

public sealed class ProductUnchangedPlan
{
    internal ProductUnchangedPlan(
        string productCode,
        string? currentName,
        string? currentBarcode,
        int excelStockQty,
        int effectiveStockQty,
        string? effectiveStockSource,
        IReadOnlyList<int> sourceExcelRowNumbers)
    {
        ProductCode = productCode;
        CurrentName = currentName;
        CurrentBarcode = currentBarcode;
        ExcelStockQty = excelStockQty;
        EffectiveStockQty = effectiveStockQty;
        EffectiveStockSource = effectiveStockSource;
        ExcelRowNumber = sourceExcelRowNumbers.Count == 0 ? null : sourceExcelRowNumbers[0];
        SourceExcelRowNumbers = Array.AsReadOnly(sourceExcelRowNumbers.ToArray());
    }

    public string ProductCode { get; }

    public string? CurrentName { get; }

    public string? CurrentBarcode { get; }

    public int ExcelStockQty { get; }

    public int EffectiveStockQty { get; }

    public string? EffectiveStockSource { get; }

    public int? ExcelRowNumber { get; }

    public IReadOnlyList<int> SourceExcelRowNumbers { get; }

    public IReadOnlyList<int> SourceRowNumbers => SourceExcelRowNumbers;
}

public sealed class NewBatchPlan
{
    internal NewBatchPlan(
        ExcelBatchKey batchKey,
        int excelRowNumber,
        IReadOnlyList<int> sourceExcelRowNumbers,
        int shelfLifeValue,
        string shelfLifeUnit,
        int currentArrivalQty,
        int maxArrivalQty,
        string? sourceDiscountReference)
    {
        BatchKey = batchKey;
        ExcelRowNumber = excelRowNumber;
        SourceExcelRowNumbers = Array.AsReadOnly(sourceExcelRowNumbers.ToArray());
        ShelfLifeValue = shelfLifeValue;
        ShelfLifeUnit = shelfLifeUnit;
        CurrentArrivalQty = currentArrivalQty;
        MaxArrivalQty = maxArrivalQty;
        SourceDiscountReference = sourceDiscountReference;
    }

    public ExcelBatchKey BatchKey { get; }

    public int ExcelRowNumber { get; }

    public IReadOnlyList<int> SourceExcelRowNumbers { get; }

    public IReadOnlyList<int> SourceRowNumbers => SourceExcelRowNumbers;

    public int ShelfLifeValue { get; }

    public string ShelfLifeUnit { get; }

    public int CurrentArrivalQty { get; }

    public int MaxArrivalQty { get; }

    public string? SourceDiscountReference { get; }
}

public sealed class BatchUpdatePlan
{
    internal BatchUpdatePlan(
        ExcelBatchKey batchKey,
        int excelRowNumber,
        IReadOnlyList<int> sourceExcelRowNumbers,
        IReadOnlyList<ImportFieldChange> fieldChanges)
    {
        BatchKey = batchKey;
        ExcelRowNumber = excelRowNumber;
        SourceExcelRowNumbers = Array.AsReadOnly(sourceExcelRowNumbers.ToArray());
        FieldChanges = Array.AsReadOnly(fieldChanges.ToArray());
    }

    public ExcelBatchKey BatchKey { get; }

    public int ExcelRowNumber { get; }

    public IReadOnlyList<int> SourceExcelRowNumbers { get; }

    public IReadOnlyList<int> SourceRowNumbers => SourceExcelRowNumbers;

    public IReadOnlyList<ImportFieldChange> FieldChanges { get; }

    public IReadOnlyList<ImportFieldChange> Changes => FieldChanges;
}

public sealed class BatchUnchangedPlan
{
    internal BatchUnchangedPlan(
        ExcelBatchKey batchKey,
        int excelRowNumber,
        IReadOnlyList<int> sourceExcelRowNumbers,
        int shelfLifeValue,
        string shelfLifeUnit,
        int currentArrivalQty,
        int maxArrivalQty,
        string? sourceDiscountReference)
    {
        BatchKey = batchKey;
        ExcelRowNumber = excelRowNumber;
        SourceExcelRowNumbers = Array.AsReadOnly(sourceExcelRowNumbers.ToArray());
        ShelfLifeValue = shelfLifeValue;
        ShelfLifeUnit = shelfLifeUnit;
        CurrentArrivalQty = currentArrivalQty;
        MaxArrivalQty = maxArrivalQty;
        SourceDiscountReference = sourceDiscountReference;
    }

    public ExcelBatchKey BatchKey { get; }

    public int ExcelRowNumber { get; }

    public IReadOnlyList<int> SourceExcelRowNumbers { get; }

    public IReadOnlyList<int> SourceRowNumbers => SourceExcelRowNumbers;

    public int ShelfLifeValue { get; }

    public string ShelfLifeUnit { get; }

    public int CurrentArrivalQty { get; }

    public int MaxArrivalQty { get; }

    public string? SourceDiscountReference { get; }
}
