using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V111WpfRenderModeTests
{
    [Fact]
    public void Software_rendering_is_set_before_any_startup_window_path()
    {
        var app = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "App.xaml.cs"));
        var renderMode = app.IndexOf("RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;", StringComparison.Ordinal);

        Assert.True(renderMode >= 0);
        Assert.True(renderMode < app.IndexOf("InstallerPreflight.TryHandle", StringComparison.Ordinal));
        Assert.True(renderMode < app.IndexOf("WpfDialogService.Show", StringComparison.Ordinal));
        Assert.True(renderMode < app.IndexOf("new UI.MainWindow", StringComparison.Ordinal));
        Assert.DoesNotContain("Registry", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Intel", app, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
