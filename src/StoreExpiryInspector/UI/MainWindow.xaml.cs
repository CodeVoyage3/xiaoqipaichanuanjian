using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        var shell = new ShellViewModel(
            confirmClearDraft: ConfirmClearDraft,
            confirmZeroInventory: ConfirmZeroInventory,
            confirmHistoryEdit: ConfirmHistoryEdit,
            confirmRestore: ConfirmRestore,
            confirmTodayOverStock: ConfirmTodayOverStock,
            confirmTodayExpiredInventory: ConfirmTodayExpiredInventory,
            confirmTodaySubmission: ConfirmTodaySubmission);
        shell.TodayInspection.PreviewFailed += ShowTodayPreviewFailure;
        DataContext = shell;
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
            ShellColumn.Width = new(208);
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

        if (_isNavigationCollapsed)
        {
            ShellColumn.Width = new GridLength(72);
        }
        else
        {
            ShellColumn.Width = new(208);
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
        NavigationTodayInspectionText.Visibility = textVisibility;
        NavigationBackupText.Visibility = textVisibility;
        NavigationSettingsText.Visibility = textVisibility;

        foreach (var button in new[]
        {
            NavigationHomeButton,
            NavigationTasksButton,
            NavigationHistoryButton,
            NavigationImportButton,
            NavigationTodayInspectionButton,
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

    private static void ScrollSelectedToCenter(ListBox listBox) => listBox.Dispatcher.BeginInvoke(() =>
    {
        listBox.UpdateLayout();
        if (listBox.SelectedItem is null || FindVisualChild<ScrollViewer>(listBox) is not { } scrollViewer ||
            listBox.ItemContainerGenerator.ContainerFromIndex(listBox.SelectedIndex) is not FrameworkElement item)
            return;
        var top = item.TransformToAncestor(scrollViewer).Transform(new Point()).Y;
        scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + top - (scrollViewer.ViewportHeight - item.ActualHeight) / 2));
    }, DispatcherPriority.Loaded);

    private static Rect GetScreenBounds(FrameworkElement element, DpiScale dpi) =>
        new(element.PointToScreen(new Point()), new Size(
            element.ActualWidth * dpi.DpiScaleX,
            element.ActualHeight * dpi.DpiScaleY));

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

    private async void ExportTodayInspection_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出今日排查计划",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"今日排查计划_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        await shell.TodayInspection.ExportAsync(dialog.FileName);
        if (shell.TodayInspection.LatestExportResult is { } result && result.OutputPath == dialog.FileName)
            WpfDialogService.ShowExportSuccess(this, result);
    }

    private async void OpenTodayInspection_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择已填写的今日排查计划",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false,
            DefaultExt = ".xlsx",
            AddExtension = false
        };
        if (dialog.ShowDialog(this) != true) return;
        await shell.TodayInspection.PreviewAsync(dialog.FileName);
        if (shell.TodayInspection.HasPreview)
        {
            var confirmation = new TodayInspectionConfirmationWindow { Owner = this, DataContext = shell.TodayInspection };
            confirmation.ShowDialog();
        }
    }

    private void ShowTodayPreviewFailure(string message) => WpfDialogService.Show(
        this, "暂时无法读取", message, "知道了", WpfDialogKind.Warning,
        "请检查文件后重新导出最新计划，再导入结果。", false);

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
                Content = new Grid { Margin = new Thickness(24) }
            };
            AutomationProperties.SetName(dialog, "提醒设置");
            var root = (Grid)dialog.Content;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            var panel = new StackPanel();
            root.Children.Add(panel);
            panel.Children.Add(new TextBlock
            {
                Text = "每日提醒时间",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "每天在该时间集中提醒，修改后立即重新安排提醒。",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 10)
            });
            var pickerState = new ReminderTimePickerState(reminderMinuteOfDay);
            var selectedReminderMinuteOfDay = reminderMinuteOfDay;
            var timeValidation = new TextBlock
            {
                Foreground = (Brush)FindResource("DangerBrush"),
                FontSize = 12,
                MinHeight = 18,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var reminderTime = new TextBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Text = ReminderSettingsUseCase.Format(reminderMinuteOfDay),
                ToolTip = "请输入 HH:mm，或点击时钟选择",
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("BorderBrush"),
                Height = 32,
                Padding = new Thickness(8, 4, 42, 4)
            };
            AutomationProperties.SetName(reminderTime, "每日提醒时间");
            var reminderTimeRow = new Grid { Width = 160, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
            reminderTimeRow.Children.Add(reminderTime);
            var pickerToggleStyle = new Style(typeof(Button));
            pickerToggleStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            pickerToggleStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            pickerToggleStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            pickerToggleStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            pickerToggleStyle.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(241, 245, 249))) } });
            pickerToggleStyle.Triggers.Add(new Trigger { Property = ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240))) } });
            pickerToggleStyle.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(100, 116, 139))) } });
            var pickerToggle = new Button
            {
                Width = 38,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = pickerToggleStyle,
                ToolTip = "选择每日提醒时间",
                Content = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("ClockIcon"), Stroke = (Brush)FindResource("SecondaryTextBrush"), StrokeThickness = 1.4, Width = 16, Height = 16 }
            };
            AutomationProperties.SetName(pickerToggle, "打开提醒时间选择器");
            reminderTimeRow.Children.Add(pickerToggle);
            panel.Children.Add(reminderTimeRow);
            panel.Children.Add(timeValidation);
            var timePicker = new Popup { PlacementTarget = pickerToggle, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true };
            var pickerBorder = new Border { Background = (Brush)FindResource("SurfaceBrush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0), MaxWidth = 180, MaxHeight = 208 };
            var pickerGrid = new Grid();
            pickerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pickerGrid.RowDefinitions.Add(new RowDefinition());
            pickerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pickerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            pickerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            pickerGrid.Children.Add(new TextBlock { Text = "小时", FontWeight = FontWeights.SemiBold });
            var minuteLabel = new TextBlock { Text = "分钟", FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(minuteLabel, 1);
            pickerGrid.Children.Add(minuteLabel);
            var hours = new ListBox
            {
                ItemsSource = ReminderTimePickerState.Hours,
                SelectedIndex = pickerState.Hour,
                Height = 116,
                Margin = new Thickness(0, 4, 6, 0)
            };
            var minutes = new ListBox
            {
                ItemsSource = ReminderTimePickerState.Minutes,
                SelectedIndex = pickerState.Minute,
                Height = 116,
                Margin = new Thickness(0, 4, 0, 0)
            };
            ScrollViewer.SetVerticalScrollBarVisibility(hours, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(minutes, ScrollBarVisibility.Auto);
            AutomationProperties.SetName(hours, "提醒小时");
            AutomationProperties.SetName(minutes, "提醒分钟");
            Grid.SetRow(hours, 1);
            Grid.SetRow(minutes, 1);
            Grid.SetColumn(minutes, 1);
            pickerGrid.Children.Add(hours);
            pickerGrid.Children.Add(minutes);
            var pickerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var pickerCancel = new Button { Content = "取消", Width = 64, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
            var pickerConfirm = new Button { Content = "确定", Width = 64, Style = (Style)FindResource("PrimaryButtonStyle") };
            pickerButtons.Children.Add(pickerCancel);
            pickerButtons.Children.Add(pickerConfirm);
            Grid.SetRow(pickerButtons, 2);
            Grid.SetColumnSpan(pickerButtons, 2);
            pickerGrid.Children.Add(pickerButtons);
            pickerBorder.Child = pickerGrid;
            timePicker.Child = pickerBorder;
            pickerBorder.Resources.Add(typeof(ListBoxItem), new Style(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(FrameworkElement.HeightProperty, 24d), new Setter(Control.PaddingProperty, new Thickness(0)), new Setter(FrameworkElement.MarginProperty, new Thickness(0)),
                    new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center), new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                    new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(71, 85, 105))), new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(203, 213, 225))), new Setter(Control.BorderThicknessProperty, new Thickness(1))
                },
                Triggers = { new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(234, 240, 247))), new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(51, 65, 85))), new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(180, 195, 210))) } } }
            });
            pickerBorder.Resources.Add(typeof(ScrollBar), new Style(typeof(ScrollBar)) { Setters = { new Setter(OpacityProperty, 0.55) } });
            var pickerWasConfirmed = false;
            void ClosePicker() { pickerState.Cancel(); timePicker.IsOpen = false; }
            bool CommitText()
            {
                if (!ReminderTimePickerState.TryApplyText(reminderTime.Text, selectedReminderMinuteOfDay, out var minuteOfDay))
                {
                    timeValidation.Text = "请输入有效时间（00:00–23:59）";
                    return false;
                }
                selectedReminderMinuteOfDay = minuteOfDay;
                reminderTime.Text = ReminderSettingsUseCase.Format(minuteOfDay);
                timeValidation.Text = string.Empty;
                return true;
            }
            void ApplyPickerState()
            {
                hours.SelectedIndex = pickerState.Hour;
                minutes.SelectedIndex = pickerState.Minute;
            }
            hours.SelectionChanged += (_, _) => pickerState.Select(hours.SelectedIndex, minutes.SelectedIndex);
            minutes.SelectionChanged += (_, _) => pickerState.Select(hours.SelectedIndex, minutes.SelectedIndex);
            reminderTime.LostKeyboardFocus += (_, _) => CommitText();
            reminderTime.KeyDown += (_, key) => { if (key.Key == Key.Enter) { CommitText(); key.Handled = true; } };
            void OpenPicker()
            {
                pickerState.Open(selectedReminderMinuteOfDay);
                ApplyPickerState();
                pickerWasConfirmed = false;
                timePicker.IsOpen = true;
                ScrollSelectedToCenter(hours);
                ScrollSelectedToCenter(minutes);
            }
            pickerToggle.Click += (_, _) => OpenPicker();
            pickerCancel.Click += (_, _) => ClosePicker();
            pickerConfirm.Click += (_, _) =>
            {
                selectedReminderMinuteOfDay = pickerState.Confirm();
                reminderTime.Text = ReminderSettingsUseCase.Format(selectedReminderMinuteOfDay);
                timeValidation.Text = string.Empty;
                pickerWasConfirmed = true;
                timePicker.IsOpen = false;
            };
            timePicker.Closed += (_, _) =>
            {
                if (!pickerWasConfirmed) pickerState.Cancel();
            };
            pickerBorder.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { ClosePicker(); key.Handled = true; } };
            dialog.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape && timePicker.IsOpen) { ClosePicker(); key.Handled = true; } };
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
            var settingsValidation = new TextBlock
            {
                Foreground = (Brush)FindResource("DangerBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Text = autoStartState.Succeeded
                    ? string.Empty
                    : $"开机自启动状态读取失败，本次不会修改：{autoStartState.ErrorMessage}"
            };
            AutomationProperties.SetName(settingsValidation, "提醒设置校验结果");
            panel.Children.Add(settingsValidation);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
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
            timePicker.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            {
                var dpi = VisualTreeHelper.GetDpi(root);
                var anchor = GetScreenBounds(pickerToggle, dpi);
                var visibleWindow = GetScreenBounds(root, dpi);
                var saveTop = save.PointToScreen(new Point()).Y;
                var location = ReminderTimePopupPlacement.Calculate(
                    new Size(popupSize.Width * dpi.DpiScaleX, popupSize.Height * dpi.DpiScaleY), anchor, visibleWindow, saveTop);
                return [new CustomPopupPlacement(new Point((location.X - anchor.X) / dpi.DpiScaleX, (location.Y - anchor.Y) / dpi.DpiScaleY), PopupPrimaryAxis.Vertical)];
            };
            int? savedMinuteOfDay = null;
            save.Click += (_, _) =>
            {
                if (!CommitText()) return;
                ReminderTimeSaveResult result;
                try
                {
                    using var context = DatabaseInitializer.CreateContext();
                    result = settings.SaveReminderTime(context, ReminderSettingsUseCase.Format(selectedReminderMinuteOfDay));
                }
                catch (Exception exception)
                {
                    settingsValidation.Text = $"提醒时间保存失败：{exception.Message}";
                    return;
                }

                if (!result.Succeeded || result.ReminderMinuteOfDay is not int savedMinute)
                {
                    timeValidation.Text = result.Message;
                    reminderTime.Focus();
                    return;
                }

                savedMinuteOfDay = savedMinute;
                reminderTime.Text = ReminderSettingsUseCase.Format(savedMinute);
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
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            dialog.Loaded += (_, _) =>
            {
                reminderTime.Focus();
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

    private bool ConfirmTodayOverStock(IReadOnlyList<OverStockConfirmation> confirmations) =>
        WpfDialogService.Show(
            this,
            "确认超库存排查",
            string.Join("\n", confirmations.Select(item =>
            {
                var productName = (DataContext as ShellViewModel)?.TodayInspection.Tasks
                    .SingleOrDefault(task => task.TaskId == item.TaskId)?.ProductName;
                return $"{productName ?? "商品"}：当前库存 {item.EffectiveStockQty}，本次排查 {item.TotalCheckedQty}";
            })),
            "确认仍然提交",
            WpfDialogKind.Warning,
            nextAction: "选择“取消”返回检查；当前事实变化后需要重新确认。",
            showCancel: true);

    private bool ConfirmTodayExpiredInventory(ExpiredInventoryWarning warning) =>
        WpfDialogService.Show(
            OwnedWindows.OfType<TodayInspectionConfirmationWindow>().FirstOrDefault(window => window.IsActive) as Window ?? this,
            "检测到过期商品仍有库存",
            $"检测到 {warning.BatchCount} 个过期批次仍填写正库存，合计 {warning.TotalCheckedQty} 件。请复核现场库存和填写值。",
            "确认无误，继续提交",
            WpfDialogKind.Warning,
            nextAction: "选择“返回检查”保留当前填写结果。",
            showCancel: true,
            cancelText: "返回检查");

    private bool ConfirmTodaySubmission() =>
        WpfDialogService.Show(
            OwnedWindows.OfType<TodayInspectionConfirmationWindow>().FirstOrDefault(window => window.IsActive) as Window ?? this,
            "确认提交",
            "是否提交本次排查数据？提交后将生成正式排查记录，请确认数据无误。",
            "确认提交",
            WpfDialogKind.Warning,
            showCancel: true);

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

internal sealed class ReminderTimePickerState
{
    public static IReadOnlyList<string> Hours { get; } = Enumerable.Range(0, 24).Select(value => value.ToString("00")).ToArray();
    public static IReadOnlyList<string> Minutes { get; } = Enumerable.Range(0, 60).Select(value => value.ToString("00")).ToArray();

    private int _openedMinuteOfDay;

    public ReminderTimePickerState(int reminderMinuteOfDay) => Open(reminderMinuteOfDay);

    public int Hour { get; private set; }

    public int Minute { get; private set; }

    public void Open(int reminderMinuteOfDay)
    {
        if (reminderMinuteOfDay is < 0 or >= 24 * 60) throw new ArgumentOutOfRangeException(nameof(reminderMinuteOfDay));
        _openedMinuteOfDay = reminderMinuteOfDay;
        Hour = reminderMinuteOfDay / 60;
        Minute = reminderMinuteOfDay % 60;
    }

    public void Select(int hour, int minute)
    {
        if (hour is >= 0 and < 24) Hour = hour;
        if (minute is >= 0 and < 60) Minute = minute;
    }

    public void Cancel() => Open(_openedMinuteOfDay);

    public int Confirm() => Hour * 60 + Minute;

    public static bool TryParse(string? value, out int minuteOfDay)
    {
        minuteOfDay = 0;
        return TimeOnly.TryParseExact(value?.Trim(), ["H:mm", "HH:mm"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
            && (minuteOfDay = time.Hour * 60 + time.Minute) >= 0;
    }

    public static bool TryApplyText(string? value, int currentMinuteOfDay, out int minuteOfDay)
    {
        if (TryParse(value, out minuteOfDay)) return true;
        minuteOfDay = currentMinuteOfDay;
        return false;
    }
}

internal static class ReminderTimePopupPlacement
{
    private const double Margin = 4;

    public static Point Calculate(Size popupSize, Rect anchor, Rect visibleWindow, double saveTop)
    {
        var x = Math.Clamp(anchor.Left, visibleWindow.Left, Math.Max(visibleWindow.Left, visibleWindow.Right - popupSize.Width));
        var down = anchor.Bottom + Margin;
        var bottomLimit = Math.Min(visibleWindow.Bottom, saveTop - Margin);
        if (down + popupSize.Height <= bottomLimit)
        {
            return new Point(x, down);
        }

        var up = anchor.Top - popupSize.Height - Margin;
        var maxY = Math.Max(visibleWindow.Top, bottomLimit - popupSize.Height);
        return new Point(x, Math.Clamp(up, visibleWindow.Top, maxY));
    }
}
