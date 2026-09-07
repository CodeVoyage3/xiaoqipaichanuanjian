using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S13T01StartupSafetyTests
{
    [Fact]
    public void NormalLaunchWaitsForShellAndMaintenanceBeforePreparingAnUpdate()
    {
        var app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "StoreExpiryInspector", "App.xaml.cs"));
        var normalStart = app.IndexOf("private async Task CompleteNormalLaunchAsync", StringComparison.Ordinal);
        var normal = app[normalStart..app.IndexOf("private void StartOrdinaryRuntime", normalStart, StringComparison.Ordinal)];
        var prepareStart = app.IndexOf("private async Task<bool> PrepareStartupUpdateAsync", StringComparison.Ordinal);
        var prepare = app[prepareStart..app.IndexOf("private void MainWindow_Closing", prepareStart, StringComparison.Ordinal)];
        Assert.True(normal.IndexOf("WaitForNormalLaunchTerminal", StringComparison.Ordinal) < normal.IndexOf("await shell.StartupLoadTask", StringComparison.Ordinal));
        Assert.True(normal.IndexOf("await shell.StartupLoadTask", StringComparison.Ordinal) < normal.IndexOf("PassStartupUpdatePolicyAsync", StringComparison.Ordinal));
        Assert.True(prepare.IndexOf("await BeginDatabaseMaintenanceAsync(keepWindowBlocked: true)", StringComparison.Ordinal) < prepare.IndexOf("new UpdateInstallationPreparer", StringComparison.Ordinal));
        Assert.Contains("EndDatabaseMaintenance(true, keepWindowBlocked: true)", prepare, StringComparison.Ordinal);
    }

    [Fact]
    public void ForcedGatesOfferOnlyUpdateAndExitAndExitOnlyRunsOnce()
    {
        var dialog = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs"));
        Assert.Contains("if (canUpdate)", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("canUpdate ? \"重新检查\"", dialog, StringComparison.Ordinal);
        Assert.Contains("var retry = new Button { Content = \"重试\"", dialog, StringComparison.Ordinal);
        Assert.Contains("var exiting = false", dialog, StringComparison.Ordinal);
        Assert.Contains("quit.Click += (_, _) => Exit()", dialog, StringComparison.Ordinal);
        Assert.Contains("if (!exiting && dialog.DialogResult is null)", dialog, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
