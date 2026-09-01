using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace StoreExpiryInspector.UI;

internal enum WpfDialogKind
{
    Information,
    Warning,
    Danger,
    Error
}

internal static class WpfDialogService
{
    public static bool Show(
        Window? owner,
        string title,
        string message,
        string confirmText,
        WpfDialogKind kind,
        string? nextAction = null,
        bool showCancel = true)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = title,
            Width = 460,
            MinWidth = 420,
            MaxWidth = 560,
            SizeToContent = SizeToContent.Height,
            MinHeight = 160,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = FindBrush(owner, "SurfaceBrush")
        };
        AutomationProperties.SetName(dialog, title);

        var panel = new StackPanel { Margin = new Thickness(24) };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        AutomationProperties.SetName(titleText, $"{title}标题");
        panel.Children.Add(titleText);

        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        AutomationProperties.SetName(messageText, $"{title}说明");
        panel.Children.Add(messageText);

        if (!string.IsNullOrWhiteSpace(nextAction))
        {
            var actionText = new TextBlock
            {
                Text = nextAction,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = FindBrush(owner, "SecondaryTextBrush"),
                Margin = new Thickness(0, 10, 0, 0)
            };
            AutomationProperties.SetName(actionText, $"{title}下一步");
            panel.Children.Add(actionText);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消",
            IsCancel = true,
            IsDefault = true,
            Width = 88,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = FindStyle(owner, "SecondaryButtonStyle")
        };
        AutomationProperties.SetName(cancel, $"取消{title}");

        var confirm = new Button
        {
            Content = confirmText,
            Width = Math.Max(88, Math.Min(136, confirmText.Length * 14 + 28)),
            Height = 36,
            Style = FindStyle(owner, kind == WpfDialogKind.Danger
                ? "DangerButtonStyle"
                : kind == WpfDialogKind.Warning
                    ? "PrimaryButtonStyle"
                    : "SecondaryButtonStyle")
        };
        AutomationProperties.SetName(confirm, confirmText);
        confirm.Click += (_, _) => dialog.DialogResult = true;

        if (showCancel)
        {
            buttons.Children.Add(cancel);
        }
        else
        {
            confirm.IsCancel = true;
            confirm.IsDefault = true;
        }

        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => (showCancel ? cancel : confirm).Focus();
        return dialog.ShowDialog() == true;
    }

    private static Brush FindBrush(Window? owner, string key) =>
        FindResource(owner, key) as Brush ?? Brushes.White;

    private static Style? FindStyle(Window? owner, string key) =>
        FindResource(owner, key) as Style;

    private static object? FindResource(Window? owner, string key) =>
        owner?.TryFindResource(key)
        ?? System.Windows.Application.Current?.TryFindResource(key);
}
