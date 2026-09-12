using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S19T02DomesticDownloadFallbackTests
{
    private static readonly Version Current = new(1, 0, 9);
    private static readonly Version Latest = new(1, 0, 10);
    private static readonly CheckedRelease Release = new(Latest, 7, "v1.0.10", []);

    [Theory]
    [InlineData(UpdatePackageOutcome.NetworkUnavailable)]
    [InlineData(UpdatePackageOutcome.RateLimited)]
    public void OnlyNetworkPackageFailuresWithBoundGiteeMetadataOfferManualFallback(UpdatePackageOutcome outcome)
    {
        var github = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "GitHub notes", Release);
        var gitee = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "Gitee notes", ManualDownloadUrl: new Uri("https://pan.quark.cn/s/test"));

        Assert.True(MainWindow.CanUseDomesticFallback(new(outcome, "failed"), github, gitee));
    }

    [Fact]
    public void EveryOtherPackageOutcomeIsBlockedIncludingSizeMismatch()
    {
        var github = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "GitHub notes", Release);
        var gitee = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "Gitee notes", ManualDownloadUrl: new Uri("https://pan.quark.cn/s/test"));

        foreach (var outcome in Enum.GetValues<UpdatePackageOutcome>().Except([UpdatePackageOutcome.NetworkUnavailable, UpdatePackageOutcome.RateLimited]))
            Assert.False(MainWindow.CanUseDomesticFallback(new(outcome, "blocked"), github, gitee), outcome.ToString());
    }

    [Fact]
    public void VersionOrReleaseMismatchAndGiteeFailureAreBlocked()
    {
        var package = new UpdatePackageResult(UpdatePackageOutcome.NetworkUnavailable, "failed");
        var github = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "GitHub notes", Release);
        var valid = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "Gitee notes", ManualDownloadUrl: new Uri("https://pan.quark.cn/s/test"));

        Assert.False(MainWindow.CanUseDomesticFallback(package, github with { LatestVersion = new Version(1, 0, 11) }, valid));
        Assert.False(MainWindow.CanUseDomesticFallback(package, github, valid with { LatestVersion = new Version(1, 0, 11) }));
        Assert.False(MainWindow.CanUseDomesticFallback(package, github, valid with { Outcome = UpdateCheckOutcome.InvalidRemoteMetadata }));
        Assert.False(MainWindow.CanUseDomesticFallback(package, github, valid with { ManualDownloadUrl = null }));
    }

    [Fact]
    public void DomesticFallbackHasExactTextButtonsAndNoLaterReminderState()
    {
        var opened = 0;
        var model = new UpdateNotificationViewModel(new(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "GitHub notes", Release), () => { }, () => { });
        model.Begin();
        model.ShowDomesticFallback(() => opened++);
        model.ManualDownloadCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.False(model.IsBusy);
        Assert.True(model.IsDomesticFallback);
        Assert.False(model.IsInitial);
        Assert.Equal("当前网络访问更新服务器较慢或连接失败。\n可以重试在线更新，或通过网盘下载最新版。", model.StatusText);
        var dialog = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs"));
        Assert.Contains("在线更新下载失败", dialog, StringComparison.Ordinal);
        Assert.Contains("点击网盘下载", dialog, StringComparison.Ordinal);
        Assert.Contains("重试在线更新", dialog, StringComparison.Ordinal);
        Assert.Contains("if (model.IsDomesticFallback) showLaterReminder = false", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryClearsFallbackAndKeepsGitHubReleasePreparationPath()
    {
        var model = new UpdateNotificationViewModel(new(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "GitHub notes", Release), () => { }, () => { });
        model.ShowDomesticFallback(() => { });
        model.Begin();

        Assert.False(model.IsDomesticFallback);
        Assert.True(model.IsBusy);
        var window = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        Assert.Contains("PrepareAsync(result.Release, result.CurrentVersion", window, StringComparison.Ordinal);
        Assert.Contains("if (_updateWorker is { IsCompleted: false }) return;", window, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
