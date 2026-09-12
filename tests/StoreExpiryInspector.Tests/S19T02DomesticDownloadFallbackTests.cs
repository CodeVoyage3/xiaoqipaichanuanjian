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
    public async Task OnlyWhitelistedAndBoundPackageFailuresCallTheConfiguredGiteeDelegate()
    {
        var github = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "GitHub notes", Release);
        var gitee = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, Latest, "Gitee notes", ManualDownloadUrl: new Uri("https://pan.quark.cn/s/test"));
        foreach (var outcome in new[] { UpdatePackageOutcome.NetworkUnavailable, UpdatePackageOutcome.RateLimited })
        {
            var calls = 0;
            var result = await MainWindow.CheckDomesticFallbackAsync(new(outcome, "failed"), github, (_, _) => { calls++; return Task.FromResult(gitee); }, CancellationToken.None);
            Assert.Same(gitee, result);
            Assert.Equal(1, calls);
        }

        foreach (var outcome in Enum.GetValues<UpdatePackageOutcome>().Except([UpdatePackageOutcome.NetworkUnavailable, UpdatePackageOutcome.RateLimited]))
        {
            var calls = 0;
            var result = await MainWindow.CheckDomesticFallbackAsync(new(outcome, "blocked"), github, (_, _) => { calls++; return Task.FromResult(gitee); }, CancellationToken.None);
            Assert.Null(result);
            Assert.Equal(0, calls);
        }
    }

    [Fact]
    public async Task GitHubIdentityMismatchDoesNotCallTheConfiguredGiteeDelegate()
    {
        var github = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Current, new Version(1, 0, 11), "GitHub notes", Release);
        var calls = 0;

        var result = await MainWindow.CheckDomesticFallbackAsync(new(UpdatePackageOutcome.NetworkUnavailable, "failed"), github, (_, _) => { calls++; return Task.FromResult(UpdateCheckResult.From(UpdateCheckOutcome.UpdateAvailable, Current)); }, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, calls);
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
        Assert.Contains("title.Text = dialog.Title", dialog, StringComparison.Ordinal);
        Assert.Contains("later.IsDefault = later.IsCancel = !fallback", dialog, StringComparison.Ordinal);
        Assert.Contains("cancel.IsDefault = cancel.IsCancel = fallback", dialog, StringComparison.Ordinal);
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
        Assert.DoesNotContain("new GiteeManualUpdateChecker()", window, StringComparison.Ordinal);
        var app = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "App.xaml.cs"));
        Assert.Contains("new GiteeFallbackUpdateChecker(github.CheckAsync, gitee.CheckAsync)", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.ConfigureGiteeManualUpdateCheck(gitee.CheckAsync)", app, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
