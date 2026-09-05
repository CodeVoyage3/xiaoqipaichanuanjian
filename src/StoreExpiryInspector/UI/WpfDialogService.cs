using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Diagnostics;
using StoreExpiryInspector.Application.Tasks;

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
    public static void ShowUpdateAvailable(Window owner, UpdateNotificationViewModel model)
    {
        var dialog = new Window { Owner = owner, Title = "发现新版本", Width = 460, MaxHeight = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = FindBrush(owner, "SurfaceBrush") };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "发现新版本", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{model.CurrentVersionText}\n{model.LatestVersionText}", Margin = new Thickness(0, 12, 0, 0) });
        if (!string.IsNullOrWhiteSpace(model.DiagnosticBanner)) panel.Children.Add(new TextBlock { Text = model.DiagnosticBanner, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = FindBrush(owner, "SecondaryTextBrush") });
        if (!string.IsNullOrWhiteSpace(model.ReleaseNotes)) panel.Children.Add(new ScrollViewer { MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = model.ReleaseNotes, TextWrapping = TextWrapping.Wrap } });
        var status = new TextBlock { Text = model.StatusText, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        var bytes = new TextBlock { Text = model.ProgressText, Margin = new Thickness(0, 4, 0, 0), Foreground = FindBrush(owner, "SecondaryTextBrush") };
        panel.Children.Add(status); panel.Children.Add(bytes);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var later = new Button { Content = "稍后提醒", IsDefault = true, IsCancel = true, Width = 88, Height = 36, Style = FindStyle(owner, "SecondaryButtonStyle") };
        var update = new Button { Content = new TextBlock { Text = "立即更新", Foreground = Brushes.White }, Width = 88, Height = 36, Margin = new Thickness(8, 0, 0, 0), Style = FindStyle(owner, "PrimaryButtonStyle") };
        var cancel = new Button { Content = "取消准备", Width = 88, Height = 36, Margin = new Thickness(8, 0, 0, 0), Style = FindStyle(owner, "SecondaryButtonStyle") };
        later.Click += (_, _) => { model.DismissCommand.Execute(null); dialog.Close(); };
        update.Click += (_, _) => model.UpdateRequestedCommand.Execute(null);
        cancel.Click += (_, _) => model.CancelCommand.Execute(null);
        System.ComponentModel.PropertyChangedEventHandler changed = (_, _) =>
        {
            if (dialog.IsVisible && !dialog.Dispatcher.HasShutdownStarted) dialog.Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) { status.Text = model.StatusText; bytes.Text = model.ProgressText; update.IsEnabled = model.UpdateRequestedCommand.CanExecute(null); cancel.IsEnabled = model.CancelCommand.CanExecute(null); } });
        };
        model.PropertyChanged += changed;
        dialog.Closed += (_, _) => model.PropertyChanged -= changed;
        buttons.Children.Add(later); buttons.Children.Add(update); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel; dialog.Loaded += (_, _) => { cancel.IsEnabled = false; later.Focus(); }; dialog.Show();
    }
    public static void ShowExportSuccess(Window owner, TodayInspectionPlanExportResult result)
    {
        var dialog = new Window
        {
            Owner = owner, Title = "导出成功", Width = 560, MinWidth = 500, SizeToContent = SizeToContent.Height,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"), Language = XmlLanguage.GetLanguage("zh-CN"),
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = FindBrush(owner, "SurfaceBrush")
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "导出成功", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"商品/任务数量：{result.TaskCount}\n批次数：{result.RowCount}\n完整路径：{result.OutputPath}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(OpenButton("打开文件", () => Open(result.OutputPath, false), owner));
        buttons.Children.Add(OpenButton("打开所在文件夹", () => Open(result.OutputPath, true), owner));
        var close = new Button { Content = "确定", IsDefault = true, IsCancel = true, Width = 88, Height = 36, Style = FindStyle(owner, "PrimaryButtonStyle") };
        close.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(close); panel.Children.Add(buttons); dialog.Content = panel; dialog.Loaded += (_, _) => close.Focus(); dialog.ShowDialog();
    }

    private static Button OpenButton(string text, Action action, Window owner)
    {
        var button = new Button { Content = text, Width = text.Length > 4 ? 120 : 96, Height = 36, Margin = new Thickness(0, 0, 8, 0), Style = FindStyle(owner, "SecondaryButtonStyle") };
        button.Click += (_, _) => action();
        return button;
    }

    private static void Open(string path, bool select)
    {
        try
        {
            var info = select ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") : new ProcessStartInfo(path) { UseShellExecute = true };
            Process.Start(info);
        }
        catch (Exception)
        {
            Show(null, "无法打开", select ? "无法打开所在文件夹，请确认文件仍存在。" : "无法打开文件，请确认文件仍存在。", "知道了", WpfDialogKind.Error, "请确认文件未被移动或删除后重试。", false);
        }
    }

    public static bool Show(
        Window? owner,
        string title,
        string message,
        string confirmText,
        WpfDialogKind kind,
        string? nextAction = null,
        bool showCancel = true,
        string cancelText = "取消")
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
            Content = cancelText,
            IsCancel = true,
            IsDefault = true,
            Width = 88,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = FindStyle(owner, "SecondaryButtonStyle")
        };
        AutomationProperties.SetName(cancel, $"{cancelText}{title}");

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
