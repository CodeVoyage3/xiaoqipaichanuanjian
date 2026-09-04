using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Reminders;
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

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        try
        {
            RuntimeDataRoot.Configure(e.Args);
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

        _instanceMutex = new Mutex(true, RuntimeDataRoot.MutexName, out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
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
        var deadline = DateTime.UtcNow.AddSeconds(30);
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
                    Shutdown();
                }
                else
                {
                    _logger?.TryWrite("error", "s9_t01_smoke_failed", "隔离发布 smoke 的 WPF Shell 首轮读取失败。");
                    Shutdown(1);
                }

                return;
            }

            if (DateTime.UtcNow < deadline)
            {
                return;
            }

            timer.Stop();
            _logger?.TryWrite("error", "s9_t01_smoke_failed", "隔离发布 smoke 未在时限内完成 WPF Shell 初始化与首轮读取。");
            Shutdown(1);
        };
        timer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
            mainWindow.Closing += MainWindow_Closing;
            mainWindow.ReminderTimeChanged += ReminderTimeChanged;
        }

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
            MainWindow?.DataContext is not ShellViewModel shell ||
            shell.Import.IsLoading ||
            shell.History.IsEditBusy ||
            shell.Detail.IsActionBusy)
        {
            return false;
        }

        // Draft autosave must settle before the gate starts rejecting database
        // workers. This preserves the existing Stage 4 save contract.
        if (!await shell.Detail.WaitForStableSaveAsync())
        {
            return false;
        }

        if (shell.Import.IsLoading || shell.History.IsEditBusy || shell.Detail.IsActionBusy)
        {
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
    }
}
