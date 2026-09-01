using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.UI;

public partial class MainWindow : Window
{
    private bool _isNavigationCollapsed;

    public event Action<int>? ReminderTimeChanged;

    public MainWindow()
    {
        InitializeComponent();
        ApplyNavigationLayout();
        DataContext = new ShellViewModel(
            confirmClearDraft: ConfirmClearDraft,
            confirmZeroInventory: ConfirmZeroInventory,
            confirmHistoryEdit: ConfirmHistoryEdit,
            confirmRestore: ConfirmRestore);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1280;
        if (_isNavigationCollapsed)
        {
            ShellColumn.Width = new GridLength(72);
        }
        else
        {
            ShellColumn.Width = new(compact ? 176 : 208);
        }
        ContentRoot.Margin = new Thickness(compact ? 16 : 24, 0, compact ? 16 : 24, 0);
        if (PendingTasksStandardGrid is not null && PendingTasksCompactGrid is not null)
        {
            PendingTasksStandardGrid.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            PendingTasksCompactGrid.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
    {
        _isNavigationCollapsed = !_isNavigationCollapsed;
        ApplyNavigationLayout();
    }

    private void ApplyNavigationLayout()
    {
        if (NavigationBrandText is null)
        {
            return;
        }

        var compact = ActualWidth < 1280;
        if (_isNavigationCollapsed)
        {
            ShellColumn.Width = new GridLength(72);
        }
        else
        {
            ShellColumn.Width = new(compact ? 176 : 208);
        }
        var textVisibility = _isNavigationCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        NavigationBrandText.Visibility = textVisibility;
        NavigationVersionText.Visibility = textVisibility;
        NavigationHomeText.Visibility = textVisibility;
        NavigationTasksText.Visibility = textVisibility;
        NavigationHistoryText.Visibility = textVisibility;
        NavigationImportText.Visibility = textVisibility;
        NavigationBackupText.Visibility = textVisibility;
        NavigationSettingsText.Visibility = textVisibility;

        foreach (var button in new[]
        {
            NavigationHomeButton,
            NavigationTasksButton,
            NavigationHistoryButton,
            NavigationImportButton,
            NavigationBackupButton,
            NavigationSettingsButton
        })
        {
            button.HorizontalContentAlignment = _isNavigationCollapsed
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            button.Padding = _isNavigationCollapsed
                ? new Thickness(0)
                : new Thickness(14, 0, 14, 0);
        }

        NavigationBrandArea.Margin = _isNavigationCollapsed
            ? new Thickness(4, 20, 4, 20)
            : new Thickness(12, 20, 12, 20);
        NavigationToggleButton.Width = _isNavigationCollapsed ? 24 : 32;
        NavigationToggleButton.Height = _isNavigationCollapsed ? 24 : 32;
        NavigationToggleButton.FontSize = _isNavigationCollapsed ? 16 : 14;
        NavigationToggleButton.HorizontalAlignment = HorizontalAlignment.Center;
        NavigationToggleButton.VerticalAlignment = VerticalAlignment.Center;
        NavigationToggleButton.Content = _isNavigationCollapsed ? "›" : "‹";
        NavigationToggleButton.ToolTip = _isNavigationCollapsed ? "展开导航" : "收起导航";
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
        if (DataContext is ShellViewModel shell && !shell.CanOpenSettings)
        {
            WpfDialogService.Show(
                this,
                "暂不可打开设置",
                shell.IsDatabaseProtectionBlocking
                    ? "数据备份或恢复正在进行，请等待完成后再打开设置。"
                    : "当前页面操作尚未完成，请等待保存或提交结束后再打开设置。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            return;
        }

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
            AutomationProperties.SetName(dialog, "提醒设置");
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
                MaxLength = 5,
                ToolTip = "请输入 HH:mm，例如 09:30",
                Style = (Style)FindResource("PageTextBoxStyle")
            };
            AutomationProperties.SetName(reminderTime, "每日提醒时间，格式 HH:mm");
            panel.Children.Add(reminderTime);
            var autoStartCheckBox = new CheckBox
            {
                Content = "开机自启动（仅当前 Windows 用户）",
                IsChecked = autoStartState.IsEnabled,
                IsEnabled = autoStartState.Succeeded,
                FontSize = 14,
                Margin = new Thickness(0, 20, 0, 0)
            };
            AutomationProperties.SetName(autoStartCheckBox, "开机自启动，仅当前 Windows 用户");
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
            AutomationProperties.SetName(validation, "提醒设置校验结果");
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
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)FindResource("SecondaryButtonStyle")
            };
            AutomationProperties.SetName(cancel, "取消提醒设置");
            var save = new Button
            {
                Content = "保存",
                IsDefault = true,
                Width = 88,
                Style = (Style)FindResource("PrimaryButtonStyle")
            };
            AutomationProperties.SetName(save, "保存提醒设置");
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
                    WpfDialogService.Show(
                        dialog,
                        "设置已部分保存",
                        "提醒时间已保存；开机自启动状态读取失败，本次未修改。",
                        "知道了",
                        WpfDialogKind.Warning,
                        showCancel: false);
                    dialog.DialogResult = true;
                    return;
                }

                var autoStartResult = autoStart.SetEnabled(autoStartCheckBox.IsChecked == true);
                if (!autoStartResult.Succeeded
                    || autoStartResult.IsEnabled != (autoStartCheckBox.IsChecked == true))
                {
                    WpfDialogService.Show(
                        dialog,
                        "设置未完全保存",
                        $"提醒时间已保存，但开机自启动设置失败：{autoStartResult.ErrorMessage ?? "Windows 状态未按预期更新。"}",
                        "知道了",
                        WpfDialogKind.Warning,
                        showCancel: false);
                }
                else
                {
                    WpfDialogService.Show(
                        dialog,
                        "设置",
                        "设置已保存。",
                        "知道了",
                        WpfDialogKind.Information,
                        showCancel: false);
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
            WpfDialogService.Show(
                this,
                "设置错误",
                $"设置无法打开：{exception.Message}",
                "知道了",
                WpfDialogKind.Error,
                showCancel: false);
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
        WpfDialogService.Show(
            this,
            "清空草稿",
            "将清空当前未提交内容，已填写数量和草稿不会保留；正式历史不受影响。",
            "清空草稿",
            WpfDialogKind.Warning,
            nextAction: "下一步：选择“取消”保留当前草稿。\n此操作不会影响正式历史。",
            showCancel: true);

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
        AutomationProperties.SetName(dialog, "确认库存修正");
        var panel = (StackPanel)dialog.Content;
        var consequence = new TextBlock
        {
            Text = "库存修正为0后，将结束该商品所有批次的效期跟踪。此操作会系统关闭当前任务并使草稿失效。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };
        AutomationProperties.SetName(consequence, "库存归零后果说明");
        panel.Children.Add(consequence);
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
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("SecondaryButtonStyle")
        };
        AutomationProperties.SetName(cancel, "取消库存修正");
        var confirm = new Button
        {
            Content = "确认修正为0",
            Width = 112,
            Style = (Style)FindResource("DangerButtonStyle")
        };
        AutomationProperties.SetName(confirm, "确认库存修正为零");
        cancel.Click += (_, _) => dialog.DialogResult = false;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }

    private bool ConfirmHistoryEdit(InspectionHistoryEditRequest request)
    {
        var displayBatchNumber = (DataContext as ShellViewModel)
            ?.History
            .SelectedDetailBatchNumber;
        var batchLabel = displayBatchNumber is > 0
            ? $"批次 {displayBatchNumber.Value}"
            : "当前批次";
        return WpfDialogService.Show(
            this,
            "确认修改正式数量",
            $"将修改{batchLabel}的正式排查数量为 {request.NewCheckedQty}。",
            "确认修改",
            WpfDialogKind.Warning,
            nextAction: $"只修改{batchLabel}，不改变商品库存或任务生命周期。保存后会保留修改记录。",
            showCancel: true);
    }

    private bool ConfirmRestore(LocalDatabaseBackupListItem backup) =>
        WpfDialogService.Show(
            this,
            "确认恢复备份",
            $"将恢复到：{backup.CreatedAtText} 的本地备份\n状态：{backup.VerificationStatusText}",
            "确认恢复",
            WpfDialogKind.Danger,
            nextAction: "当前数据将被该备份替换。\n恢复前会自动创建保护备份。恢复完成后应用将退出，请重新打开。\n恢复完成后无法在当前操作中直接撤销。",
            showCancel: true);

}
