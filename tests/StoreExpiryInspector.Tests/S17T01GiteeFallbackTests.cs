using System.Net;
using System.Text;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S17T01GiteeFallbackTests
{
    [Fact]
    public async Task GiteeNewerVersionUsesFixedRawAndManualUrl()
    {
        Uri? requested = null;
        var checker = new GiteeManualUpdateChecker(new Handler(request =>
        {
            requested = request.RequestUri;
            return Json("{\"version\":\"1.0.8\",\"releaseNotes\":\"第一行\\n第二行\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}");
        }));

        var result = await checker.CheckAsync(new Version(1, 0, 7), CancellationToken.None);

        Assert.Equal("https://gitee.com/CodeVoyage3/xiaoqipaichanuanjian-update/raw/master/latest.json", requested!.AbsoluteUri);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal(new Version(1, 0, 8), result.LatestVersion);
        Assert.Equal("第一行\n第二行", result.ReleaseNotes);
        Assert.Equal("https://pan.quark.cn/s/test", result.ManualDownloadUrl!.AbsoluteUri);
        Assert.Null(result.Release);
    }

    [Theory]
    [InlineData("1.0.7", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.0.6", UpdateCheckOutcome.RemoteOlder)]
    public async Task GiteeSameOrOlderNeverOffersUpdate(string version, UpdateCheckOutcome outcome)
    {
        var result = await CheckGiteeAsync($"{{\"version\":\"{version}\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}}", new Version(1, 0, 7));
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.ManualDownloadUrl);
    }

    [Theory]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\"}")]
    [InlineData("{\"version\":1,\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":[],\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\",\"extra\":\"x\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"v1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"01.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"http://pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://example.invalid/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://user@pan.quark.cn/s/test\"}")]
    [InlineData("{\"version\":\"1.0.8\",\"releaseNotes\":\"notes\",\"manualDownloadUrl\":\"https://pan.quark.cn:444/s/test\"}")]
    [InlineData("not-json")]
    public async Task InvalidMetadataNeverOffersUpdate(string body)
    {
        var result = await CheckGiteeAsync(body, new Version(1, 0, 7));
        Assert.Equal(UpdateCheckOutcome.InvalidRemoteMetadata, result.Outcome);
    }

    [Fact]
    public async Task GiteeHttpErrorTimeoutAndSizeLimitAreSilentFailures()
    {
        var error = await new GiteeManualUpdateChecker(new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))).CheckAsync(new Version(1, 0, 7), CancellationToken.None);
        var timeout = await new GiteeManualUpdateChecker(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) }), TimeSpan.FromMilliseconds(20)).CheckAsync(new Version(1, 0, 7), CancellationToken.None);
        var oversized = await CheckGiteeAsync("{\"version\":\"1.0.8\",\"releaseNotes\":\"" + new string('x', 262_145) + "\",\"manualDownloadUrl\":\"https://pan.quark.cn/s/test\"}", new Version(1, 0, 7));

        Assert.Equal(UpdateCheckOutcome.InvalidRemoteMetadata, error.Outcome);
        Assert.Equal(UpdateCheckOutcome.InvalidRemoteMetadata, timeout.Outcome);
        Assert.Equal(UpdateCheckOutcome.InvalidRemoteMetadata, oversized.Outcome);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.UpdateAvailable)]
    [InlineData(UpdateCheckOutcome.UpToDate)]
    [InlineData(UpdateCheckOutcome.RemoteOlder)]
    [InlineData(UpdateCheckOutcome.NoPublishedRelease)]
    [InlineData(UpdateCheckOutcome.InvalidRemoteMetadata)]
    public async Task NonNetworkGitHubOutcomesNeverRequestGitee(UpdateCheckOutcome outcome)
    {
        var giteeCalls = 0;
        var checker = new GiteeFallbackUpdateChecker(
            (_, _) => Task.FromResult(UpdateCheckResult.From(outcome, new Version(1, 0, 7))),
            (_, _) => { giteeCalls++; return Task.FromResult(UpdateCheckResult.From(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7))); });

        var result = await checker.CheckAsync(new Version(1, 0, 7), CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(0, giteeCalls);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.NetworkUnavailable)]
    [InlineData(UpdateCheckOutcome.RateLimited)]
    public async Task OnlySpecifiedGitHubFailuresRequestGitee(UpdateCheckOutcome outcome)
    {
        var giteeCalls = 0;
        var fallback = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8), "notes", ManualDownloadUrl: new Uri("https://pan.quark.cn/s/test"));
        var checker = new GiteeFallbackUpdateChecker(
            (_, _) => Task.FromResult(UpdateCheckResult.From(outcome, new Version(1, 0, 7))),
            (_, _) => { giteeCalls++; return Task.FromResult(fallback); });

        var result = await checker.CheckAsync(new Version(1, 0, 7), CancellationToken.None);

        Assert.Same(fallback, result);
        Assert.Equal(1, giteeCalls);
    }

    [Fact]
    public async Task GitHubUpdateAvailablePreservesCheckedReleaseWithoutGitee()
    {
        var github = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8), "notes", new CheckedRelease(new Version(1, 0, 8), 7, "v1.0.8", ["update-manifest.json"]));
        var giteeCalls = 0;
        var checker = new GiteeFallbackUpdateChecker((_, _) => Task.FromResult(github), (_, _) => { giteeCalls++; return Task.FromResult(UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, new Version(1, 0, 7))); });

        var result = await checker.CheckAsync(new Version(1, 0, 7), CancellationToken.None);

        Assert.Same(github, result);
        Assert.NotNull(result.Release);
        Assert.Equal(0, giteeCalls);
    }

    [Fact]
    public void ManualPromptUsesDownloadButtonAndDoesNotUseAutomaticPreparation()
    {
        var result = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8), "notes", ManualDownloadUrl: new Uri("https://pan.quark.cn/s/test"));
        var model = new UpdateNotificationViewModel(result, () => { }, () => { });
        var root = FindRoot();
        var dialog = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));

        Assert.True(model.IsManualDownload);
        Assert.Equal("下载最新版", model.PrimaryActionText);
        Assert.Contains("Text = model.PrimaryActionText", dialog, StringComparison.Ordinal);
        Assert.Contains("if (result.ManualDownloadUrl is not null) OpenManualDownload", window, StringComparison.Ordinal);
        Assert.True(window.IndexOf("OpenManualDownload(result.ManualDownloadUrl)", StringComparison.Ordinal) < window.IndexOf("PrepareUpdateAsync(result", StringComparison.Ordinal));
        Assert.Contains("UseShellExecute = true", window, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubPromptKeepsImmediateUpdateButton()
    {
        var model = new UpdateNotificationViewModel(new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8), "notes", new CheckedRelease(new Version(1, 0, 8), 7, "v1.0.8", [])), () => { }, () => { });

        Assert.False(model.IsManualDownload);
        Assert.Equal("立即更新", model.PrimaryActionText);
    }

    private static async Task<UpdateCheckResult> CheckGiteeAsync(string body, Version current) =>
        await new GiteeManualUpdateChecker(new Handler(_ => Json(body))).CheckAsync(current, CancellationToken.None);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class SlowStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { await Task.Delay(1000, cancellationToken); return 0; }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
