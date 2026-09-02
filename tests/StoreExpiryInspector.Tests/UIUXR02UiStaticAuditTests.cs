using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class UIUXR02UiStaticAuditTests
{
    [Fact]
    public void R02KeepsTheApprovedShellAndThreePageIdentityRules()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        var taskCard = File.ReadAllText(Path.Combine(root, ".ai-dev", "TASKS", "UIUX-R02.md"));

        foreach (var token in new[] { "MutedTextBrush", "HoverSurfaceBrush", "SelectedSurfaceBrush" })
        {
            Assert.Contains($"x:Key=\"{token}\"", app, StringComparison.Ordinal);
        }

        Assert.Contains("ShellColumn\" Width=\"208\"", window, StringComparison.Ordinal);
        Assert.Contains("ShellColumn.Width = new(compact ? 176 : 208)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"72\" />", window, StringComparison.Ordinal);
        Assert.Contains("Height=\"72\"", window, StringComparison.Ordinal);
        Assert.Contains("PendingTasksStandardGrid", window, StringComparison.Ordinal);
        Assert.Contains("PendingTasksCompactGrid", window, StringComparison.Ordinal);
        Assert.Contains("商品身份（名称 / 条码 / 编码，可复制）", window, StringComparison.Ordinal);
        Assert.True(Count(window, "ClipboardCopyMode=\"ExcludeHeader\"") >= 2);
        Assert.True(Count(window, "SelectionUnit=\"Cell\"") >= 2);
        Assert.Contains("<Style TargetType=\"DataGridCell\">", app, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"{DynamicResource SelectedSurfaceBrush}\"", app, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"{DynamicResource PrimaryTextBrush}\"", app, StringComparison.Ordinal);
        Assert.Contains("BorderBrush\" Value=\"{DynamicResource FocusRingBrush}\"", app, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"2\"", app, StringComparison.Ordinal);
        var rowStyleStart = app.IndexOf("<Style TargetType=\"DataGridRow\">", StringComparison.Ordinal);
        var rowStyleEnd = app.IndexOf("</Style>", rowStyleStart, StringComparison.Ordinal);
        Assert.True(rowStyleStart >= 0 && rowStyleEnd > rowStyleStart);
        var rowStyle = app[rowStyleStart..rowStyleEnd];
        Assert.Contains("SelectedSurfaceBrush", rowStyle, StringComparison.Ordinal);
        Assert.Contains("HoverSurfaceBrush", rowStyle, StringComparison.Ordinal);

        var dashboardStart = window.IndexOf("<!-- 首页", StringComparison.Ordinal);
        var pendingStart = window.IndexOf("<!-- 待排查任务", dashboardStart, StringComparison.Ordinal);
        var historyStart = window.IndexOf("<!-- 排查历史", pendingStart, StringComparison.Ordinal);
        var detailStart = window.IndexOf("<!-- 排查详情", historyStart, StringComparison.Ordinal);
        var detailEnd = window.IndexOf("<!-- 数据导入", detailStart, StringComparison.Ordinal);
        Assert.True(dashboardStart >= 0
            && pendingStart > dashboardStart
            && historyStart > pendingStart
            && detailStart > historyStart
            && detailEnd > detailStart);

        var dashboard = window[dashboardStart..pendingStart];
        var pending = window[pendingStart..historyStart];
        var detail = window[detailStart..detailEnd];
        Assert.Contains("商品编码", dashboard, StringComparison.Ordinal);
        var centeredHeaderMarker = "HeaderStyle=\"{StaticResource TableCenterColumnHeaderStyle}\"";
        var centeredNumericMarker = "ElementStyle=\"{StaticResource TableNumericCenterTextStyle}\"";
        Assert.Equal(3, Count(dashboard, centeredHeaderMarker));
        Assert.Equal(6, Count(pending, centeredHeaderMarker));
        Assert.Equal(14, Count(window, centeredHeaderMarker));
        Assert.Equal(2, Count(dashboard, centeredNumericMarker));
        Assert.Equal(4, Count(pending, centeredNumericMarker));
        Assert.Equal(11, Count(window, centeredNumericMarker));
        var centeredHeaderStyleStart = window.IndexOf("<Style x:Key=\"TableCenterColumnHeaderStyle\"", StringComparison.Ordinal);
        var centeredHeaderStyleEnd = window.IndexOf("</Style>", centeredHeaderStyleStart, StringComparison.Ordinal);
        Assert.True(centeredHeaderStyleStart >= 0 && centeredHeaderStyleEnd > centeredHeaderStyleStart);
        var centeredHeaderStyle = window[centeredHeaderStyleStart..centeredHeaderStyleEnd];
        Assert.Contains("TargetType=\"DataGridColumnHeader\"", centeredHeaderStyle, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource {x:Type DataGridColumnHeader}}\"", centeredHeaderStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalContentAlignment\" Value=\"Center\" />", centeredHeaderStyle, StringComparison.Ordinal);
        var centeredNumericStyleStart = window.IndexOf("<Style x:Key=\"TableNumericCenterTextStyle\"", StringComparison.Ordinal);
        var centeredNumericStyleEnd = window.IndexOf("</Style>", centeredNumericStyleStart, StringComparison.Ordinal);
        Assert.True(centeredNumericStyleStart >= 0 && centeredNumericStyleEnd > centeredNumericStyleStart);
        var centeredNumericStyle = window[centeredNumericStyleStart..centeredNumericStyleEnd];
        Assert.Contains("TargetType=\"TextBlock\"", centeredNumericStyle, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource TableNumericTextStyle}\"", centeredNumericStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalAlignment\" Value=\"Center\" />", centeredNumericStyle, StringComparison.Ordinal);
        var normalizedDashboard = NormalizeWhitespace(dashboard);
        var normalizedPending = NormalizeWhitespace(pending);
        Assert.Contains("<DataGridTextColumn Header=\"库存\" HeaderStyle=\"{StaticResource TableCenterColumnHeaderStyle}\" Binding=\"{Binding EffectiveStockQty}\" Width=\"72\" ElementStyle=\"{StaticResource TableNumericCenterTextStyle}\" />", normalizedDashboard, StringComparison.Ordinal);
        Assert.Contains("<DataGridTextColumn Header=\"最近有效期\" HeaderStyle=\"{StaticResource TableCenterColumnHeaderStyle}\" Binding=\"{Binding NearestExpiryDate, Converter={StaticResource DateOnlyDisplayConverter}}\" Width=\"124\" ElementStyle=\"{StaticResource TableNumericCenterTextStyle}\" />", normalizedDashboard, StringComparison.Ordinal);
        Assert.Contains("<DataGridTemplateColumn Header=\"操作\" HeaderStyle=\"{StaticResource TableCenterColumnHeaderStyle}\" Width=\"96\">", normalizedDashboard, StringComparison.Ordinal);
        Assert.Equal(2, Count(normalizedPending, "<DataGridTemplateColumn Header=\"操作\" HeaderStyle=\"{StaticResource TableCenterColumnHeaderStyle}\" Width=\"94\">"));
        Assert.Contains("Header=\"批次数\" Binding=\"{Binding PendingBatchCount}\" Width=\"76\" ElementStyle=\"{StaticResource TableNumericTextStyle}\"", normalizedPending, StringComparison.Ordinal);
        Assert.Contains("Header=\"批次\" Binding=\"{Binding PendingBatchCount}\" Width=\"70\" ElementStyle=\"{StaticResource TableNumericTextStyle}\"", normalizedPending, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.OpenDetailCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", pending, StringComparison.Ordinal);
        var actionOffset = 0;
        var actionButtonCount = 0;
        const string actionMarker = "Content=\"排查  →\"";
        while ((actionOffset = window.IndexOf(actionMarker, actionOffset, StringComparison.Ordinal)) >= 0)
        {
            var actionEnd = window.IndexOf("/>", actionOffset, StringComparison.Ordinal);
            Assert.True(actionEnd > actionOffset);
            Assert.Contains("HorizontalAlignment=\"Center\"", window[actionOffset..actionEnd], StringComparison.Ordinal);
            actionButtonCount++;
            actionOffset = actionEnd + 2;
        }

        Assert.Equal(3, actionButtonCount);
        Assert.Contains("Style=\"{StaticResource ReadOnlyIdentityTextBoxStyle}\"", detail, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Detail.ProductBarcode, Mode=OneWay}\"", detail, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Detail.ProductCode, Mode=OneWay}\"", detail, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", detail, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Detail.SubmitButtonText}\"", detail, StringComparison.Ordinal);
        Assert.True(Count(detail, "Height=\"56\"") >= 2);
        Assert.Equal(4, Count(detail, "<ColumnDefinition Width=\"112\" />"));
        Assert.Equal(2, Count(detail, "<ColumnDefinition Width=\"100\" />"));
        Assert.Equal(2, Count(detail, "<ColumnDefinition Width=\"150\" />"));
        Assert.Equal(2, Count(detail, "<ColumnDefinition Width=\"148\" />"));
        Assert.Equal(2, Count(detail, "<ColumnDefinition Width=\"200\" />"));
        Assert.DoesNotContain("<ColumnDefinition Width=\"300\" />", detail, StringComparison.Ordinal);
        foreach (var token in new[]
        {
            "InspectionStatusTagStyle", "InspectionStatusTagTextStyle", "WarningSurfaceBrush", "WarningTextBrush",
            "SuccessSurfaceBrush", "SuccessBrush", "ErrorSurfaceBrush", "DangerBrush",
            "ReconfirmSurfaceBrush", "ReconfirmBrush", "HasCheckedQty", "HasInputError",
            "RequiresReconfirmation", "CheckedQtyStateText", "ReconfirmCommand"
        })
        {
            Assert.Contains(token, window, StringComparison.Ordinal);
        }

        Assert.Contains("Text\" Value=\"需要重新确认\"", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"重新确认\"", detail, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ReconfirmCommand}\"", detail, StringComparison.Ordinal);
        var statusStyleStart = window.IndexOf("<Style x:Key=\"InspectionStatusTagStyle\"", StringComparison.Ordinal);
        var statusStyleEnd = window.IndexOf("</Style>", statusStyleStart, StringComparison.Ordinal);
        Assert.True(statusStyleStart >= 0 && statusStyleEnd > statusStyleStart);
        var statusStyle = window[statusStyleStart..statusStyleEnd];
        Assert.Contains("<Setter Property=\"HorizontalAlignment\" Value=\"Center\" />", statusStyle, StringComparison.Ordinal);
        var normalizedDetail = NormalizeWhitespace(detail);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">", normalizedDetail, StringComparison.Ordinal);

        Assert.Contains("新的整体视觉方向认可，可以按该设计系统进入生产 UI 重构。", taskCard, StringComparison.Ordinal);
        Assert.Contains("Dashboard / 首页", taskCard, StringComparison.Ordinal);
        Assert.Contains("待排查任务列表", taskCard, StringComparison.Ordinal);
        Assert.Contains("排查详情", taskCard, StringComparison.Ordinal);
        Assert.Contains("不得进入 Stage 8", taskCard, StringComparison.Ordinal);
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

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
