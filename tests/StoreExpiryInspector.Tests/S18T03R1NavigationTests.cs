using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S18T03R1NavigationTests
{
    [Fact]
    public void CollapsedNavigationKeepsOnlyLightweightGroupSeparators()
    {
        var window = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));

        foreach (var name in new[] { "NavigationInspectionGroupSpacer", "NavigationDataGroupSpacer", "NavigationSystemGroupSpacer" })
        {
            Assert.Contains($"x:Name=\"{name}\"", window, StringComparison.Ordinal);
            Assert.Contains($"{name}.Visibility = _isNavigationCollapsed ? Visibility.Visible : Visibility.Collapsed;", code, StringComparison.Ordinal);
        }

        Assert.True(window.IndexOf("NavigationHomeButton", StringComparison.Ordinal) < window.IndexOf("NavigationInspectionGroupSpacer", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationHistoryButton", StringComparison.Ordinal) < window.IndexOf("NavigationDataGroupSpacer", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationBackupButton", StringComparison.Ordinal) < window.IndexOf("NavigationSystemGroupSpacer", StringComparison.Ordinal));
    }

    [Fact]
    public void NavigationSelectedStateUsesTheBoundBooleanAndOutranksHover()
    {
        var app = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var style = Block(app, "<Style x:Key=\"NavButtonStyle\"");

        Assert.Contains("<DataTrigger Binding=\"{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}\" Value=\"True\">", style, StringComparison.Ordinal);
        Assert.DoesNotContain("<Trigger Property=\"Tag\" Value=\"True\">", style, StringComparison.Ordinal);
        Assert.True(style.IndexOf("<Trigger Property=\"IsMouseOver\"", StringComparison.Ordinal) < style.IndexOf("<DataTrigger Binding=\"{Binding Tag", StringComparison.Ordinal));
        foreach (var binding in new[] { "IsHomeSectionVisible", "IsPendingTasksVisible", "IsTodayInspectionVisible", "IsHistoryVisible", "IsImportVisible", "IsBackupRestoreVisible" })
            Assert.Contains($"Tag=\"{{Binding {binding}}}\"", window, StringComparison.Ordinal);
    }

    private static string Block(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        var end = text.IndexOf("</Style>", start, StringComparison.Ordinal);
        return text[start..end];
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "StoreExpiryInspector.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
