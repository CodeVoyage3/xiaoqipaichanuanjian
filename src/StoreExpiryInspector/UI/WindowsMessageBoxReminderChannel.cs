using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;
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
            Width = 660,
            Height = 390,
            MinWidth = 620,
            MinHeight = 360,
            MaxWidth = 720,
            MaxHeight = 430,
            SizeToContent = SizeToContent.Manual,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = FindBrush(owner, "SurfaceBrush")
        };
        AutomationProperties.SetName(dialog, "门店效期提醒");

        var panel = new Grid { Margin = new Thickness(24, 22, 24, 20) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(75) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = Text("门店效期提醒", 24, FontWeights.Bold);
        panel.Children.Add(title);
        var overview = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.Children.Add(OverviewCard("今日待排查", $"{notification.FormalTaskItemCount}", "个商品", "M 2,3 L 5,3 L 7,14 L 19,14 L 22,7 L 8,7 M 10,20 A 1.5,1.5 0 1 0 10,23 A 1.5,1.5 0 1 0 10,20 M 18,20 A 1.5,1.5 0 1 0 18,23 A 1.5,1.5 0 1 0 18,20", FindBrush(owner, "HoverSurfaceBrush"), FindBrush(owner, "PrimaryActionBrush"), false));
        var urgency = OverviewCard("最高紧急阶段", StageText(notification), null, "M 12,2 L 22,21 L 2,21 Z M 12,8 L 12,14 M 12,17 L 12,17.2", FindBrush(owner, notification.FormalTaskItemCount == 0 ? "SurfaceSubtleBrush" : "ErrorSurfaceBrush"), FindBrush(owner, notification.FormalTaskItemCount == 0 ? "SecondaryTextBrush" : "DangerBrush"), true);
        Grid.SetColumn(urgency, 2);
        overview.Children.Add(urgency);
        Grid.SetRow(overview, 1);
        panel.Children.Add(overview);

        var upcomingTitle = Text("提前 3 天预提醒", 16, FontWeights.SemiBold, new Thickness(10, 14, 0, 6), FindBrush(owner, "PrimaryTextBrush"));
        panel.Children.Add(upcomingTitle);
        Grid.SetRow(upcomingTitle, 2);
        var upcoming = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        for (var index = 0; index < 4; index++) upcoming.ColumnDefinitions.Add(new ColumnDefinition());
        AddUpcoming(upcoming, 0, "即将5折", notification.UpcomingDiscount50Count, owner);
        AddUpcoming(upcoming, 1, "即将2折", notification.UpcomingDiscount20Count, owner);
        AddUpcoming(upcoming, 2, "即将收仓", notification.UpcomingWithdrawCount, owner);
        AddUpcoming(upcoming, 3, "即将过期", notification.UpcomingExpiredCount, owner);
        Grid.SetRow(upcoming, 3);
        panel.Children.Add(upcoming);
        var divider = new Border { BorderBrush = FindBrush(owner, "BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(divider, 4);
        panel.Children.Add(divider);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(Text($"涉及商品总数  {notification.ItemCount} 个商品", 16, FontWeights.SemiBold));
        summary.Children.Add(Text("请进入“待排查任务”完成处理。", 13, FontWeights.Normal, new Thickness(0, 3, 0, 0), FindBrush(owner, "SecondaryTextBrush")));
        footer.Children.Add(summary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var dismiss = Button("知道了", "SecondaryButtonStyle", owner);
        dismiss.IsCancel = true;
        dismiss.Click += (_, _) => dialog.Close();
        var viewTasks = Button("查看待排查任务", "PrimaryButtonStyle", owner, new Thickness(8, 0, 0, 0), 144);
        viewTasks.IsDefault = true;
        viewTasks.Click += (_, _) => { dialog.Close(); _openPendingTasks(); };
        buttons.Children.Add(dismiss);
        buttons.Children.Add(viewTasks);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 5);
        panel.Children.Add(footer);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => dismiss.Focus();
        return dialog;
    }

    private static string StageText(ReminderNotification notification) => notification.FormalTaskItemCount == 0
        ? "无今日任务"
        : StageLabels.ToDisplay(notification.HighestStage);

    private static Border OverviewCard(string title, string value, string? unit, string icon, Brush background, Brush foreground, bool pill)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new Path
        {
            Data = Geometry.Parse(icon), Stroke = foreground, StrokeThickness = 2, Stretch = Stretch.Uniform,
            Margin = new Thickness(2, 4, 10, 4), StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        });
        var copy = new StackPanel { Children = { Text(title, 15, FontWeights.SemiBold), pill ? Pill(value, foreground) : InlineValue(value, unit!, foreground) } };
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);
        return new Border { Background = background, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 7, 16, 7), Child = content };
    }

    private static void AddUpcoming(Grid parent, int column, string title, int count, Window? owner)
    {
        var accent = column switch
        {
            0 => FindBrush(owner, "PrimaryActionBrush"),
            1 => new SolidColorBrush(Color.FromRgb(161, 98, 7)),
            2 => FindBrush(owner, "WarningTextBrush"),
            _ => FindBrush(owner, "DangerBrush")
        };
        var card = new Border { BorderBrush = FindBrush(owner, "BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 3 ? 0 : 4, 0), Child = new StackPanel { Children = { Text(title, 14, FontWeights.SemiBold), InlineValue(count.ToString(), "个商品", accent) } } };
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private static TextBlock Text(string value, double size, FontWeight weight, Thickness? margin = null, Brush? foreground = null) => new()
    {
        Text = value, FontSize = size, FontWeight = weight, Margin = margin ?? new Thickness(), Foreground = foreground ?? Brushes.Black
    };

    private static StackPanel InlineValue(string value, string unit, Brush foreground) => new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 2, 0, 0),
        Children = { Text(value, 24, FontWeights.Bold, foreground: foreground), Text(unit, 13, FontWeights.SemiBold, new Thickness(4, 8, 0, 0), foreground) }
    };

    private static Border Pill(string value, Brush foreground) => new()
    {
        Background = foreground,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(9, 3, 9, 3),
        Margin = new Thickness(0, 4, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = Text(value, 13, FontWeights.SemiBold, foreground: Brushes.White)
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
