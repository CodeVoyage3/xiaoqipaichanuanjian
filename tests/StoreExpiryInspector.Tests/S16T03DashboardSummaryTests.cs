using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S16T03DashboardSummaryTests
{
    [Theory]
    [InlineData(6, 5)]
    [InlineData(5, 5)]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    public async Task DashboardShowsAtMostFiveUrgentTasksAndKeepsTrueTotal(int suppliedCount, int displayedCount)
    {
        var tasks = Enumerable.Range(1, suppliedCount).Select(index => TaskItem(index)).ToArray();
        var vm = new DashboardViewModel(() => new InspectionDashboardResult(
            suppliedCount,
            0,
            0,
            0,
            0,
            tasks,
            DateTime.UtcNow));

        await vm.LoadAsync();

        Assert.Equal(displayedCount, vm.UrgentTasks.Count);
        Assert.Equal(suppliedCount, vm.OpenTaskCount);
        Assert.Equal(tasks.Take(displayedCount).Select(task => task.TaskId), vm.UrgentTasks.Select(task => task.TaskId));
        Assert.Equal(suppliedCount == 0, vm.HasNoOpenTasks);
    }

    [Fact]
    public async Task DashboardSearchShowsFiveButKeepsTrueSearchTotal()
    {
        var tasks = Enumerable.Range(1, 6).Select(index => TaskItem(index)).ToArray();
        InspectionTaskSearchRequest? request = null;
        var vm = new DashboardViewModel(
            () => new InspectionDashboardResult(6, 0, 0, 0, 0, tasks, DateTime.UtcNow),
            searchTasks: value =>
            {
                request = value;
                return new InspectionTaskSearchResult(tasks, 6, value.Page, value.PageSize);
            });

        await vm.SearchAsync("商品");

        Assert.Equal(5, request!.PageSize);
        Assert.Equal(5, vm.UrgentTasks.Count);
        Assert.Equal(6, vm.SearchResultCount);
        Assert.Equal(tasks.Take(5).Select(task => task.TaskId), vm.UrgentTasks.Select(task => task.TaskId));
    }

    private static InspectionTaskListItem TaskItem(int index) => new(
        index,
        index,
        $"商品 {index}",
        $"code-{index}",
        null,
        "expired",
        1,
        1,
        new DateOnly(2026, 9, index),
        false);
}
