using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S4T09UatStaticAuditTests
{
    private static readonly string[] TraditionalChineseMarkers =
    [
        "\u9580", "\u6f22", "\u6a94", "\u532f", "\u78bc", "\u5eab", "\u8996", "\u9801",
        "\u5c0e", "\u78ba", "\u8a8d", "\u5fa9", "\u88fd", "\u689d", "\u932f", "\u8aa4",
        "\u8a0a", "\u9032", "\u9078", "\u64c7", "\u95dc", "\u9589", "\u81e8", "\u6578",
        "\u64da", "\u6ffe", "\u7e3d", "\u6aa2", "\u8abf", "\u72c0", "\u614b", "\u7e7c",
        "\u7e8c", "\u6b77", "\u5834", "\u9805", "\u767c", "\u8b8a", "\u842c", "\u8207",
        "\u5c08", "\u696d", "\u6771", "\u5169", "\u70ba", "\u9ebc", "\u7fa9", "\u6a02",
        "\u4e7e", "\u4e82", "\u65bc", "\u4e9e", "\u89aa", "\u5104", "\u50c5", "\u5f9e",
        "\u500b", "\u5011", "\u50f9", "\u512a", "\u50b7", "\u5152", "\u5167", "\u518a",
        "\u5beb", "\u8ecd", "\u8fb2", "\u51cd", "\u6de8", "\u5247", "\u5283", "\u5289",
        "\u5275", "\u5287", "\u52f8", "\u52d5", "\u52d9", "\u52dd", "\u52de", "\u52e2",
        "\u52f5", "\u5340", "\u91ab", "\u83ef", "\u5354", "\u55ae", "\u8ce3", "\u537b",
        "\u53b2", "\u53c3", "\u53e2", "\u958b", "\u5ee3", "\u5be6", "\u9019"
    ];

    [Fact]
    public void FixedChineseTextUsesSimplifiedCharactersInUserVisibleFixedSources()
    {
        var root = FindRepositoryRoot();
        var projectRoot = Path.Combine(root, "src", "StoreExpiryInspector");
        var files = Directory.EnumerateFiles(
                Path.Combine(projectRoot, "UI"),
                "*",
                SearchOption.AllDirectories)
            .Where(IsUiTextSource)
            .Concat(new[]
            {
                Path.Combine(projectRoot, "App.xaml"),
                Path.Combine(projectRoot, "App.xaml.cs")
            })
            .Concat(Directory.EnumerateFiles(projectRoot, "*.resx", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(projectRoot, "*.xaml", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "S4T09UatStaticAuditTests.cs",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(files);
        foreach (var path in files)
        {
            var contents = File.ReadAllText(path);
            foreach (var marker in TraditionalChineseMarkers)
            {
                Assert.False(
                    contents.Contains(marker, StringComparison.Ordinal),
                    $"固定文案源文件包含繁体字 U+{(int)marker[0]:X4}：{path}");
            }
        }
    }

    [Fact]
    public void UatFixesRemainScopedToDashboardImportAndExistingDtoSurface()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "UI",
            "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "UI",
            "MainWindow.xaml.cs"));
        var importViewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "UI",
            "ImportViewModel.cs"));

        Assert.Contains(
            "PreviewMouseWheel=\"DashboardDataGrid_PreviewMouseWheel\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("SelectionUnit=\"Cell\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ClipboardCopyMode=\"ExcludeHeader\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ProductBarcode}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"正在导入，请稍候…\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"True\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SetState(ImportPageState.Confirming, \"正在导入，请稍候…\"", importViewModel, StringComparison.Ordinal);
        Assert.Contains("var successMessage = \"导入成功\"", importViewModel, StringComparison.Ordinal);
        Assert.Contains("FindVisualChild<ScrollViewer>", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.AddHandler", codeBehind, StringComparison.Ordinal);
    }

    private static bool IsUiTextSource(string path) =>
        Path.GetExtension(path) is ".cs" or ".xaml" or ".resx";

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 StoreExpiryInspector 仓库根目录。");
    }
}
