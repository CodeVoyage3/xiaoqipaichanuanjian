using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StoreExpiryInspector.Infrastructure.Excel;

public sealed class ExcelTemplateReader
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

    public ExcelWorkbookDto Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An .xlsx file path is required.", nameof(filePath));
        }

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The source file must have an .xlsx extension.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The source .xlsx file was not found.", filePath);
        }

        var sourceFileSha256 = ComputeSha256(filePath);
        try
        {
            using var document = SpreadsheetDocument.Open(filePath, false);
            var workbookPart = document.WorkbookPart
                ?? throw new InvalidDataException("The .xlsx file has no workbook.");
            var workbook = workbookPart.Workbook
                ?? throw new InvalidDataException("The .xlsx file has no workbook content.");
            var sheet = workbook.Sheets?.Elements<Sheet>().FirstOrDefault()
                ?? throw new InvalidDataException("The .xlsx file has no worksheets.");
            var relationshipId = sheet.Id?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                throw new InvalidDataException("The first worksheet has no relationship.");
            }

            OpenXmlPart? relatedPart;
            try
            {
                relatedPart = workbookPart.GetPartById(relationshipId);
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                throw new InvalidDataException("The first worksheet is missing.", exception);
            }

            var worksheetPart = relatedPart as WorksheetPart
                ?? throw new InvalidDataException("The first worksheet is missing.");
            var sharedStrings = ReadSharedStrings(workbookPart);

            using var reader = OpenXmlReader.Create(worksheetPart);
            var rowNumberFallback = 1;
            var header = (HeaderMap?)null;
            var rows = new List<ExcelRowDto>();

            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
                {
                    continue;
                }

                var row = reader.LoadCurrentElement() as Row
                    ?? throw new InvalidDataException("The worksheet contains an invalid row.");
                var rowNumber = GetRowNumber(row, ref rowNumberFallback);
                var cells = ReadCells(row, sharedStrings);
                if (header is null)
                {
                    header = BuildHeader(cells);
                    continue;
                }

                if (cells.Values.All(static cell => cell.Value is null))
                {
                    continue;
                }

                rows.Add(CreateRow(rowNumber, cells, header));
            }

            if (header is null)
            {
                throw new InvalidDataException("The first worksheet has no header row.");
            }

            return new ExcelWorkbookDto(
                Path.GetFileName(filePath),
                sourceFileSha256,
                sheet.Name?.Value ?? string.Empty,
                Array.AsReadOnly(header.NormalizedHeaders),
                Array.AsReadOnly(rows.ToArray()));
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (DirectoryNotFoundException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (OpenXmlPackageException exception)
        {
            throw new InvalidDataException("The source file is not a readable .xlsx workbook.", exception);
        }
        catch (FileFormatException exception)
        {
            throw new InvalidDataException("The source file is not a readable .xlsx workbook.", exception);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The source file is not a readable .xlsx workbook.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException("The source file is not a readable .xlsx workbook.", exception);
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(stream, digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string[] ReadSharedStrings(WorkbookPart workbookPart)
    {
        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
        return sharedStringTable is null
            ? []
            : sharedStringTable.Elements<SharedStringItem>().Select(static item => item.InnerText).ToArray();
    }

    private static Dictionary<int, CellText> ReadCells(Row row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, CellText>();
        var nextColumn = 0;
        foreach (var cell in row.Elements<Cell>())
        {
            var column = ColumnIndex(cell.CellReference?.Value);
            if (column < 0)
            {
                column = nextColumn;
            }

            nextColumn = column + 1;
            var value = ReadCellValue(cell, sharedStrings);
            cells[column] = value;
        }

        return cells;
    }

    private static CellText ReadCellValue(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var dataType = cell.DataType?.Value;
        string? value;
        if (dataType == CellValues.SharedString)
        {
            var indexText = cell.CellValue?.Text;
            if (!int.TryParse(indexText, out var index) || index < 0 || index >= sharedStrings.Count)
            {
                throw new InvalidDataException("The workbook contains an invalid shared-string reference.");
            }

            value = sharedStrings[index];
        }
        else if (dataType == CellValues.InlineString)
        {
            value = cell.InlineString?.InnerText;
        }
        else
        {
            value = cell.CellValue?.Text;
        }

        return new CellText(
            value is { Length: > 0 } ? value : null,
            dataType is null || dataType == CellValues.Number);
    }

    private static HeaderMap BuildHeader(IReadOnlyDictionary<int, CellText> cells)
    {
        if (cells.Count == 0)
        {
            throw new InvalidDataException("The worksheet header row is empty.");
        }

        var orderedCells = cells.OrderBy(static pair => pair.Key).ToArray();
        var normalizedHeaders = new string[orderedCells.Length];
        var positionsByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var emptyColumns = new List<int>();
        for (var headerIndex = 0; headerIndex < orderedCells.Length; headerIndex++)
        {
            var (column, cell) = orderedCells[headerIndex];
            var normalized = cell.Value?.Trim() ?? string.Empty;
            normalizedHeaders[headerIndex] = normalized;
            if (normalized.Length == 0)
            {
                emptyColumns.Add(column + 1);
                continue;
            }

            if (!positionsByName.TryGetValue(normalized, out var positions))
            {
                positions = [];
                positionsByName.Add(normalized, positions);
            }

            positions.Add(column);
        }

        var duplicateHeaders = positionsByName
            .Where(static pair => pair.Value.Count > 1)
            .Select(static pair => pair.Key)
            .ToArray();
        var missingHeaders = RequiredHeaders
            .Where(required => !positionsByName.ContainsKey(required))
            .ToArray();
        if (emptyColumns.Count > 0 || duplicateHeaders.Length > 0 || missingHeaders.Length > 0)
        {
            var errors = new List<string>();
            if (emptyColumns.Count > 0)
            {
                errors.Add($"empty header column(s): {string.Join(", ", emptyColumns)}");
            }

            if (duplicateHeaders.Length > 0)
            {
                errors.Add($"duplicate normalized header(s): {string.Join(", ", duplicateHeaders)}");
            }

            if (missingHeaders.Length > 0)
            {
                errors.Add($"missing required column(s): {string.Join(", ", missingHeaders)}");
            }

            throw new InvalidDataException(string.Join("; ", errors));
        }

        var columns = positionsByName.ToDictionary(static pair => pair.Key, static pair => pair.Value[0], StringComparer.Ordinal);
        return new HeaderMap(normalizedHeaders, columns);
    }

    private static ExcelRowDto CreateRow(
        int rowNumber,
        IReadOnlyDictionary<int, CellText> cells,
        HeaderMap header)
    {
        string? Value(string name, bool identifier = false)
        {
            if (!cells.TryGetValue(header.Columns[name], out var cell) || cell.Value is null)
            {
                return null;
            }

            return identifier && cell.IsNumeric ? ExpandIntegerScientificValue(cell.Value) : cell.Value;
        }

        return new ExcelRowDto(
            rowNumber,
            Value("商品大类"),
            Value("商品编码", identifier: true),
            Value("商品条码", identifier: true),
            Value("商品名称"),
            Value("生产日期"),
            Value("有效日期"),
            Value("保质期"),
            Value("保质期单位"),
            Value("是否该做临期折扣"),
            Value("该批次累计到货数量"),
            Value("该商品门店库存总数"));
    }

    private static int GetRowNumber(Row row, ref int fallback)
    {
        var rowNumber = row.RowIndex?.Value;
        if (rowNumber is null || rowNumber.Value <= 0 || rowNumber.Value > int.MaxValue)
        {
            return fallback++;
        }

        fallback = checked((int)rowNumber.Value + 1);
        return (int)rowNumber.Value;
    }

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return -1;
        }

        var column = 0;
        var hasLetters = false;
        foreach (var character in cellReference)
        {
            if (character is >= 'a' and <= 'z')
            {
                column = checked(column * 26 + character - 'a' + 1);
                hasLetters = true;
                continue;
            }

            if (character is < 'A' or > 'Z')
            {
                break;
            }

            hasLetters = true;
            column = checked(column * 26 + character - 'A' + 1);
        }

        return hasLetters ? column - 1 : -1;
    }

    private static string ExpandIntegerScientificValue(string value)
    {
        var exponentMarker = value.IndexOfAny(['e', 'E']);
        if (exponentMarker < 0)
        {
            return value;
        }

        var mantissa = value[..exponentMarker];
        var exponentText = value[(exponentMarker + 1)..];
        if (!int.TryParse(exponentText, out var exponent) || exponent is > 100_000 or < -100_000)
        {
            return value;
        }

        var sign = string.Empty;
        if (mantissa.StartsWith('+') || mantissa.StartsWith('-'))
        {
            sign = mantissa[..1];
            mantissa = mantissa[1..];
        }

        var decimalPoint = mantissa.IndexOf('.');
        if (decimalPoint >= 0 && mantissa.IndexOf('.', decimalPoint + 1) >= 0)
        {
            return value;
        }

        var digits = decimalPoint >= 0
            ? mantissa.Remove(decimalPoint, 1)
            : mantissa;
        if (digits.Length == 0 || digits.Any(static character => character is < '0' or > '9'))
        {
            return value;
        }

        if (digits.All(static character => character == '0'))
        {
            return "0";
        }

        var decimalPosition = (decimalPoint >= 0 ? decimalPoint : digits.Length) + exponent;
        if (decimalPosition < 0)
        {
            return digits.Trim('0').Length == 0 ? "0" : value;
        }

        if (decimalPosition < digits.Length && digits[decimalPosition..].Any(static character => character != '0'))
        {
            return value;
        }

        var integerDigits = decimalPosition >= digits.Length
            ? digits + new string('0', decimalPosition - digits.Length)
            : digits[..decimalPosition];
        integerDigits = integerDigits.TrimStart('0');
        return sign + (integerDigits.Length == 0 ? "0" : integerDigits);
    }

    private sealed class HeaderMap
    {
        public HeaderMap(string[] normalizedHeaders, Dictionary<string, int> columns)
        {
            NormalizedHeaders = normalizedHeaders;
            Columns = columns;
        }

        public string[] NormalizedHeaders { get; }

        public Dictionary<string, int> Columns { get; }
    }

    private readonly record struct CellText(string? Value, bool IsNumeric);
}
