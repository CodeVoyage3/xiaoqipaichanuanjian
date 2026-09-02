using System.Windows;
using StoreExpiryInspector.Application.Reminders;

namespace StoreExpiryInspector.UI;

public sealed class WindowsMessageBoxReminderChannel : IReminderChannel
{
    private readonly Func<Window?> _owner;

    public WindowsMessageBoxReminderChannel(Func<Window?>? owner = null)
    {
        _owner = owner ?? new Func<Window?>(() => System.Windows.Application.Current?.MainWindow);
    }

    public bool TryShow(ReminderNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.ItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(notification));
        }

        var message = FormatMessage(notification);
        var owner = _owner();
        WpfDialogService.Show(
            owner,
            "门店效期提醒",
            message,
            "知道了",
            WpfDialogKind.Warning,
            showCancel: false);

        return true;
    }

    public static string FormatMessage(ReminderNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.ItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(notification));
        }

        var preReminderCount = notification.UpcomingDiscount50Count + notification.UpcomingDiscount20Count
            + notification.UpcomingWithdrawCount + notification.UpcomingExpiredCount;
        var formalTaskItemCount = notification.FormalTaskItemCount == 0 && preReminderCount == 0
            ? notification.ItemCount
            : notification.FormalTaskItemCount;
        if (preReminderCount == 0)
        {
            return $"当前有 {notification.ItemCount} 个商品待排查。\n"
                + $"最高紧急阶段：{StageLabels.ToDisplay(notification.HighestStage)}。\n"
                + "请打开应用的“待排查任务”页面处理。";
        }

        var preReminder = $"即将5折 {notification.UpcomingDiscount50Count} 个商品、即将2折 {notification.UpcomingDiscount20Count} 个商品、"
            + $"即将收仓 {notification.UpcomingWithdrawCount} 个商品、即将过期 {notification.UpcomingExpiredCount} 个商品。";
        return formalTaskItemCount == 0
            ? $"提前 3 天预提醒：{preReminder}\n涉及商品总数：{notification.ItemCount} 个。"
            : $"今日待排查：{formalTaskItemCount} 个商品。\n最高紧急阶段：{StageLabels.ToDisplay(notification.HighestStage)}。\n"
                + $"提前 3 天预提醒：{preReminder}\n涉及商品总数：{notification.ItemCount} 个。\n请打开应用的“待排查任务”页面处理。";
    }
}
