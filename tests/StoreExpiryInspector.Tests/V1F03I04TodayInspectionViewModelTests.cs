using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F03I04TodayInspectionViewModelTests
{
    private static readonly DateOnly Today = new(2026, 9, 2);

    [Fact]
    public async Task SelectionExportAndPreviewKeepBlankZeroPositiveAndReasonsDistinct()
    {
        IReadOnlyCollection<long>? exported = null;
        var vm = Create(
            export: (_, ids) => { exported = ids; return new("C:\\plan.xlsx", ids.Count, 3); },
            preview: _ => Preview(applicable: [1], rows:
            [
                Row(1, null), Row(1, 0), Row(2, 3), Row(3, 3, ["本次排查数量必须是非负 Int32 整数。"])
            ], reasons: new Dictionary<long, string> { [2] = "Task 快照已陈旧" }));

        await vm.LoadAsync();
        vm.Tasks[0].IsSelected = true;
        await vm.ExportAsync("C:\\plan.xlsx");
        await vm.PreviewAsync("C:\\filled.xlsx");

        Assert.Equal([1], exported);
        Assert.Equal(4, vm.PreviewRows.Count);
        Assert.Equal(string.Empty, vm.PreviewRows[0].CheckedQtyText);
        Assert.Equal("0", vm.PreviewRows[1].CheckedQtyText);
        Assert.Equal("3", vm.PreviewRows[2].CheckedQtyText);
        Assert.Equal("001", vm.PreviewRows[0].ProductBarcode);
        Assert.Equal("2026-09-01", vm.PreviewRows[0].ProductionDate);
        Assert.Equal("2026-09-30", vm.PreviewRows[0].ExpiryDate);
        Assert.Equal("未填写", vm.PreviewRows[0].StatusText);
        Assert.Equal("可提交", vm.PreviewRows[1].StatusText);
        Assert.Equal("需要重新导出", vm.PreviewRows[2].StatusText);
        Assert.Contains("Task 快照已陈旧", vm.PreviewRows[2].Reason);
        Assert.Equal("数据错误", vm.PreviewRows[3].StatusText);
        Assert.Contains("本次排查数量", vm.PreviewRows[3].Reason);
        Assert.Contains("可应用 1", vm.PreviewSummaryText);
        Assert.Contains("陈旧/失效 2", vm.PreviewSummaryText);
    }

    [Fact]
    public async Task PreviewRowsKeepTheirObservableItemsSourceContract()
    {
        var vm = Create(preview: _ => Preview([1], [Row(1, 1), Row(1, 0)]));
        var notifications = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)vm.PreviewRows).CollectionChanged += (_, _) => notifications++;

        await vm.PreviewAsync("C:\\filled.xlsx");

        Assert.Equal(2, vm.PreviewRows.Count);
        Assert.True(notifications >= 2);
    }

    [Fact]
    public async Task DraftGateKeepsEveryTaskOutOfI03UntilAllAreComplete()
    {
        var submissions = 0;
        var refreshes = 0;
        var confirmations = 0;
        IReadOnlyCollection<long>? submittedTaskIds = null;
        var submittedAt = new List<DateTime>();
        var vm = Create(
            preview: _ => Preview(applicable: [1, 2]),
            apply: _ => new(true,
            [
                new(1, 11, true, new(1, 1, 0, 0, true, true, true, true)),
                new(2, 12, true, new(1, 0, 1, 0, true, true, false, false))
            ]),
            submit: request =>
            {
                submissions++;
                Assert.Equal([1], request.TaskIds);
                submittedTaskIds = request.TaskIds;
                submittedAt.Add(request.SubmittedAtUtc);
                return submissions == 1
                    ? new(BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation, [], [new(1, 9, 5, 8)])
                    : submissions == 2
                    ? new(BulkInspectionSubmissionOutcome.OverStockConfirmationStale, [], [new(1, 9, 4, 8)])
                    : new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 101)], []);
            },
            refresh: _ => { refreshes++; return Task.CompletedTask; },
            confirm: _ => { confirmations++; return true; },
            utcNow: () => new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));

        await vm.PreviewAsync("C:\\filled.xlsx");
        vm.InspectorName = "检查员";
        vm.CheckDateText = "2026-09-02";
        await vm.SaveDraftAsync();
        await vm.SubmitAsync();

        Assert.Null(submittedTaskIds);
        Assert.Equal([1], vm.CompleteTaskIds);
        Assert.Equal(0, submissions);
        Assert.Equal(0, confirmations);
        Assert.Empty(submittedAt);
        Assert.Equal(0, refreshes);
        Assert.Equal("仍有未完成排查项，请填写完整后提交。", vm.StatusText);
    }

    [Fact]
    public async Task InvalidInspectorOrFutureDateDoesNotApplyDraft()
    {
        var applies = 0;
        var vm = Create(preview: _ => Preview(applicable: [1]), apply: _ => { applies++; return new(false, []); });
        await vm.PreviewAsync("C:\\filled.xlsx");
        vm.CheckDateText = "2026-09-03";
        await vm.SaveDraftAsync();
        Assert.Equal("排查日期不能晚于今天", vm.CheckDateError);
        vm.CheckDateText = "2026-09-02";
        await vm.SaveDraftAsync();

        Assert.Equal(0, applies);
        Assert.Contains("请完善排查人", vm.StatusText);
        Assert.Equal("请输入排查人", vm.InspectorNameError);
    }

    [Fact]
    public async Task FirstSubmitWithDefaultDateMarksEmptyInspectorAndRaisesBusinessBlocker()
    {
        var vm = Create();
        string? blocker = null;
        vm.SubmissionBlocked += message => blocker = message;
        await vm.PreviewAsync("C:\\filled.xlsx");
        await vm.SubmitAsync();

        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), vm.CheckDateValue);
        Assert.True(vm.HasInspectorNameError);
        Assert.Equal("请输入排查人", vm.InspectorNameError);
        Assert.Contains("请输入排查人", blocker);
    }

    [Fact]
    public async Task AllSelectionKeepsEveryLoadedTaskAndFormChangesInvalidateSavedDraft()
    {
        var items = Enumerable.Range(1, 501).Select(id => new InspectionTaskListItem(id, id, $"商品{id}", id.ToString(), null, "expired", 1, 1, Today, false)).ToArray();
        var vm = Create(loadTasks: () => new(items, items.Length, 1, int.MaxValue), preview: _ => Preview([1]));
        await vm.LoadAsync();
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(501, vm.SelectedCount);
        vm.ClearSelectionCommand.Execute(null);
        vm.Tasks[500].IsSelected = true;
        Assert.Equal(1, vm.SelectedCount);
        await vm.PreviewAsync("C:\\filled.xlsx");
        vm.InspectorName = "检查员";
        await vm.SaveDraftAsync();
        Assert.Equal([1], vm.CompleteTaskIds);
        vm.InspectorName = "新检查员";
        Assert.Empty(vm.CompleteTaskIds);
        Assert.Contains("尚未处理", vm.DraftStatusText);
    }

    [Fact]
    public async Task LargeLoadPublishesOnceAndKeepsAllTaskIdsSelectable()
    {
        var items = Enumerable.Range(1, 576).Select(id => new InspectionTaskListItem(id, id, $"商品{id}", id.ToString(), null, "expired", 1, 1, Today, false, "食品")).ToArray();
        var changes = new List<string?>();
        var vm = Create(loadTasks: () => new(items, items.Length, 1, int.MaxValue));
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await vm.LoadAsync();
        vm.SelectAllCommand.Execute(null);

        Assert.Equal(576, vm.Tasks.Count);
        Assert.Equal(576, vm.SelectedCount);
        Assert.Equal(1, changes.Count(name => name == nameof(TodayInspectionViewModel.Tasks)));
    }

    [Fact]
    public async Task CategoryFilterKeepsTaskIdentityAndLimitsBulkSelectionToVisibleTasks()
    {
        var items = new[]
        {
            new InspectionTaskListItem(1, 1, "食品", "A", null, "expired", 1, 1, Today, false, "食品"),
            new InspectionTaskListItem(2, 2, "宠物", "B", null, "withdraw", 1, 1, Today, false, "宠物"),
            new InspectionTaskListItem(3, 3, "食品二", "C", null, "expired", 1, 1, Today, false, "食品")
        };
        IReadOnlyCollection<long>? exported = null;
        var vm = Create(loadTasks: () => new(items, items.Length, 1, int.MaxValue), export: (_, ids) => { exported = ids; return new("C:\\plan.xlsx", ids.Count, ids.Count); });
        await vm.LoadAsync();

        Assert.Equal(new[] { "全部", "宠物", "食品" }, vm.Categories);
        vm.SelectedCategory = "食品";
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(new long[] { 1, 3 }, vm.VisibleTasks.Where(task => task.IsSelected).Select(task => task.TaskId));
        vm.SelectedCategory = "宠物";
        Assert.False(vm.VisibleTasks.Single().IsSelected);
        vm.VisibleTasks.Single().IsSelected = true;
        await vm.ExportAsync("C:\\plan.xlsx");

        Assert.Equal(new long[] { 1, 2, 3 }, exported!.OrderBy(id => id));
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(new long[] { 1, 3 }, vm.Tasks.Where(task => task.IsSelected).Select(task => task.TaskId));
    }

    [Fact]
    public async Task LatestExportOnlyChangesAfterEachSuccessfulResult()
    {
        var count = 0;
        var vm = Create(export: (_, ids) => ++count == 1 ? new("C:\\A.xlsx", ids.Count, 3) : throw new IOException());
        await vm.LoadAsync(); vm.Tasks[0].IsSelected = true;
        await vm.ExportAsync("C:\\A.xlsx");
        await vm.ExportAsync("C:\\B.xlsx");
        Assert.Equal("C:\\A.xlsx", vm.LatestExportResult?.OutputPath);
        Assert.Equal("导出今日排查计划失败", vm.StatusText);
    }

    [Fact]
    public async Task ConsecutiveSuccessfulExportsKeepOnlyTheLatestPathAndSelection()
    {
        var calls = new List<IReadOnlyCollection<long>>();
        var vm = Create(export: (path, ids) => { calls.Add(ids); return new(path, ids.Count, ids.Count + 1); });
        await vm.LoadAsync();
        vm.Tasks[0].IsSelected = true;
        await vm.ExportAsync("C:\\A.xlsx");
        vm.Tasks[0].IsSelected = false;
        vm.Tasks[1].IsSelected = true;
        await vm.ExportAsync("C:\\B.xlsx");

        Assert.Equal(new long[] { 1 }, calls[0]);
        Assert.Equal(new long[] { 2 }, calls[1]);
        Assert.Equal("C:\\B.xlsx", vm.LatestExportResult?.OutputPath);
        Assert.Equal(1, vm.LatestExportResult?.TaskCount);
    }

    [Fact]
    public async Task PreviewFailureRaisesSafeBusinessBlockerWithoutExceptionText()
    {
        var vm = Create(preview: _ => throw new InvalidDataException("internal parser stack detail"));
        string? message = null;
        vm.PreviewFailed += value => message = value;
        await vm.PreviewAsync("C:\\bad.xlsx");

        Assert.Equal("读取排查结果文件失败", vm.StatusText);
        Assert.Contains("无法读取排查结果文件", message);
        Assert.DoesNotContain("internal parser", message);
    }

    [Fact]
    public async Task SubmittedTasksDisappearOnlyAfterAuthoritativeReload()
    {
        var completed = false;
        var item = new InspectionTaskListItem(1, 1, "商品", "A", null, "expired", 1, 1, Today, false, "食品");
        var vm = Create(
            loadTasks: () => completed ? new([], 0, 1, int.MaxValue) : new([item], 1, 1, int.MaxValue),
            submit: _ => { completed = true; return new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 1)], []); });
        await vm.LoadAsync();
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员";
        await vm.SubmitAsync();
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public async Task BulkSelectionPublishesSelectedCountOncePerCommand()
    {
        var items = Enumerable.Range(1, 576).Select(id => new InspectionTaskListItem(id, id, $"商品{id}", id.ToString(), null, "expired", 1, 1, Today, false)).ToArray();
        var vm = Create(loadTasks: () => new(items, items.Length, 1, int.MaxValue));
        await vm.LoadAsync();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(576, vm.SelectedCount);
        Assert.Equal(1, changes.Count(name => name == nameof(TodayInspectionViewModel.SelectedCount)));

        changes.Clear();
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Equal(1, changes.Count(name => name == nameof(TodayInspectionViewModel.SelectedCount)));
    }

    [Fact]
    public async Task ShellDoesNotReloadLoadedTodayTasksButRefreshStillDoes()
    {
        var loads = 0;
        var shell = new ShellViewModel(
            dashboardLoader: () => new(0, 0, 0, 0, 0, []),
            taskLoader: request =>
            {
                if (request.PageSize == int.MaxValue) Interlocked.Increment(ref loads);
                return new([], 0, request.Page, request.PageSize);
            });

        await shell.NavigateToAsync(ShellPage.TodayInspection);
        await WaitUntil(() => shell.TodayInspection.HasLoadedTasks);
        await shell.NavigateToAsync(ShellPage.Dashboard);
        await shell.NavigateToAsync(ShellPage.TodayInspection);
        Assert.Equal(1, loads);

        await shell.TodayInspection.LoadAsync();
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task ListLoadingKeepsShellNavigationEnabledButDisablesTodayActions()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shell = new ShellViewModel(
            dashboardLoader: () => new(0, 0, 0, 0, 0, []),
            taskLoader: request =>
            {
                if (request.PageSize == int.MaxValue)
                {
                    started.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                }
                return new([], 0, request.Page, request.PageSize);
            });

        var load = shell.TodayInspection.LoadAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(shell.NavigateHomeCommand.CanExecute(null));
        Assert.False(shell.TodayInspection.ReloadCommand.CanExecute(null));
        Assert.False(shell.TodayInspection.PreviewCommand.CanExecute(null));
        release.SetResult();
        await load;
    }

    [Fact]
    public async Task SubmissionFreezesUtcWaitsForRefreshAndCannotRepeat()
    {
        var utc = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        var refresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<DateTime>();
        var vm = Create(
            submit: request => { calls.Add(request.SubmittedAtUtc); return new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 1)], []); },
            refresh: _ => refresh.Task,
            utcNow: () => utc);
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员"; await vm.SaveDraftAsync();
        var submitting = vm.SubmitAsync();
        await Task.Delay(20);
        Assert.True(vm.IsBusy);
        Assert.False(vm.SubmitCommand.CanExecute(null));
        await vm.SubmitAsync();
        Assert.Single(calls);
        refresh.SetResult();
        await submitting;
        Assert.False(vm.SubmitCommand.CanExecute(null));
        Assert.Equal([utc], calls);
    }

    [Fact]
    public async Task RejectedOverStockConfirmationDoesNotSubmitAndRequiresAnotherPrompt()
    {
        var calls = 0;
        var prompts = 0;
        var vm = Create(
            submit: _ => { calls++; return new(BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation, [], [new(1, 9, 5, 8)]); },
            confirm: _ => { prompts++; return false; });
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员"; await vm.SaveDraftAsync();
        await vm.SubmitAsync();
        await vm.SubmitAsync();
        Assert.Equal(2, calls);
        Assert.Equal(2, prompts);
        Assert.Contains("确认", vm.StatusText);
    }

    [Fact]
    public async Task FixedFailuresClearPreviewAndNeverCallTheWrongDelegate()
    {
        var exports = 0;
        var vm = Create(export: (_, _) => { exports++; throw new InvalidOperationException("secret"); }, preview: _ => throw new InvalidDataException("bad format"));
        await vm.ExportAsync("C:\\plan.xlsx");
        Assert.Equal(0, exports);
        Assert.Contains("请先选择", vm.StatusText);
        await vm.LoadAsync(); vm.Tasks[0].IsSelected = true;
        await vm.ExportAsync("C:\\plan.xlsx");
        Assert.Equal(1, exports);
        Assert.Equal("导出今日排查计划失败", vm.StatusText);
        await vm.PreviewAsync("C:\\bad.xlsx");
        Assert.False(vm.HasPreview);
        Assert.Empty(vm.PreviewRows);
        Assert.Equal("读取排查结果文件失败", vm.StatusText);
    }

    [Fact]
    public async Task RefreshFailureStillReloadsTodayAndClearsAlreadySubmittedSession()
    {
        var loads = 0;
        var submits = 0;
        var vm = Create(
            loadTasks: () => { loads++; return new([], 0, 1, int.MaxValue); },
            submit: _ => { submits++; return new(BulkInspectionSubmissionOutcome.AlreadySubmitted, [], []); },
            refresh: _ => Task.FromException(new InvalidOperationException("page refresh")));
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员"; await vm.SaveDraftAsync();
        await vm.SubmitAsync();
        Assert.Equal(1, loads);
        Assert.Equal(1, submits);
        Assert.False(vm.HasPreview);
        Assert.Contains("部分页面刷新失败", vm.StatusText);
        Assert.False(vm.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShellLoadsTodayTasksWithoutThePendingPageCapAndKeepsImportSeparate()
    {
        InspectionTaskSearchRequest? todayRequest = null;
        var shell = new ShellViewModel(
            dashboardLoader: () => new(0, 0, 0, 0, 0, []),
            taskLoader: request =>
            {
                if (request.PageSize == int.MaxValue) todayRequest = request;
                return new([], 0, request.Page, request.PageSize);
            });
        await shell.TodayInspection.LoadAsync();
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        Assert.Equal(int.MaxValue, todayRequest?.PageSize);
        Assert.Contains("NavigationTodayInspectionButton", window, StringComparison.Ordinal);
        Assert.Contains("NavigationImportButton", window, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenTodayInspection_Click\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Import.TrySelectFile", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "TodayInspectionViewModel.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void TodayTaskGridUsesTheSixColumnVirtualizedSelectionContract()
    {
        var allWindow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var todayStart = allWindow.IndexOf("<Grid Visibility=\"{Binding IsTodayInspectionVisible", StringComparison.Ordinal);
        var window = allWindow[todayStart..allWindow.IndexOf("IsImportVisible", todayStart, StringComparison.Ordinal)];
        foreach (var header in new[] { "选择", "条码", "商品名称", "大类", "当前最高阶段", "总库存" })
            Assert.Contains($"Header=\"{header}\"", window, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", window, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding ProductName}\"", window, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{StaticResource StageBadgeTemplate}\"", window, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"", window, StringComparison.Ordinal);
        Assert.Contains("TableNumericCenterTextStyle", window, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", window, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"240\" MaxHeight=\"240\"", window, StringComparison.Ordinal);
        Assert.Contains("GridLinesVisibility=\"All\"", window, StringComparison.Ordinal);
        Assert.Contains("SelectionUnit=\"Cell\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"商品编码\" Binding=\"{Binding ProductCode}\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"批次数\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"任务状态\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TodayInspection.VisibleTasks}\"", window, StringComparison.Ordinal);
        Assert.Contains("TodayInspection.Categories", window, StringComparison.Ordinal);
        Assert.Contains("TableGridColumnHeaderStyle", window, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"0,0,1,1\"", allWindow, StringComparison.Ordinal);
        Assert.True(new[] { "选择", "条码", "商品名称", "大类", "当前最高阶段", "总库存" }
            .Select(header => window.IndexOf($"Header=\"{header}\"", StringComparison.Ordinal))
            .Zip(new[] { "选择", "条码", "商品名称", "大类", "当前最高阶段", "总库存" }.Select(header => window.IndexOf($"Header=\"{header}\"", StringComparison.Ordinal)).Skip(1), (left, right) => left < right)
            .All(value => value));
        Assert.Contains("CheckBox IsChecked=\"{Binding IsSelected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"选择今日排查任务\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("<DataGridCheckBoxColumn Header=\"选择\"", window, StringComparison.Ordinal);
        Assert.Contains("TodayInspection.IsLoadingTasks", window, StringComparison.Ordinal);
        Assert.Equal("expired", new TodayInspectionTaskViewModel(new(1, 1, "商品", "SKU", null, "expired", 1, 1, Today, false)).HighestStage);
    }

    [Fact]
    public async Task SubmitAppliesOnceThenRequiresConfirmationBeforeI03()
    {
        var applies = 0;
        var submits = 0;
        var confirmations = 0;
        var vm = Create(
            apply: _ => { applies++; return new(true, [new(1, 1, true, new(1, 1, 0, 0, true, true, true, true))]); },
            submit: _ => { submits++; return new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 1)], []); },
            confirmSubmission: () => { confirmations++; return false; });
        await vm.PreviewAsync("C:\\filled.xlsx");
        vm.InspectorName = "检查员";

        await vm.SubmitAsync();

        Assert.Equal(1, applies);
        Assert.Equal(1, confirmations);
        Assert.Equal(0, submits);
        Assert.Equal("已取消提交数据。", vm.StatusText);
    }

    [Fact]
    public async Task ExpiredPositiveInventoryRequiresItsOwnWarningBeforeI03()
    {
        var submits = 0;
        ExpiredInventoryWarning? warning = null;
        var vm = Create(
            submit: _ => { submits++; return new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 1)], []); },
            confirmExpiredInventory: value => { warning = value; return false; },
            confirmSubmission: () => throw new InvalidOperationException("ordinary confirmation must be replaced"));
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员";

        await vm.SubmitAsync();

        Assert.Equal(new ExpiredInventoryWarning(1, 1), warning);
        Assert.Equal(0, submits);
        Assert.True(vm.HasPreview);
    }

    [Theory]
    [InlineData(null, "expired")]
    [InlineData(0, "expired")]
    [InlineData(1, "withdraw")]
    public async Task OnlyExpiredPositiveInventoryTriggersTheStrengthenedWarning(int? checkedQty, string stage)
    {
        var warnings = 0;
        var submissions = 0;
        var vm = Create(
            preview: _ => Preview([1], [Row(1, checkedQty, stage: stage)]),
            submit: _ => { submissions++; return new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 1)], []); },
            confirmExpiredInventory: _ => { warnings++; return true; },
            confirmSubmission: () => false);
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员";

        await vm.SubmitAsync();

        Assert.Equal(0, warnings);
        Assert.Equal(0, submissions);
    }

    [Fact]
    public async Task ExpiredPositiveInventoryAggregatesBatchesAndContinuesOnlyAfterConfirmation()
    {
        var submissions = 0;
        ExpiredInventoryWarning? warning = null;
        var vm = Create(
            preview: _ => Preview([1], [Row(1, 2), Row(1, 3)]),
            submit: _ => { submissions++; return new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 1)], []); },
            confirmExpiredInventory: value => { warning = value; return true; },
            confirmSubmission: () => throw new InvalidOperationException("ordinary confirmation must be replaced"));
        await vm.PreviewAsync("C:\\filled.xlsx"); vm.InspectorName = "检查员";

        await vm.SubmitAsync();

        Assert.Equal(new ExpiredInventoryWarning(2, 5), warning);
        Assert.Equal(1, submissions);
    }

    [Fact]
    public void ConfirmationWindowKeepsOnlyTheFiveDataColumnsAndRetainsExceptionExpression()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "TodayInspectionConfirmationWindow.xaml"));
        foreach (var header in new[] { "条码", "商品名称", "生产日期", "有效日期", "本次排查数量" })
            Assert.Contains($"Header=\"{header}\"", window, StringComparison.Ordinal);
        Assert.Contains("GridLinesVisibility=\"All\"", window, StringComparison.Ordinal);
        Assert.Contains("Height=\"520\"", window, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"440\"", window, StringComparison.Ordinal);
        Assert.Contains("ConfirmationGridHeaderStyle", window, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"0,0,1,1\"", window, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding Reason}\"", window, StringComparison.Ordinal);
        Assert.Contains("PreviewIssueText", window, StringComparison.Ordinal);
        Assert.Contains("HasIssue", window, StringComparison.Ordinal);
        Assert.Contains("DatePicker", window, StringComparison.Ordinal);
        Assert.Contains("ConfirmationInspectorTextBoxStyle", window, StringComparison.Ordinal);
        Assert.Contains("ConfirmationDatePickerStyle", window, StringComparison.Ordinal);
        Assert.Contains("HasInspectorNameError", window, StringComparison.Ordinal);
        Assert.Contains("HasCheckDateError", window, StringComparison.Ordinal);
        Assert.Contains("BorderBrush\" Value=\"{DynamicResource DangerBrush}\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"校验状态\"", window, StringComparison.Ordinal);
        Assert.Contains("OwnedWindows.OfType<TodayInspectionConfirmationWindow>().FirstOrDefault(window => window.IsActive) as Window ?? this", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"原因\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("草稿", window, StringComparison.Ordinal);
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        Assert.Contains("Text=\"大类\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("PreviewFailed += ShowTodayPreviewFailure", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("请确认文件未被移动或删除后重试", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs")), StringComparison.Ordinal);
    }

    private static TodayInspectionViewModel Create(
        Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? export = null,
        Func<string, InspectionPlanPreview>? preview = null,
        Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult>? apply = null,
        Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult>? submit = null,
        Func<IReadOnlyCollection<long>, Task>? refresh = null,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirm = null,
        Func<InspectionTaskSearchResult>? loadTasks = null,
        Func<DateTime>? utcNow = null,
        Func<bool>? confirmSubmission = null,
        Func<ExpiredInventoryWarning, bool>? confirmExpiredInventory = null) => new(
            loadTasks: loadTasks ?? (() => new([
                new(1, 9, "商品 A", "A", "001", "expired", 2, 5, Today, false),
                new(2, 10, "商品 B", "B", "002", "withdraw", 1, 4, Today, true)
            ], 2, 1, 50)),
            export: export ?? ((path, ids) => new(path, ids.Count, ids.Count)),
            preview: preview ?? (_ => Preview([1])),
            apply: apply ?? (_ => new(true, [new(1, 11, true, new(1, 1, 0, 0, true, true, true, true))])),
            submit: submit ?? (_ => new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 101)], [])),
            refreshAfterSubmit: refresh ?? (_ => Task.CompletedTask),
            confirmOverStock: confirm,
            confirmExpiredInventory: confirmExpiredInventory,
            confirmSubmission: confirmSubmission ?? (() => true),
            businessToday: () => Today,
            utcNow: utcNow);

    private static InspectionPlanPreview Preview(IReadOnlyList<long> applicable, IReadOnlyList<InspectionPlanRow>? rows = null, IReadOnlyDictionary<long, string>? reasons = null)
    {
        rows ??= [Row(1, 1)];
        reasons ??= new Dictionary<long, string>();
        return new(new(rows), new(1, rows.Select(row => row.TaskId).Distinct().Count(), rows.Count, rows.Count(row => row.CheckedQty is not null), rows.Count(row => row.CheckedQty is null), rows.Sum(row => row.Errors.Count)), rows.Select(row => new InspectionPlanTaskPreview(row.TaskId!.Value, applicable.Contains(row.TaskId.Value), reasons.TryGetValue(row.TaskId.Value, out var reason) ? reason : null)).ToArray(), applicable, reasons);
    }

    private static InspectionPlanRow Row(long taskId, int? checkedQty, IReadOnlyList<string>? errors = null, string stage = "expired") =>
        new(2, taskId, taskId, 9, taskId, 1, DateTime.UtcNow, 1, "active", stage, 1, 1, 5, checkedQty, "A", "商品 A", "2026-09-30", errors ?? [], "001", "2026-09-01", "2026-09-30");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 StoreExpiryInspector 仓库根目录。");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
