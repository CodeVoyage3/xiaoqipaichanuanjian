using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S4T10UiRefreshStaticAuditTests
{
    [Fact]
    public void RefreshKeepsTheApprovedShellPagesAndVisualTokens()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));

        foreach (var token in new[]
        {
            "CanvasBrush", "SurfaceBrush", "SurfaceSubtleBrush", "PrimaryTextBrush",
            "SecondaryTextBrush", "DisabledTextBrush", "BorderBrush", "PrimaryActionBrush",
            "PrimaryActionHoverBrush", "FocusRingBrush", "DangerBrush", "WarningTextBrush",
            "WarningSurfaceBrush", "SuccessBrush", "SuccessSurfaceBrush", "ErrorSurfaceBrush",
            "ReconfirmBrush", "ReconfirmSurfaceBrush"
        })
        {
            Assert.Contains($"x:Key=\"{token}\"", app, StringComparison.Ordinal);
        }

        Assert.Contains("FontFamily=\"Microsoft YaHei UI, Segoe UI\"", window, StringComparison.Ordinal);
        Assert.Contains("Language=\"zh-CN\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"门店效期排查软件\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"排查记录\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"设置\"", window, StringComparison.Ordinal);

        var pageOrder = new[] { "Text=\"首页\"", "Text=\"待排查任务\"", "Text=\"排查记录\"", "Text=\"数据导入\"", "Text=\"设置\"" };
        var previous = -1;
        foreach (var marker in pageOrder)
        {
            var current = window.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(current > previous, $"导航项顺序错误：{marker}");
            previous = current;
        }
    }

    [Fact]
    public void RefreshPreservesTableCopyPagingDetailAndImportGates()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        var viewModels = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "Stage4ViewModels.cs"));

        Assert.Contains("MinWidth=\"1024\"", window, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"600\"", window, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel=\"DashboardDataGrid_PreviewMouseWheel\"", window, StringComparison.Ordinal);
        Assert.Contains("ContentRoot.Margin", codeBehind, StringComparison.Ordinal);
        Assert.True(Count(window, "SelectionUnit=\"Cell\"") >= 2);
        Assert.True(Count(window, "ClipboardCopyMode=\"ExcludeHeader\"") >= 2);
        Assert.Contains("Binding=\"{Binding ProductBarcode}\"", window, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ProductCode}\"", window, StringComparison.Ordinal);
        Assert.Contains("FixedPageSize = 50", viewModels, StringComparison.Ordinal);
        Assert.Contains("Content=\"‹  上一页\"", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"下一页  ›\"", window, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", window, StringComparison.Ordinal);
        Assert.Contains("ShowSubmitFooter", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Detail.SubmitButtonText}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"正在导入，请稍候…\"", window, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"True\"", window, StringComparison.Ordinal);
    }

    private static int Count(string value, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }

        return count;
    }

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
