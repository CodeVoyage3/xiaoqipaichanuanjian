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

        return $"当前有 {notification.ItemCount} 个商品待排查。\n"
            + $"最高紧急阶段：{StageLabels.ToDisplay(notification.HighestStage)}。\n"
            + "请打开应用的“待排查任务”页面处理。";
    }
}
