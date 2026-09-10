using System.Windows;
using System.Windows.Media;

namespace StoreExpiryInspector.UI;

internal sealed class Ean13Barcode : FrameworkElement
{
    private const string Left = "0001101,0011001,0010011,0111101,0100011,0110001,0101111,0111011,0110111,0001011";
    private const string Right = "1110010,1100110,1101100,1000010,1011100,1001110,1010000,1000100,1001000,1110100";
    private const string Parity = "LLLLLL,LLGLGG,LLGGLG,LLGGGL,LGLLGG,LGGLLG,LGGGLL,LGLGLG,LGLGGL,LGGLGL";

    public static readonly DependencyProperty BarcodeProperty = DependencyProperty.Register(
        nameof(Barcode), typeof(string), typeof(Ean13Barcode), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Barcode
    {
        get => (string?)GetValue(BarcodeProperty);
        set => SetValue(BarcodeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(300, 80);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(new Point(), RenderSize);
        drawingContext.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(214, 220, 229)), 1), bounds);
        if (!TryEncode(Barcode, out var modules))
        {
            DrawCenteredText(drawingContext, "暂无可用商品条码", bounds, Brushes.Gray);
            return;
        }

        const int quietZone = 10;
        const double moduleWidth = 2;
        var barcodeWidth = (modules.Length + quietZone * 2) * moduleWidth;
        var left = bounds.Left + Math.Max(0, (bounds.Width - barcodeWidth) / 2);
        var top = bounds.Top + 5;
        var fullHeight = Math.Max(1, bounds.Height - 10);
        var normalHeight = Math.Max(1, fullHeight - 12);
        for (var index = 0; index < modules.Length; index++)
        {
            if (modules[index] != '1') continue;
            var guard = index < 3 || (index >= 45 && index < 50) || index >= 92;
            drawingContext.DrawRectangle(Brushes.Black, null, new Rect(left + (quietZone + index) * moduleWidth, top, moduleWidth, guard ? fullHeight : normalHeight));
        }
    }

    internal static bool TryEncode(string? barcode, out string modules)
    {
        modules = string.Empty;
        if (barcode is null || barcode.Length != 13 || barcode.Any(character => character is < '0' or > '9')) return false;
        var sum = 0;
        for (var index = 0; index < 12; index++) sum += (barcode[index] - '0') * (index % 2 == 0 ? 1 : 3);
        if ((10 - sum % 10) % 10 != barcode[12] - '0') return false;

        var left = Left.Split(',');
        var right = Right.Split(',');
        var parity = Parity.Split(',')[barcode[0] - '0'];
        var encoded = "101";
        for (var index = 1; index <= 6; index++)
        {
            var pattern = left[barcode[index] - '0'];
            encoded += parity[index - 1] == 'L' ? pattern : ReverseAndInvert(pattern);
        }
        encoded += "01010";
        for (var index = 7; index <= 12; index++) encoded += right[barcode[index] - '0'];
        modules = encoded + "101";
        return true;
    }

    private static string ReverseAndInvert(string value) => new(value.Reverse().Select(character => character == '0' ? '1' : '0').ToArray());

    private void DrawCenteredText(DrawingContext drawingContext, string text, Rect bounds, Brush brush)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(formatted, new Point(bounds.Left + (bounds.Width - formatted.Width) / 2, bounds.Top + (bounds.Height - formatted.Height) / 2));
    }
}
