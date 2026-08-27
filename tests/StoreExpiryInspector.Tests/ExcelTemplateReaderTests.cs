using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ExcelTemplateReaderTests
{
    private static readonly string[] RequiredHeaders =
    [
        "商品大类",
        "商品编码",
        "商品条码",
        "商品名称",
        "生产日期",
        "有效日期",
        "保质期",
        "保质期单位",
        "是否该做临期折扣",
        "该批次累计到货数量",
        "该商品门店库存总数"
    ];

    [Fact]
    public void ReadsTrimmedHeadersSparseRowsAndAllSupportedCellKinds()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "sparse.xlsx");
        try
        {
            var headers = RequiredHeaders
                .Select((header, index) => index == 0 ? $"  {header}  " : header)
                .ToArray();
            var sharedStrings = headers
                .Concat(["食品", "M", "商品二"])
                .ToArray();
            WriteWorkbook(
                path,
                sharedStrings,
                HeaderRow(headers),
                new TestRow(
                    2,
                    SharedCell("A2", 11),
                    InlineCell("B2", "001234567890"),
                    TextCell("C2", "000987654321"),
                    TextCell("D2", "商品一"),
                    InlineCell("F2", "2026-12-31"),
                    NumberCell("G2", "12"),
                    SharedCell("H2", 12),
                    BooleanCell("I2", "1"),
                    NumberCell("J2", "7"),
                    NumberCell("K2", "20")),
                new TestRow(
                    3,
                    SharedCell("A3", 11),
                    NumberCell("B3", "123456789012345"),
                    ExplicitNumberCell("C3", "1.23456789012345E+14"),
                    SharedCell("D3", 13),
                    InlineCell("E3", "2026-01-01"),
                    TextCell("F3", "2026-12-31"),
                    NumberCell("G3", "9"),
                    SharedCell("H3", 12),
                    TextCell("I3", "否"),
                    NumberCell("J3", "3"),
                    NumberCell("K3", "4")),
                new TestRow(4, EmptyCell("A4"), EmptyCell("K4")),
                new TestRow(5, TextCell("B5", "1E+14"), TextCell("C5", "1E+14")));

            var result = new ExcelTemplateReader().Read(path);

            Assert.Equal(headers.Length, result.NormalizedHeaders.Count);
            Assert.Equal(RequiredHeaders, result.NormalizedHeaders);
            Assert.Equal(3, result.Rows.Count);

            var first = result.Rows[0];
            Assert.Equal(2, first.ExcelRowNumber);
            Assert.Equal("食品", first.ProductCategory);
            Assert.Equal("001234567890", first.ProductCode);
            Assert.Equal("000987654321", first.ProductBarcode);
            Assert.Equal("商品一", first.ProductName);
            Assert.Null(first.ProductionDate);
            Assert.Equal("2026-12-31", first.ExpiryDate);
            Assert.Equal("12", first.ShelfLife);
            Assert.Equal("M", first.ShelfLifeUnit);
            Assert.Equal("1", first.IsNearExpiryDiscountRequired);
            Assert.Equal("7", first.CumulativeArrivalQuantity);
            Assert.Equal("20", first.StoreStockQuantity);

            var second = result.Rows[1];
            Assert.Equal(3, second.ExcelRowNumber);
            Assert.Equal("123456789012345", second.ProductCode);
            Assert.Equal("123456789012345", second.ProductBarcode);
            Assert.Equal("商品二", second.ProductName);

            var third = result.Rows[2];
            Assert.Equal(5, third.ExcelRowNumber);
            Assert.Equal("1E+14", third.ProductCode);
            Assert.Equal("1E+14", third.ProductBarcode);

            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void RejectsTrimmedDuplicateHeadersWithTheNormalizedName()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "duplicate.xlsx");
        try
        {
            var headers = RequiredHeaders.Append(" 商品编码 ").ToArray();
            WriteWorkbook(path, headers, HeaderRow(headers));

            var exception = Assert.Throws<InvalidDataException>(() => new ExcelTemplateReader().Read(path));

            Assert.Contains("duplicate normalized header", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("商品编码", exception.Message);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void ReportsAllMissingRequiredHeadersAtOnce()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "missing.xlsx");
        try
        {
            var headers = new[] { "商品大类", "商品编码" };
            WriteWorkbook(path, headers, HeaderRow(headers));

            var exception = Assert.Throws<InvalidDataException>(() => new ExcelTemplateReader().Read(path));

            foreach (var missing in RequiredHeaders.Skip(2))
            {
                Assert.Contains(missing, exception.Message);
            }
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void RejectsTrimmedEmptyHeader()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "empty-header.xlsx");
        try
        {
            var headers = RequiredHeaders.ToArray();
            headers[3] = "   ";
            WriteWorkbook(path, headers, HeaderRow(headers));

            var exception = Assert.Throws<InvalidDataException>(() => new ExcelTemplateReader().Read(path));

            Assert.Contains("empty header", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("商品名称", exception.Message);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void RejectsMissingCorruptAndWorkbooksWithoutWorksheets()
    {
        var directory = CreateTempDirectory();
        var missingPath = Path.Combine(directory, "missing.xlsx");
        var corruptPath = Path.Combine(directory, "corrupt.xlsx");
        var noSheetPath = Path.Combine(directory, "no-sheet.xlsx");
        try
        {
            Assert.Throws<FileNotFoundException>(() => new ExcelTemplateReader().Read(missingPath));

            File.WriteAllBytes(corruptPath, [0x01, 0x02, 0x03, 0x04]);
            Assert.Throws<InvalidDataException>(() => new ExcelTemplateReader().Read(corruptPath));

            WriteWorkbook(noSheetPath, [], includeWorksheet: false);
            var exception = Assert.Throws<InvalidDataException>(() => new ExcelTemplateReader().Read(noSheetPath));
            Assert.Contains("no worksheets", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void ReadsTheUnmodifiedRegressionWorkbook()
    {
        var path = FindRegressionWorkbook();
        Assert.True(File.Exists(path), $"Regression workbook was not copied to {path}");

        var beforeBytes = File.ReadAllBytes(path);
        var beforeHash = Convert.ToHexString(SHA256.HashData(beforeBytes)).ToLowerInvariant();
        var beforeLength = beforeBytes.Length;
        var beforeLastWriteUtc = File.GetLastWriteTimeUtc(path);

        var result = new ExcelTemplateReader().Read(path);

        var afterBytes = File.ReadAllBytes(path);
        var afterHash = Convert.ToHexString(SHA256.HashData(afterBytes)).ToLowerInvariant();
        Assert.Equal("食品效期排查表.xlsx", result.SourceFileName);
        Assert.Equal("20fe1898dba98f48bcd8b83673f2001fe3ed0dfc01a89aa4ebdff9d1af6cacce", beforeHash);
        Assert.Equal(beforeHash, result.SourceFileSha256);
        Assert.Matches("^[0-9a-f]{64}$", result.SourceFileSha256);
        Assert.Equal(beforeHash, afterHash);
        Assert.Equal(397308, beforeLength);
        Assert.Equal(397308, afterBytes.Length);
        Assert.Equal(beforeLastWriteUtc, File.GetLastWriteTimeUtc(path));
        Assert.Equal("Sheet1", result.WorksheetName);
        Assert.Equal(3712, result.Rows.Count);
        Assert.Equal(2, result.Rows[0].ExcelRowNumber);
        Assert.Equal(3713, result.Rows[^1].ExcelRowNumber);
        Assert.Equal(1, result.NormalizedHeaders.Count(header => header == "该商品门店库存总数"));
        Assert.Equal(806, result.Rows.Select(row => row.ProductCode).Where(code => code is not null).Distinct().Count());
        Assert.Equal("15190400110028", result.Rows[0].ProductCode);
        Assert.Equal("6974098813108", result.Rows[0].ProductBarcode);
        Assert.Equal("KKV×星期零·陈皮梅子豆腐100g", result.Rows[0].ProductName);
        Assert.IsType<string>(result.Rows[0].ProductCode);
        Assert.IsType<string>(result.Rows[0].ProductBarcode);
    }

    private static TestRow HeaderRow(IReadOnlyList<string> headers)
    {
        var cells = headers
            .Select((_, index) => SharedCell(ColumnName(index) + "1", index))
            .ToArray();
        return new TestRow(1, cells);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorExcelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

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

    private static void DeleteTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteWorkbook(
        string path,
        IReadOnlyList<string> sharedStrings,
        params TestRow[] rows)
    {
        WriteWorkbook(path, sharedStrings, true, rows);
    }

    private static void WriteWorkbook(
        string path,
        IReadOnlyList<string> sharedStrings,
        bool includeWorksheet,
        params TestRow[] rows)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", ContentTypesXml(includeWorksheet, sharedStrings.Count > 0));
        AddEntry(archive, "_rels/.rels", RootRelationshipsXml());
        AddEntry(archive, "xl/workbook.xml", WorkbookXml(includeWorksheet));
        AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml(includeWorksheet, sharedStrings.Count > 0));

        if (sharedStrings.Count > 0)
        {
            AddEntry(archive, "xl/sharedStrings.xml", SharedStringsXml(sharedStrings));
        }

        if (includeWorksheet)
        {
            AddEntry(archive, "xl/worksheets/sheet1.xml", WorksheetXml(rows));
        }
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string ContentTypesXml(bool includeWorksheet, bool includeSharedStrings)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var overrides = new List<XElement>
        {
            new(ns + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"))
        };
        if (includeWorksheet)
        {
            overrides.Add(new XElement(ns + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }

        if (includeSharedStrings)
        {
            overrides.Add(new XElement(ns + "Override", new XAttribute("PartName", "/xl/sharedStrings.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml")));
        }

        return Xml(new XElement(ns + "Types", new XElement(ns + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ns + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), overrides));
    }

    private static string RootRelationshipsXml()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XElement(ns + "Relationships", new XElement(ns + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static string WorkbookXml(bool includeWorksheet)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheets = includeWorksheet
            ? new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", "Sheet1"), new XAttribute("sheetId", "1"), new XAttribute(relationships + "id", "rId1")))
            : new XElement(ns + "sheets");
        return Xml(new XElement(ns + "workbook", sheets));
    }

    private static string WorkbookRelationshipsXml(bool includeWorksheet, bool includeSharedStrings)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = new List<XElement>();
        if (includeWorksheet)
        {
            relationships.Add(new XElement(ns + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")));
        }

        if (includeSharedStrings)
        {
            relationships.Add(new XElement(ns + "Relationship", new XAttribute("Id", "rIdShared"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings"), new XAttribute("Target", "sharedStrings.xml")));
        }

        return Xml(new XElement(ns + "Relationships", relationships));
    }

    private static string SharedStringsXml(IReadOnlyList<string> values)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return Xml(new XElement(ns + "sst", new XAttribute("count", values.Count), new XAttribute("uniqueCount", values.Count), values.Select(value => new XElement(ns + "si", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)))));
    }

    private static string WorksheetXml(IReadOnlyList<TestRow> rows)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rowElements = rows.Select(row => new XElement(ns + "row", new XAttribute("r", row.Number), row.Cells.Select(CellXml)));
        return Xml(new XElement(ns + "worksheet", new XElement(ns + "sheetData", rowElements)));
    }

    private static XElement CellXml(TestCell cell)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var attributes = new List<XAttribute> { new("r", cell.Reference) };
        if (cell.Type is not null)
        {
            attributes.Add(new XAttribute("t", cell.Type));
        }

        if (cell.Type == "inlineStr")
        {
            return new XElement(ns + "c", attributes, new XElement(ns + "is", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), cell.Value ?? string.Empty)));
        }

        return cell.Value is null
            ? new XElement(ns + "c", attributes)
            : new XElement(ns + "c", attributes, new XElement(ns + "v", cell.Value));
    }

    private static string Xml(XElement element) => new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), element).ToString(SaveOptions.DisableFormatting);

    private static TestCell SharedCell(string reference, int index) => new(reference, "s", index.ToString());

    private static TestCell InlineCell(string reference, string value) => new(reference, "inlineStr", value);

    private static TestCell TextCell(string reference, string value) => new(reference, "str", value);

    private static TestCell NumberCell(string reference, string value) => new(reference, null, value);

    private static TestCell ExplicitNumberCell(string reference, string value) => new(reference, "n", value);

    private static TestCell BooleanCell(string reference, string value) => new(reference, "b", value);

    private static TestCell EmptyCell(string reference) => new(reference, null, null);

    private static string ColumnName(int zeroBasedColumn)
    {
        var value = zeroBasedColumn + 1;
        var name = string.Empty;
        while (value > 0)
        {
            value--;
            name = (char)('A' + value % 26) + name;
            value /= 26;
        }

        return name;
    }

    private sealed record TestRow(uint Number, params TestCell[] Cells);

    private sealed record TestCell(string Reference, string? Type, string? Value);
}
