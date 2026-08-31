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
    private const string SingleInstanceMutexName = @"Local\StoreExpiryInspector.SingleInstance";

    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private DailyReminderScheduler? _reminderScheduler;
    private WindowsTrayIcon? _trayIcon;
    private LocalFileLogger? _logger;
    private bool _explicitExit;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            MessageBox.Show(
                "门店效期排查软件已在运行，请从系统托盘打开。",
                "门店效期排查软件",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _logger = new LocalFileLogger(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoreExpiryInspector",
            "logs"));
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
                "启动初始化或补算失败，已继续打开主窗口。",
                exception.ToString());
        }

        base.OnStartup(e);
        Dispatcher.BeginInvoke(
            InitializeTrayAndReminderScheduler,
            DispatcherPriority.ApplicationIdle);
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
            using var context = DatabaseInitializer.CreateContext();
            reminderMinuteOfDay = context.Settings
                .AsNoTracking()
                .Select(setting => setting.ReminderMinuteOfDay)
                .Single();
            var coordinator = new DailyReminderRuntimeCoordinator(
                new WindowsMessageBoxReminderChannel(
                    () => mainWindow.IsVisible ? mainWindow : null),
                _logger);
            _reminderScheduler = new DailyReminderScheduler(
                reminderMinuteOfDay,
                localNow =>
                {
                    using var reminderContext = DatabaseInitializer.CreateContext();
                    return coordinator.Run(reminderContext, localNow);
                },
                _logger);
            _trayIcon = trayIcon;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            mainWindow.Closing += MainWindow_Closing;
            mainWindow.ReminderTimeChanged += ReminderTimeChanged;
            _reminderScheduler.Start();
        }
        catch (Exception exception)
        {
            trayIcon.Dispose();
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
}
