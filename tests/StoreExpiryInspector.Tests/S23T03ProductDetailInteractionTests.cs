using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Markup;
using System.Xml.Linq;
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
        Assert.Contains("CurrentProductCatalogBatches", detail, StringComparison.Ordinal);
        Assert.Contains("HistoricalProductCatalogBatches", detail, StringComparison.Ordinal);
        Assert.Contains("查看历史批次", detail, StringComparison.Ordinal);
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
    public void Local_product_detail_template_routes_all_regions_to_the_outer_scrollviewer_once()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new StoreExpiryInspector.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var shell = CreateShell(); SetSelectedDetail(shell.ProductCatalog); shell.NavigateTo(ShellPage.ProductCatalogDetail);
                var window = LocalDetailWindow(shell); window.Show(); window.UpdateLayout();
                ProbeNativeCopy(window, shell);
                var outer = Descendants<ScrollViewer>(window).Single(viewer => Descendants<DataGrid>(viewer).Any());
                var grid = Descendants<DataGrid>(outer).Single(value => AutomationProperties.GetName(value) == "当前商品批次明细");
                var inner = Descendants<ScrollViewer>(grid).First();
                var top = Descendants<TextBlock>(outer).First(x => x.IsVisible && x.Text == "商品编码");
                var body = Descendants<TextBlock>(outer).First(x => x.IsVisible && x.Text.StartsWith("该商品存在需关注批次", StringComparison.Ordinal));

                // Baseline has neither the production local handler nor the hit-test background.
                // Record the real local-template routing before proving the local fix.
                outer.Background = null;
                Console.WriteLine(Probe(window, outer, inner, "baseline-top", -120, () => CenterInWindow(window, top), hit => IsInVisualBranch(hit, outer)));
                Console.WriteLine(Probe(window, outer, inner, "baseline-body", -120, () => CenterInWindow(window, body), hit => IsInVisualBranch(hit, outer)));
                outer.ScrollToVerticalOffset(outer.ScrollableHeight); window.UpdateLayout();
                Console.WriteLine(Probe(window, outer, inner, "baseline-grid", 120, () => CenterInWindow(window, Descendants<DataGridRow>(grid).Last()), hit => Ancestors<DataGrid>(hit).Any(value => ReferenceEquals(value, grid))));
                Assert.NotNull(FindBlankPointOrNull(window, outer));
                Console.WriteLine(Probe(window, outer, inner, "baseline-blank", -120, () => FindBlankPoint(window, outer), hit => IsInVisualBranch(hit, outer)));
                outer.Background = Brushes.Transparent;

                var target = RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                var method = typeof(MainWindow).GetMethod("ProductCatalogDetailScrollViewer_PreviewMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var handler = (MouseWheelEventHandler)Delegate.CreateDelegate(typeof(MouseWheelEventHandler), target, method);
                outer.AddHandler(UIElement.PreviewMouseWheelEvent, handler);

                outer.ScrollToVerticalOffset(0); window.UpdateLayout();
                AssertScrolls(window, outer, inner, CenterInWindow(window, top), -120, down: true);
                outer.ScrollToVerticalOffset(0); window.UpdateLayout();
                AssertScrolls(window, outer, inner, CenterInWindow(window, body), -120, down: true);
                outer.ScrollToVerticalOffset(outer.ScrollableHeight); window.UpdateLayout();
                AssertScrolls(window, outer, inner, CenterInWindow(window, Descendants<DataGridRow>(grid).Last()), 120, down: false);
                outer.ScrollToVerticalOffset(0); window.UpdateLayout();
                AssertScrolls(window, outer, inner, FindBlankPointOrNull(window, outer)!.Value, -120, down: true);

                outer.ScrollToVerticalOffset(0); window.UpdateLayout(); var before = outer.VerticalOffset;
                var remainder = typeof(MainWindow).GetField("_productCatalogDetailWheelRemainder", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Console.WriteLine($"sub-before={new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -60).Delta}; remainder={remainder.GetValue(target)}");
                Raise(window, CenterInWindow(window, top), -60); Assert.Equal(before, outer.VerticalOffset); window.UpdateLayout(); Console.WriteLine($"sub-after-first remainder={remainder.GetValue(target)}");
                var expectedSub = NativeWheelOffset(outer, -120);
                Raise(window, CenterInWindow(window, top), -60); window.UpdateLayout(); Console.WriteLine($"sub-after-second remainder={remainder.GetValue(target)} outer={outer.VerticalOffset}"); Assert.Equal(expectedSub, outer.VerticalOffset, 4);
                outer.ScrollToVerticalOffset(0); window.UpdateLayout();
                var topBoundary = FindBlankPoint(window, outer);
                Raise(window, topBoundary, 120); window.UpdateLayout(); Assert.Equal(0, outer.VerticalOffset, 4);
                outer.ScrollToVerticalOffset(outer.ScrollableHeight); window.UpdateLayout();
                var bottom = outer.VerticalOffset;
                Raise(window, CenterInWindow(window, Descendants<DataGridRow>(grid).Last()), -120); window.UpdateLayout(); Assert.Equal(bottom, outer.VerticalOffset, 4);

                shell.ProductCatalog.ToggleHistoryCommand.Execute(null); window.UpdateLayout();
                var historyGrid = Descendants<DataGrid>(outer).Single(value => AutomationProperties.GetName(value) == "历史商品批次明细");
                var historyInner = Descendants<ScrollViewer>(historyGrid).First();
                outer.ScrollToVerticalOffset(outer.ScrollableHeight); window.UpdateLayout();
                AssertScrolls(window, outer, historyInner, CenterInWindow(window, Descendants<DataGridRow>(historyGrid).Last()), 120, down: false);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure);
    }

    [Fact]
    public async Task Catalog_detail_navigation_uses_the_row_parameter_without_row_selection()
    {
        var shell = CreateShell(); shell.NavigateTo(ShellPage.ProductCatalog);
        var row = new ProductCatalogItem(1, "测试商品", "15060502310890", "6976879890025", "食品", 80, 99, null, "discount_50", 1, null, 7);
        shell.OpenProductCatalogDetailCommand.Execute(row);
        for (var count = 0; count < 200 && shell.ProductCatalog.IsLoading; count++) await Task.Delay(5);
        Assert.Equal(ShellPage.ProductCatalogDetail, shell.CurrentPage);
        Assert.Equal(row.ProductId, shell.ProductCatalog.Selected!.Product.ProductId);
        shell.ReturnFromProductCatalogCommand.Execute(null);
        Assert.Equal(ShellPage.ProductCatalog, shell.CurrentPage);
    }
    private static ShellViewModel CreateShell() => new(
        dashboardLoader: () => new(0, 0, 0, 0, 0, [], ProductCount: 0, BatchCount: 0),
        taskLoader: _ => new([], 0, 1, 50),
        detailLoader: taskId => new(taskId, "open", 1, null, null, null),
        productCatalogLoader: _ => new([], 0, 1, 50),
        productCatalogDetailLoader: _ => new(
            new ProductCatalogItem(1, "测试商品", "15060502310890", "6976879890025", "食品", 80, 99, new DateOnly(2026, 10, 1), "discount_50", 1, DateTime.UtcNow, 7),
            TestBatches(),
            100,
            null));

    private static void SetSelectedDetail(ProductCatalogViewModel catalog)
    {
        var detail = new ProductCatalogDetail(
            new ProductCatalogItem(1, "测试商品", "15060502310890", "6976879890025", "食品", 80, 99, new DateOnly(2026, 10, 1), "discount_50", 1, DateTime.UtcNow, 7),
            TestBatches(),
            100,
            null);
        typeof(ProductCatalogViewModel).GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(catalog, detail);
    }

    private static IReadOnlyList<ProductCatalogBatch> TestBatches() => Enumerable.Range(1, 80).Select(index => new ProductCatalogBatch(index, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1).AddDays(index), index, "discount_50", index == 1, index > 40, index > 40 ? "无待处理" : null)).ToArray();

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

    private static Point? FindBlankPointOrNull(Window window, ScrollViewer outer)
    {
        try { return FindBlankPoint(window, outer); }
        catch (InvalidOperationException) { return null; }
    }

    private static Window LocalDetailWindow(ShellViewModel shell)
    {
        var source = XDocument.Load(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var detail = source.Descendants(ui + "Grid").Single(node => ((string?)node.Attribute("Visibility"))?.Contains("IsProductCatalogDetailVisible", StringComparison.Ordinal) == true);
        detail.Descendants(ui + "ScrollViewer").First().Attribute("PreviewMouseWheel")?.Remove();
        var host = new MainWindow(shell);
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(host.Resources);
        var grid = (Grid)XamlReader.Parse(detail.ToString());
        return new Window { Width = 1024, Height = 680, Content = grid, DataContext = shell };
    }

    private static void ProbeNativeCopy(Window detailWindow, ShellViewModel shell)
    {
        var originalClipboard = CaptureClipboard();
        try
        {
            var code = Descendants<TextBox>(detailWindow).Single(value => AutomationProperties.GetName(value) == "商品编码");
            var barcode = Descendants<TextBox>(detailWindow).Single(value => AutomationProperties.GetName(value) == "商品条码");
            Assert.True(code.IsReadOnly); Assert.True(barcode.IsReadOnly);
            var category = Descendants<TextBlock>(detailWindow).Single(value => value.Text == "大类");
            Assert.Equal(Ancestors<StackPanel>(category).First().ActualHeight, Ancestors<StackPanel>(code).First().ActualHeight, 4);
            Assert.Equal("15060502310890", code.Text); Assert.Equal("6976879890025", barcode.Text);
            code.SelectAll(); ApplicationCommands.Copy.Execute(null, code); Assert.Equal("15060502310890", ReadClipboard(Clipboard.GetText));
            barcode.SelectAll(); ApplicationCommands.Copy.Execute(null, barcode); Assert.Equal("6976879890025", ReadClipboard(Clipboard.GetText));
            foreach (var (value, expected) in new[] { (code, "15060502310890"), (barcode, "6976879890025") })
            {
                value.SelectAll(); ApplicationCommands.Cut.Execute(null, value); ApplicationCommands.Paste.Execute(null, value); EditingCommands.Delete.Execute(null, value); Assert.Equal(expected, value.Text);
            }

            shell.ProductCatalog.Items.Add(new ProductCatalogItem(2, "复制商品", "15060502310890", "6976879890025", "食品", 3, 80, new DateOnly(2026, 10, 1), "discount_50", 1, DateTime.UtcNow, 7));
            var catalogWindow = LocalCatalogWindow(shell); catalogWindow.Show(); catalogWindow.UpdateLayout();
            var grid = Descendants<DataGrid>(catalogWindow).Single(value => AutomationProperties.GetName(value) == "商品明细列表");
            AttachProductCatalogCopyBinding(grid);
            Assert.Equal(DataGridSelectionUnit.Cell, grid.SelectionUnit); Assert.Equal(DataGridSelectionMode.Single, grid.SelectionMode); Assert.Equal(DataGridClipboardCopyMode.ExcludeHeader, grid.ClipboardCopyMode);
            var row = grid.Items[0];
            foreach (var (column, expected) in new[] { (0, "复制商品"), (1, "6976879890025"), (2, "15060502310890"), (3, "食品"), (4, "3"), (5, "80"), (6, "2026-10-01"), (7, "5折"), (8, "1") })
            {
                grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[column]); grid.SelectedCells.Clear(); grid.SelectedCells.Add(grid.CurrentCell); grid.Focus();
                Assert.Single(grid.SelectedCells);
                ApplicationCommands.Copy.Execute(null, grid);
                var copied = ReadClipboard(Clipboard.GetText); Console.WriteLine($"copy-column={column};raw={Escape(copied)}");
                Assert.Equal(expected, copied); Assert.DoesNotContain('\t', copied); Assert.DoesNotContain('\r', copied); Assert.DoesNotContain('\n', copied);
            }
            Assert.Null(grid.SelectedItem);
            var entry = Descendants<Button>(catalogWindow).Single(value => AutomationProperties.GetName(value) == "查看商品详情");
            Assert.Same(row, entry.CommandParameter);
            Assert.True(entry.Command.CanExecute(entry.CommandParameter));
            catalogWindow.Close();
        }
        finally
        {
            RestoreClipboard(originalClipboard);
        }
    }

    private static Window LocalCatalogWindow(ShellViewModel shell)
    {
        var source = XDocument.Load(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var catalog = source.Descendants(ui + "Grid").Single(node => ((string?)node.Attribute("Visibility"))?.Contains("IsProductCatalogVisible", StringComparison.Ordinal) == true);
        catalog.Attribute("Visibility")?.Remove();
        catalog.Descendants(ui + "DataGrid.CommandBindings").Remove();
        var host = new MainWindow(shell);
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(host.Resources);
        var grid = (Grid)XamlReader.Parse(catalog.ToString());
        return new Window { Width = 1024, Height = 680, Content = grid, DataContext = shell };
    }

    private static void AttachProductCatalogCopyBinding(DataGrid catalogGrid)
    {
        var target = RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var type = typeof(MainWindow);
        var execute = (ExecutedRoutedEventHandler)Delegate.CreateDelegate(typeof(ExecutedRoutedEventHandler), target, type.GetMethod("ProductCatalogCopy_Executed", BindingFlags.Instance | BindingFlags.NonPublic)!);
        var canExecute = (CanExecuteRoutedEventHandler)Delegate.CreateDelegate(typeof(CanExecuteRoutedEventHandler), target, type.GetMethod("ProductCatalogCopy_CanExecute", BindingFlags.Instance | BindingFlags.NonPublic)!);
        catalogGrid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, execute, canExecute));
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

    private static void RestoreClipboard(IDataObject? original)
    {
        for (var attempt = 0; attempt < 20; attempt++)
            try { if (original is null) Clipboard.Clear(); else Clipboard.SetDataObject(original, true); return; }
            catch (COMException) { Thread.Sleep(50); }
    }

    private static IDataObject? CaptureClipboard()
    {
        try { return ReadClipboard(Clipboard.GetDataObject); }
        catch (COMException) { return null; }
    }

    private static T ReadClipboard<T>(Func<T> read)
    {
        for (var attempt = 0; ; attempt++)
            try { return read(); }
            catch (COMException) when (attempt < 19) { Thread.Sleep(50); }
    }

    private static void AssertScrolls(Window window, ScrollViewer outer, ScrollViewer inner, Point point, int delta, bool down)
    {
        var before = outer.VerticalOffset; var innerBefore = inner.VerticalOffset;
        var expected = NativeWheelOffset(outer, delta);
        Raise(window, point, delta); window.UpdateLayout();
        Assert.Equal(innerBefore, inner.VerticalOffset);
        Assert.Equal(expected, outer.VerticalOffset, 4);
        Assert.True(down ? outer.VerticalOffset > before : outer.VerticalOffset < before);
    }

    private static double NativeWheelOffset(ScrollViewer outer, int delta)
    {
        var before = outer.VerticalOffset;
        var lines = SystemParameters.WheelScrollLines;
        var notches = Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine;
        for (var notch = 0; notch < notches; notch++)
        {
            if (lines < 0)
            {
                if (delta > 0) outer.PageUp(); else outer.PageDown();
                continue;
            }
            for (var line = 0; line < lines; line++)
                if (delta > 0) outer.LineUp(); else outer.LineDown();
        }
        outer.UpdateLayout();
        var expected = outer.VerticalOffset;
        outer.ScrollToVerticalOffset(before);
        outer.UpdateLayout();
        return expected;
    }

    private static void Raise(Window window, Point point, int delta)
    {
        var hit = Assert.IsAssignableFrom<UIElement>(window.InputHitTest(point));
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        Console.WriteLine($"raise requested={delta} actual={args.Delta} hit={hit.GetType().Name}");
        hit.RaiseEvent(args);
        if (!args.Handled) { args.RoutedEvent = UIElement.MouseWheelEvent; hit.RaiseEvent(args); }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"))) directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private static Point CenterInWindow(Window window, UIElement element) =>
        element.TranslatePoint(new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2), window);

    private static string Probe(Window window, ScrollViewer outer, ScrollViewer inner, string region, int delta, Func<Point> pointFactory, Func<UIElement, bool> isExpectedHit)
    {
        if (region.Contains("grid", StringComparison.Ordinal)) outer.ScrollToVerticalOffset(outer.ScrollableHeight);
        else outer.ScrollToVerticalOffset(0);
        window.UpdateLayout();
        var point = pointFactory();
        var hit = Assert.IsAssignableFrom<UIElement>(window.InputHitTest(point));
        Assert.True(isExpectedHit(hit), $"{region} hit {TypeName(hit)} is outside its expected visual branch.");
        var preview = new List<string>();
        var bubble = new List<string>();
        MouseWheelEventHandler previewHandler = (_, args) => preview.Add($"{TypeName(args.OriginalSource)}|{TypeName(args.Source)}|{args.Handled}");
        MouseWheelEventHandler bubbleHandler = (_, args) => bubble.Add($"{TypeName(args.OriginalSource)}|{TypeName(args.Source)}|{args.Handled}");
        var outerBubble = new List<bool>();
        var innerBubble = new List<bool>();
        MouseWheelEventHandler outerHandler = (_, args) => outerBubble.Add(args.Handled);
        MouseWheelEventHandler innerHandler = (_, args) => innerBubble.Add(args.Handled);
        window.AddHandler(UIElement.PreviewMouseWheelEvent, previewHandler, true);
        window.AddHandler(UIElement.MouseWheelEvent, bubbleHandler, true);
        outer.AddHandler(UIElement.MouseWheelEvent, outerHandler, true);
        inner.AddHandler(UIElement.MouseWheelEvent, innerHandler, true);
        var before = outer.VerticalOffset;
        var innerBefore = inner.VerticalOffset;
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        hit.RaiseEvent(args);
        if (!args.Handled) { args.RoutedEvent = UIElement.MouseWheelEvent; hit.RaiseEvent(args); }
        window.UpdateLayout(); // Observe the queued native scroll before recording baseline offsets.
        window.RemoveHandler(UIElement.PreviewMouseWheelEvent, previewHandler);
        window.RemoveHandler(UIElement.MouseWheelEvent, bubbleHandler);
        outer.RemoveHandler(UIElement.MouseWheelEvent, outerHandler);
        inner.RemoveHandler(UIElement.MouseWheelEvent, innerHandler);
        return $"{region}:hit={TypeName(hit)};preview={string.Join(',', preview)};innerBubble={string.Join(',', innerBubble)};outerBubble={string.Join(',', outerBubble)};bubble={string.Join(',', bubble)};outer={before}->{outer.VerticalOffset};inner={innerBefore}->{inner.VerticalOffset};lines={SystemParameters.WheelScrollLines}";
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
