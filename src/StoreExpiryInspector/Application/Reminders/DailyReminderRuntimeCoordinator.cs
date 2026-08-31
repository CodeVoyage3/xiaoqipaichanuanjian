using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;

namespace StoreExpiryInspector.Application.Reminders;

public sealed record ReminderNotification(int ItemCount, string HighestStage);

public interface IReminderChannel
{
    bool TryShow(ReminderNotification notification);
}

public sealed record DailyReminderRuntimeResult(
    string Status,
    bool NotificationAttempted,
    bool NotificationSucceeded,
    bool ReminderRecorded);

public sealed class DailyReminderRuntimeCoordinator
{
    private readonly DailyReminderUseCase _reminder;
    private readonly IReminderChannel _channel;
    private readonly LocalFileLogger _logger;

    public DailyReminderRuntimeCoordinator(
        IReminderChannel channel,
        LocalFileLogger logger,
        DailyReminderUseCase? reminder = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reminder = reminder ?? new DailyReminderUseCase();
    }

    public DailyReminderRuntimeResult Run(StoreDbContext context, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(context);
        var notificationAttempted = false;
        var notificationSucceeded = false;

        try
        {
            var result = _reminder.Evaluate(context, localNow);
            if (result.Status != DailyReminderStatuses.Due)
            {
                return new(result.Status, false, false, false);
            }

            var notification = new ReminderNotification(
                result.Items.Count,
                result.Items[0].HighestStage);
            notificationAttempted = true;
            if (!_channel.TryShow(notification))
            {
                _logger.TryWrite(
                    "warning",
                    "daily_reminder_notification_failed",
                    "每日集中提醒未能显示，今日提醒状态未登记。");
                return new(result.Status, true, false, false);
            }

            notificationSucceeded = true;
            var recorded = _reminder.RecordSuccessfulReminder(
                context,
                result.BusinessDate,
                notificationSucceeded: true);
            return new(result.Status, true, true, recorded);
        }
        catch (Exception exception)
        {
            _logger.TryWrite(
                "error",
                "daily_reminder_failed",
                "每日集中提醒执行失败，主窗口继续运行且今日提醒状态未登记。",
                exception.ToString());
            return new("error", notificationAttempted, notificationSucceeded, false);
        }
    }
}
