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
        var dialog = new Window { Owner = owner, Title = "发现新版本", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = FindBrush(owner, "SurfaceBrush") };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var title = new TextBlock { Text = "发现新版本", FontSize = 18, FontWeight = FontWeights.SemiBold };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = $"{model.CurrentVersionText}\n{model.LatestVersionText}", Margin = new Thickness(0, 12, 0, 0) });
        StackPanel? notes = null;
        if (model.IsInitial && model.HasReleaseNotes)
        {
            notes = new StackPanel();
            notes.Children.Add(new TextBlock { Text = "本次更新", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) });
            notes.Children.Add(new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new TextBlock { Text = model.ReleaseNotes, TextWrapping = TextWrapping.Wrap } });
            panel.Children.Add(notes);
        }
        var status = new TextBlock { Text = model.StatusText, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var progress = new ProgressBar { Height = 8, Margin = new Thickness(0, 8, 0, 0), Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        panel.Children.Add(status); panel.Children.Add(progress);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var later = new Button { Content = "稍后提醒", IsDefault = true, IsCancel = true, Width = 88, Height = 36, Style = FindStyle(owner, "SecondaryButtonStyle") };
        var update = new Button { Content = new TextBlock { Text = model.PrimaryActionText, Foreground = Brushes.White }, Width = 104, Height = 36, Margin = new Thickness(8, 0, 0, 0), Style = FindStyle(owner, "PrimaryButtonStyle") };
        var cancel = new Button { Content = "取消更新", Width = 88, Height = 36, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, Style = FindStyle(owner, "SecondaryButtonStyle") };
        var showLaterReminder = false;
        later.Click += (_, _) => { if (model.IsDomesticFallback) { model.ManualDownloadCommand.Execute(null); dialog.Close(); } else if (!model.IsBusy) { showLaterReminder = true; dialog.Close(); } };
        update.Click += (_, _) => model.UpdateRequestedCommand.Execute(null);
        cancel.Click += (_, _) => { if (model.IsDomesticFallback) dialog.Close(); else model.CancelCommand.Execute(null); };
        System.ComponentModel.PropertyChangedEventHandler changed = (_, _) =>
        {
            if (dialog.IsVisible && !dialog.Dispatcher.HasShutdownStarted) dialog.Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) { var fallback = model.IsDomesticFallback; title.Text = fallback ? "在线更新下载失败" : "发现新版本"; status.Text = model.StatusText; status.Visibility = model.IsInitial ? Visibility.Collapsed : Visibility.Visible; progress.Visibility = model.IsDownloading || model.IsUpdating ? Visibility.Visible : Visibility.Collapsed; progress.IsIndeterminate = model.IsProgressIndeterminate; progress.Value = model.DownloadPercent; notes?.Visibility = model.IsInitial ? Visibility.Visible : Visibility.Collapsed; later.Content = fallback ? "点击网盘下载" : "稍后提醒"; update.Content = fallback ? "重试在线更新" : new TextBlock { Text = model.PrimaryActionText, Foreground = Brushes.White }; later.Visibility = update.Visibility = model.IsBusy ? Visibility.Collapsed : Visibility.Visible; cancel.Visibility = model.CanCancel || fallback ? Visibility.Visible : Visibility.Collapsed; update.IsEnabled = model.UpdateRequestedCommand.CanExecute(null); cancel.IsEnabled = fallback || model.CancelCommand.CanExecute(null); } });
        };
        model.PropertyChanged += changed;
        dialog.Closing += (_, e) =>
        {
            if (model.IsDomesticFallback) showLaterReminder = false;
            else if (!model.IsBusy) showLaterReminder = true;
            else if (model.CanCancel) model.CancelCommand.Execute(null);
            else e.Cancel = true;
        };
        dialog.Closed += (_, _) =>
        {
            model.PropertyChanged -= changed;
            model.DialogClosed();
            if (showLaterReminder)
            {
                model.DismissCommand.Execute(null);
                Show(owner, "稍后提醒", "当前版本暂时可以继续使用，请尽快完成升级。旧版本后续可能停止支持，届时可能无法继续使用软件。", "知道了", WpfDialogKind.Information, showCancel: false);
            }
        };
        buttons.Children.Add(later); buttons.Children.Add(update); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel; dialog.Loaded += (_, _) => later.Focus(); dialog.ShowDialog();
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
        buttons.Children.Add(OpenButton("打开文件", () => Open(result.OutputPath, false, dialog), owner));
        buttons.Children.Add(OpenButton("打开所在文件夹", () => Open(result.OutputPath, true, dialog), owner));
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

    private static void Open(string path, bool select, Window owner)
    {
        try
        {
            var info = select ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") : new ProcessStartInfo(path) { UseShellExecute = true };
            Process.Start(info);
        }
        catch (Exception)
        {
            Show(owner, "无法打开", select ? "无法打开所在文件夹，请确认文件仍存在。" : "无法打开文件，请确认文件仍存在。", "知道了", WpfDialogKind.Error, "请确认文件未被移动或删除后重试。", false);
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
