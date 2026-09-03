using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using System.Xml.Linq;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class UIUXR03UiStaticAuditTests
{
    [Fact]
    public void R03KeepsTheApprovedWorkflowsTableFirstAndAccessible()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));

        foreach (var token in new[]
        {
            "PageSectionBorderStyle", "StateMessageBorderStyle", "HistoryListDataGridStyle",
            "BackupListDataGridStyle", "AutomationProperties.Name", "Height=\"72\""
        })
        {
            Assert.Contains(token, window, StringComparison.Ordinal);
        }

        var import = Section(window, "<!-- 数据导入 -->", "</Window>");
        Assert.DoesNotContain("AutomationProperties.Name=\"导入步骤\"", import, StringComparison.Ordinal);
        Assert.Contains("正在解析文件，请稍候…", import, StringComparison.Ordinal);
        Assert.Contains("正在导入，请稍候…", import, StringComparison.Ordinal);
        Assert.Contains("Text=\"影响摘要\"", import, StringComparison.Ordinal);
        Assert.Contains("SelectedFilePath", import, StringComparison.Ordinal);
        Assert.Contains("IssueRows", import, StringComparison.Ordinal);
        Assert.Contains("Content=\"确认导入\"", import, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Import.ConfirmCommand}\"", import, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource PrimaryButtonStyle}\"", import, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"导入成功结果\"", import, StringComparison.Ordinal);
        Assert.Contains("Import.SuccessSummaryText", import, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Import.IsSucceeded, Converter={StaticResource BoolToVisibility}}\"", import, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Import.IsSucceeded, Converter={StaticResource InverseBoolToVisibility}}\"", import, StringComparison.Ordinal);
        Assert.Equal(1, import.Split("Text=\"导入成功\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("UniformGrid Columns=\"5\"", import, StringComparison.Ordinal);

        var dashboard = Section(window, "<!-- 首页 -->", "<!-- 待排查任务 -->");
        Assert.Contains("Text=\"正在加载首页数据…\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Text=\"暂无导入数据，请先导入最新的商品效期 Excel。\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Dashboard.HasError, Converter={StaticResource InverseBoolToVisibility}}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#8995AA\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("DashboardDataGridStyle", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DashboardUrgentTasksDataGrid\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Text=\"优先处理\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("HomeSearchBox", dashboard, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SearchTasksCommand}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SearchTasksCommand}\"", ButtonMarkup(dashboard, "搜索"), StringComparison.Ordinal);
        Assert.Contains("Dashboard.SearchResultText", dashboard, StringComparison.Ordinal);
        Assert.Contains("ClearDashboardSearchCommand", dashboard, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding PendingTasks.HasSearchText, Converter={StaticResource BoolToVisibility}}\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardDataGridSurfaceStyle", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("StateMessageBorderStyle", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("首页优先处理任务列表", dashboard, StringComparison.Ordinal);

        var pending = Section(window, "<!-- 待排查任务 -->", "<!-- 排查历史 -->");
        Assert.Contains("x:Name=\"PendingTasksStandardGrid\"", pending, StringComparison.Ordinal);
        Assert.Contains("Text=\"待排查任务加载失败\"", pending, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#A4262C\"", pending, StringComparison.Ordinal);
        Assert.Contains("Text=\"没有符合当前条件的任务\"", pending, StringComparison.Ordinal);
        Assert.Contains("PendingDataGridStyle", pending, StringComparison.Ordinal);
        Assert.Contains("PendingTasksCompactGrid", pending, StringComparison.Ordinal);
        Assert.Contains("Header=\"商品条码\"", pending, StringComparison.Ordinal);
        Assert.Contains("Header=\"商品编码\"", pending, StringComparison.Ordinal);
        Assert.Contains("Header=\"批次数\"", pending, StringComparison.Ordinal);
        Assert.Contains("HeaderStyle=\"{StaticResource TableCenterColumnHeaderStyle}\"", pending, StringComparison.Ordinal);
        Assert.Contains("ElementStyle=\"{StaticResource TableNumericCenterTextStyle}\"", pending, StringComparison.Ordinal);
        Assert.Contains("Grid Grid.Row=\"4\" Margin=\"0,12,0,16\"", pending, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PendingTasks.SearchCommand}\"", ButtonMarkup(pending, "搜索"), StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding PendingTasks.IsFilterActive, Converter={StaticResource BoolToVisibility}}\"", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("商品条码（可复制）", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("商品编码（可复制）", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingDataGridSurfaceStyle", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("StateMessageBorderStyle", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("待排查任务列表", pending, StringComparison.Ordinal);

        var detail = Section(window, "<!-- 排查详情 -->", "<!-- 数据导入 -->");
        Assert.Contains("Text=\"正在加载…\"", detail, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Detail.IsTerminal, Converter={StaticResource BoolToVisibility}}\"", detail, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource PrimaryButtonStyle}\"", detail, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#A4262C\"", detail, StringComparison.Ordinal);
        Assert.Contains("InspectionStatusTagStyle", detail, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Detail.TaskItems}\"", detail, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailScrollViewer\"", detail, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", detail, StringComparison.Ordinal);
        Assert.Contains("CanContentScroll=\"False\"", detail, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InspectionDetailRoot\"", detail, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", detail, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\"", detail, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"本次排查数量\"", detail, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"提交失败\"", detail, StringComparison.Ordinal);
        Assert.Contains("下一步：请修改后重新提交；如仍失败请重试。", detail, StringComparison.Ordinal);
        var detailErrorIndex = detail.IndexOf("AutomationProperties.Name=\"提交失败\"", StringComparison.Ordinal);
        var detailScrollEnd = detail.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        Assert.True(detailScrollEnd >= 0 && detailErrorIndex > detailScrollEnd, "提交错误区必须位于固定底部操作区，而不是详情滚动内容末尾。");
        Assert.Equal(1, detail.Split("AutomationProperties.Name=\"提交失败\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("StateMessageBorderStyle", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("重试读取排查详情", detail, StringComparison.Ordinal);
        AssertDetailLayout(window);

        var history = Section(window, "<!-- 排查历史 -->", "<!-- 数据备份与恢复 -->");
        Assert.Contains("正式排查历史列表", history, StringComparison.Ordinal);
        Assert.Contains("HistoryListDataGridStyle", history, StringComparison.Ordinal);
        Assert.Contains("HasEmptyResult", history, StringComparison.Ordinal);
        Assert.Contains("重试加载排查历史", history, StringComparison.Ordinal);
        Assert.Contains("正式排查批次快照列表", window, StringComparison.Ordinal);
        Assert.Contains("选中批次的 Revision 历史", window, StringComparison.Ordinal);
        Assert.Contains("StringFormat=批次 {0}", history, StringComparison.Ordinal);
        Assert.DoesNotContain("明细", history, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectionItemId", history, StringComparison.Ordinal);
        Assert.Contains("DisplayBatchNumber", history, StringComparison.Ordinal);
        Assert.Contains("SelectedDetailBatchNumber", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("request.InspectionItemId", codeBehind, StringComparison.Ordinal);
        AssertHistoryDetailColumnAlignment(window);

        var backup = Section(window, "<!-- 数据备份与恢复 -->", "<!-- 排查详情 -->");
        foreach (var token in new[]
        {
            "数据备份与恢复工作区", "HumanReadableFileSizeConverter", "FileName", "VerificationStatusText",
            "立即创建并验证备份", "恢复所选备份", "备份恢复状态", "IsBusy",
            "IsRestartRequired", "IsCriticalFailure", "操作已锁定"
        })
        {
            Assert.Contains(token, backup, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Text=\"{Binding BackupRestore.SelectedBackup.BackupId", backup, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding FileSize, Converter={StaticResource HumanReadableFileSizeConverter}}\"", backup, StringComparison.Ordinal);
        Assert.Contains("Text=\"本地备份\"", backup, StringComparison.Ordinal);
        var verificationColumn = backup.IndexOf("Header=\"验证状态\"", StringComparison.Ordinal);
        var columnsEnd = backup.IndexOf("</DataGrid.Columns>", verificationColumn, StringComparison.Ordinal);
        Assert.True(verificationColumn >= 0 && columnsEnd > verificationColumn);
        Assert.Contains("Width=\"*\"", backup[verificationColumn..columnsEnd], StringComparison.Ordinal);

        foreach (var token in new[]
        {
            "AutomationProperties.SetName(dialog, \"提醒设置\")",
            "AutomationProperties.SetName(reminderTime, \"每日提醒时间\")",
            "AutomationProperties.SetName(pickerToggle, \"选择提醒时间\")",
            "Title = \"选择提醒时间\"", "Content = \"选择时间\"", "请输入有效时间（00:00–23:59）", "Enumerable.Range(0, 24)", "Enumerable.Range(0, 60)",
            "WpfDialogService.Show(", "WpfDialogKind.Danger", "DangerButtonStyle"
        })
        {
            Assert.Contains(token, codeBehind, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("new Popup", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomPopupPlacementCallback", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAppDialog(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AppDialogKind", codeBehind, StringComparison.Ordinal);
        Assert.Contains("NavigationToggleButton.Width = _isNavigationCollapsed ? 24 : 32", codeBehind, StringComparison.Ordinal);
        Assert.Contains("InspectionInspectorNameTextBoxStyle", detail, StringComparison.Ordinal);
        Assert.Contains("HasInspectorNameError", window, StringComparison.Ordinal);

        var appCode = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var reminderCode = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WindowsMessageBoxReminderChannel.cs"));
        var dialogService = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs"));
        Assert.DoesNotContain("MessageBox.Show", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox.Show", reminderCode, StringComparison.Ordinal);
        Assert.Contains("WpfDialogService.Show", appCode, StringComparison.Ordinal);
        Assert.Contains("WpfDialogService.Show", reminderCode, StringComparison.Ordinal);
        Assert.Contains("WpfDialogKind.Warning", dialogService, StringComparison.Ordinal);

        var dateConverter = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "Stage4ViewModels.cs"));
        Assert.Contains("ToString(\"yyyy-MM-dd HH:mm\", CultureInfo.InvariantCulture)", dateConverter, StringComparison.Ordinal);
        Assert.DoesNotContain("'UTC'", dateConverter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardSearchUsesTheCanonicalApplicationQueryAndUpdatesVisibleRows()
    {
        var requests = new List<InspectionTaskSearchRequest>();
        var initial = TaskItem("initial", "expired");
        var found = TaskItem("found", "withdraw");
        var vm = new DashboardViewModel(
            () => new InspectionDashboardResult(
                1,
                1,
                0,
                0,
                0,
                new[] { initial },
                DateTime.UtcNow),
            searchTasks: request =>
            {
                requests.Add(request);
                return new InspectionTaskSearchResult(
                    new[] { found },
                    1,
                    request.Page,
                    request.PageSize);
            });

        await vm.LoadAsync();
        await vm.SearchAsync("  690000000001  ");

        var request = Assert.Single(requests);
        Assert.Equal("690000000001", request.SearchText);
        Assert.Null(request.Stage);
        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(new[] { "found" }, vm.UrgentTasks.Select(item => item.ProductCode));
        Assert.True(vm.IsSearchActive);
        Assert.Equal(1, vm.SearchResultCount);
        Assert.False(vm.HasNoSearchResults);
    }

    [Fact]
    public async Task ClearingDashboardSearchPreservesPendingStageAndDoesNotReloadPendingTasks()
    {
        var dashboardLoadCount = 0;
        var initialDashboardLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dashboardReloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingStageRequestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRequests = new List<InspectionTaskSearchRequest>();

        var shell = new ShellViewModel(
            dashboardLoader: () =>
            {
                var loadNumber = Interlocked.Increment(ref dashboardLoadCount);
                if (loadNumber == 1)
                {
                    initialDashboardLoaded.TrySetResult();
                }
                else
                {
                    dashboardReloaded.TrySetResult();
                }

                return new InspectionDashboardResult(
                    0,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<InspectionTaskListItem>(),
                    DateTime.UtcNow);
            },
            taskLoader: request =>
            {
                lock (pendingRequests)
                {
                    pendingRequests.Add(request);
                }

                if (request.Stage == "expired")
                {
                    pendingStageRequestSeen.TrySetResult();
                }

                return new InspectionTaskSearchResult(
                    Array.Empty<InspectionTaskListItem>(),
                    0,
                    request.Page,
                    request.PageSize);
            });

        await initialDashboardLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shell.PendingTasks.SelectedStage = "expired";
        await pendingStageRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shell.PendingTasks.SearchText = "首页搜索";
        int pendingRequestCount;
        lock (pendingRequests)
        {
            pendingRequestCount = pendingRequests.Count;
        }

        shell.ClearDashboardSearchCommand.Execute(null);
        await dashboardReloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("expired", shell.PendingTasks.SelectedStage);
        Assert.Equal(string.Empty, shell.PendingTasks.SearchText);
        lock (pendingRequests)
        {
            Assert.Equal(pendingRequestCount, pendingRequests.Count);
        }
    }

    private static InspectionTaskListItem TaskItem(string code, string stage) => new(
        1,
        1,
        "测试商品",
        code,
        "690000000001",
        stage,
        2,
        10,
        new DateOnly(2026, 8, 28),
        false);

    private static string Section(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        var end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"无法定位 UI section: {startMarker} -> {endMarker}");
        return value[start..end];
    }

    private static string ButtonMarkup(string section, string content)
    {
        var start = section.IndexOf($"Content=\"{content}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"无法定位 Content=\"{content}\" 按钮。");
        var end = section.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"无法定位 Content=\"{content}\" 按钮结束标记。");
        return section[start..(end + 2)];
    }

    private static void AssertDetailLayout(string window)
    {
        const string wpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        const string xNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = wpfNamespace;
        XNamespace x = xNamespace;
        var xaml = XDocument.Parse(window);

        var shell = Assert.Single(xaml.Descendants(wpf + "Grid"), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "ShellRoot", StringComparison.Ordinal));
        Assert.Equal(new[] { "*" }, shell
            .Elements(wpf + "Grid.RowDefinitions")
            .Single()
            .Elements(wpf + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height")));

        var contentRoot = Assert.Single(xaml.Descendants(wpf + "Grid"), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "ContentRoot", StringComparison.Ordinal));
        Assert.Equal(new[] { "Auto", "*" }, contentRoot
            .Elements(wpf + "Grid.RowDefinitions")
            .Single()
            .Elements(wpf + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height")));

        var pageRoot = Assert.Single(xaml.Descendants(wpf + "Grid"), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "ContentPageRoot", StringComparison.Ordinal));
        Assert.Equal("1", (string?)pageRoot.Attribute("Grid.Row"));
        Assert.Equal(new[] { "*" }, pageRoot
            .Elements(wpf + "Grid.RowDefinitions")
            .Single()
            .Elements(wpf + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height")));

        var detailPage = Assert.Single(pageRoot.Elements(wpf + "Grid"), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "InspectionDetailPage", StringComparison.Ordinal));
        var detailRoot = Assert.Single(detailPage.Elements(wpf + "Grid"), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "InspectionDetailRoot", StringComparison.Ordinal));
        Assert.Empty(detailRoot.Ancestors(wpf + "StackPanel"));
        Assert.Empty(detailRoot.Ancestors(wpf + "ScrollViewer"));
        Assert.Equal(new[] { "Auto", "*", "Auto" }, detailRoot
            .Elements(wpf + "Grid.RowDefinitions")
            .Single()
            .Elements(wpf + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height")));

        var top = Assert.Single(detailRoot.Elements(wpf + "StackPanel"), element =>
            string.Equals((string?)element.Attribute("Grid.Row"), "0", StringComparison.Ordinal));
        var scroll = Assert.Single(detailRoot.Elements(wpf + "ScrollViewer"), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "DetailScrollViewer", StringComparison.Ordinal));
        Assert.Equal("1", (string?)scroll.Attribute("Grid.Row"));
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("False", (string?)scroll.Attribute("CanContentScroll"));
        Assert.Equal("Stretch", (string?)scroll.Attribute("VerticalAlignment"));
        Assert.Equal("0", (string?)scroll.Attribute("MinHeight"));
        Assert.Empty(scroll.Descendants(wpf + "ScrollViewer"));
        Assert.Single(scroll.Descendants(wpf + "ItemsControl"), element =>
            string.Equals((string?)element.Attribute("ItemsSource"), "{Binding Detail.TaskItems}", StringComparison.Ordinal));
        Assert.Single(scroll.Descendants(wpf + "ItemsControl"), element =>
            string.Equals((string?)element.Attribute("ItemsSource"), "{Binding Detail.NormalBatches}", StringComparison.Ordinal));

        var footer = Assert.Single(detailRoot.Elements(wpf + "Grid"), element =>
            string.Equals((string?)element.Attribute("Grid.Row"), "2", StringComparison.Ordinal));
        Assert.Contains(footer.DescendantsAndSelf(), element =>
            string.Equals((string?)element.Attribute("AutomationProperties.Name"), "提交失败", StringComparison.Ordinal));
        Assert.DoesNotContain(top.DescendantsAndSelf(), element =>
            string.Equals((string?)element.Attribute(x + "Name"), "DetailScrollViewer", StringComparison.Ordinal));
    }

    private static void AssertHistoryDetailColumnAlignment(string window)
    {
        const string wpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace wpf = wpfNamespace;
        var xaml = XDocument.Parse(window);
        var detailGrid = Assert.Single(xaml.Descendants(wpf + "DataGrid"), element =>
            string.Equals((string?)element.Attribute("ItemsSource"), "{Binding History.DisplayDetailItems}", StringComparison.Ordinal));
        var columns = detailGrid.Elements(wpf + "DataGrid.Columns").Single().Elements();
        Assert.Equal("Item", (string?)detailGrid.Attribute("SelectedValuePath"));
        Assert.Equal("{Binding History.SelectedDetailItem, Mode=TwoWay}", (string?)detailGrid.Attribute("SelectedValue"));
        foreach (var header in new[] { "批次", "累计到货", "正式排查数量" })
        {
            var column = Assert.Single(columns, element =>
                string.Equals((string?)element.Attribute("Header"), header, StringComparison.Ordinal));
            Assert.Equal("{StaticResource TableCenterColumnHeaderStyle}", (string?)column.Attribute("HeaderStyle"));
            Assert.Equal("{StaticResource TableNumericCenterTextStyle}", (string?)column.Attribute("ElementStyle"));
            if (header == "批次")
            {
                Assert.Equal("{Binding DisplayBatchNumber}", (string?)column.Attribute("Binding"));
                Assert.DoesNotContain("InspectionItemId", column.ToString(), StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 StoreExpiryInspector 仓库根目录。");
    }
}
