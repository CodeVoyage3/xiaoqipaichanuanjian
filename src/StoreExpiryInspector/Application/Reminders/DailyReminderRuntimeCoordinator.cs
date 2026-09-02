using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Application.Reminders;

public sealed record ReminderNotification(
    int ItemCount,
    string HighestStage,
    int FormalTaskItemCount = 0,
    int UpcomingDiscount50Count = 0,
    int UpcomingDiscount20Count = 0,
    int UpcomingWithdrawCount = 0,
    int UpcomingExpiredCount = 0);

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

            var formalProductIds = result.Items.Select(item => item.ProductId).ToHashSet();
            var preReminderProductIds = result.PreReminderItems.Select(item => item.ProductId).ToHashSet();
            var notification = new ReminderNotification(
                formalProductIds.Union(preReminderProductIds).Count(),
                result.Items.FirstOrDefault()?.HighestStage ?? ExpiryStageCalculator.None,
                formalProductIds.Count,
                CountPreReminderProducts(result, ExpiryStageCalculator.Discount50),
                CountPreReminderProducts(result, ExpiryStageCalculator.Discount20),
                CountPreReminderProducts(result, ExpiryStageCalculator.Withdraw),
                CountPreReminderProducts(result, ExpiryStageCalculator.Expired));
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

    private static int CountPreReminderProducts(DailyReminderResult result, string stage) => result.PreReminderItems
        .Where(item => item.TargetStage == stage)
        .Select(item => item.ProductId)
        .Distinct()
        .Count();
}
