using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Infrastructure.Logging;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S6T03TrayAndReminderSchedulerTests
{
    private static readonly DateTime TodayAtNine = new(2026, 8, 31, 9, 0, 0);

    [Fact]
    public void BeforeReminderRunsBusinessCheckWithoutNotificationAndSchedulesDueTime()
    {
        using var fixture = new SchedulerFixture(
            TodayAtNine,
            Result(DailyReminderStatuses.NotDue));

        fixture.Scheduler.Start();

        Assert.Equal(1, fixture.RunCount);
        Assert.Equal(new DateTime(2026, 8, 31, 10, 0, 0), fixture.Scheduler.NextCheckAt);
    }

    [Fact]
    public void DueReminderRunsOnceAndSuccessfulRecordSchedulesNextBusinessDay()
    {
        using var fixture = new SchedulerFixture(
            TodayAtNine.AddHours(1),
            Result(DailyReminderStatuses.Due, attempted: true, succeeded: true, recorded: true));

        fixture.Scheduler.Start();
        fixture.Scheduler.Start();

        Assert.Equal(1, fixture.RunCount);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), fixture.Scheduler.NextCheckAt);
    }

    [Theory]
    [InlineData(DailyReminderStatuses.AlreadyRemindedToday)]
    [InlineData(DailyReminderStatuses.NoItems)]
    [InlineData(DailyReminderStatuses.ClockRollback)]
    public void CompletedDailyStatesScheduleTheNextBusinessDay(string status)
    {
        var now = TodayAtNine.AddHours(2);

        var next = DailyReminderScheduler.CalculateNextCheck(now, 600, Result(status));

        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), next);
    }

    [Fact]
    public void NotificationOrRecordingFailureRetriesWithoutMarkingSchedulerComplete()
    {
        var now = TodayAtNine.AddHours(1);

        var next = DailyReminderScheduler.CalculateNextCheck(
            now,
            600,
            Result(DailyReminderStatuses.Due, attempted: true));

        Assert.Equal(now.AddMinutes(15), next);
    }

    [Fact]
    public void SchedulerExceptionIsLoggedAndLeavesSchedulerRunningForRetry()
    {
        using var fixture = new SchedulerFixture(TodayAtNine, Result("unused"), throws: true);

        fixture.Scheduler.Start();

        Assert.True(fixture.Scheduler.IsRunning);
        Assert.Equal(TodayAtNine.AddMinutes(15), fixture.Scheduler.NextCheckAt);
        Assert.Contains("daily_reminder_scheduler_failed", File.ReadAllText(fixture.LogPath));
    }

    [Fact]
    public void StopCancelsSchedulerLifecycle()
    {
        using var fixture = new SchedulerFixture(TodayAtNine, Result(DailyReminderStatuses.NotDue));
        fixture.Scheduler.Start();

        fixture.Scheduler.Stop();

        Assert.False(fixture.Scheduler.IsRunning);
    }

    [Fact]
    public void CrossMidnightUsesTheConfiguredTimeOnTheNextLocalDay()
    {
        var now = new DateTime(2026, 8, 31, 23, 59, 59);

        var next = DailyReminderScheduler.CalculateNextCheck(
            now,
            600,
            Result(DailyReminderStatuses.AlreadyRemindedToday));

        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), next);
    }

    [Fact]
    public void AppLifecycleHidesOneMainWindowAndExplicitExitStopsRuntime()
    {
        var app = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "StoreExpiryInspector",
            "App.xaml.cs"));

        Assert.Contains("ShutdownMode.OnExplicitShutdown", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.Closing += MainWindow_Closing", app, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true", app, StringComparison.Ordinal);
        Assert.Contains("MainWindow.Hide()", app, StringComparison.Ordinal);
        Assert.Contains("MainWindow.Show()", app, StringComparison.Ordinal);
        Assert.Contains("MainWindow.WindowState = WindowState.Normal", app, StringComparison.Ordinal);
        Assert.Contains("MainWindow.Activate()", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new MainWindow", app, StringComparison.Ordinal);
        Assert.Contains("_explicitExit = true", app, StringComparison.Ordinal);
        Assert.Contains("StopRuntime()", app, StringComparison.Ordinal);
        Assert.Contains("_reminderScheduler?.Dispose()", app, StringComparison.Ordinal);
        Assert.Contains("_trayIcon?.Dispose()", app, StringComparison.Ordinal);
        Assert.Contains("Shutdown()", app, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeTrayProvidesOpenExitDoubleClickAndDeterministicRemoval()
    {
        var tray = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "StoreExpiryInspector",
            "UI",
            "WindowsTrayIcon.cs"));

        Assert.Contains("Shell_NotifyIcon", tray, StringComparison.Ordinal);
        Assert.Contains("Header = \"打开\"", tray, StringComparison.Ordinal);
        Assert.Contains("Header = \"退出应用\"", tray, StringComparison.Ordinal);
        Assert.Contains("LeftButtonDoubleClick", tray, StringComparison.Ordinal);
        Assert.Contains("DeleteIcon", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleInstanceAndDependencyBoundariesRemainMinimal()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var runtimeDataRoot = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "Infrastructure", "RuntimeDataRoot.cs"));
        var scheduler = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "Application",
            "Reminders",
            "DailyReminderScheduler.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "StoreExpiryInspector.csproj"));

        Assert.Contains("RuntimeDataRoot.MutexName", app, StringComparison.Ordinal);
        Assert.Contains("new Mutex", app, StringComparison.Ordinal);
        Assert.Contains("Local\\StoreExpiryInspector.SingleInstance", runtimeDataRoot, StringComparison.Ordinal);
        Assert.Contains("MaximumWakeDelay = TimeSpan.FromMinutes(1)", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWindowsForms", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NotifyIcon", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Registry", app, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("S6-T04", app, StringComparison.OrdinalIgnoreCase);
    }

    private static DailyReminderRuntimeResult Result(
        string status,
        bool attempted = false,
        bool succeeded = false,
        bool recorded = false) => new(status, attempted, succeeded, recorded);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class SchedulerFixture : IDisposable
    {
        private readonly string _logDirectory = Path.Combine(
            Path.GetTempPath(),
            "StoreExpiryInspectorTests",
            Guid.NewGuid().ToString("N"));
        private readonly bool _throws;
        private readonly DailyReminderRuntimeResult _result;

        public SchedulerFixture(DateTime now, DailyReminderRuntimeResult result, bool throws = false)
        {
            _result = result;
            _throws = throws;
            Scheduler = new DailyReminderScheduler(
                600,
                Run,
                new LocalFileLogger(_logDirectory),
                () => now);
        }

        public DailyReminderScheduler Scheduler { get; }

        public int RunCount { get; private set; }

        public string LogPath => Directory.GetFiles(_logDirectory, "app-*.log").Single();

        public void Dispose()
        {
            Scheduler.Dispose();
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }

        private DailyReminderRuntimeResult Run(DateTime now)
        {
            RunCount++;
            if (_throws)
            {
                throw new InvalidOperationException("simulated scheduler failure");
            }

            return _result;
        }
    }
}
