using System.Net;
using System.Text;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T03UpdateCheckTests
{
    [Theory]
    [InlineData("v1.0.10", "1.0.9", UpdateCheckOutcome.UpdateAvailable)]
    [InlineData("v1.10.0", "1.9.9", UpdateCheckOutcome.UpdateAvailable)]
    [InlineData("v1.0.0", "1.0.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("v0.9.9", "1.0.0", UpdateCheckOutcome.RemoteOlder)]
    public async Task NumericStableTagsMapToExpectedOutcome(string tag, string current, UpdateCheckOutcome expected)
    {
        var result = await CheckAsync(HttpStatusCode.OK, $"{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":false}}", current);
        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, UpdateCheckOutcome.NoPublishedRelease)]
    [InlineData(HttpStatusCode.Forbidden, UpdateCheckOutcome.RateLimited)]
    [InlineData((HttpStatusCode)429, UpdateCheckOutcome.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, UpdateCheckOutcome.NetworkUnavailable)]
    public async Task HttpOutcomesAreFailSafe(HttpStatusCode status, UpdateCheckOutcome expected)
    {
        var result = await CheckAsync(status, "{}");
        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"1.0.1\"}")]
    [InlineData("{\"tag_name\":\"v1.0\"}")]
    [InlineData("{\"tag_name\":\"v1.0.1\\n\",\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v01.0.1\",\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v1.0.1\",\"draft\":false}")]
    [InlineData("{\"tag_name\":\"v1.0.1\",\"draft\":\"false\",\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v1.0.1\",\"draft\":true}")]
    [InlineData("{\"tag_name\":\"v1.0.1\",\"prerelease\":true}")]
    public async Task InvalidOrUnstableMetadataNeverReportsUpdate(string body)
    {
        var result = await CheckAsync(HttpStatusCode.OK, body);
        Assert.Equal(UpdateCheckOutcome.InvalidRemoteMetadata, result.Outcome);
    }

    [Fact]
    public async Task MissingContentLengthStillHasByteLimitAndNotesAreBounded()
    {
        var huge = "{\"tag_name\":\"v1.0.1\",\"draft\":false,\"prerelease\":false,\"body\":\"" + new string('x', 300_000) + "\"}";
        var hugeResult = await CheckAsync(HttpStatusCode.OK, huge);
        Assert.Equal(UpdateCheckOutcome.InvalidRemoteMetadata, hugeResult.Outcome);
        var notes = await CheckAsync(HttpStatusCode.OK, "{\"tag_name\":\"v1.0.1\",\"draft\":false,\"prerelease\":false,\"body\":\"" + new string('x', 1500) + "\"}");
        Assert.Equal(1000, notes.ReleaseNotes!.Length);
    }

    [Fact]
    public async Task CallerCancellationAndSlowBodyAreSafe()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledResult = await new GitHubReleaseUpdateChecker(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK))).CheckAsync(new Version(1, 0, 0), cancelled.Token);
        Assert.Equal(UpdateCheckOutcome.Cancelled, cancelledResult.Outcome);

        var slow = new GitHubReleaseUpdateChecker(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) }), TimeSpan.FromMilliseconds(40));
        var slowResult = await slow.CheckAsync(new Version(1, 0, 0), CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.NetworkUnavailable, slowResult.Outcome);

        var aborted = await new GitHubReleaseUpdateChecker(new ThrowingHandler(new OperationCanceledException())).CheckAsync(new Version(1, 0, 0), CancellationToken.None);
        var brokenBody = await new GitHubReleaseUpdateChecker(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) })).CheckAsync(new Version(1, 0, 0), CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.NetworkUnavailable, aborted.Outcome);
        Assert.Equal(UpdateCheckOutcome.NetworkUnavailable, brokenBody.Outcome);
    }

    [Fact]
    public async Task RuntimeIsOnceOnlyAndNeverCompletesAfterDispose()
    {
        var called = 0;
        var release = new TaskCompletionSource<UpdateCheckResult>();
        using var runtime = new UpdateCheckRuntime(_ => release.Task, _ => called++);
        runtime.Start(); runtime.Start();
        runtime.Dispose();
        release.SetResult(UpdateCheckResult.From(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0)));
        await Task.Delay(20);
        Assert.Equal(0, called);
    }

    [Fact]
    public async Task RuntimeWaitsForCoreReadWithoutBlockingIt()
    {
        var coreRead = new TaskCompletionSource();
        var checks = 0;
        using var runtime = new UpdateCheckRuntime(_ =>
        {
            checks++;
            return Task.FromResult(UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0)));
        }, _ => { });
        runtime.StartAfter(coreRead.Task);
        await Task.Delay(20);
        Assert.Equal(0, checks);
        coreRead.SetResult();
        await Task.Delay(20);
        Assert.Equal(1, checks);
    }

    [Fact]
    public void MainWindowCanReachCoreReadyOnStaWithoutAnyRuntimeDatabase()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new StoreExpiryInspector.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                System.Windows.Application.LoadComponent(app, new Uri("/StoreExpiryInspector;component/App.xaml", UriKind.Relative));
                var shell = new ShellViewModel(
                    dashboardLoader: () => new StoreExpiryInspector.Application.Tasks.InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<StoreExpiryInspector.Application.Tasks.InspectionTaskListItem>()),
                    taskLoader: request => new StoreExpiryInspector.Application.Tasks.InspectionTaskSearchResult(Array.Empty<StoreExpiryInspector.Application.Tasks.InspectionTaskListItem>(), 0, request.Page, request.PageSize),
                    categoryLoader: () => [],
                    logException: _ => { });
                var window = new MainWindow(shell);
                window.Show();
                var deadline = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
                deadline.Tick += (_, _) => { failure = new TimeoutException("Synthetic WPF core readiness did not complete."); deadline.Stop(); app.Shutdown(); };
                deadline.Start();
                _ = shell.StartupLoadTask.ContinueWith(_ => window.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        Assert.False(shell.Dashboard.IsLoading); Assert.False(shell.PendingTasks.IsLoading);
                        Assert.False(shell.Dashboard.HasError); Assert.False(shell.PendingTasks.HasError); Assert.True(window.IsLoaded);
                        var update = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0), new Version(1, 0, 1));
                        var shown = 0;
                        Assert.True(window.TryShowUpdateAvailable(update, model => { shown++; model.DismissCommand.Execute(null); }));
                        Assert.False(window.TryShowUpdateAvailable(update, _ => shown++));
                        Assert.False(window.TryShowUpdateAvailable(UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0)), _ => shown++));
                        Assert.Equal(1, shown);
                        WpfDialogService.ShowUpdateAvailable(window, new UpdateNotificationViewModel(update, () => { }, () => { }));
                        var prompt = System.Windows.Application.Current.Windows.Cast<Window>().Single(item => item.Title == "发现新版本");
                        var buttons = (StackPanel)((StackPanel)prompt.Content).Children[^1];
                        Assert.Equal(Brushes.White, ((TextBlock)((Button)buttons.Children[1]).Content).Foreground);
                        prompt.Close();
                        window.Close();
                        Assert.False(window.TryShowUpdateAvailable(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0), new Version(1, 0, 2)), _ => shown++));
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { deadline.Stop(); app.Shutdown(); }
                }));
                app.Run();
            }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure);
    }

    private static async Task<UpdateCheckResult> CheckAsync(HttpStatusCode status, string body, string current = "1.0.0") =>
        await new GitHubReleaseUpdateChecker(new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) })).CheckAsync(Version.Parse(current), CancellationToken.None);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private class SlowStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { await Task.Delay(1000, cancellationToken); return 0; }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BrokenStream : SlowStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(new IOException("synthetic"));
    }
}
