using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S8T02IsolationTests
{
    [Fact]
    public async Task InjectedShellReadDependenciesNeverReachTheDefaultContextFactory()
    {
        var defaultContextFactoryCalls = 0;
        var dashboardCalls = 0;
        var taskCalls = 0;
        var categoryCalls = 0;
        var shell = new ShellViewModel(
            dashboardLoader: () =>
            {
                Interlocked.Increment(ref dashboardCalls);
                return new(0, 0, 0, 0, 0, []);
            },
            taskLoader: request =>
            {
                Interlocked.Increment(ref taskCalls);
                return new([TaskItem("食品")], 1, request.Page, request.PageSize);
            },
            categoryLoader: () =>
            {
                Interlocked.Increment(ref categoryCalls);
                return ["食品"];
            },
            logException: _ => { },
            defaultContextFactory: () =>
            {
                Interlocked.Increment(ref defaultContextFactoryCalls);
                throw new InvalidOperationException("The production default context factory must not be reached.");
            });

        await WaitUntil(() => dashboardCalls > 0 && taskCalls > 0 && categoryCalls > 0);
        await shell.NavigateToAsync(ShellPage.PendingTasks);
        shell.PendingTasks.SelectedStage = "expired";
        shell.PendingTasks.SelectedCategory = "食品";
        await shell.PendingTasks.LoadAsync();
        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => shell.History.HasError);
        await shell.NavigateToAsync(ShellPage.TodayInspection);
        await WaitUntil(() => shell.TodayInspection.HasLoadedTasks);
        await shell.TodayInspection.LoadAsync();

        Assert.Equal(0, defaultContextFactoryCalls);
        Assert.True(dashboardCalls > 0);
        Assert.True(taskCalls > 0);
        Assert.True(categoryCalls > 0);
    }

    [Fact]
    public async Task TaskOnlyInjectionDerivesCategoriesWithoutAProductionFallback()
    {
        var defaultContextFactoryCalls = 0;
        var shell = new ShellViewModel(
            taskLoader: request => new([TaskItem("食品")], 1, request.Page, request.PageSize),
            logException: _ => { },
            defaultContextFactory: () =>
            {
                Interlocked.Increment(ref defaultContextFactoryCalls);
                throw new InvalidOperationException("The production default context factory must not be reached.");
            });

        await WaitUntil(() => shell.PendingTasks.HasLoadedResult);

        Assert.Equal(new[] { "全部大类", "食品" }, shell.PendingTasks.CategoryFilters.Select(item => item.Label));
        Assert.True(shell.Dashboard.HasError);
        Assert.Equal(0, defaultContextFactoryCalls);
    }

    [Fact]
    public async Task CategoryOnlyInjectionFailsClosedBeforeTheDefaultTaskOrDashboardLoader()
    {
        var defaultContextFactoryCalls = 0;
        var shell = new ShellViewModel(
            categoryLoader: () => ["食品"],
            logException: _ => { },
            defaultContextFactory: () =>
            {
                Interlocked.Increment(ref defaultContextFactoryCalls);
                throw new InvalidOperationException("The production default context factory must not be reached.");
            });

        await WaitUntil(() => shell.Dashboard.HasError && shell.PendingTasks.HasError);

        Assert.Equal(0, defaultContextFactoryCalls);
    }

    [Fact]
    public async Task HistoryOnlyInjectionFailsClosedDuringConstructorLoadsAndUsesItsInjectedHistoryLoader()
    {
        var defaultContextFactoryCalls = 0;
        var historyCalls = 0;
        var shell = new ShellViewModel(
            historyListLoader: () =>
            {
                Interlocked.Increment(ref historyCalls);
                return Array.Empty<InspectionHistoryListItem>();
            },
            logException: _ => { },
            defaultContextFactory: () =>
            {
                Interlocked.Increment(ref defaultContextFactoryCalls);
                throw new InvalidOperationException("The production default context factory must not be reached.");
            });

        await WaitUntil(() => shell.Dashboard.HasError && shell.PendingTasks.HasError);
        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => historyCalls > 0);

        Assert.Equal(0, defaultContextFactoryCalls);
    }

    [Fact]
    public async Task DetailOnlyInjectionFailsClosedDuringConstructorLoadsAndUsesItsInjectedDetailLoader()
    {
        var defaultContextFactoryCalls = 0;
        var detailCalls = 0;
        var shell = new ShellViewModel(
            detailLoader: taskId =>
            {
                Interlocked.Increment(ref detailCalls);
                return new(taskId, "not_found", null, null, null, null);
            },
            logException: _ => { },
            defaultContextFactory: () =>
            {
                Interlocked.Increment(ref defaultContextFactoryCalls);
                throw new InvalidOperationException("The production default context factory must not be reached.");
            });

        await WaitUntil(() => shell.Dashboard.HasError && shell.PendingTasks.HasError);
        shell.OpenDetail(1);
        await WaitUntil(() => detailCalls > 0);

        Assert.Equal(0, defaultContextFactoryCalls);
    }

    [Fact]
    public void ImportExecutorFallsClosedBeforeAnyDefaultContextFactoryUseInInjectionMode()
    {
        var defaultContextFactoryCalls = 0;
        var shell = new ShellViewModel(
            importParser: _ => throw new InvalidOperationException("Parser is not used by this guard test."),
            logException: _ => { },
            defaultContextFactory: () =>
            {
                Interlocked.Increment(ref defaultContextFactoryCalls);
                throw new InvalidOperationException("The production default context factory must not be reached.");
            });
        var field = typeof(ImportViewModel).GetField("_executeImport", BindingFlags.Instance | BindingFlags.NonPublic);
        var execute = Assert.IsType<Func<ImportConfirmationContract, DateTime, ConfirmedImportResult>>(field?.GetValue(shell.Import));

        Assert.Throws<InvalidOperationException>(() => execute(null!, DateTime.UtcNow));
        Assert.Equal(0, defaultContextFactoryCalls);
    }

    [Fact]
    public void TestDatabaseHelperUsesAUniqueMarkedTemporaryDirectory()
    {
        using var first = SqliteTestDatabase.Create();
        using var second = SqliteTestDatabase.Create();
        var marker = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorTests");

        Assert.StartsWith(marker, first.Directory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(marker, second.Directory, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first.Directory, second.Directory);
        using var context = first.Open();
        Assert.Equal(first.Path, context.Database.GetDbConnection().DataSource);
    }

    [Fact]
    public async Task TodayIdLoaderOnlyInjectionsFailClosedBeforeTheDefaultContextFactory()
    {
        var factoryCalls = 0;
        foreach (var shell in new[]
        {
            new ShellViewModel(todayTaskIdsLoader: _ => [1L], logException: _ => { }, defaultContextFactory: () => { Interlocked.Increment(ref factoryCalls); throw new InvalidOperationException(); }),
            new ShellViewModel(todayOpenTaskIdsLoader: ids => ids.ToArray(), logException: _ => { }, defaultContextFactory: () => { Interlocked.Increment(ref factoryCalls); throw new InvalidOperationException(); })
        })
        {
            await WaitUntil(() => shell.Dashboard.HasError && shell.PendingTasks.HasError);
            await shell.NavigateToAsync(ShellPage.TodayInspection);
            await WaitUntil(() => shell.TodayInspection.HasLoadedTasks || shell.TodayInspection.StatusText == "加载今日任务失败");
        }
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task TodayBulkSelectionDisablesContentCommandsAndNotifiesBindings()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var items = Enumerable.Range(1, 51).Select(id => new InspectionTaskListItem(id, id, "商品", $"SKU{id}", null, "expired", 1, 1, new DateOnly(2026, 9, 4), false, "食品")).ToArray();
        var shell = new ShellViewModel(taskLoader: request => new(items.Take(request.PageSize).ToArray(), items.Length, request.Page, request.PageSize), categoryLoader: () => ["食品"], todayTaskIdsLoader: _ => { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); return items.Select(item => item.TaskId).ToArray(); }, todayOpenTaskIdsLoader: ids => ids.ToArray(), logException: _ => { });
        await shell.TodayInspection.LoadAsync();
        var properties = new List<string?>(); var commandChanges = 0;
        shell.TodayInspection.PropertyChanged += (_, eventArgs) => properties.Add(eventArgs.PropertyName);
        shell.TodayInspection.ExportCommand.CanExecuteChanged += (_, _) => commandChanges++;
        shell.TodayInspection.SelectAllCommand.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(shell.TodayInspection.CanUseContent);
        Assert.False(shell.TodayInspection.ReloadCommand.CanExecute(null));
        Assert.False(shell.TodayInspection.ExportCommand.CanExecute(null));
        Assert.False(shell.TodayInspection.NextPageCommand.CanExecute(null));
        Assert.Contains(nameof(TodayInspectionViewModel.CanUseContent), properties);
        release.SetResult();
        await WaitUntil(() => shell.TodayInspection.SelectedCount == 51);
        Assert.True(commandChanges > 0);
    }

    private static InspectionTaskListItem TaskItem(string category) =>
        new(1, 1, "商品", "SKU", null, "expired", 1, 1, new DateOnly(2026, 9, 4), false, category);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the injected loader.");
            await Task.Delay(10);
        }
    }
}
