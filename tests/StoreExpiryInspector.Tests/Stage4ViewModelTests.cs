using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class Stage4ViewModelTests
{
    [Fact]
    public async Task DashboardLoadsFactsAndPreservesApplicationUrgentOrder()
    {
        var first = TaskItem("urgent-1", "expired");
        var second = TaskItem("urgent-2", "withdraw");
        var vm = new DashboardViewModel(() => new InspectionDashboardResult(
            2,
            1,
            1,
            0,
            0,
            new[] { first, second },
            new DateTime(2026, 8, 28, 1, 2, 3, DateTimeKind.Utc)));

        await vm.LoadAsync();

        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.Equal(2, vm.OpenTaskCount);
        Assert.Equal(1, vm.ExpiredCount);
        Assert.Equal(1, vm.WithdrawCount);
        Assert.Equal(new[] { "urgent-1", "urgent-2" }, vm.UrgentTasks.Select(item => item.ProductCode));
        Assert.Contains("最近一次成功导入", vm.LastImportText);
    }

    [Fact]
    public async Task DashboardSeparatesNoImportAndNoOpenTaskStates()
    {
        var noImport = new DashboardViewModel(() => new InspectionDashboardResult(
            0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()));
        await noImport.LoadAsync();

        Assert.True(noImport.HasNoImportData);
        Assert.False(noImport.HasNoOpenTasks);
        Assert.Equal("暂无导入数据", noImport.LastImportText);

        var noTasks = new DashboardViewModel(() => new InspectionDashboardResult(
            0,
            0,
            0,
            0,
            0,
            Array.Empty<InspectionTaskListItem>(),
            DateTime.UtcNow));
        await noTasks.LoadAsync();

        Assert.False(noTasks.HasNoImportData);
        Assert.True(noTasks.HasNoOpenTasks);
    }

    [Fact]
    public async Task DashboardShowsStaleWarningOnlyAfterSevenDays()
    {
        var now = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc);
        var vm = new DashboardViewModel(
            () => new InspectionDashboardResult(
                0,
                0,
                0,
                0,
                0,
                Array.Empty<InspectionTaskListItem>(),
                now.AddDays(-8)),
            utcNow: () => now);

        await vm.LoadAsync();

        Assert.True(vm.IsStale);
        Assert.Equal("数据已超过7天未更新", vm.FreshnessWarningText);
        Assert.Equal(
            now.AddDays(-8).ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            DashboardViewModel.FormatLastSuccessfulImport(now.AddDays(-8)));
    }

    [Fact]
    public async Task DashboardFailureIsErrorAndRetryLoadsAgain()
    {
        var attempts = 0;
        var logged = 0;
        var vm = new DashboardViewModel(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("private failure");
                }

                return new InspectionDashboardResult(
                    1,
                    0,
                    0,
                    0,
                    1,
                    new[] { TaskItem("retry", "discount_50") },
                    DateTime.UtcNow);
            },
            _ => logged++);

        await vm.LoadAsync();
        Assert.True(vm.HasError);
        Assert.Equal("数据加载失败", vm.ErrorMessage);
        Assert.False(vm.HasNoImportData);

        await vm.LoadAsync();
        Assert.False(vm.HasError);
        Assert.Equal(2, attempts);
        Assert.Equal(1, logged);
        Assert.Single(vm.UrgentTasks);
    }

    [Fact]
    public void StageLabelsAreDisplayOnlyCanonicalMappings()
    {
        Assert.Equal(
            new[] { "全部阶段", "过期", "收仓", "2折", "5折" },
            StageFilterOption.All.Select(option => option.Label));
        Assert.Equal("过期", StageLabels.ToDisplay("expired"));
        Assert.Equal("收仓", StageLabels.ToDisplay("withdraw"));
        Assert.Equal("2折", StageLabels.ToDisplay("discount_20"));
        Assert.Equal("5折", StageLabels.ToDisplay("discount_50"));
        Assert.Equal("正常", StageLabels.ToDisplay("none"));
        Assert.Equal(
            new[] { "expired", "withdraw", "discount_20", "discount_50" },
            StageFilterOption.All.Skip(1).Select(option => option.CanonicalStage));
    }

    [Fact]
    public void ImportStartsWithAnActionableConfirmationReason()
    {
        var vm = new ImportViewModel();

        Assert.False(vm.CanConfirm);
        Assert.Equal("请选择文件并完成预览", vm.ConfirmAvailabilityText);
    }

    [Fact]
    public async Task PendingTasksLoadsWithFixedPageSizeAndSearchText()
    {
        var requests = new List<InspectionTaskSearchRequest>();
        var vm = new PendingTasksViewModel(request =>
        {
            requests.Add(request);
            return new InspectionTaskSearchResult(
                new[] { TaskItem("found", "expired") },
                1,
                request.Page,
                request.PageSize);
        });

        await vm.LoadAsync();
        vm.SearchText = "商品编码";
        await vm.SearchAsync();

        Assert.Equal(50, vm.PageSize);
        Assert.Equal(3, requests.Count);
        Assert.Equal("商品编码", requests[^1].SearchText);
        Assert.Equal(int.MaxValue, requests[^1].PageSize);
        Assert.Equal(1, requests[^1].Page);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task PendingTasksPassesNameCodeAndBarcodeSearchWithoutUiFiltering()
    {
        var requests = new List<InspectionTaskSearchRequest>();
        var vm = new PendingTasksViewModel(request =>
        {
            requests.Add(request);
            return new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize);
        });

        foreach (var value in new[] { "商品名称", "SKU-001", "690000000001" })
        {
            vm.SearchText = value;
            await vm.SearchAsync();
        }

        Assert.Equal(
            new[] { "商品名称", "SKU-001", "690000000001" },
            requests.Where(request => request.SearchText is not null).Select(request => request.SearchText));
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task PendingTasksStageSelectionMapsAndResetsToFirstPage()
    {
        var requests = new List<InspectionTaskSearchRequest>();
        var vm = new PendingTasksViewModel(request =>
        {
            requests.Add(request);
            return new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize);
        });

        await vm.LoadAsync();
        vm.SelectedStage = "discount_20";
        await WaitForRequestCount(requests, 3);

        Assert.Equal("discount_20", requests[^1].Stage);
        Assert.Equal(1, requests[^1].Page);
        Assert.True(vm.IsFilterActive);
    }

    [Fact]
    public async Task PendingTasksDistinguishesDatabaseEmptyAndFilterEmpty()
    {
        var vm = new PendingTasksViewModel(request =>
            new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize));
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await vm.LoadAsync();
        Assert.True(vm.IsDatabaseEmpty);
        Assert.False(vm.IsFilterEmpty);
        Assert.Contains(nameof(PendingTasksViewModel.IsDatabaseEmpty), changes);

        changes.Clear();
        vm.SearchText = "没有这个商品";
        await vm.SearchAsync();
        Assert.False(vm.IsDatabaseEmpty);
        Assert.True(vm.IsFilterEmpty);
        Assert.Contains(nameof(PendingTasksViewModel.IsDatabaseEmpty), changes);
        Assert.Contains(nameof(PendingTasksViewModel.IsFilterEmpty), changes);
    }

    [Fact]
    public async Task PendingTasksErrorDoesNotBecomeEmptyAndRetryWorks()
    {
        var attempts = 0;
        var vm = new PendingTasksViewModel(request =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("private failure");
            }

            return new InspectionTaskSearchResult(new[] { TaskItem("recovered", "withdraw") }, 1, request.Page, request.PageSize);
        });

        await vm.LoadAsync();
        Assert.True(vm.HasError);
        Assert.False(vm.HasEmptyResult);
        Assert.False(vm.IsDatabaseEmpty);
        Assert.False(vm.IsFilterEmpty);

        await vm.LoadAsync();
        Assert.False(vm.HasError);
        Assert.Equal(2, attempts);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task PendingTasksPaginationStopsAtBothBoundaries()
    {
        var tasks = Enumerable.Range(1, 101).Select(index => TaskItem($"page-{index}", "discount_50", "食品", index)).ToArray();
        var vm = new PendingTasksViewModel(request =>
            new InspectionTaskSearchResult(tasks, tasks.Length, request.Page, request.PageSize));

        await vm.LoadAsync();
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(3, vm.TotalPages);
        Assert.False(vm.CanGoPrevious);
        Assert.True(vm.CanGoNext);

        await vm.GoToNextPageAsync();
        await vm.GoToNextPageAsync();
        Assert.Equal(3, vm.CurrentPage);
        Assert.True(vm.CanGoPrevious);
        Assert.False(vm.CanGoNext);

        await vm.GoToNextPageAsync();
        Assert.Equal(3, vm.CurrentPage);
        await vm.GoToPreviousPageAsync();
        await vm.GoToPreviousPageAsync();
        await vm.GoToPreviousPageAsync();
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public async Task PendingTasksCategoriesAreDistinctAndAllFiltersIntersect()
    {
        var tasks = new[]
        {
            TaskItem("apple-50", "discount_50", "食品", 1),
            TaskItem("apple-20", "discount_20", "食品", 2),
            TaskItem("apple-50-general", "discount_50", "百货", 3),
            TaskItem("pet-50", "discount_50", "宠物", 4),
            TaskItem("apple-50-duplicate-category", "discount_50", "食品", 5)
        };
        var vm = new PendingTasksViewModel(request =>
        {
            var filtered = tasks
                .Where(item => string.IsNullOrEmpty(request.SearchText) || item.ProductCode.Contains(request.SearchText, StringComparison.Ordinal))
                .Where(item => string.IsNullOrEmpty(request.Stage) || item.HighestStage == request.Stage)
                .ToArray();
            return new InspectionTaskSearchResult(filtered, filtered.Length, request.Page, request.PageSize);
        });

        await vm.LoadAsync();
        Assert.Equal(new[] { "全部大类", "宠物", "百货", "食品" }, vm.CategoryFilters.Select(option => option.Label));

        vm.SearchText = "apple";
        await vm.SearchAsync();
        Assert.Equal(4, vm.TotalCount);
        vm.SelectedStage = "discount_50";
        await WaitFor(() => vm.TotalCount == 3);
        vm.SelectedCategory = "食品";
        await WaitFor(() => vm.TotalCount == 2);
        Assert.Equal(new[] { "apple-50", "apple-50-duplicate-category" }, vm.Items.Select(item => item.ProductCode));

        await vm.ClearFiltersAsync();
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Null(vm.SelectedStage);
        Assert.Null(vm.SelectedCategory);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(5, vm.TotalCount);
    }

    [Fact]
    public void PendingTaskUiUsesDropdownFiltersAndKeepsStageBadgeContract()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var stageBadge = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "StageBadgeResources.xaml"));

        Assert.DoesNotContain("Text=\"门店效期排查软件\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NavigationToggleButton\"\n                            Grid.Column=\"2\"", window, StringComparison.Ordinal);
        Assert.True(window.IndexOf("NavigationTasksButton", StringComparison.Ordinal) < window.IndexOf("NavigationTodayInspectionButton", StringComparison.Ordinal));
        Assert.True(window.IndexOf("NavigationTodayInspectionButton", StringComparison.Ordinal) < window.IndexOf("NavigationHistoryButton", StringComparison.Ordinal));
        Assert.Contains("PendingTasks.StageFilters", window, StringComparison.Ordinal);
        Assert.Contains("PendingTasks.CategoryFilters", window, StringComparison.Ordinal);
        Assert.Contains("PendingFilterComboBoxStyle", window, StringComparison.Ordinal);
        Assert.Contains("FocusRingBrush", window, StringComparison.Ordinal);
        Assert.Contains("#FDECEC", stageBadge, StringComparison.Ordinal);
        Assert.Contains("#FFF0E5", stageBadge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellKeepsOnlyApprovedPagesNavigable()
    {
        var shell = new ShellViewModel(
            () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize));

        Assert.True(shell.NavigateHomeCommand.CanExecute(null));
        Assert.True(shell.NavigateTasksCommand.CanExecute(null));
        Assert.True(shell.NavigateHistoryCommand.CanExecute(null));
        Assert.True(shell.NavigateImportCommand.CanExecute(null));
        Assert.False(shell.NavigateSettingsCommand.CanExecute(null));

        shell.NavigateTasksCommand.Execute(null);
        Assert.Equal(ShellPage.PendingTasks, shell.CurrentPage);
        shell.NavigateHomeCommand.Execute(null);
        Assert.Equal(ShellPage.Dashboard, shell.CurrentPage);
        shell.NavigateImportCommand.Execute(null);
        Assert.Equal(ShellPage.Import, shell.CurrentPage);

        await Task.WhenAll(shell.Dashboard.LoadAsync(), shell.PendingTasks.LoadAsync());
    }

    private static InspectionTaskListItem TaskItem(string code, string stage, string category = "", long taskId = 1) => new(
        taskId,
        1,
        "测试商品",
        code,
        "690000000001",
        stage,
        2,
        10,
        new DateOnly(2026, 8, 28),
        false,
        category);

    private static async Task WaitForRequestCount(
        List<InspectionTaskSearchRequest> requests,
        int expected)
    {
        for (var attempt = 0; attempt < 100 && requests.Count < expected; attempt++)
        {
            await Task.Delay(1);
        }

        Assert.Equal(expected, requests.Count);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(1);
        }

        Assert.True(condition());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
