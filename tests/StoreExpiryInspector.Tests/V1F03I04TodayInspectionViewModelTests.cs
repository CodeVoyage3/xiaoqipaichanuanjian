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
                Row(1, null), Row(1, 0), Row(2, 3, ["本次排查数量必须是非负 Int32 整数。"])
            ], reasons: new Dictionary<long, string> { [2] = "Task 快照已陈旧" }));

        await vm.LoadAsync();
        vm.Tasks[0].IsSelected = true;
        await vm.ExportAsync("C:\\plan.xlsx");
        await vm.PreviewAsync("C:\\filled.xlsx");

        Assert.Equal([1], exported);
        Assert.Equal(3, vm.PreviewRows.Count);
        Assert.Equal("未填写", vm.PreviewRows[0].CheckedQtyText);
        Assert.Equal("0", vm.PreviewRows[1].CheckedQtyText);
        Assert.Equal("3", vm.PreviewRows[2].CheckedQtyText);
        Assert.Equal("行错误；陈旧/失效", vm.PreviewRows[2].StatusText);
        Assert.Contains("本次排查数量", vm.PreviewRows[2].Reason);
        Assert.Contains("Task 快照已陈旧", vm.PreviewRows[2].Reason);
        Assert.Contains("可应用 1", vm.PreviewSummaryText);
        Assert.Contains("陈旧/失效 1", vm.PreviewSummaryText);
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
    public async Task DraftGateOnlySubmitsCompleteTasksAndRefreshesAfterConfirmation()
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
        await Task.Delay(20);

        Assert.Equal([1], submittedTaskIds);
        Assert.Empty(vm.CompleteTaskIds);
        Assert.Equal("尚未保存草稿", vm.DraftStatusText);
        Assert.False(vm.HasPreview);
        Assert.Empty(vm.PreviewRows);
        Assert.Equal(3, submissions);
        Assert.Equal(2, confirmations);
        Assert.Single(submittedAt.Distinct());
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task InvalidInspectorOrFutureDateDoesNotApplyDraft()
    {
        var applies = 0;
        var vm = Create(preview: _ => Preview(applicable: [1]), apply: _ => { applies++; return new(false, []); });
        await vm.PreviewAsync("C:\\filled.xlsx");
        vm.CheckDateText = "2026-09-03";
        await vm.SaveDraftAsync();
        vm.CheckDateText = "2026-09-02";
        await vm.SaveDraftAsync();

        Assert.Equal(0, applies);
        Assert.Contains("排查人必填", vm.StatusText);
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
        Assert.Contains("尚未保存", vm.DraftStatusText);
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
    public void TodayTaskGridUsesTheDenseVirtualizedEightColumnContract()
    {
        var window = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        foreach (var header in new[] { "选择", "商品编码", "商品名称", "大类", "当前最高阶段", "批次数", "商品当前库存", "任务状态" })
            Assert.Contains($"Header=\"{header}\"", window, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", window, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding ProductName}\"", window, StringComparison.Ordinal);
        Assert.Contains("CellTemplate=\"{StaticResource StageBadgeTemplate}\"", window, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding HighestStage}\" Value=\"expired\">", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding HighestStage, Converter={StaticResource StageLabelConverter}}\"", window, StringComparison.Ordinal);
        Assert.Contains("TableNumericCenterTextStyle", window, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", window, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", window, StringComparison.Ordinal);
        Assert.Contains("Height=\"240\" MaxHeight=\"240\"", window, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\" CanContentScroll=\"True\">", window, StringComparison.Ordinal);
        Assert.Contains("<DataGridTemplateColumn Header=\"选择\" Width=\"52\">", window, StringComparison.Ordinal);
        Assert.Contains("CheckBox IsChecked=\"{Binding IsSelected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"选择今日排查任务\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("<DataGridCheckBoxColumn Header=\"选择\"", window, StringComparison.Ordinal);
        Assert.Contains("TodayInspection.IsLoadingTasks", window, StringComparison.Ordinal);
        Assert.Equal("expired", new TodayInspectionTaskViewModel(new(1, 1, "商品", "SKU", null, "expired", 1, 1, Today, false)).HighestStage);
    }

    private static TodayInspectionViewModel Create(
        Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? export = null,
        Func<string, InspectionPlanPreview>? preview = null,
        Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult>? apply = null,
        Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult>? submit = null,
        Func<IReadOnlyCollection<long>, Task>? refresh = null,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirm = null,
        Func<InspectionTaskSearchResult>? loadTasks = null,
        Func<DateTime>? utcNow = null) => new(
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
            businessToday: () => Today,
            utcNow: utcNow);

    private static InspectionPlanPreview Preview(IReadOnlyList<long> applicable, IReadOnlyList<InspectionPlanRow>? rows = null, IReadOnlyDictionary<long, string>? reasons = null)
    {
        rows ??= [Row(1, 1)];
        reasons ??= new Dictionary<long, string>();
        return new(new(rows), new(1, rows.Select(row => row.TaskId).Distinct().Count(), rows.Count, rows.Count(row => row.CheckedQty is not null), rows.Count(row => row.CheckedQty is null), rows.Sum(row => row.Errors.Count)), rows.Select(row => new InspectionPlanTaskPreview(row.TaskId!.Value, applicable.Contains(row.TaskId.Value), reasons.TryGetValue(row.TaskId.Value, out var reason) ? reason : null)).ToArray(), applicable, reasons);
    }

    private static InspectionPlanRow Row(long taskId, int? checkedQty, IReadOnlyList<string>? errors = null) =>
        new(2, taskId, taskId, 9, taskId, 1, DateTime.UtcNow, 1, "active", "expired", 1, 1, 5, checkedQty, "A", "商品 A", "2026-09-01", errors ?? []);

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
