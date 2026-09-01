using System.Security.Cryptography;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ExcelFileClassifierTests
{
    [Fact]
    public void ClassifiesTheRegressionWorkbookWithTheApprovedCounts()
    {
        var path = FindRegressionWorkbook();
        var beforeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        var workbook = new ExcelTemplateReader().Read(path);
        var result = new ExcelFileClassifier().Classify(workbook);

        Assert.Equal("20fe1898dba98f48bcd8b83673f2001fe3ed0dfc01a89aa4ebdff9d1af6cacce", beforeHash);
        Assert.Equal(3712, result.TotalRowCount);
        Assert.Equal(3712, result.AcceptedRowCount);
        Assert.Equal(0, result.SkippedRowCount);
        Assert.Empty(result.RowIssues);
        Assert.Empty(result.DuplicateRows);
        Assert.Empty(result.StockConflicts);
        Assert.Equal(3709, result.BatchKeyCount);
        Assert.Equal(3706, result.NormalBatches.Count);
        Assert.Equal(3, result.BatchConflicts.Count);
        Assert.Equal(6, result.BatchConflicts.Sum(conflict => conflict.RowNumbers.Count));

        var conflicts = result.BatchConflicts.ToDictionary(
            conflict => string.Join('/', conflict.RowNumbers));
        Assert.Contains("812/813", conflicts.Keys);
        Assert.Contains("2284/2285", conflicts.Keys);
        Assert.Contains("2610/2611", conflicts.Keys);
        Assert.Contains("保质期", conflicts["812/813"].DifferingFields);
        Assert.Contains("该批次累计到货数量", conflicts["812/813"].DifferingFields);
        Assert.Contains("保质期", conflicts["2610/2611"].DifferingFields);
        Assert.Contains("该批次累计到货数量", conflicts["2610/2611"].DifferingFields);
        Assert.Contains("保质期", conflicts["2284/2285"].DifferingFields);
        Assert.Contains("保质期单位", conflicts["2284/2285"].DifferingFields);
        Assert.Contains("该批次累计到货数量", conflicts["2284/2285"].DifferingFields);

        var afterHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Assert.Equal(beforeHash, afterHash);
    }

    [Fact]
    public void AcceptsKnownNonFoodRowsAndCollectsEveryApplicableIssue()
    {
        var workbook = Workbook(
            Row(2, category: " 日用 ", code: null, expiry: "bad", shelfLife: null, shelfLifeUnit: null),
            Row(3, code: " ", expiry: " ", shelfLife: " ", shelfLifeUnit: " "));

        var result = new ExcelFileClassifier().Classify(workbook);

        Assert.Equal(2, result.TotalRowCount);
        Assert.Equal(2, result.AcceptedRowCount);
        Assert.Equal(0, result.SkippedRowCount);
        Assert.Equal(8, result.RowIssues.Count);
        Assert.All(result.RowIssues, issue => Assert.Contains(issue.ExcelRowNumber, new[] { 2, 3 }));
    }

    [Fact]
    public void ParsesApprovedTextAndOleDatesAndRejectsInvalidValues()
    {
        Assert.True(ExcelDateParser.TryParse("2026-1-2", out var hyphenDate));
        Assert.Equal(new DateOnly(2026, 1, 2), hyphenDate);
        Assert.True(ExcelDateParser.TryParse("2026-01-02", out var paddedHyphenDate));
        Assert.Equal(hyphenDate, paddedHyphenDate);
        Assert.True(ExcelDateParser.TryParse(" 2026/1/2 ", out var slashDate));
        Assert.Equal(hyphenDate, slashDate);
        Assert.True(ExcelDateParser.TryParse("2026/01/02", out var paddedSlashDate));
        Assert.Equal(hyphenDate, paddedSlashDate);
        Assert.True(ExcelDateParser.TryParse("45981", out var serialDate));
        Assert.Equal(DateOnly.FromDateTime(DateTime.FromOADate(45981)), serialDate);

        Assert.False(ExcelDateParser.TryParse("2026-02-29", out _));
        Assert.False(ExcelDateParser.TryParse("45981.5", out _));
        Assert.False(ExcelDateParser.TryParse("2958466", out _));
        Assert.False(ExcelDateParser.TryParse("2026.01.02", out _));
    }

    [Fact]
    public void SeparatesDuplicateBatchConflictAndStockOnlyConflict()
    {
        var workbook = Workbook(
            Row(10, code: "P-duplicate", production: "2026/1/2"),
            Row(11, code: " P-duplicate ", production: "2026-01-02"),
            Row(20, code: "P-conflict", expiry: "2026-12-31", shelfLife: "9"),
            Row(21, code: "P-conflict", expiry: "2026-12-31", shelfLife: "12"),
            Row(30, code: "P-stock", stock: null),
            Row(31, code: "P-stock", stock: " 0 "));

        var result = new ExcelFileClassifier().Classify(workbook);

        Assert.Equal(3, result.BatchKeyCount);
        Assert.Single(result.DuplicateRows);
        Assert.Equal(10, result.DuplicateRows[0].RepresentativeRowNumber);
        Assert.Equal(11, result.DuplicateRows[0].DuplicateRowNumber);
        Assert.Single(result.BatchConflicts);
        Assert.Equal([20, 21], result.BatchConflicts[0].RowNumbers);
        Assert.Equal(["保质期"], result.BatchConflicts[0].DifferingFields);
        Assert.Equal(2, result.NormalBatches.Count);
        var stockBatch = result.NormalBatches.Single(batch => batch.BatchKey.ProductCode == "P-stock");
        Assert.Equal([30, 31], stockBatch.SourceRowNumbers);
        Assert.Null(stockBatch.RepresentativeRow.StoreStockQuantity);

        var stock = Assert.Single(result.StockConflicts);
        Assert.Equal("P-stock", stock.ProductCode);
        Assert.True(stock.IsConflict);
        Assert.Null(stock.StockValue);
        Assert.Equal(2, stock.Values.Count);
        Assert.Contains(stock.Values, value => value.Value is null && value.RowNumbers.SequenceEqual([30]));
        Assert.Contains(stock.Values, value => value.Value == "0" && value.RowNumbers.SequenceEqual([31]));
    }

    [Fact]
    public void KeepsStockConflictIndependentFromRowIssuesAndOtherBatches()
    {
        var workbook = Workbook(
            Row(2, code: "P", expiry: "2026-12-31", stock: "4"),
            Row(3, code: "P", expiry: "bad", stock: "0"),
            Row(4, code: "Q", expiry: "2026-12-31", stock: "0"));

        var result = new ExcelFileClassifier().Classify(workbook);

        Assert.Contains(result.RowIssues, issue => issue.ExcelRowNumber == 3 && issue.Code == "invalid_expiry_date");
        Assert.Single(result.StockConflicts);
        Assert.Equal("P", result.StockConflicts[0].ProductCode);
        Assert.Equal(2, result.NormalBatches.Count);
        Assert.Contains(result.NormalBatches, batch => batch.BatchKey.ProductCode == "P");
        Assert.Contains(result.NormalBatches, batch => batch.BatchKey.ProductCode == "Q");
    }

    [Fact]
    public void DifferentProductCodesRemainDifferentKeysWhenDatesMatch()
    {
        var result = new ExcelFileClassifier().Classify(Workbook(
            Row(2, code: "P1"),
            Row(3, code: "P2")));

        Assert.Equal(2, result.BatchKeyCount);
        Assert.Equal(2, result.NormalBatches.Count);
        Assert.Equal(
            ["P1", "P2"],
            result.NormalBatches.Select(batch => batch.BatchKey.ProductCode));
        Assert.All(result.NormalBatches, batch =>
        {
            Assert.Equal(new DateOnly(2026, 1, 1), batch.BatchKey.ProductionDate);
            Assert.Equal(new DateOnly(2026, 12, 31), batch.BatchKey.ExpiryDate);
        });
    }

    [Fact]
    public void KeepsConflictAndStockResultsDeterministicWhenInputOrderChanges()
    {
        var rows = new[]
        {
            Row(10, code: "P-conflict", shelfLife: "12", stock: "4"),
            Row(11, code: "P-conflict", shelfLife: "24", stock: "0"),
            Row(20, code: "P-stock", stock: null),
            Row(21, code: "P-stock", stock: "0")
        };

        var first = new ExcelFileClassifier().Classify(Workbook(rows));
        var second = new ExcelFileClassifier().Classify(Workbook(rows.Reverse().ToArray()));

        Assert.Equal(first.BatchConflicts.Count, second.BatchConflicts.Count);
        for (var index = 0; index < first.BatchConflicts.Count; index++)
        {
            Assert.Equal(first.BatchConflicts[index].BatchKey, second.BatchConflicts[index].BatchKey);
            Assert.Equal(first.BatchConflicts[index].RowNumbers, second.BatchConflicts[index].RowNumbers);
            Assert.Equal(first.BatchConflicts[index].DifferingFields, second.BatchConflicts[index].DifferingFields);
        }

        Assert.Equal(first.StockConflicts.Count, second.StockConflicts.Count);
        for (var index = 0; index < first.StockConflicts.Count; index++)
        {
            var firstStock = first.StockConflicts[index];
            var secondStock = second.StockConflicts[index];
            Assert.Equal(firstStock.ProductCode, secondStock.ProductCode);
            Assert.Equal(firstStock.StockValue, secondStock.StockValue);
            Assert.Equal(firstStock.Values.Count, secondStock.Values.Count);
            for (var valueIndex = 0; valueIndex < firstStock.Values.Count; valueIndex++)
            {
                Assert.Equal(firstStock.Values[valueIndex].Value, secondStock.Values[valueIndex].Value);
                Assert.Equal(firstStock.Values[valueIndex].RowNumbers, secondStock.Values[valueIndex].RowNumbers);
            }
        }
        Assert.Null(first.StockConflicts.Single(stock => stock.ProductCode == "P-stock").StockValue);
        Assert.Null(first.NormalBatches.Single(batch => batch.BatchKey.ProductCode == "P-stock").RepresentativeRow.StoreStockQuantity);
    }

    [Fact]
    public void KeepsTheLastThreeManualFieldsOutOfParsingAndClassificationDtos()
    {
        var dtoPropertyNames = typeof(ExcelRowDto)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedNames = new[]
        {
            "CumulativeArrivalQuantity",
            "ExcelRowNumber",
            "ExpiryDate",
            "IsNearExpiryDiscountRequired",
            "ProductBarcode",
            "ProductCategory",
            "ProductCode",
            "ProductName",
            "ProductionDate",
            "ShelfLife",
            "ShelfLifeUnit",
            "StoreStockQuantity"
        };

        Assert.Equal(expectedNames, dtoPropertyNames);
        Assert.DoesNotContain(
            typeof(ExcelClassificationResult).GetProperties(),
            property => property.Name.Contains("Inspection", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Signature", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Check", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ExcelProductStock).GetProperties(),
            property => property.Name.Contains("Status", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("State", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Zero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UsesTheLowestExcelRowAsRepresentativeRegardlessOfInputOrder()
    {
        var rows = new[]
        {
            Row(12, code: "P", expiry: "2026-12-31"),
            Row(10, code: "P", expiry: "2026-12-31"),
            Row(11, code: "P", expiry: "2026-12-31")
        };

        var result = new ExcelFileClassifier().Classify(Workbook(rows));

        Assert.Equal(2, result.DuplicateRows.Count);
        Assert.All(result.DuplicateRows, duplicate => Assert.Equal(10, duplicate.RepresentativeRowNumber));
        Assert.Equal([10, 11, 12], result.NormalBatches.Single().SourceRowNumbers);
    }

    [Fact]
    public void AllowsMissingProductionDateAndDistinguishesDateAndUnitErrors()
    {
        var workbook = Workbook(
            Row(2, code: "P1", production: null),
            Row(3, code: "P2", production: "bad"),
            Row(4, code: "P3", expiry: null),
            Row(5, code: "P4", shelfLifeUnit: "m"),
            Row(6, code: "P5", shelfLifeUnit: " X "));

        var result = new ExcelFileClassifier().Classify(workbook);

        Assert.DoesNotContain(result.RowIssues, issue => issue.ExcelRowNumber == 2);
        Assert.Contains(result.RowIssues, issue => issue.ExcelRowNumber == 3 && issue.Code == "invalid_production_date");
        Assert.Contains(result.RowIssues, issue => issue.ExcelRowNumber == 4 && issue.Code == "missing_expiry_date");
        Assert.Contains(result.RowIssues, issue => issue.ExcelRowNumber == 5 && issue.Code == "invalid_shelf_life_unit");
        Assert.Contains(result.RowIssues, issue => issue.ExcelRowNumber == 6 && issue.Code == "invalid_shelf_life_unit");
    }

    [Fact]
    public void UsesBothBatchKeyShapes()
    {
        var result = new ExcelFileClassifier().Classify(Workbook(
            Row(2, code: "P", production: null),
            Row(3, code: "P", production: "2026-01-01")));

        Assert.Equal(2, result.BatchKeyCount);
        Assert.Contains(result.NormalBatches, batch => batch.BatchKey.ProductionDate is null);
        Assert.Contains(result.NormalBatches, batch => batch.BatchKey.ProductionDate == new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void ReportsEveryApprovedBatchConflictField()
    {
        var fields = new[]
        {
            ("商品条码", (Func<ExcelRowDto, ExcelRowDto>)(row => Row(row.ExcelRowNumber, code: row.ProductCode, barcode: "B2"))),
            ("商品名称", (Func<ExcelRowDto, ExcelRowDto>)(row => Row(row.ExcelRowNumber, code: row.ProductCode, name: "商品二"))),
            ("保质期", (Func<ExcelRowDto, ExcelRowDto>)(row => Row(row.ExcelRowNumber, code: row.ProductCode, shelfLife: "24"))),
            ("保质期单位", (Func<ExcelRowDto, ExcelRowDto>)(row => Row(row.ExcelRowNumber, code: row.ProductCode, shelfLifeUnit: "D"))),
            ("是否该做临期折扣", (Func<ExcelRowDto, ExcelRowDto>)(row => Row(row.ExcelRowNumber, code: row.ProductCode, discount: "是"))),
            ("该批次累计到货数量", (Func<ExcelRowDto, ExcelRowDto>)(row => Row(row.ExcelRowNumber, code: row.ProductCode, cumulativeArrival: "2")))
        };
        var rows = new List<ExcelRowDto>();
        for (var index = 0; index < fields.Length; index++)
        {
            var code = $"P{index}";
            var first = Row(index * 2 + 2, code: code);
            rows.Add(first);
            rows.Add(fields[index].Item2(Row(index * 2 + 3, code: code)));
        }

        var result = new ExcelFileClassifier().Classify(Workbook(rows.ToArray()));

        Assert.Equal(6, result.BatchConflicts.Count);
        Assert.All(result.BatchConflicts, conflict => Assert.Single(conflict.DifferingFields));
        Assert.Equal(
            fields.Select(field => field.Item1),
            result.BatchConflicts
                .OrderBy(conflict => conflict.BatchKey.ProductCode, StringComparer.Ordinal)
                .Select(conflict => conflict.DifferingFields[0]));
    }

    private static ExcelWorkbookDto Workbook(params ExcelRowDto[] rows) => new(
        "test.xlsx",
        "",
        "Sheet1",
        Array.Empty<string>(),
        rows);

    private static ExcelRowDto Row(
        int rowNumber,
        string? category = "食品",
        string? code = "P",
        string? barcode = "B",
        string? name = "商品",
        string? production = "2026-01-01",
        string? expiry = "2026-12-31",
        string? shelfLife = "12",
        string? shelfLifeUnit = "M",
        string? discount = "否",
        string? cumulativeArrival = "1",
        string? stock = "5") => new(
            rowNumber,
            category,
            code,
            barcode,
            name,
            production,
            expiry,
            shelfLife,
            shelfLifeUnit,
            discount,
            cumulativeArrival,
            stock);

    private static string FindRegressionWorkbook()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "test-data", "食品效期排查表.xlsx");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "test-data", "食品效期排查表.xlsx");
    }
}
