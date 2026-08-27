using System.Globalization;

namespace StoreExpiryInspector.Infrastructure.Excel;

public static class ExcelDateParser
{
    private static readonly string[] TextFormats =
    [
        "yyyy-M-d",
        "yyyy-MM-dd",
        "yyyy/M/d",
        "yyyy/MM/dd"
    ];

    public static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (DateOnly.TryParseExact(
                text,
                TextFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return true;
        }

        const NumberStyles numericStyles =
            NumberStyles.AllowLeadingSign
            | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowExponent;
        if (!decimal.TryParse(text, numericStyles, CultureInfo.InvariantCulture, out var serial)
            || serial != decimal.Truncate(serial))
        {
            return false;
        }

        try
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate((double)serial));
            return true;
        }
        catch (ArgumentException)
        {
            date = default;
            return false;
        }
        catch (OverflowException)
        {
            date = default;
            return false;
        }
    }
}
