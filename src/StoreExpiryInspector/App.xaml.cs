using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.UI;

namespace StoreExpiryInspector;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private DailyReminderScheduler? _reminderScheduler;
    private WindowsTrayIcon? _trayIcon;
    private LocalFileLogger? _logger;
    private IDisposable? _databaseMaintenanceLease;
    private bool _explicitExit;
    private UpdateCheckRuntime? _updateCheckRuntime;
    private int _updateCheckStarted;
    private UpdateNetworkDiagnostics? _updateDiagnostics;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (InstallerPreflight.TryHandle(e.Args, out var preflightExitCode))
        {
            Shutdown(preflightExitCode);
            return;
        }

        try
        {
            RuntimeDataRoot.Configure(e.Args);
            _updateDiagnostics = UpdateNetworkDiagnostics.TryCreate(e.Args, RuntimeDataRoot.IsIsolated);
            _updateDiagnostics?.Add("diagnostic-enabled", new { actualCandidateVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3), simulatedSourceVersion = _updateDiagnostics.SimulatedSourceVersion.ToString(3), dataRoot = "TEMP/GUID", installDisabled = true });
        }
        catch (Exception exception)
        {
            WpfDialogService.Show(
                owner: null,
                "门店效期排查软件",
                exception.Message,
                "知道了",
                WpfDialogKind.Error,
                showCancel: false);
            Shutdown();
            return;
        }

        try
        {
            if (!RuntimeDataRoot.IsIsolated && RuntimeDataRoot.UpgradeVerificationOperationId is null && PendingUpdateRecovery.TryResume(RuntimeDataRoot.RootDirectory))
            {
                Shutdown();
                return;
            }
        }
        catch (Exception exception)
        {
            WpfDialogService.Show(null, "门店效期排查软件", exception.Message, "知道了", WpfDialogKind.Error, showCancel: false);
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, RuntimeDataRoot.MutexName, out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            if (RuntimeDataRoot.UpgradeVerificationOperationId is not null)
            {
                Shutdown(1);
                return;
            }
            WpfDialogService.Show(
                owner: null,
                "门店效期排查软件",
                "门店效期排查软件已在运行，请从系统托盘打开。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            Shutdown();
            return;
        }

        _logger = new LocalFileLogger(RuntimeDataRoot.LogDirectory);
        if (RuntimeDataRoot.UpgradeVerificationOperationId is { } operationId)
        {
            base.OnStartup(e);
            MainWindow = new UI.MainWindow(CreateVerificationShell()) { IsEnabled = false };
            MainWindow.Show();
            StartUpgradeVerification(operationId);
            return;
        }
        try
        {
            DatabaseInitializer.Initialize();
            var businessDate = DateOnly.FromDateTime(DateTime.Now);
            var occurredAtUtc = DateTime.UtcNow;
            using var context = DatabaseInitializer.CreateContext();
            var result = new ApplicationStartupCoordinator().Execute(
                context,
                businessDate,
                occurredAtUtc);
            _logger.TryWrite(
                result.ClockRollback ? "warning" : "info",
                result.ClockRollback
                    ? "startup_clock_rollback"
                    : "startup_recalculation_completed",
                result.ClockRollback
                    ? "检测到系统日期回拨，已跳过启动补算。"
                    : "启动补算已完成。");
        }
        catch (Exception exception)
        {
            _logger.TryWrite(
                "error",
                "startup_failed",
                "启动初始化或补算失败，已停止启动。",
                exception.ToString());
            WpfDialogService.Show(
                owner: null,
                "门店效期排查软件",
                "数据库初始化失败，应用未启动且未自动恢复。请联系管理员并查看日志。",
                "知道了",
                WpfDialogKind.Error,
                showCancel: false);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = _updateDiagnostics is null ? new UI.MainWindow() : new UI.MainWindow(_updateDiagnostics);
        MainWindow.Show();
        if (RuntimeDataRoot.IsSmokeRun)
        {
            Dispatcher.BeginInvoke(
                VerifySmokeStartupAndExit,
                DispatcherPriority.ApplicationIdle);
            return;
        }

        Dispatcher.BeginInvoke(
            InitializeTrayAndReminderScheduler,
            DispatcherPriority.ApplicationIdle);
    }

    private void VerifySmokeStartupAndExit()
    {
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (MainWindow is UI.MainWindow { IsLoaded: true, DataContext: ShellViewModel shell } &&
                !shell.Dashboard.IsLoading && !shell.PendingTasks.IsLoading)
            {
                timer.Stop();
                if (!shell.Dashboard.HasError && !shell.PendingTasks.HasError)
                {
                    _logger?.TryWrite("info", "s9_t01_smoke_ready", "隔离发布 smoke 已完成 WPF Shell 初始化与首轮读取。");
                    MainWindow?.Close();
                    Shutdown();
                }
                else
                {
                    _logger?.TryWrite("error", "s9_t01_smoke_failed", "隔离发布 smoke 的 WPF Shell 首轮读取失败。");
                    MainWindow?.Close();
                    Shutdown(1);
                }

                return;
            }

            if (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                return;
            }

            timer.Stop();
            _logger?.TryWrite("error", "s9_t01_smoke_failed", "隔离发布 smoke 未在时限内完成 WPF Shell 初始化与首轮读取。");
            MainWindow?.Close();
            Shutdown(1);
        };
        timer.Start();
    }

    private void StartUpgradeVerification(string operationId)
    {
        // This path is deliberately before Initialize/Migrate/recalculation and scheduler setup.
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
                if (MainWindow is UI.MainWindow { IsLoaded: true, DataContext: ShellViewModel shell } &&
                    !shell.Dashboard.IsLoading && !shell.PendingTasks.IsLoading)
                {
                    timer.Stop();
                    VerifyUpgradeAndExit(operationId);
                    return;
                }
                if (elapsed.Elapsed < TimeSpan.FromSeconds(30)) return;
                timer.Stop();
                if (MainWindow?.DataContext is ShellViewModel timedOutShell)
                    _logger?.TryWrite("error", "s9_t05_verification_shell_timeout", $"startup={timedOutShell.StartupLoadTask.Status}; dashboardLoading={timedOutShell.Dashboard.IsLoading}; dashboardError={timedOutShell.Dashboard.HasError}; pendingLoading={timedOutShell.PendingTasks.IsLoading}; pendingError={timedOutShell.PendingTasks.HasError}", timedOutShell.StartupLoadTask.Exception?.ToString());
                Shutdown(1);
        };
        timer.Start();
    }

    private static ShellViewModel CreateVerificationShell()
    {
        var connection = VerificationConnectionString();
        return new ShellViewModel(defaultContextFactory: () => new StoreDbContext(
            new DbContextOptionsBuilder<StoreDbContext>().UseSqlite(connection).Options));
    }

    private void VerifyUpgradeAndExit(string operationId)
    {
        try
        {
            if (MainWindow is not UI.MainWindow { IsLoaded: true, DataContext: ShellViewModel shell } ||
                shell.Dashboard.HasError || shell.PendingTasks.HasError)
            {
                Shutdown(1);
                return;
            }

            var databasePath = RuntimeDataRoot.DatabasePath;
            using var connection = new SqliteConnection(VerificationConnectionString(databasePath));
            connection.Open();
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            if (integrity.ExecuteScalar() is not string result || !string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) { Shutdown(1); return; }
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var reader = foreignKeys.ExecuteReader();
            if (reader.Read()) { Shutdown(1); return; }
            using var migrationsCommand = connection.CreateCommand();
            migrationsCommand.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            using var migrationsReader = migrationsCommand.ExecuteReader();
            var migrations = new List<string>(); while (migrationsReader.Read()) migrations.Add(migrationsReader.GetString(0));
            if (migrations.Count != 9 || migrations[^1] != "20260901155124_AddPolicyAndBaselineFoundation") { Shutdown(1); return; }
            UpgradeHealthAck.Write(RuntimeDataRoot.RootDirectory, operationId, Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown", migrations.Count, migrations[^1]);
            MainWindow?.Close();
            Shutdown();
        }
        catch { MainWindow?.Close(); Shutdown(1); }
    }

    private static string VerificationConnectionString(string? databasePath = null) => new SqliteConnectionStringBuilder
    {
        // immutable=1 prevents SQLite's WAL reader from creating -wal/-shm beside the verified database.
        DataSource = new Uri(databasePath ?? RuntimeDataRoot.DatabasePath).AbsoluteUri + "?immutable=1",
        Mode = SqliteOpenMode.ReadOnly,
        ForeignKeys = true,
        Pooling = false
    }.ToString();

    protected override void OnExit(ExitEventArgs e)
    {
        _updateCheckRuntime?.Dispose();
        _updateCheckRuntime = null;
        (MainWindow as UI.MainWindow)?.StopUpdatePreparation();
        StopRuntime();
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void InitializeTrayAndReminderScheduler()
    {
        if (MainWindow is not UI.MainWindow mainWindow || _logger is null)
        {
            return;
        }

        if (mainWindow.DataContext is ShellViewModel shell)
        {
            shell.ConfigureDatabaseProtectionRuntime(
                BeginDatabaseMaintenanceAsync,
                EndDatabaseMaintenance,
                ExitApplication);
            if (_updateDiagnostics is null) mainWindow.ConfigureUpdateInstallation(InstallPreparedUpdateAsync);
            mainWindow.Closing += MainWindow_Closing;
            mainWindow.ReminderTimeChanged += ReminderTimeChanged;
        }

        StartUpdateCheck(mainWindow);

        WindowsTrayIcon trayIcon;
        try
        {
            trayIcon = new WindowsTrayIcon(mainWindow, ShowMainWindow, ExitApplication);
        }
        catch (Exception exception)
        {
            _logger.TryWrite(
                "error",
                "tray_icon_creation_failed",
                "系统托盘图标创建失败，关闭主窗口将正常退出应用。",
                exception.ToString());
            return;
        }

        try
        {
            int reminderMinuteOfDay;
            using (var context = DatabaseInitializer.CreateContext())
            {
                reminderMinuteOfDay = context.Settings
                    .AsNoTracking()
                    .Select(setting => setting.ReminderMinuteOfDay)
                    .Single();
            }
            var coordinator = new DailyReminderRuntimeCoordinator(
                new WindowsMessageBoxReminderChannel(
                    () => mainWindow.IsVisible ? mainWindow : null),
                _logger);
            _reminderScheduler = new DailyReminderScheduler(
                reminderMinuteOfDay,
                localNow =>
                {
                    try
                    {
                        return DatabaseRuntimeGate.Run(() =>
                        {
                            using var reminderContext = DatabaseInitializer.CreateContext();
                            return coordinator.Run(reminderContext, localNow);
                        });
                    }
                    catch (DatabaseRuntimeStoppedException)
                    {
                        return new(
                            "paused",
                            NotificationAttempted: false,
                            NotificationSucceeded: false,
                            ReminderRecorded: false);
                    }
                },
                _logger);
            _trayIcon = trayIcon;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _reminderScheduler.Start();
        }
        catch (Exception exception)
        {
            trayIcon.Dispose();
            _trayIcon = null;
            _reminderScheduler?.Dispose();
            _reminderScheduler = null;
            // If scheduler startup failed after the tray path selected explicit
            // shutdown, restore the ordinary close behavior before returning.
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            _logger.TryWrite(
                "error",
                "daily_reminder_runtime_failed",
                "每日集中提醒运行时初始化失败，关闭主窗口将正常退出应用。",
                exception.ToString());
        }

    }

    private void StartUpdateCheck(UI.MainWindow mainWindow)
    {
        if (Interlocked.Exchange(ref _updateCheckStarted, 1) != 0) return;
        if (!GitHubReleaseUpdateChecker.TryGetCurrentVersion(out var currentVersion))
        {
            _logger?.TryWrite("warning", "update_version_unavailable", "无法确认更新状态，已跳过本次检查。");
            return;
        }
        if (_updateDiagnostics is not null) currentVersion = _updateDiagnostics.SimulatedSourceVersion;
        var checker = new GitHubReleaseUpdateChecker(diagnostics: _updateDiagnostics);
        _updateDiagnostics?.Add("gui-check-start", new { simulatedSourceVersion = currentVersion.ToString(3), threadId = Environment.CurrentManagedThreadId });
        _updateCheckRuntime = new UpdateCheckRuntime(
            cancellationToken => checker.CheckAsync(currentVersion, cancellationToken),
            result => Dispatcher.BeginInvoke(() =>
            {
                if (!_explicitExit && !mainWindow.IsClosed && result.Outcome == UpdateCheckOutcome.UpdateAvailable)
                    mainWindow.ShowUpdateAvailable(result);
            }));
        _updateCheckRuntime.StartAfter(((ShellViewModel)mainWindow.DataContext).StartupLoadTask);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_explicitExit || MainWindow is null)
        {
            return;
        }

        if (MainWindow.DataContext is ShellViewModel shell && shell.IsDatabaseProtectionBusy)
        {
            e.Cancel = true;
            WpfDialogService.Show(
                MainWindow,
                "操作进行中",
                "数据备份或恢复正在进行，请等待操作完成后再关闭应用。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            return;
        }

        if (MainWindow.DataContext is ShellViewModel { IsDatabaseProtectionLocked: true })
        {
            e.Cancel = true;
            WpfDialogService.Show(
                MainWindow,
                "请退出应用",
                "当前数据保护操作已完成或遇到严重错误，请使用页面中的“退出应用”完成正常退出。",
                "知道了",
                WpfDialogKind.Warning,
                showCancel: false);
            return;
        }

        // If tray setup failed, preserve the original fallback: closing the
        // window must be able to terminate the process.
        if (_trayIcon is null)
        {
            return;
        }

        e.Cancel = true;
        MainWindow.Hide();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
    }

    private void ExitApplication()
    {
        if (MainWindow?.DataContext is ShellViewModel { IsDatabaseProtectionBusy: true })
        {
            WpfDialogService.Show(
                MainWindow,
                "操作进行中",
                "数据备份或恢复正在进行，请等待操作完成后再退出应用。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            return;
        }

        _explicitExit = true;
        StopRuntime();
        MainWindow?.Close();
        Shutdown();
    }

    private async Task<UpdatePackageResult> InstallPreparedUpdateAsync(VerifiedUpdatePackage package, SignedUpdatePackageDownloader downloader)
    {
        if (!await BeginDatabaseMaintenanceAsync())
            return new(UpdatePackageOutcome.IoFailure, "当前仍有写入操作，未进入升级维护状态。");
        try
        {
            var prepared = await Task.Run(() =>
            {
                using var parent = Process.GetCurrentProcess();
                return new UpdateInstallationPreparer(downloader).Prepare(package, parent, CancellationToken.None);
            });
            _ = Process.Start(new ProcessStartInfo(prepared.UpdaterPath, $"--journal \"{prepared.JournalPath}\"") { UseShellExecute = false }) ?? throw new InvalidOperationException("独立 Updater 未启动。");
            _explicitExit = true;
            StopRuntime();
            MainWindow?.Close();
            Shutdown();
            return new(UpdatePackageOutcome.Verified, "独立 Updater 已接管程序切换。");
        }
        catch
        {
            EndDatabaseMaintenance(true);
            return new(UpdatePackageOutcome.IoFailure, "更新安装准备失败，原程序继续运行。");
        }
    }

    private void ReminderTimeChanged(int reminderMinuteOfDay) =>
        _reminderScheduler?.Reschedule(reminderMinuteOfDay);

    private void StopRuntime()
    {
        _reminderScheduler?.Dispose();
        _reminderScheduler = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private async Task<bool> BeginDatabaseMaintenanceAsync()
    {
        if (_databaseMaintenanceLease is not null ||
            MainWindow?.DataContext is not ShellViewModel shell)
        {
            return false;
        }

        MainWindow.IsEnabled = false;
        // Draft autosave must settle before the gate starts rejecting database
        // workers. This preserves the existing Stage 4 save contract.
        if (!await shell.Detail.WaitForStableSaveAsync())
        {
            MainWindow.IsEnabled = true;
            return false;
        }

        var wasSchedulerRunning = _reminderScheduler?.IsRunning == true;
        _reminderScheduler?.Stop();
        try
        {
            var lease = await DatabaseRuntimeGate.EnterMaintenanceAsync();
            if (lease is null)
            {
                if (wasSchedulerRunning && !_explicitExit)
                {
                    _reminderScheduler?.Start();
                }
                MainWindow.IsEnabled = true;

                return false;
            }

            _databaseMaintenanceLease = lease;
            return true;
        }
        catch
        {
            if (wasSchedulerRunning && !_explicitExit)
            {
                _reminderScheduler?.Start();
            }
            MainWindow.IsEnabled = true;

            throw;
        }
    }

    private void EndDatabaseMaintenance(bool resumeScheduler)
    {
        var lease = _databaseMaintenanceLease;
        _databaseMaintenanceLease = null;
        lease?.Dispose();
        if (resumeScheduler && !_explicitExit)
        {
            _reminderScheduler?.Start();
        }
        if (!_explicitExit && MainWindow is not null) MainWindow.IsEnabled = true;
    }
}
