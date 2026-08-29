using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S4T10UiRefreshStaticAuditTests
{
    [Fact]
    public void RefreshKeepsTheApprovedShellPagesAndVisualTokens()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));

        foreach (var token in new[]
        {
            "CanvasBrush", "SurfaceBrush", "SurfaceSubtleBrush", "PrimaryTextBrush",
            "SecondaryTextBrush", "DisabledTextBrush", "BorderBrush", "PrimaryActionBrush",
            "PrimaryActionHoverBrush", "FocusRingBrush", "DangerBrush", "WarningTextBrush",
            "WarningSurfaceBrush", "SuccessBrush", "SuccessSurfaceBrush", "ErrorSurfaceBrush",
            "ReconfirmBrush", "ReconfirmSurfaceBrush"
        })
        {
            Assert.Contains($"x:Key=\"{token}\"", app, StringComparison.Ordinal);
        }

        Assert.Contains("FontFamily=\"Microsoft YaHei UI, Segoe UI\"", window, StringComparison.Ordinal);
        Assert.Contains("Language=\"zh-CN\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"门店效期排查软件\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"排查记录\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"设置\"", window, StringComparison.Ordinal);

        var pageOrder = new[] { "Text=\"首页\"", "Text=\"待排查任务\"", "Text=\"排查记录\"", "Text=\"数据导入\"", "Text=\"设置\"" };
        var previous = -1;
        foreach (var marker in pageOrder)
        {
            var current = window.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(current > previous, $"导航项顺序错误：{marker}");
            previous = current;
        }
    }

    [Fact]
    public void RefreshPreservesTableCopyPagingDetailAndImportGates()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        var viewModels = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "Stage4ViewModels.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var dataGridStyle = StyleBlock(app, "<Style TargetType=\"DataGrid\">");
        var columnHeaderStyle = StyleBlock(app, "<Style TargetType=\"DataGridColumnHeader\">");
        var cellStyle = StyleBlock(app, "<Style TargetType=\"DataGridCell\">");
        var rowStyle = StyleBlock(app, "<Style TargetType=\"DataGridRow\">");

        Assert.Contains("MinWidth=\"1024\"", window, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"600\"", window, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel=\"DashboardDataGrid_PreviewMouseWheel\"", window, StringComparison.Ordinal);
        Assert.Contains("ContentRoot.Margin", codeBehind, StringComparison.Ordinal);
        Assert.True(Count(window, "SelectionUnit=\"Cell\"") >= 2);
        Assert.True(Count(window, "ClipboardCopyMode=\"ExcludeHeader\"") >= 2);
        Assert.Contains("Binding=\"{Binding ProductBarcode}\"", window, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ProductCode}\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TableDividerBrush\" Color=\"#E7EBF0\"", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", dataGridStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"RowHeight\" Value=\"40\" />", dataGridStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ColumnHeaderHeight\" Value=\"40\" />", dataGridStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"GridLinesVisibility\" Value=\"Horizontal\" />", dataGridStyle, StringComparison.Ordinal);
        Assert.Contains("HorizontalGridLinesBrush\" Value=\"{DynamicResource TableDividerBrush}\"", dataGridStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("VerticalGridLinesBrush", dataGridStyle, StringComparison.Ordinal);
        Assert.Contains("AlternatingRowBackground\" Value=\"{DynamicResource SurfaceBrush}\"", dataGridStyle, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment\" Value=\"Center\"", columnHeaderStyle, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment\" Value=\"Center\"", cellStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", rowStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("0,0,0,1", rowStyle, StringComparison.Ordinal);
        Assert.Contains("FixedPageSize = 50", viewModels, StringComparison.Ordinal);
        Assert.Contains("Content=\"‹  上一页\"", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"下一页  ›\"", window, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", window, StringComparison.Ordinal);
        Assert.Contains("ShowSubmitFooter", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Detail.SubmitButtonText}\"", window, StringComparison.Ordinal);
        Assert.Contains("DraftFooterStatusText", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"正在导入，请稍候…\"", window, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"True\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate", window, StringComparison.Ordinal);
        Assert.Contains("new(\"全部阶段\", null)", viewModels, StringComparison.Ordinal);
        Assert.Contains("Dashboard.ExpiredCount", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"搜索\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"（以当前 Application 结果为准）\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmissionHintText", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"查看导入记录  →\"", window, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"False\"", window, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"导入记录功能暂未开放\"", window, StringComparison.Ordinal);
        Assert.Contains("Import.IssueRows", window, StringComparison.Ordinal);
        Assert.Contains("Header=\"问题类型\"", window, StringComparison.Ordinal);
        Assert.Contains("ConfirmAvailabilityText", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"搜索商品名称 / 商品条码 / 商品编码\"", window, StringComparison.Ordinal);
        Assert.Equal(2, Count(window, "Text=\"搜索商品名称 / 商品条码 / 商品编码\""));
        Assert.Contains("ShellColumn\" Width=\"224\"", window, StringComparison.Ordinal);
        Assert.Contains("ShellColumn.Width = new(224)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("M 12,16 L 12,3", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"UploadIcon\"", app, StringComparison.Ordinal);
        Assert.Contains("M 12,3 L 12,14 M 7,9 L 12,14 L 17,9 M 4,16 L 4,21 L 20,21 L 20,16", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NavIconPathStyle\"", app, StringComparison.Ordinal);
        Assert.Contains("StrokeStartLineCap", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"DatePicker\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DatePickerCalendarStyle\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DatePickerCalendarDayButtonStyle\" TargetType=\"CalendarDayButton\"", app, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"CalendarDayButton\">", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DatePickerCalendarButtonStyle\" TargetType=\"CalendarButton\"", app, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"CalendarButton\">", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DatePickerCalendarItemStyle\" TargetType=\"CalendarItem\"", app, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"CalendarItem\">", app, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"Calendar\">", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_MonthView\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_YearView\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_PreviousButton\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_NextButton\"", app, StringComparison.Ordinal);
        Assert.Contains("{x:Static CalendarItem.DayTitleTemplateResourceKey}", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_CalendarItem\"", app, StringComparison.Ordinal);
        Assert.Contains("CalendarDayButtonStyle\" Value=\"{StaticResource DatePickerCalendarDayButtonStyle}\"", app, StringComparison.Ordinal);
        Assert.Contains("CalendarItemStyle\" Value=\"{StaticResource DatePickerCalendarItemStyle}\"", app, StringComparison.Ordinal);
        Assert.Contains("CalendarStyle\" Value=\"{StaticResource DatePickerCalendarStyle}\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Button\"", app, StringComparison.Ordinal);
        Assert.Contains("CalendarIcon", app, StringComparison.Ordinal);
        Assert.Contains("DisplayDateEnd=\"{Binding Detail.CheckDateMaxValue, Mode=OneWay}\"", window, StringComparison.Ordinal);

        var detailStart = window.IndexOf("<!-- 排查详情", StringComparison.Ordinal);
        var detailEnd = window.IndexOf("<!-- 数据导入", detailStart, StringComparison.Ordinal);
        Assert.True(detailStart >= 0 && detailEnd > detailStart);
        var detail = window.Substring(detailStart, detailEnd - detailStart);
        Assert.Equal(1, Count(detail, "<ScrollViewer"));
        Assert.Contains("x:Name=\"DetailScrollViewer\"", detail, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Detail.TaskItems}\"", detail, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Detail.NormalBatches}\"", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("<DataGrid", detail, StringComparison.Ordinal);
        var detailScrollEnd = detail.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        var taskItems = detail.IndexOf("ItemsSource=\"{Binding Detail.TaskItems}\"", StringComparison.Ordinal);
        var normalItems = detail.IndexOf("ItemsSource=\"{Binding Detail.NormalBatches}\"", StringComparison.Ordinal);
        Assert.True(taskItems >= 0 && normalItems > taskItems && normalItems < detailScrollEnd);
        var pendingItemsEnd = detail.IndexOf("</ItemsControl>", taskItems, StringComparison.Ordinal);
        var pendingBorderEnd = detail.IndexOf("</Border>", pendingItemsEnd, StringComparison.Ordinal);
        var pendingGridEnd = detail.IndexOf("</Grid>", pendingBorderEnd, StringComparison.Ordinal);
        var expanderStart = detail.IndexOf("<Expander", StringComparison.Ordinal);
        Assert.True(pendingItemsEnd > taskItems && pendingBorderEnd > pendingItemsEnd && pendingGridEnd > pendingBorderEnd && pendingGridEnd < expanderStart, "正常批次 Expander 必须位于待排查表容器之后");
        var taskItemsContainer = detail.LastIndexOf("<Border", taskItems, StringComparison.Ordinal);
        Assert.True(taskItemsContainer >= 0);
        Assert.Contains("Grid.Row=\"1\"", detail[taskItemsContainer..taskItems], StringComparison.Ordinal);
        var footer = detail.IndexOf("Grid Grid.Row=\"1\"", detailScrollEnd, StringComparison.Ordinal);
        Assert.True(footer > detailScrollEnd, "固定底栏必须位于详情滚动区之外");
        var expanderEnd = detail.IndexOf("</Expander>", expanderStart, StringComparison.Ordinal);
        Assert.True(expanderStart >= 0 && expanderEnd > expanderStart);
        var expander = detail.Substring(expanderStart, expanderEnd - expanderStart);
        Assert.Contains("ItemsSource=\"{Binding Detail.NormalBatches}\"", expander, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding Detail.TaskItems}\"", expander, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", expander, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail.DraftStatusText", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail.SaveStatusText", detail, StringComparison.Ordinal);
        Assert.Contains("Detail.DraftFooterStatusText", detail, StringComparison.Ordinal);
        Assert.Contains("草稿保存失败 · 重试", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "InspectionDetailViewModel.cs")), StringComparison.Ordinal);
    }

    private static int Count(string value, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }

        return count;
    }

    private static string StyleBlock(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        var end = source.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"未找到样式块：{marker}");
        return source[start..end];
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
