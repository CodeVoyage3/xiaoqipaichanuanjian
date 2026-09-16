using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S23T03ProductDetailInteractionTests
{
    [Fact]
    public void Product_detail_keeps_only_the_batch_task_entry_and_local_outer_wheel_owner()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"))) root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root.FullName, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var detailStart = xaml.IndexOf("<Grid Visibility=\"{Binding IsProductCatalogDetailVisible", StringComparison.Ordinal);
        var detail = xaml[detailStart..xaml.IndexOf("<!-- 数据导入 -->", detailStart, StringComparison.Ordinal)];
        Assert.Contains("ProductCatalogDetailScrollViewer", detail, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel=\"ProductCatalogDetailScrollViewer_PreviewMouseWheel\"", detail, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"去排查商品\"", detail, StringComparison.Ordinal);
        Assert.Equal(1, detail.Split("OpenProductTaskCommand", StringSplitOptions.None).Length - 1);
        Assert.Contains("Content=\"去排查  →\"", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_task_entry_opens_the_existing_task_and_returns_to_product_detail()
    {
        var shell = CreateShell();
        SetSelectedDetail(shell.ProductCatalog);
        shell.NavigateTo(ShellPage.ProductCatalogDetail);

        shell.OpenProductTaskCommand.Execute(null);
        Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);

        shell.Detail.BackCommand.Execute(null);
        Assert.Equal(ShellPage.ProductCatalogDetail, shell.CurrentPage);

        shell.ReturnFromProductCatalogCommand.Execute(null);
        Assert.Equal(ShellPage.ProductCatalog, shell.CurrentPage);
    }

    [Fact]
    public void Baseline_product_detail_wheel_trace_uses_real_hit_test_sources()
    {
        Exception? failure = null;
        var trace = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new StoreExpiryInspector.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var shell = CreateShell();
                SetSelectedDetail(shell.ProductCatalog);
                shell.NavigateTo(ShellPage.ProductCatalogDetail);
                var window = new MainWindow(shell) { Width = 1024, Height = 680, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();

                var detailGrid = Descendants<DataGrid>(window).Single(grid => AutomationProperties.GetName(grid) == "商品批次明细");
                var outer = Descendants<ScrollViewer>(window)
                    .Where(viewer => Descendants<DataGrid>(viewer).Any(grid => ReferenceEquals(grid, detailGrid)))
                    .OrderByDescending(viewer => viewer.ScrollableHeight)
                    .First();
                var top = Descendants<TextBlock>(outer).First(block => block.IsVisible && block.ActualWidth > 0 && block.Text == "商品编码");
                var body = Descendants<TextBlock>(outer).First(block => block.IsVisible && block.ActualWidth > 0 && block.Text.StartsWith("该商品存在需关注批次", StringComparison.Ordinal));
                trace.Add(Probe(window, outer, "top", -120, () => CenterInWindow(window, top), hit => ReferenceEquals(hit, top) || Ancestors<ScrollViewer>(hit).Any(viewer => ReferenceEquals(viewer, outer))));
                trace.Add(Probe(window, outer, "body", -120, () => CenterInWindow(window, body), hit => ReferenceEquals(hit, body) || Ancestors<ScrollViewer>(hit).Any(viewer => ReferenceEquals(viewer, outer))));
                trace.Add(Probe(window, outer, "grid", 120, () =>
                {
                    outer.ScrollToVerticalOffset(outer.ScrollableHeight);
                    window.UpdateLayout();
                    return CenterInWindow(window, Descendants<DataGridRow>(detailGrid).Last());
                }, hit => ReferenceEquals(hit, detailGrid) || Ancestors<DataGrid>(hit).Any(grid => ReferenceEquals(grid, detailGrid))));
                trace.Add(Probe(window, outer, "blank", -120, () => FindBlankPoint(window, outer), hit => IsInVisualBranch(hit, outer)));

                Assert.All(trace, row => Assert.Contains("preview=", row, StringComparison.Ordinal));
                Console.WriteLine(string.Join(Environment.NewLine, trace));
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.True(failure is null, failure?.ToString() + Environment.NewLine + string.Join(Environment.NewLine, trace));
    }

    private static ShellViewModel CreateShell() => new(
        dashboardLoader: () => new(0, 0, 0, 0, 0, [], ProductCount: 0, BatchCount: 0),
        taskLoader: _ => new([], 0, 1, 50),
        detailLoader: taskId => new(taskId, "open", 1, null, null, null),
        productCatalogLoader: _ => new([], 0, 1, 50),
        productCatalogDetailLoader: _ => new(
            new ProductCatalogItem(1, "测试商品", "P-001", "6900000000001", "食品", 80, 99, new DateOnly(2026, 10, 1), "discount_50", 1, DateTime.UtcNow, 7),
            Enumerable.Range(1, 80).Select(index => new ProductCatalogBatch(index, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1).AddDays(index), index, "discount_50", index == 1)).ToArray(),
            100,
            null));

    private static void SetSelectedDetail(ProductCatalogViewModel catalog)
    {
        var detail = new ProductCatalogDetail(
            new ProductCatalogItem(1, "测试商品", "P-001", "6900000000001", "食品", 80, 99, new DateOnly(2026, 10, 1), "discount_50", 1, DateTime.UtcNow, 7),
            Enumerable.Range(1, 80).Select(index => new ProductCatalogBatch(index, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1).AddDays(index), index, "discount_50", index == 1)).ToArray(),
            100,
            null);
        typeof(ProductCatalogViewModel).GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(catalog, detail);
    }

    private static Point FindBlankPoint(Window window, ScrollViewer outer)
    {
        for (var y = 8d; y < outer.ActualHeight - 8; y += 8)
        for (var x = 8d; x < outer.ActualWidth - 8; x += 8)
        {
            var point = outer.TranslatePoint(new Point(x, y), window);
            if (window.InputHitTest(point) is UIElement hit && IsInVisualBranch(hit, outer) && hit is ScrollContentPresenter or ScrollViewer)
                return point;
        }
        throw new InvalidOperationException("No real blank-area hit target was found.");
    }

    private static Point CenterInWindow(Window window, UIElement element) =>
        element.TranslatePoint(new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2), window);

    private static string Probe(Window window, ScrollViewer outer, string region, int delta, Func<Point> pointFactory, Func<UIElement, bool> isExpectedHit)
    {
        if (region is not "grid") outer.ScrollToVerticalOffset(0);
        window.UpdateLayout();
        var point = pointFactory();
        var hit = Assert.IsAssignableFrom<UIElement>(window.InputHitTest(point));
        Assert.True(isExpectedHit(hit), $"{region} hit {TypeName(hit)} is outside its expected visual branch.");
        var preview = new List<string>();
        var bubble = new List<string>();
        MouseWheelEventHandler previewHandler = (_, args) => preview.Add($"{TypeName(args.OriginalSource)}|{TypeName(args.Source)}|{args.Handled}");
        MouseWheelEventHandler bubbleHandler = (_, args) => bubble.Add($"{TypeName(args.OriginalSource)}|{TypeName(args.Source)}|{args.Handled}");
        var outerBubble = new List<bool>();
        MouseWheelEventHandler outerHandler = (_, args) => outerBubble.Add(args.Handled);
        window.AddHandler(UIElement.PreviewMouseWheelEvent, previewHandler, true);
        window.AddHandler(UIElement.MouseWheelEvent, bubbleHandler, true);
        outer.AddHandler(UIElement.MouseWheelEvent, outerHandler, true);
        var before = outer.VerticalOffset;
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        hit.RaiseEvent(args);
        if (!args.Handled) { args.RoutedEvent = UIElement.MouseWheelEvent; hit.RaiseEvent(args); }
        window.RemoveHandler(UIElement.PreviewMouseWheelEvent, previewHandler);
        window.RemoveHandler(UIElement.MouseWheelEvent, bubbleHandler);
        outer.RemoveHandler(UIElement.MouseWheelEvent, outerHandler);
        return $"{region}:hit={TypeName(hit)};preview={string.Join(',', preview)};outerBubble={string.Join(',', outerBubble)};bubble={string.Join(',', bubble)};outer={before}->{outer.VerticalOffset};lines={SystemParameters.WheelScrollLines}";
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<T> Ancestors<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T match) yield return match;
    }

    private static string TypeName(object? value) => value?.GetType().Name ?? "null";

    private static bool IsInVisualBranch(DependencyObject child, DependencyObject ancestor) =>
        ReferenceEquals(child, ancestor) || Ancestors<DependencyObject>(child).Any(parent => ReferenceEquals(parent, ancestor));

}
