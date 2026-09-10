using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S18T03NavigationTests
{
    [Fact]
    public void NavigationKeepsTheFrozenShellAndSevenSemanticIcons()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));

        Assert.Contains("ShellColumn\" Width=\"220\"", window, StringComparison.Ordinal);
        Assert.Contains("new GridLength(72)", code, StringComparison.Ordinal);
        Assert.Contains("<Geometry x:Key=\"DatabaseIcon\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NavigationBrandArea\" Margin=\"16,20,12,16\" HorizontalAlignment=\"Stretch\"", window, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", BrandArea(window), StringComparison.Ordinal);
        Assert.Contains("new Thickness(4, 20, 4, 16)", code, StringComparison.Ordinal);
        var iconStyle = StyleBlock(app, "<Style x:Key=\"NavIconPathStyle\"");
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\" />", iconStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("14,0", iconStyle, StringComparison.Ordinal);
        foreach (var name in new[] { "NavigationWorkspaceGroupText", "NavigationInspectionGroupText", "NavigationDataGroupText", "NavigationSystemGroupText" }) Assert.Contains(name, window, StringComparison.Ordinal);
        foreach (var name in new[] { "NavigationHomeButton", "NavigationTasksButton", "NavigationTodayInspectionButton", "NavigationHistoryButton", "NavigationImportButton", "NavigationBackupButton", "NavigationSettingsButton" }) Assert.Contains(name, window, StringComparison.Ordinal);
        Assert.True(window.IndexOf("NavigationHomeButton", StringComparison.Ordinal) < window.IndexOf("NavigationTasksButton", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationTasksButton", StringComparison.Ordinal) < window.IndexOf("NavigationTodayInspectionButton", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationTodayInspectionButton", StringComparison.Ordinal) < window.IndexOf("NavigationHistoryButton", StringComparison.Ordinal));
        foreach (var entry in new[]
        {
            ("NavigationHomeButton", "首页", "HomeIcon", "NavigationHomeText"),
            ("NavigationTasksButton", "待排查任务", "ClipboardIcon", "NavigationTasksText"),
            ("NavigationTodayInspectionButton", "今日排查", "CalendarIcon", "NavigationTodayInspectionText"),
            ("NavigationHistoryButton", "排查历史", "ClockIcon", "NavigationHistoryText"),
            ("NavigationImportButton", "数据导入", "ImportIcon", "NavigationImportText"),
            ("NavigationBackupButton", "数据备份与恢复", "DatabaseIcon", "NavigationBackupText"),
            ("NavigationSettingsButton", "设置", "GearIcon", "NavigationSettingsText")
        })
        {
            var button = ButtonBlock(window, entry.Item1);
            Assert.Contains($"ToolTip=\"{entry.Item2}\"", button, StringComparison.Ordinal);
            Assert.Contains($"Data=\"{{StaticResource {entry.Item3}}}\"", button, StringComparison.Ordinal);
            Assert.Contains($"x:Name=\"{entry.Item4}\" Margin=\"14,0,0,0\"", button, StringComparison.Ordinal);
            Assert.Contains($"{entry.Item4}.Visibility = textVisibility;", code, StringComparison.Ordinal);
        }
        foreach (var name in new[] { "NavigationBrandText", "NavigationWorkspaceGroupText", "NavigationInspectionGroupText", "NavigationDataGroupText", "NavigationSystemGroupText", "NavigationVersionText" }) Assert.Contains($"{name}.Visibility = textVisibility;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationPreferenceUsesTheRuntimeRootAndFailsOpen()
    {
        var code = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        Assert.Contains("NavigationStateFileName = \"ui-navigation-state.txt\"", code, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(RuntimeDataRoot.RootDirectory, NavigationStateFileName)", code, StringComparison.Ordinal);
        Assert.Contains("LoadNavigationCollapsedState()", code, StringComparison.Ordinal);
        Assert.Contains("SaveNavigationCollapsedState();", code, StringComparison.Ordinal);
        Assert.Contains("return false;", code, StringComparison.Ordinal);
    }

    private static string ButtonBlock(string xaml, string name)
    {
        var start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</Button>", start, StringComparison.Ordinal);
        return xaml[start..end];
    }

    private static string StyleBlock(string xaml, string marker)
    {
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        return xaml[start..end];
    }

    private static string BrandArea(string xaml)
    {
        var start = xaml.IndexOf("x:Name=\"NavigationBrandArea\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</Grid>", start, StringComparison.Ordinal);
        return xaml[start..end];
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "StoreExpiryInspector.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
