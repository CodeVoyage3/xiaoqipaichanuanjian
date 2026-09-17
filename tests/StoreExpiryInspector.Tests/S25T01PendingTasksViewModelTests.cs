using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S25T01PendingTasksViewModelTests
{
    [Fact]
    public async Task CrossPageSelectionExportsOnlyTheExplicitSelectedTaskIds()
    {
        IReadOnlyCollection<long>? exported = null;
        var vm = Create((_, ids) => { exported = ids; return new("C:\\selected.xlsx", ids.Count, ids.Count); });
        await vm.LoadAsync();
        vm.Items[0].IsSelected = true;
        await vm.ExportSelectedAsync("C:\\selected.xlsx");
        Assert.Equal([1], exported!.Order());
        vm.Items[1].IsSelected = true;
        await vm.ExportSelectedAsync("C:\\selected.xlsx");
        Assert.Equal([1, 2], exported!.Order());
        await vm.GoToNextPageAsync();
        vm.Items[0].IsSelected = true;
        await vm.ExportSelectedAsync("C:\\selected.xlsx");
        Assert.Equal([1, 2, 51], exported!.Order());
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.All(vm.Items, item => Assert.False(item.IsSelected));
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchFiltersAndRefreshClearSelectionWhilePageChangesRetainIt()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.Items[0].IsSelected = true;
        await vm.GoToNextPageAsync();
        Assert.Equal(1, vm.SelectedCount);

        vm.SearchText = "商品";
        Assert.Equal(0, vm.SelectedCount);
        vm.Items[0].IsSelected = true;
        vm.SelectedStage = "expired";
        Assert.Equal(0, vm.SelectedCount);
        vm.Items[0].IsSelected = true;
        vm.SelectedCategory = "食品";
        Assert.Equal(0, vm.SelectedCount);
        vm.Items[0].IsSelected = true;
        await vm.ClearFiltersAsync();
        Assert.Equal(0, vm.SelectedCount);
        vm.Items[0].IsSelected = true;
        vm.RetryCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task ZeroSelectionBlocksExportButKeepsImportAvailableAndBothTablesUseCheckboxes()
    {
        var exports = 0;
        var vm = Create((path, ids) => { exports++; return new(path, ids.Count, ids.Count); });
        await vm.LoadAsync();

        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.True(vm.CanUseImport);
        await vm.ExportSelectedAsync("C:\\must-not-export.xlsx");
        Assert.Equal(0, exports);
        var window = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var start = window.IndexOf("PendingTasksStandardGrid", StringComparison.Ordinal);
        var pending = window[start..window.IndexOf("IsHistoryVisible", start, StringComparison.Ordinal)];
        Assert.Equal(2, pending.Split("Header=\"选择\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, pending.Split("选择待排查任务", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task FailedRetryDoesNotReuseAnEarlierExportAsSuccess()
    {
        var succeed = true;
        var vm = Create((path, ids) => succeed ? new(path, ids.Count, ids.Count) : throw new IOException("locked"));
        await vm.LoadAsync();
        vm.Items[0].IsSelected = true;
        await vm.ExportSelectedAsync("C:\\same.xlsx");
        Assert.NotNull(vm.LatestExportResult);

        succeed = false;
        await vm.ExportSelectedAsync("C:\\same.xlsx");

        Assert.Null(vm.LatestExportResult);
        Assert.Contains("失败", vm.ActionStatusText);
    }

    private static PendingTasksViewModel Create(Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? export = null) => new(
        request =>
        {
            var all = Enumerable.Range(1, 55).Select(id => new InspectionTaskListItem(id, id, $"商品 {id}", $"SKU-{id}", null, "expired", 1, 10, DateOnly.FromDateTime(DateTime.Today), false, id % 2 == 0 ? "宠物" : "食品")).ToArray();
            var filtered = all.Where(item => string.IsNullOrEmpty(request.SearchText) || item.ProductName!.Contains(request.SearchText, StringComparison.Ordinal))
                .Where(item => string.IsNullOrEmpty(request.Stage) || item.HighestStage == request.Stage)
                .Where(item => string.IsNullOrEmpty(request.CategoryName) || item.CategoryName == request.CategoryName).ToArray();
            return new(filtered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArray(), filtered.Length, request.Page, request.PageSize);
        }, export: export);

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "StoreExpiryInspector.slnx"))) directory = Directory.GetParent(directory)?.FullName ?? throw new DirectoryNotFoundException();
        return directory;
    }
}
