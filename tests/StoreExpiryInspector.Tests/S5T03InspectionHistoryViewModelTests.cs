using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using System.Text.RegularExpressions;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S5T03InspectionHistoryViewModelTests
{
    [Fact]
    public async Task ListLoadsBackendOrderAndShowsEmptyState()
    {
        var newer = ListItem(2, "NEWER");
        var older = ListItem(1, "OLDER");
        var vm = CreateVm(() => new[] { newer, older });

        await vm.LoadAsync();

        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
        Assert.True(vm.HasLoadedResult);
        Assert.False(vm.HasEmptyResult);
        Assert.Equal(new[] { 2L, 1L }, vm.Items.Select(item => item.InspectionId));

        var empty = CreateVm(Array.Empty<InspectionHistoryListItem>);
        await empty.LoadAsync();

        Assert.True(empty.HasLoadedResult);
        Assert.True(empty.HasEmptyResult);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task ListFailureIsDistinctAndRetryRecovers()
    {
        var attempts = 0;
        var vm = CreateVm(() =>
        {
            if (++attempts == 1)
            {
                throw new InvalidOperationException("private failure");
            }

            return new[] { ListItem(1, "RECOVERED") };
        });

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.False(vm.HasEmptyResult);
        Assert.Equal("排查历史加载失败", vm.ErrorMessage);

        await vm.LoadAsync();

        Assert.False(vm.HasError);
        Assert.Single(vm.Items);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ListAndDetailPreserveAllFormalSnapshotFieldsAndItems()
    {
        var record = ListItem(42, "HISTORY-42");
        var zeroItem = DetailItem(
            4201,
            42,
            checkedQty: 0,
            productionDate: null,
            stage: "none",
            arrivalQty: 0);
        var secondItem = DetailItem(
            4202,
            42,
            checkedQty: 6,
            productionDate: new DateOnly(2026, 8, 2),
            stage: "withdraw",
            arrivalQty: 12);
        var detail = Detail(42, "HISTORY-42", new[] { zeroItem, secondItem });
        var vm = CreateVm(
            () => new[] { record },
            _ => new(42, "found", detail));

        await vm.LoadAsync();
        vm.SelectedRecord = record;

        Assert.False(vm.IsDetailVisible);
        Assert.Same(record, vm.SelectedRecord);
        Assert.Equal(record.InspectionId, vm.Items.Single().InspectionId);
        Assert.Equal(record.TaskId, vm.Items.Single().TaskId);
        Assert.Equal(record.ProductId, vm.Items.Single().ProductId);
        Assert.Equal(record.ProductCode, vm.Items.Single().ProductCode);
        Assert.Equal(record.ProductName, vm.Items.Single().ProductName);
        Assert.Equal(record.ProductBarcode, vm.Items.Single().ProductBarcode);
        Assert.Equal(record.SubmittedAtUtc, vm.Items.Single().SubmittedAtUtc);
        Assert.Equal(record.ItemCount, vm.Items.Single().ItemCount);

        vm.OpenDetailCommand.Execute(record);
        await WaitUntil(() => vm.HasDetail || vm.HasDetailError || vm.IsDetailNotFound);

        Assert.True(vm.IsDetailVisible);
        Assert.True(vm.HasDetail);
        Assert.Equal(detail.InspectionId, vm.Detail!.InspectionId);
        Assert.Equal(detail.TaskId, vm.Detail.TaskId);
        Assert.Equal(detail.ProductId, vm.Detail.ProductId);
        Assert.Equal(detail.ProductCodeSnapshot, vm.Detail.ProductCodeSnapshot);
        Assert.Equal(detail.ProductNameSnapshot, vm.Detail.ProductNameSnapshot);
        Assert.Equal(detail.BarcodeSnapshot, vm.Detail.BarcodeSnapshot);
        Assert.Equal(detail.StageSnapshot, vm.Detail.StageSnapshot);
        Assert.Equal(detail.StockQtySnapshot, vm.Detail.StockQtySnapshot);
        Assert.Equal(detail.InspectorName, vm.Detail.InspectorName);
        Assert.Equal(detail.CheckDate, vm.Detail.CheckDate);
        Assert.Equal(detail.SubmittedAtUtc, vm.Detail.SubmittedAtUtc);
        Assert.Equal("HISTORY-42", vm.Detail!.ProductCodeSnapshot);
        Assert.Equal(2, vm.DetailItems.Count);
        Assert.Equal(0, vm.DetailItems[0].CheckedQty);
        Assert.Null(vm.DetailItems[0].ProductionDateSnapshot);
        Assert.Equal("none", vm.DetailItems[0].StageSnapshot);
        Assert.Equal(0, vm.DetailItems[0].ArrivalQtySnapshot);
        Assert.Equal(6, vm.DetailItems[1].CheckedQty);
        Assert.Equal(new DateOnly(2026, 8, 2), vm.DetailItems[1].ProductionDateSnapshot);
        Assert.Equal("withdraw", vm.DetailItems[1].StageSnapshot);
        Assert.Equal(12, vm.DetailItems[1].ArrivalQtySnapshot);
    }

    [Fact]
    public async Task DetailNotFoundIsDistinctFromLoadError()
    {
        var record = ListItem(7, "MISSING");
        var vm = CreateVm(
            () => new[] { record },
            _ => new(7, "not_found", null));

        await vm.LoadAsync();
        vm.OpenDetail(record);
        await WaitUntil(() => vm.IsDetailNotFound || vm.HasDetailError);

        Assert.True(vm.IsDetailNotFound);
        Assert.False(vm.HasDetailError);
        Assert.False(vm.HasDetail);
    }

    [Fact]
    public async Task DetailErrorExposesRetryAndRecoversWithoutNotFoundState()
    {
        var attempts = 0;
        var record = ListItem(8, "DETAIL-RETRY");
        var vm = CreateVm(
            () => new[] { record },
            _ => Interlocked.Increment(ref attempts) == 1
                ? throw new IOException("detail unavailable")
                : new(8, "found", Detail(8, "DETAIL-RETRY", Array.Empty<InspectionHistoryItemDetail>())));

        await vm.LoadAsync();
        vm.OpenDetail(record);
        await WaitUntil(() => vm.HasDetailError || vm.HasDetail);

        Assert.True(vm.HasDetailError);
        Assert.False(vm.IsDetailNotFound);
        Assert.Equal("排查详情加载失败", vm.DetailErrorMessage);

        vm.RetryDetailCommand.Execute(null);
        await WaitUntil(() => vm.HasDetail);

        Assert.Equal(2, attempts);
        Assert.False(vm.HasDetailError);
        Assert.False(vm.IsDetailNotFound);
    }

    [Fact]
    public async Task RevisionOrderErrorRetryEmptyAndNotFoundStatesFollowBackendResult()
    {
        var record = ListItem(9, "REVISION");
        var item = DetailItem(901, 9, 8);
        var emptyItem = DetailItem(902, 9, 1);
        var missingItem = DetailItem(903, 9, 2);
        var revisions = new[]
        {
            new InspectionItemRevisionDetail(2, 901, 5, 8, Utc(12)),
            new InspectionItemRevisionDetail(1, 901, 0, 5, Utc(10))
        };
        var attempts = 0;
        var vm = CreateVm(
            () => new[] { record },
            _ => new(9, "found", Detail(9, "REVISION", new[] { item, emptyItem, missingItem })),
            (_, itemId) => itemId switch
            {
                901 when Interlocked.Increment(ref attempts) == 1 => throw new IOException("revision unavailable"),
                901 => new(9, 901, "found", new(9, 901, 8, Utc(12), revisions)),
                902 => new(9, 902, "found", new(9, 902, 1, Utc(12), Array.Empty<InspectionItemRevisionDetail>())),
                _ => new(9, itemId, "not_found", null)
            });

        await vm.LoadAsync();
        vm.OpenDetail(record);
        await WaitUntil(() => vm.HasDetail);
        var selectedNotificationNames = new List<string?>();
        vm.PropertyChanged += (_, args) => selectedNotificationNames.Add(args.PropertyName);
        vm.SelectedDetailItem = item;
        await WaitUntil(() => vm.HasRevisionError || vm.HasRevisionHistory);

        Assert.True(vm.HasRevisionError);
        Assert.False(vm.IsRevisionNotFound);
        Assert.False(vm.HasNoRevisions);

        vm.RetryRevisionCommand.Execute(null);
        await WaitUntil(() => vm.HasRevisionHistory);

        Assert.Contains(nameof(vm.HasSelectedDetailItem), selectedNotificationNames);
        Assert.Equal(2, attempts);
        Assert.Equal(new[] { 2L, 1L }, vm.Revisions.Select(revision => revision.RevisionId));
        Assert.Equal(new[] { 5, 0 }, vm.Revisions.Select(revision => revision.PreviousCheckedQty));

        vm.SelectedDetailItem = emptyItem;
        await WaitUntil(() => vm.HasNoRevisions);

        Assert.Empty(vm.Revisions);
        Assert.False(vm.HasRevisionHistory);
        Assert.False(vm.IsRevisionNotFound);
        Assert.False(vm.HasRevisionError);

        vm.SelectedDetailItem = missingItem;
        await WaitUntil(() => vm.IsRevisionNotFound || vm.HasRevisionError);

        Assert.True(vm.IsRevisionNotFound);
        Assert.False(vm.HasRevisionError);
        Assert.False(vm.HasNoRevisions);
    }

    [Fact]
    public async Task StaleDetailAndRevisionResultsCannotOverwriteCurrentSelection()
    {
        var firstRecord = ListItem(1, "FIRST");
        var secondRecord = ListItem(2, "SECOND");
        var firstGate = new TaskCompletionSource<InspectionHistoryDetailResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var detailCalls = 0;
        var vm = CreateVm(
            () => new[] { firstRecord, secondRecord },
            id =>
            {
                Interlocked.Increment(ref detailCalls);
                if (id == 1)
                {
                    return firstGate.Task.GetAwaiter().GetResult();
                }

                return new(2, "found", Detail(2, "SECOND", new[] { DetailItem(202, 2, 2) }));
            });

        await vm.LoadAsync();
        vm.SelectedRecord = firstRecord;
        var oldDetailTask = vm.LoadDetailAsync();
        await WaitUntil(() => detailCalls == 1);
        vm.OpenDetail(secondRecord);
        await WaitUntil(() => vm.HasDetail && vm.Detail!.InspectionId == 2);
        firstGate.SetResult(new(1, "found", Detail(1, "FIRST", new[] { DetailItem(101, 1, 1) })));
        await oldDetailTask;

        Assert.Equal(2, vm.Detail!.InspectionId);
        Assert.Equal(202, vm.DetailItems.Single().InspectionItemId);

        var firstRevisionGate = new TaskCompletionSource<InspectionItemRevisionHistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var revisionCalls = 0;
        var revisionVm = CreateVm(
            () => new[] { secondRecord },
            _ => new(2, "found", Detail(2, "SECOND", new[] { DetailItem(201, 2, 1), DetailItem(202, 2, 2) })),
            (_, itemId) =>
            {
                Interlocked.Increment(ref revisionCalls);
                if (itemId == 201)
                {
                    return firstRevisionGate.Task.GetAwaiter().GetResult();
                }

                return RevisionResult(2, 202, 9);
            });

        await revisionVm.LoadAsync();
        revisionVm.OpenDetail(secondRecord);
        await WaitUntil(() => revisionVm.HasDetail);
        var firstItem = revisionVm.DetailItems.Single(item => item.InspectionItemId == 201);
        var secondItem = revisionVm.DetailItems.Single(item => item.InspectionItemId == 202);
        revisionVm.SelectedDetailItem = firstItem;
        var oldRevisionTask = revisionVm.LoadRevisionsAsync();
        await WaitUntil(() => revisionCalls >= 2);
        revisionVm.SelectedDetailItem = secondItem;
        await WaitUntil(() => revisionVm.HasRevisionHistory);
        firstRevisionGate.SetResult(RevisionResult(2, 201, 3));
        await oldRevisionTask;

        Assert.Equal(202, revisionVm.SelectedDetailItem!.InspectionItemId);
        Assert.Equal(9, revisionVm.Revisions.Single().NewCheckedQty);
    }

    [Fact]
    public async Task ShellLoadsHistoryOnlyAfterEnteringHistoryAndKeepsNavigationEnabled()
    {
        var historyLoads = 0;
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            taskLoader: request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize),
            historyListLoader: () =>
            {
                Interlocked.Increment(ref historyLoads);
                return Array.Empty<InspectionHistoryListItem>();
            },
            historyDetailLoader: _ => new(1, "not_found", null),
            historyRevisionLoader: (_, _) => new(1, 1, "not_found", null));

        Assert.True(shell.NavigateHistoryCommand.CanExecute(null));
        Assert.Equal(0, historyLoads);

        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => historyLoads == 1 && shell.History.HasLoadedResult);
        Assert.Equal(ShellPage.History, shell.CurrentPage);
        Assert.True(shell.IsHistoryVisible);
        Assert.True(shell.History.HasLoadedResult);

        await shell.NavigateToAsync(ShellPage.PendingTasks);
        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => historyLoads == 2);
    }

    [Fact]
    public async Task HistoryNavigationHonorsBusyDetailAndWaitsForStableDetailExit()
    {
        var submitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubmit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var historyLoads = 0;
        var detailLoads = 0;
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            taskLoader: request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize),
            detailLoader: _ => Interlocked.Increment(ref detailLoads) == 1
                ? OpenTaskResult(42)
                : CompletedTaskResult(42),
            submit: _ =>
            {
                submitStarted.TrySetResult();
                releaseSubmit.Task.GetAwaiter().GetResult();
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    908,
                    5,
                    1);
            },
            historyListLoader: () =>
            {
                Interlocked.Increment(ref historyLoads);
                return Array.Empty<InspectionHistoryListItem>();
            },
            historyDetailLoader: _ => new(1, "not_found", null),
            historyRevisionLoader: (_, _) => new(1, 1, "not_found", null));

        shell.OpenDetail(42);
        await WaitUntil(() => shell.Detail.IsOpen);
        var submitTask = shell.Detail.SubmitAsync();
        await submitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await shell.NavigateToAsync(ShellPage.History);

        Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);
        Assert.Equal(0, historyLoads);

        releaseSubmit.SetResult();
        await submitTask;
        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => shell.CurrentPage == ShellPage.History
            && historyLoads == 1
            && shell.History.HasLoadedResult);

        Assert.True(shell.IsHistoryVisible);
        Assert.Equal(2, detailLoads);
    }

    [Fact]
    public void HistoryViewIsReadonlyAndKeepsCopyableDataGrids()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var start = window.IndexOf("<!-- 排查历史", StringComparison.Ordinal);
        var end = window.IndexOf("<!-- 排查详情", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var history = window[start..end];

        Assert.True(Count(history, "<DataGrid") >= 3);
        Assert.True(Count(history, "SelectionUnit=\"Cell\"") >= 2);
        Assert.Contains("SelectionUnit=\"FullRow\"", history, StringComparison.Ordinal);
        Assert.True(Count(history, "ClipboardCopyMode=\"ExcludeHeader\"") >= 3);
        Assert.Contains("ProductBarcode", history, StringComparison.Ordinal);
        Assert.Contains("ProductCode", history, StringComparison.Ordinal);
        var historyTextBoxes = Regex.Matches(history, @"<TextBox\b[\s\S]*?/>");
        Assert.Equal(2, historyTextBoxes.Count);
        foreach (Match textBox in historyTextBoxes)
        {
            Assert.Contains("IsReadOnly=\"True\"", textBox.Value, StringComparison.Ordinal);
            Assert.Contains("Mode=OneWay", textBox.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("CheckedQty", textBox.Value, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("TextBox Text=\"{Binding History.DetailItems", history, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSourceTrigger=", history, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectionHistoryEditUseCase", history, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiryStageCalculator", history, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectionDraftUseCase", history, StringComparison.Ordinal);
        Assert.DoesNotContain("保存修改", history, StringComparison.Ordinal);
        Assert.DoesNotContain("回滚", history, StringComparison.Ordinal);

        var viewModel = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "InspectionHistoryViewModel.cs"));
        Assert.DoesNotContain("InspectionHistoryEditUseCase", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiryStageCalculator", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectionDraftUseCase", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreDbContext", viewModel, StringComparison.Ordinal);

        var shell = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "Stage4ViewModels.cs"));
        Assert.Contains("new InspectionHistoryQuery().List(context)", shell, StringComparison.Ordinal);
        Assert.Contains("new InspectionHistoryQuery().GetDetail(context, inspectionId)", shell, StringComparison.Ordinal);
        Assert.Contains("new InspectionHistoryQuery().GetItemRevisions(context, inspectionId, inspectionItemId)", shell, StringComparison.Ordinal);
        Assert.Contains("WaitForStableSaveAsync", shell, StringComparison.Ordinal);
        Assert.Contains("if (page == ShellPage.History && CurrentPage == ShellPage.History)", shell, StringComparison.Ordinal);
    }

    private static InspectionHistoryViewModel CreateVm(
        Func<IReadOnlyList<InspectionHistoryListItem>>? list = null,
        Func<long, InspectionHistoryDetailResult>? detail = null,
        Func<long, long, InspectionItemRevisionHistoryResult>? revisions = null) => new(
        list ?? (() => Array.Empty<InspectionHistoryListItem>()),
        detail ?? (_ => new InspectionHistoryDetailResult(1, "not_found", null)),
        revisions ?? ((_, _) => new InspectionItemRevisionHistoryResult(1, 1, "not_found", null)));

    private static InspectionHistoryListItem ListItem(long id, string code) => new(
        id,
        id + 100,
        id + 200,
        code,
        $"商品-{code}",
        $"BAR-{code}",
        Utc(id),
        2);

    private static InspectionHistoryDetail Detail(
        long id,
        string code,
        IReadOnlyList<InspectionHistoryItemDetail> items) => new(
        id,
        id + 100,
        id + 200,
        code,
        $"商品-{code}",
        $"BAR-{code}",
        "withdraw",
        7,
        "检查员",
        new DateOnly(2026, 8, 30),
        Utc(id),
        items);

    private static InspectionTaskDetailResult OpenTaskResult(long taskId) => new(
        taskId,
        "open",
        7,
        null,
        null,
        new InspectionTaskDetail(
            taskId,
            7,
            "测试商品",
            "SKU-007",
            "690000000007",
            5,
            "expired",
            new[]
            {
                new InspectionTaskItemResult(
                    101,
                    201,
                    null,
                    new DateOnly(2026, 9, 1),
                    "expired",
                    3,
                    0,
                    false,
                    null,
                    null)
            },
            Array.Empty<InspectionNormalBatchResult>(),
            null));

    private static InspectionTaskDetailResult CompletedTaskResult(long taskId) => new(
        taskId,
        "completed",
        7,
        null,
        null,
        null);

    private static InspectionHistoryItemDetail DetailItem(
        long id,
        long inspectionId,
        int checkedQty,
        DateOnly? productionDate = null,
        string stage = "discount_20",
        int arrivalQty = 10) => new(
        id,
        inspectionId,
        inspectionId + 200,
        id + 1000,
        productionDate,
        new DateOnly(2026, 9, 1),
        stage,
        arrivalQty,
        checkedQty,
        Utc(10));

    private static InspectionItemRevisionHistoryResult RevisionResult(long inspectionId, long itemId, int newCheckedQty) => new(
        inspectionId,
        itemId,
        "found",
        new InspectionItemRevisionHistory(
            inspectionId,
            itemId,
            newCheckedQty,
            Utc(12),
            new[] { new InspectionItemRevisionDetail(1, itemId, 0, newCheckedQty, Utc(12)) }));

    private static DateTime Utc(long hour) => new(2026, 8, 30, (int)(hour % 24), 0, 0, DateTimeKind.Utc);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(2);
        }

        Assert.True(condition());
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
