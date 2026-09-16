using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S18T03NavigationTests
{
    [Fact]
    public void NavigationKeepsTheFrozenShellAndEightSemanticIcons()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));

        Assert.Contains("ShellColumn\" Width=\"220\"", window, StringComparison.Ordinal);
        Assert.Contains("new GridLength(72)", code, StringComparison.Ordinal);
        foreach (var key in new[] { "NavHomeIcon", "NavTasksIcon", "NavTodayInspectionIcon", "NavHistoryIcon", "NavProductCatalogIcon", "NavImportIcon", "NavBackupRestoreIcon", "NavSettingsIcon" })
            Assert.Contains($"x:Key=\"{key}\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NavigationBrandArea\" Margin=\"16,20,12,16\" HorizontalAlignment=\"Stretch\"", window, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", BrandArea(window), StringComparison.Ordinal);
        Assert.Contains("new Thickness(4, 20, 4, 16)", code, StringComparison.Ordinal);
        var iconStyle = StyleBlock(app, "<Style x:Key=\"NavIconPathStyle\"");
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\" />", iconStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("14,0", iconStyle, StringComparison.Ordinal);
        foreach (var name in new[] { "NavigationWorkspaceGroupText", "NavigationInspectionGroupText", "NavigationDataGroupText", "NavigationSystemGroupText" }) Assert.Contains(name, window, StringComparison.Ordinal);
        foreach (var name in new[] { "NavigationHomeButton", "NavigationTasksButton", "NavigationTodayInspectionButton", "NavigationHistoryButton", "NavigationProductCatalogButton", "NavigationImportButton", "NavigationBackupButton", "NavigationSettingsButton" }) Assert.Contains(name, window, StringComparison.Ordinal);
        Assert.True(window.IndexOf("NavigationHomeButton", StringComparison.Ordinal) < window.IndexOf("NavigationTasksButton", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationTasksButton", StringComparison.Ordinal) < window.IndexOf("NavigationTodayInspectionButton", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationTodayInspectionButton", StringComparison.Ordinal) < window.IndexOf("NavigationHistoryButton", StringComparison.Ordinal));
        foreach (var entry in new[]
        {
            ("NavigationHomeButton", "首页", "NavHomeIcon", "NavigationHomeText"),
            ("NavigationTasksButton", "待排查任务", "NavTasksIcon", "NavigationTasksText"),
            ("NavigationTodayInspectionButton", "今日排查", "NavTodayInspectionIcon", "NavigationTodayInspectionText"),
            ("NavigationHistoryButton", "排查历史", "NavHistoryIcon", "NavigationHistoryText"),
            ("NavigationProductCatalogButton", "商品明细", "NavProductCatalogIcon", "NavigationProductCatalogText"),
            ("NavigationImportButton", "数据导入", "NavImportIcon", "NavigationImportText"),
            ("NavigationBackupButton", "数据备份与恢复", "NavBackupRestoreIcon", "NavigationBackupText"),
            ("NavigationSettingsButton", "设置", "NavSettingsIcon", "NavigationSettingsText")
        })
        {
            var button = ButtonBlock(window, entry.Item1);
            Assert.Contains($"ToolTip=\"{entry.Item2}\"", button, StringComparison.Ordinal);
            Assert.Contains($"Data=\"{{StaticResource {entry.Item3}}}\"", button, StringComparison.Ordinal);
            Assert.Contains("Fill=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}\"", button, StringComparison.Ordinal);
            Assert.Contains("<Viewbox Width=\"22\" Height=\"22\" Stretch=\"Uniform\" VerticalAlignment=\"Center\">", button, StringComparison.Ordinal);
            Assert.Contains("Width=\"1024\" Height=\"1024\" Stretch=\"None\"", button, StringComparison.Ordinal);
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

    [Fact]
    public void NavigationSvgGeometriesParseWithNonzeroFillAndUniform22DipPaths()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = FindRoot();
                System.Xml.Linq.XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var app = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
                var icons = app.Root!.Element(ui + "Application.Resources")!.Elements()
                    .Where(element => element.Name == ui + "Geometry" || element.Name == ui + "GeometryGroup")
                    .Where(element => ((string?)element.Attribute(x + "Key"))?.StartsWith("Nav", StringComparison.Ordinal) == true)
                    .ToArray();
                var dictionary = new System.Xml.Linq.XElement(ui + "ResourceDictionary", new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", x), icons);
                var resources = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());

                foreach (var key in new[] { "NavHomeIcon", "NavTasksIcon", "NavTodayInspectionIcon", "NavHistoryIcon", "NavProductCatalogIcon", "NavImportIcon", "NavBackupRestoreIcon", "NavSettingsIcon" })
                {
                    var geometry = Assert.IsAssignableFrom<System.Windows.Media.Geometry>(resources[key]);
                    Assert.False(geometry.Bounds.IsEmpty);
                    Assert.Equal(System.Windows.Media.FillRule.Nonzero, geometry is System.Windows.Media.GeometryGroup group ? group.FillRule : ((System.Windows.Media.StreamGeometry)geometry).FillRule);
                    var path = new System.Windows.Shapes.Path { Data = geometry, Fill = System.Windows.Media.Brushes.MediumPurple, Width = 1024, Height = 1024, Stretch = System.Windows.Media.Stretch.None };
                    var icon = new System.Windows.Controls.Viewbox { Width = 22, Height = 22, Stretch = System.Windows.Media.Stretch.Uniform, Child = path };
                    icon.Measure(new System.Windows.Size(22, 22)); icon.Arrange(new System.Windows.Rect(0, 0, 22, 22));
                    Assert.Equal(new System.Windows.Size(22, 22), icon.RenderSize); Assert.Same(System.Windows.Media.Brushes.MediumPurple, path.Fill);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure);
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
