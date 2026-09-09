using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S15T01UpdateProgressUiTests
{
    [Fact]
    public void InitialPromptHasOnlyVersionsAndTheTwoActions()
    {
        var root = FindRoot();
        var dialog = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs"));
        var model = new UpdateNotificationViewModel(new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6), "long release body"), () => { }, () => { });

        Assert.True(model.IsInitial);
        Assert.False(model.CanCancel);
        Assert.Equal(string.Empty, model.StatusText);
        Assert.Equal("当前版本：v1.0.5", model.CurrentVersionText);
        Assert.Equal("最新版本：v1.0.6", model.LatestVersionText);
        Assert.DoesNotContain("ReleaseNotes", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticBanner", dialog, StringComparison.Ordinal);
        Assert.Contains("Visibility = Visibility.Collapsed", dialog, StringComparison.Ordinal);
        Assert.Contains("later.Visibility = update.Visibility = model.IsBusy", dialog, StringComparison.Ordinal);
        Assert.Contains("cancel.Visibility = model.CanCancel", dialog, StringComparison.Ordinal);
        Assert.Contains("if (!model.IsBusy) showLaterReminder = true", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibleProgressMapsOnlyDownloadUpdateAndInstall()
    {
        var cancelled = 0;
        var model = new UpdateNotificationViewModel(new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6)), () => { }, () => { });
        model.CancelRequested += () => cancelled++;

        model.Begin();
        Assert.True(model.IsUpdating);
        Assert.Equal("更新中…", model.StatusText);
        Assert.False(model.CancelCommand.CanExecute(null));

        model.Report(new("正在下载更新包", 35, 100));
        Assert.True(model.IsDownloading);
        Assert.Equal("下载中 35%", model.StatusText);
        Assert.DoesNotContain("字节", model.StatusText, StringComparison.Ordinal);
        Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.Equal(1, cancelled);

        foreach (var internalStage in new[] { "正在校验更新包", "manifest verified", "maintenance", "ACK", "journal" })
        {
            model.Report(new(internalStage, 100, 100));
            Assert.True(model.IsUpdating);
            Assert.Equal("更新中…", model.StatusText);
            Assert.False(model.CancelCommand.CanExecute(null));
        }

        model.BeginInstalling();
        Assert.True(model.IsInstalling);
        Assert.Equal("安装中…", model.StatusText);
        Assert.False(model.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void FailureIsGenericAndDoesNotExposeInternalOutcome()
    {
        var model = new UpdateNotificationViewModel(new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6)), () => { }, () => { });
        model.Begin();
        model.Complete(new(UpdatePackageOutcome.HashMismatch, "manifest verified failed"));

        Assert.True(model.HasFailure);
        Assert.Equal("更新失败，请稍后重试。", model.StatusText);
        Assert.DoesNotContain("manifest", model.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandoffSetsInstallStateImmediatelyBeforeUpdaterLaunch()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));

        Assert.True(app.IndexOf("installing();", StringComparison.Ordinal) < app.IndexOf("Process.Start(UpdaterLaunch.Create", StringComparison.Ordinal));
        Assert.Contains("model.BeginUpdating();", window, StringComparison.Ordinal);
        Assert.Contains("model.BeginInstalling", window, StringComparison.Ordinal);
        Assert.Contains("当前版本暂时可以继续使用，请尽快完成升级。旧版本后续可能停止支持，届时可能无法继续使用软件。", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs")), StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
