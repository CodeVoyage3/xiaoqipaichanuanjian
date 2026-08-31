using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.UI;

public partial class MainWindow : Window
{
    public event Action<int>? ReminderTimeChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(
            confirmClearDraft: ConfirmClearDraft,
            confirmZeroInventory: ConfirmZeroInventory,
            confirmHistoryEdit: ConfirmHistoryEdit);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1280;
        ShellColumn.Width = new(224);
        ContentRoot.Margin = new Thickness(compact ? 16 : 24, 0, compact ? 16 : 24, 0);
    }

    private void DashboardDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        var dataGridScrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
        var dataGridCanScroll = dataGridScrollViewer is not null
            && dataGridScrollViewer.ScrollableHeight > 0;
        if (dataGridCanScroll &&
            ((e.Delta > 0 && dataGridScrollViewer!.VerticalOffset > 0) ||
             (e.Delta < 0 && dataGridScrollViewer!.VerticalOffset < dataGridScrollViewer.ScrollableHeight)))
        {
            return;
        }

        if (DashboardScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        DashboardScrollViewer.ScrollToVerticalOffset(Math.Clamp(
            DashboardScrollViewer.VerticalOffset - e.Delta,
            0,
            DashboardScrollViewer.ScrollableHeight));
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private async void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            await shell.NavigateToAsync(ShellPage.PendingTasks);
            if (shell.CurrentPage != ShellPage.PendingTasks)
            {
                return;
            }
        }

        await Dispatcher.BeginInvoke(() =>
        {
            TaskSearchBox.Focus();
            TaskSearchBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void SelectExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Excel 文件",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false,
            DefaultExt = ".xlsx",
            AddExtension = false
        };
        if (dialog.ShowDialog(this) != true ||
            DataContext is not ShellViewModel shell)
        {
            return;
        }

        shell.Import.TrySelectFile(dialog.FileName);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new ReminderSettingsUseCase();
            int reminderMinuteOfDay;
            using (var context = DatabaseInitializer.CreateContext())
            {
                reminderMinuteOfDay = settings.GetReminderMinuteOfDay(context);
            }

            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前应用程序路径。");
            var autoStart = new WindowsAutoStartService(
                new CurrentUserRunRegistry(),
                executablePath);
            var autoStartState = autoStart.ReadState();

            var dialog = new Window
            {
                Owner = this,
                Title = "设置",
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                Language = XmlLanguage.GetLanguage("zh-CN"),
                Width = 460,
                Height = 300,
                MinWidth = 420,
                MinHeight = 280,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Content = new StackPanel { Margin = new Thickness(24) }
            };
            var panel = (StackPanel)dialog.Content;
            panel.Children.Add(new TextBlock
            {
                Text = "每日提醒时间",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "使用本地时间，格式为 HH:mm。修改后立即重新安排提醒。",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 10)
            });
            var reminderTime = new TextBox
            {
                Text = ReminderSettingsUseCase.Format(reminderMinuteOfDay),
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxLength = 5
            };
            panel.Children.Add(reminderTime);
            var autoStartCheckBox = new CheckBox
            {
                Content = "开机自启动（仅当前 Windows 用户）",
                IsChecked = autoStartState.IsEnabled,
                IsEnabled = autoStartState.Succeeded,
                FontSize = 14,
                Margin = new Thickness(0, 20, 0, 0)
            };
            panel.Children.Add(autoStartCheckBox);
            var validation = new TextBlock
            {
                Foreground = (Brush)FindResource("DangerBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Text = autoStartState.Succeeded
                    ? string.Empty
                    : $"开机自启动状态读取失败，本次不会修改：{autoStartState.ErrorMessage}"
            };
            panel.Children.Add(validation);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var cancel = new Button
            {
                Content = "取消",
                IsCancel = true,
                Width = 88,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var save = new Button
            {
                Content = "保存",
                IsDefault = true,
                Width = 88,
                Style = (Style)FindResource("PrimaryButtonStyle")
            };
            int? savedMinuteOfDay = null;
            save.Click += (_, _) =>
            {
                ReminderTimeSaveResult result;
                try
                {
                    using var context = DatabaseInitializer.CreateContext();
                    result = settings.SaveReminderTime(context, reminderTime.Text);
                }
                catch (Exception exception)
                {
                    validation.Text = $"提醒时间保存失败：{exception.Message}";
                    return;
                }

                if (!result.Succeeded || result.ReminderMinuteOfDay is not int savedMinute)
                {
                    validation.Text = result.Message;
                    reminderTime.Focus();
                    reminderTime.SelectAll();
                    return;
                }

                savedMinuteOfDay = savedMinute;
                if (!autoStartState.Succeeded)
                {
                    MessageBox.Show(
                        dialog,
                        "提醒时间已保存；开机自启动状态读取失败，本次未修改。",
                        "设置已部分保存",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    dialog.DialogResult = true;
                    return;
                }

                var autoStartResult = autoStart.SetEnabled(autoStartCheckBox.IsChecked == true);
                if (!autoStartResult.Succeeded
                    || autoStartResult.IsEnabled != (autoStartCheckBox.IsChecked == true))
                {
                    MessageBox.Show(
                        dialog,
                        $"提醒时间已保存，但开机自启动设置失败：{autoStartResult.ErrorMessage ?? "Windows 状态未按预期更新。"}",
                        "设置未完全保存",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        dialog,
                        "设置已保存。",
                        "设置",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);
            dialog.Loaded += (_, _) =>
            {
                reminderTime.Focus();
                reminderTime.SelectAll();
            };
            dialog.ShowDialog();
            if (savedMinuteOfDay.HasValue)
            {
                ReminderTimeChanged?.Invoke(savedMinuteOfDay.Value);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"设置无法打开：{exception.Message}",
                "设置错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ReturnFromOverStockButton_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(() => ReturnFromOverStockButton.Focus(), DispatcherPriority.Input);
        }
    }

    private bool ConfirmClearDraft() =>
        MessageBox.Show(
            this,
            "将清空当前未提交内容。",
            "清空草稿",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private bool ConfirmZeroInventory()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "确认库存修正",
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            Width = 480,
            Height = 210,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel { Margin = new Thickness(20) }
        };
        var panel = (StackPanel)dialog.Content;
        panel.Children.Add(new TextBlock
        {
            Text = "库存修正为0后，将结束该商品所有批次的效期跟踪。此操作会系统关闭当前任务并使草稿失效。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消",
            IsDefault = true,
            IsCancel = true,
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var confirm = new Button
        {
            Content = "确认修正为0",
            Width = 112,
            Style = (Style)FindResource("DangerButtonStyle")
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }

    private bool ConfirmHistoryEdit(InspectionHistoryEditRequest request) =>
        MessageBox.Show(
            this,
            $"将明细 {request.InspectionItemId} 的正式排查数量修改为 {request.NewCheckedQty}。\n确认写入并保留修改记录吗？",
            "确认修改正式数量",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
