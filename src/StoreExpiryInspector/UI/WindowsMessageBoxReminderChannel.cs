using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using StoreExpiryInspector.Application.Reminders;

namespace StoreExpiryInspector.UI;

public sealed class WindowsMessageBoxReminderChannel : IReminderChannel
{
    private readonly Func<Window?> _owner;
    private readonly Action _openPendingTasks;

    public WindowsMessageBoxReminderChannel(Func<Window?>? owner = null, Action? openPendingTasks = null)
    {
        _owner = owner ?? new Func<Window?>(() => System.Windows.Application.Current?.MainWindow);
        _openPendingTasks = openPendingTasks ?? (() => { });
    }

    public bool TryShow(ReminderNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.ItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(notification));
        }

        var owner = _owner();
        var dialog = CreateDialog(owner, notification);
        dialog.ShowDialog();

        return true;
    }

    public static string FormatMessage(ReminderNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.ItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(notification));
        }

        return $"今日待排查：{notification.FormalTaskItemCount} 个商品。\n最高紧急阶段：{StageText(notification)}。\n"
            + $"提前 3 天预提醒：即将5折 {notification.UpcomingDiscount50Count} 个商品、即将2折 {notification.UpcomingDiscount20Count} 个商品、"
            + $"即将收仓 {notification.UpcomingWithdrawCount} 个商品、即将过期 {notification.UpcomingExpiredCount} 个商品。\n"
            + $"涉及商品总数：{notification.ItemCount} 个。\n请打开应用的“待排查任务”页面处理。";
    }

    private Window CreateDialog(Window? owner, ReminderNotification notification)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "门店效期提醒",
            Width = 760,
            MinWidth = 680,
            SizeToContent = SizeToContent.Height,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = FindBrush(owner, "SurfaceBrush")
        };
        AutomationProperties.SetName(dialog, "门店效期提醒");

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        panel.Children.Add(Text("门店效期提醒", 30, FontWeights.Bold));
        var overview = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.Children.Add(OverviewCard("今日待排查", $"{notification.FormalTaskItemCount} 个商品", FindBrush(owner, "HoverSurfaceBrush"), FindBrush(owner, "PrimaryActionBrush")));
        var urgency = OverviewCard("最高紧急阶段", StageText(notification), FindBrush(owner, notification.FormalTaskItemCount == 0 ? "SurfaceSubtleBrush" : "ErrorSurfaceBrush"), FindBrush(owner, notification.FormalTaskItemCount == 0 ? "SecondaryTextBrush" : "DangerBrush"));
        Grid.SetColumn(urgency, 2);
        overview.Children.Add(urgency);
        panel.Children.Add(overview);

        panel.Children.Add(Text("提前 3 天预提醒", 22, FontWeights.SemiBold, new Thickness(0, 28, 0, 12)));
        var upcoming = new Grid();
        for (var index = 0; index < 4; index++) upcoming.ColumnDefinitions.Add(new ColumnDefinition());
        AddUpcoming(upcoming, 0, "即将5折", notification.UpcomingDiscount50Count, owner);
        AddUpcoming(upcoming, 1, "即将2折", notification.UpcomingDiscount20Count, owner);
        AddUpcoming(upcoming, 2, "即将收仓", notification.UpcomingWithdrawCount, owner);
        AddUpcoming(upcoming, 3, "即将过期", notification.UpcomingExpiredCount, owner);
        panel.Children.Add(upcoming);
        panel.Children.Add(new Border { BorderBrush = FindBrush(owner, "BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 22, 0, 16) });
        panel.Children.Add(Text($"涉及商品总数：{notification.ItemCount} 个", 18, FontWeights.SemiBold));
        panel.Children.Add(Text("请打开“待排查任务”页面处理。", 15, FontWeights.Normal, new Thickness(0, 10, 0, 0), FindBrush(owner, "SecondaryTextBrush")));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var dismiss = Button("知道了", "SecondaryButtonStyle", owner);
        dismiss.IsCancel = true;
        dismiss.Click += (_, _) => dialog.Close();
        var viewTasks = Button("查看待排查任务", "PrimaryButtonStyle", owner, new Thickness(8, 0, 0, 0), 144);
        viewTasks.IsDefault = true;
        viewTasks.Click += (_, _) => { dialog.Close(); _openPendingTasks(); };
        buttons.Children.Add(dismiss);
        buttons.Children.Add(viewTasks);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => dismiss.Focus();
        return dialog;
    }

    private static string StageText(ReminderNotification notification) => notification.FormalTaskItemCount == 0
        ? "无今日任务"
        : StageLabels.ToDisplay(notification.HighestStage);

    private static Border OverviewCard(string title, string value, Brush background, Brush foreground) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(24, 18, 24, 20),
        Child = new StackPanel { Children = { Text(title, 18, FontWeights.SemiBold), Text(value, 32, FontWeights.Bold, new Thickness(0, 8, 0, 0), foreground) } }
    };

    private static void AddUpcoming(Grid parent, int column, string title, int count, Window? owner)
    {
        var accent = column switch
        {
            0 => FindBrush(owner, "PrimaryActionBrush"),
            1 => new SolidColorBrush(Color.FromRgb(161, 98, 7)),
            2 => FindBrush(owner, "WarningTextBrush"),
            _ => FindBrush(owner, "DangerBrush")
        };
        var card = new Border { BorderBrush = FindBrush(owner, "BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 16, 14, 14), Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 3 ? 0 : 4, 0), Child = new StackPanel { Children = { Text(title, 16, FontWeights.SemiBold), Text($"{count} 个商品", 24, FontWeights.Bold, new Thickness(0, 10, 0, 0), accent) } } };
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private static TextBlock Text(string value, double size, FontWeight weight, Thickness? margin = null, Brush? foreground = null) => new()
    {
        Text = value, FontSize = size, FontWeight = weight, Margin = margin ?? new Thickness(), Foreground = foreground ?? Brushes.Black
    };

    private static Button Button(string text, string style, Window? owner, Thickness? margin = null, double width = 96) => new()
    {
        Content = text, Width = width, Height = 38, Margin = margin ?? new Thickness(), Style = FindStyle(owner, style)
    };

    private static Brush FindBrush(Window? owner, string key) => owner?.TryFindResource(key) as Brush
        ?? System.Windows.Application.Current?.TryFindResource(key) as Brush
        ?? Brushes.White;

    private static Style? FindStyle(Window? owner, string key) => owner?.TryFindResource(key) as Style
        ?? System.Windows.Application.Current?.TryFindResource(key) as Style;
}
