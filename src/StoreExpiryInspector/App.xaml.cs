using System.IO;
using System.Windows.Threading;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.UI;

namespace StoreExpiryInspector;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var logger = new LocalFileLogger(Path.Combine(
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
            logger.TryWrite(
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
            logger.TryWrite(
                "error",
                "startup_failed",
                "启动初始化或补算失败，已继续打开主窗口。",
                exception.ToString());
        }

        base.OnStartup(e);
        Dispatcher.BeginInvoke(
            () => RunDailyReminder(logger),
            DispatcherPriority.ApplicationIdle);
    }

    private static void RunDailyReminder(LocalFileLogger logger)
    {
        try
        {
            using var context = DatabaseInitializer.CreateContext();
            new DailyReminderRuntimeCoordinator(
                new WindowsMessageBoxReminderChannel(),
                logger).Run(
                    context,
                    TimeProvider.System.GetLocalNow().DateTime);
        }
        catch (Exception exception)
        {
            logger.TryWrite(
                "error",
                "daily_reminder_runtime_failed",
                "每日集中提醒运行时初始化失败，主窗口继续运行。",
                exception.ToString());
        }
    }
}
