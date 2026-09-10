using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S18T02Ean13BarcodeTests
{
    [Fact]
    public void EncodesOnlyValidEan13WithoutChangingTheSourceValue()
    {
        const string barcode = "6974396950994";

        Assert.True(Ean13Barcode.TryEncode(barcode, out var modules));
        Assert.Equal("10100010110010001001110101000010001011010111101010111010010011101110010111010011101001011100101", modules);
        Assert.False(Ean13Barcode.TryEncode("6974396950995", out _));
        Assert.False(Ean13Barcode.TryEncode(null, out _));
        Assert.False(Ean13Barcode.TryEncode("697439695099", out _));
        Assert.False(Ean13Barcode.TryEncode("69743969509A4", out _));
    }

    [Fact]
    public void DetailBindsTheBarcodeControlToTheExistingDetailBarcodeOnly()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "InspectionDetailViewModel.cs"));

        Assert.Contains("<ui:Ean13Barcode Barcode=\"{Binding Detail.ProductBarcode}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ui:Ean13Barcode Barcode=\"{Binding Detail.ProductCode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("public string ProductBarcode => _detail?.ProductBarcode ?? string.Empty;", viewModel, StringComparison.Ordinal);
        Assert.Contains("public string ProductCode => _detail?.ProductCode ?? string.Empty;", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedButtonTemplateAndDetailLayoutKeepTheS18T02Contracts()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));

        Assert.Contains("<ContentPresenter.Resources>", app, StringComparison.Ordinal);
        Assert.Contains("RelativeSource={RelativeSource AncestorType=Button}", app, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"PrimaryButtonStyle\"", app, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"DangerButtonStyle\"", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"White\"", app, StringComparison.Ordinal);
        Assert.Contains("DisabledTextBrush", app, StringComparison.Ordinal);
        Assert.Contains("<ui:Ean13Barcode Barcode=\"{Binding Detail.ProductBarcode}\" Width=\"300\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TextBox Text=\"{Binding Detail.ProductBarcode, Mode=OneWay}\" Style=\"{StaticResource ReadOnlyIdentityTextBoxStyle}\" Focusable=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Center\" TextAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"ReadOnlyIdentityTextBoxStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"IsReadOnly\" Value=\"True\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", xaml, StringComparison.Ordinal);
        Assert.Equal(3, xaml.Split("Background=\"{DynamicResource TableDividerBrush}\"").Length - 1);
        Assert.DoesNotContain("Text=\"商品条码：\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"排查信息\" FontSize=\"16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"排查人\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"排查日期\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Detail.HasValidEan13", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailIdentityGroupIsVerticallyBalancedInTheLeftColumn()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));

        Assert.Matches("<StackPanel VerticalAlignment=\"Center\">\\s*<TextBlock Text=\"\\{Binding Detail\\.ProductName\\}\"", xaml);
        Assert.Contains("Text=\"商品编码：\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"商品条码：\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BarcodeRendererHasNoControlOwnedOuterBorder()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "Ean13Barcode.cs"));

        Assert.DoesNotContain("new Pen(new SolidColorBrush(Color.FromRgb(214, 220, 229)), 1)", source, StringComparison.Ordinal);
        Assert.Contains("const int quietZone = 10;", source, StringComparison.Ordinal);
        Assert.Contains("const double moduleWidth = 2;", source, StringComparison.Ordinal);
        Assert.Contains("index < 3 || (index >= 45 && index < 50) || index >= 92", source, StringComparison.Ordinal);
        Assert.Contains("if (!TryEncode(Barcode, out var modules))", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
