using System.IO;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S23T01InspectionPlanTests
{
    private static readonly DateOnly Today = new(2026, 9, 16);
    private static readonly DateTime Utc = new(2026, 9, 16, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThreeDayCardsUseSelectionNotCheckmarksHoverOrFocus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cardStyle = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "InspectionPlanCardStyle");
        var selected = cardStyle.Descendants().Single(element => element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "Tag");
        Assert.Equal("True", (string?)selected.Attribute("Value"));
        Assert.Contains(selected.Elements(), element => (string?)element.Attribute("Property") == "BorderBrush" && (string?)element.Attribute("Value") == "{DynamicResource PrimaryActionBrush}");
        Assert.Contains(selected.Elements(), element => (string?)element.Attribute("Property") == "Background" && (string?)element.Attribute("Value") == "{DynamicResource SelectedSurfaceBrush}");
        Assert.DoesNotContain(cardStyle.Descendants(), element => element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "IsMouseOver");
        var focus = cardStyle.Descendants().Single(element => element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "IsKeyboardFocused");
        Assert.All(focus.Elements(), element => Assert.Equal("KeyboardFocus", (string?)element.Attribute("TargetName")));
        var emphasis = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "InspectionPlanCardEmphasisTextStyle");
        Assert.Contains(emphasis.Elements(), element => (string?)element.Attribute("Property") == "FontWeight" && (string?)element.Attribute("Value") == "Normal");
        var emphasisTrigger = emphasis.Descendants().Single(element => element.Name.LocalName == "DataTrigger");
        Assert.Equal("{Binding Tag, RelativeSource={RelativeSource AncestorType=Button}}", (string?)emphasisTrigger.Attribute("Binding"));
        Assert.Equal("Bold", (string?)emphasisTrigger.Elements().Single().Attribute("Value"));
        var cards = document.Descendants().Where(element => element.Name.LocalName == "Button" && (string?)element.Attribute("Style") == "{StaticResource InspectionPlanCardStyle}").ToArray();
        Assert.Equal(3, cards.Length);
        var dateColors = new[] { "PrimaryActionBrush", "SuccessBrush", "WarningTextBrush" };
        for (var day = 0; day < cards.Length; day++)
        {
            Assert.Equal("{Binding TodayInspection.SelectDayCommand}", (string?)cards[day].Attribute("Command"));
            Assert.Equal(day.ToString(), (string?)cards[day].Attribute("CommandParameter"));
            Assert.DoesNotContain(cards[day].Descendants(), element => (string?)element.Attribute("Text") == "✓");
            Assert.Equal(2, cards[day].Descendants().Count(element => element.Name.LocalName == "ColumnDefinition"));
            var texts = cards[day].Descendants().Where(element => (string?)element.Attribute("Style") == "{StaticResource InspectionPlanCardEmphasisTextStyle}").ToArray();
            Assert.Equal(2, texts.Length);
            Assert.All(texts, element => Assert.Null(element.Attribute("FontWeight")));
            Assert.Equal("{DynamicResource " + dateColors[day] + "}", (string?)texts[1].Attribute("Foreground"));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ProjectionIsReadOnlyAndEqualsRealStartupAtTargetDate(int offset)
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var pending = AddBatch(seed, "pending", Today.AddDays(31), Today.AddDays(1));
            new ProductTaskAggregator().Aggregate(seed, new(pending.ProductId, [new(pending.Id, "discount_50", 1, false)], Utc));
            AddBatch(seed, "new-tomorrow", Today.AddDays(31), Today.AddDays(1));
            AddBatch(seed, "new-after", Today.AddDays(32), Today.AddDays(2));
            var handled = AddBatch(seed, "handled-old-stage", Today.AddDays(15), Today.AddDays(1));
            handled.CurrentStage = "discount_50"; handled.HandledAttentionVersion = 1;
            var noTrigger = AddBatch(seed, "already-handled-no-trigger", Today, null);
            noTrigger.CurrentStage = "expired"; noTrigger.HandledAttentionVersion = 1;
            var stopped = AddBatch(seed, "stopped-zero", Today.AddDays(31), Today.AddDays(1));
            stopped.TrackingStatus = "stopped"; stopped.Product.EffectiveStockQty = 0; stopped.Product.IsStockZeroTerminated = true;
            var unbaselined = AddBatch(seed, "unbaselined", Today.AddDays(90), Today.AddDays(1));
            unbaselined.Product.CategoryCode = "pet"; unbaselined.Product.PolicyCode = ExpiryPolicies.Pet;
            var unmanaged = AddBatch(seed, "unmanaged", Today.AddDays(31), Today.AddDays(1));
            unmanaged.Product.ExpiryManagementStatus = ExpiryManagementStatus.Excluded;
            unmanaged.Product.PolicyCode = null; unmanaged.Product.PolicyVersion = null;
            seed.SaveChanges();
        }
        using var context = database.Open();
        var originalTaskCount = context.Tasks.Count(); var originalItems = context.TaskItems.Count();
        var originalInspections = context.Inspections.Count();
        var before = context.Batches.AsNoTracking().Select(batch => new { batch.Id, batch.CurrentStage, batch.NextTriggerDate, batch.AttentionVersion, batch.HandledAttentionVersion }).ToArray();
        var projection = new InspectionPlanQuery().Search(context, Today.AddDays(offset), new(PageSize: int.MaxValue));
        Assert.Equal(originalTaskCount, context.Tasks.Count()); Assert.Equal(originalItems, context.TaskItems.Count()); Assert.Equal(originalInspections, context.Inspections.Count());
        Assert.Equal(before, context.Batches.AsNoTracking().Select(batch => new { batch.Id, batch.CurrentStage, batch.NextTriggerDate, batch.AttentionVersion, batch.HandledAttentionVersion }).ToArray());
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.All(projection.Items, item => { Assert.True(item.TaskId < 0); Assert.Equal(Today.AddDays(offset), item.PlannedInspectionDate); });
        Assert.DoesNotContain(projection.Items, item => item.ProductCode is "already-handled-no-trigger" or "stopped-zero" or "unbaselined" or "unmanaged");
        new StartupRecalculationUseCase().Execute(context, new(Today.AddDays(offset), Utc.AddDays(offset)));
        var actual = new InspectionTaskQuery().SearchOpenTasks(context, new(PageSize: int.MaxValue));
        Assert.Equal(actual.TotalCount, projection.TotalCount);
        Assert.Equal(actual.Items.OrderBy(item => item.ProductId).Select(Facts), projection.Items.OrderBy(item => item.ProductId).Select(Facts));
    }

    [Fact]
    public async Task ThreeCountsDateSwitchCategoryPagingAndDashboardShareTheSource()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            for (var i = 0; i < 51; i++) AddBatch(seed, $"food-{i:D3}", Today.AddDays(31), Today.AddDays(1));
            AddBatch(seed, "food-after", Today.AddDays(32), Today.AddDays(2));
            var pet = AddBatch(seed, "pet-tomorrow", Today.AddDays(91), Today.AddDays(1));
            pet.Product.CategoryCode = "pet"; pet.Product.PolicyCode = ExpiryPolicies.Pet;
            AddBaseline(seed, "pet", ExpiryPolicies.Pet); seed.SaveChanges();
        }
        var vm = Create(database);
        await vm.LoadAsync();
        Assert.True(vm.IsTodaySelected); Assert.Equal(0, vm.TodayPlanCount); Assert.Equal(52, vm.TomorrowPlanCount); Assert.Equal(53, vm.DayAfterTomorrowPlanCount);
        using (var context = database.Open())
        {
            var dashboard = new DashboardViewModel(() => new InspectionTaskQuery().Dashboard(context, Today));
            await dashboard.LoadAsync();
            Assert.Equal(vm.TomorrowPlanCount, dashboard.TomorrowPlanCount);
            Assert.Equal("明日需排查 52 项", dashboard.TomorrowPlanText);
        }
        await vm.SelectDayAsync(1);
        Assert.True(vm.IsTomorrowSelected); Assert.Equal(50, vm.Tasks.Count); Assert.Equal(52, vm.TotalCount); Assert.Equal(2, vm.TotalPages);
        Assert.All(vm.Tasks, row => Assert.Equal(Today.AddDays(1), row.PlannedInspectionDate));
        vm.SelectAllCommand.Execute(null); await WaitUntil(() => vm.SelectedCount == 52 && vm.CanUseContent);
        vm.NextPageCommand.Execute(null); await WaitUntil(() => vm.CurrentPage == 2 && vm.Tasks.Count == 2 && vm.CanUseContent);
        Assert.All(vm.Tasks, row => Assert.True(row.IsSelected));
        vm.ClearSelectionCommand.Execute(null); await WaitUntil(() => vm.SelectedCount == 0 && vm.CanUseContent);
        vm.SelectedCategory = "宠物"; await WaitUntil(() => vm.TotalCount == 1 && vm.CanUseContent);
        Assert.Equal("pet-tomorrow", Assert.Single(vm.Tasks).ProductCode);
        vm.Tasks[0].IsSelected = true;
        await vm.SelectDayAsync(2);
        Assert.True(vm.IsDayAfterTomorrowSelected); Assert.Equal(0, vm.SelectedCount); Assert.Equal(1, vm.CurrentPage);
        await vm.SelectDayAsync(0);
        Assert.True(vm.PreviewCommand.CanExecute(null)); Assert.Empty(vm.Tasks);
    }

    [Fact]
    public async Task FutureDatesBlockDirectPreviewDraftAndSubmitAndClearTodayPreview()
    {
        using var database = SqliteTestDatabase.Create();
        var previewCalls = 0; var applyCalls = 0; var submitCalls = 0;
        var vm = Create(database, preview: _ => { previewCalls++; return EmptyPreview(); },
            apply: _ => { applyCalls++; throw new InvalidOperationException(); }, submit: _ => { submitCalls++; throw new InvalidOperationException(); });
        await vm.LoadAsync(); await vm.PreviewAsync("C:/filled.xlsx"); Assert.True(vm.HasPreview);
        foreach (var offset in new[] { 1, 2 })
        {
            await vm.SelectDayAsync(offset);
            Assert.False(vm.HasPreview); Assert.False(vm.CanSaveDraft); Assert.Empty(vm.CompleteTaskIds);
            Assert.False(vm.PreviewCommand.CanExecute(null)); Assert.False(vm.SubmitCommand.CanExecute(null));
            vm.PreviewCommand.Execute(null); vm.SubmitCommand.Execute(null); vm.SaveDraftCommand.Execute(null);
            await vm.PreviewAsync("C:/filled.xlsx"); await vm.SaveDraftAsync(); await vm.SubmitAsync();
            Assert.Equal("到排查日后可导入排查结果", vm.StatusText);
        }
        Assert.Equal(1, previewCalls); Assert.Equal(0, applyCalls); Assert.Equal(0, submitCalls);
        using var verify = database.Open(); Assert.Empty(verify.Inspections); Assert.Empty(verify.Tasks);
    }

    [Fact]
    public async Task BusyPreviewAndExportCannotSwitchDate()
    {
        using var database = SqliteTestDatabase.Create();
        using var gate = new ManualResetEventSlim();
        var vm = Create(database, preview: _ => { gate.Wait(TimeSpan.FromSeconds(10)); return EmptyPreview(); });
        var reading = vm.PreviewAsync("C:/filled.xlsx");
        Assert.False(vm.SelectDayCommand.CanExecute(null)); vm.SelectDayCommand.Execute("1"); await vm.SelectDayAsync(2);
        Assert.Equal(0, vm.SelectedDayOffset); gate.Set(); await reading; Assert.True(vm.HasPreview);
        using (var seed = database.Open()) AddBatch(seed, "future", Today.AddDays(31), Today.AddDays(1));
        gate.Reset();
        var exportingVm = Create(database, exportFuture: (path, date, ids) => { gate.Wait(TimeSpan.FromSeconds(10)); return new(path, ids.Count, ids.Count); });
        await exportingVm.LoadAsync(); await exportingVm.SelectDayAsync(1); exportingVm.Tasks[0].IsSelected = true;
        var exporting = exportingVm.ExportAsync("C:/plan.xlsx");
        Assert.False(exportingVm.SelectDayCommand.CanExecute(null)); await exportingVm.SelectDayAsync(0);
        Assert.Equal(1, exportingVm.SelectedDayOffset); gate.Set(); await exporting;
    }

    [Fact]
    public async Task LoadCapturesBusinessDateOnceAcrossMidnight()
    {
        using var database = SqliteTestDatabase.Create();
        var calls = 0; var countsDay = default(DateOnly); var requested = new List<DateOnly?>();
        var vm = new TodayInspectionViewModel(() => new([], 0, 1, 50), (_, _) => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), _ => Task.CompletedTask,
            businessToday: () => ++calls <= 3 ? Today : Today.AddDays(1),
            searchTasks: request => { requested.Add(request.TargetDate); return new([], 0, request.Page, request.PageSize); },
            loadPlanCounts: day => { countsDay = day; return [0, 0, 0]; });
        await vm.SelectDayAsync(1);
        Assert.Equal(countsDay.AddDays(1), vm.TargetDate);
        Assert.All(requested.Where(date => date.HasValue), date => Assert.Equal(vm.TargetDate, date));
        Assert.Equal(vm.TargetDate, vm.TargetDate);
    }

    [Fact]
    public void FutureWorkbookContainsArrangementDateAndCannotBeReadAsFormalResultOrOverwrite()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var batch = AddBatch(context, "00001", Today.AddDays(31), Today.AddDays(1));
        var path = Path.Combine(database.Directory, "future.xlsx");
        var exporter = new FutureInspectionPlanExportUseCase();
        var result = exporter.Execute(context, path, Today.AddDays(1), [batch.ProductId], Today);
        Assert.Equal(1, result.TaskCount);
        using (var workbook = SpreadsheetDocument.Open(path, false))
        {
            var part = workbook.WorkbookPart!;
            Assert.Equal("未来排查工作安排", Assert.Single(part.Workbook!.Sheets!.Elements<Sheet>()).Name!.Value);
            var text = part.WorksheetParts.Single().Worksheet!.InnerText;
            Assert.Contains("不能导入正式排查结果", text); Assert.Contains("2026-09-17", text); Assert.Contains("00001", text);
        }
        Assert.Throws<InvalidDataException>(() => new InspectionPlanResultReader().Read(path));
        Assert.Throws<IOException>(() => exporter.Execute(context, path, Today.AddDays(1), [batch.ProductId], Today));
        Assert.Throws<ArgumentException>(() => exporter.Execute(context, Path.Combine(database.Directory, "today.xlsx"), Today, [batch.ProductId], Today));
        Assert.Empty(context.Tasks); Assert.Empty(context.Inspections);
    }

    [Fact]
    public async Task FutureRefreshDropsSelectionForPlansThatNoLongerExist()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open()) AddBatch(seed, "future", Today.AddDays(31), Today.AddDays(1));
        var vm = Create(database); await vm.LoadAsync(); await vm.SelectDayAsync(1);
        vm.Tasks[0].IsSelected = true; Assert.Equal(1, vm.SelectedCount);
        using (var change = database.Open()) { change.Batches.Single().TrackingStatus = "stopped"; change.SaveChanges(); }
        await vm.LoadAsync(); Assert.Equal(0, vm.TotalCount); Assert.Equal(0, vm.SelectedCount); Assert.False(vm.ExportCommand.CanExecute(null));
    }

    private static object Facts(InspectionTaskListItem item) => new { item.ProductId, item.HighestStage, item.PendingBatchCount, item.EffectiveStockQty, item.NearestExpiryDate, item.CategoryName };
    private static TodayInspectionViewModel Create(SqliteTestDatabase database, Func<string, InspectionPlanPreview>? preview = null,
        Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult>? apply = null, Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult>? submit = null,
        Func<string, DateOnly, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? exportFuture = null)
    {
        InspectionTaskSearchResult Search(InspectionTaskSearchRequest request)
        {
            using var context = database.Open();
            return request.TargetDate is DateOnly target ? new InspectionPlanQuery().Search(context, target, request) : new InspectionTaskQuery().SearchOpenTasks(context, request);
        }
        return new(
        () => Search(new()), (_, _) => throw new InvalidOperationException(), preview ?? (_ => throw new InvalidOperationException()),
        apply ?? (_ => throw new InvalidOperationException()), submit ?? (_ => throw new InvalidOperationException()), _ => Task.CompletedTask,
        businessToday: () => Today, searchTasks: Search,
        loadPlanCounts: today => [Search(new()).TotalCount, Search(new(TargetDate: today.AddDays(1))).TotalCount, Search(new(TargetDate: today.AddDays(2))).TotalCount],
        exportFuture: exportFuture ?? ((path, date, ids) => { using var context = database.Open(); return new FutureInspectionPlanExportUseCase().Execute(context, path, date, ids, Today); }));
    }
    private static InspectionPlanPreview EmptyPreview() => new(new([]), new(0, 0, 0, 0, 0, 0), [], [], new Dictionary<long, string>());

    private static Batch AddBatch(StoreDbContext context, string code, DateOnly expiryDate, DateOnly? nextDate)
    {
        var product = new Product { ProductCode = code, CurrentName = code, CurrentBarcode = "00001", EffectiveStockQty = 10, CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed };
        context.Products.Add(product); context.SaveChanges(); AddBaseline(context, "food", ExpiryPolicies.Food);
        var batch = new Batch { Product = product, ExpiryDate = expiryDate, ShelfLifeValue = 270, ShelfLifeUnit = "D", CurrentArrivalQty = 10, MaxArrivalQty = 10,
            TrackingStatus = "active", CurrentStage = "none", NextTriggerDate = nextDate, AttentionVersion = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
        context.Batches.Add(batch); context.SaveChanges(); return batch;
    }
    private static void AddBaseline(StoreDbContext context, string category, string policy)
    {
        if (context.ScopeBaselines.Any(baseline => baseline.ScopeKey == category)) return;
        var import = new ImportRecord { SourceFileName = "fixture.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = Utc, ConfirmedAtUtc = Utc, Status = "succeeded" };
        context.Imports.Add(import); context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = category, PolicyCode = policy, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = Today, IsCompleted = true, CompletedAtUtc = Utc }); context.SaveChanges();
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500; i++) { if (condition()) return; await Task.Delay(10); }
        Assert.True(condition());
    }
}
