using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S4T07InspectionDetailViewModelTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 28);
    private static readonly DateTime UtcNow = new(2026, 8, 28, 8, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("completed", InspectionDetailPageState.Completed, "该任务已完成排查")]
    [InlineData("system_closed", InspectionDetailPageState.SystemClosed, "该任务已由系统结束")]
    [InlineData("not_found", InspectionDetailPageState.NotFound, "任务不存在或已无法访问")]
    public async Task GetDetailStatesMapWithoutGuessingFromExceptions(
        string status,
        InspectionDetailPageState expectedState,
        string expectedMessage)
    {
        var vm = CreateVm(_ => new InspectionTaskDetailResult(42, status, 7, null, null, null));

        await vm.LoadAsync(42);

        Assert.Equal(expectedState, vm.State);
        Assert.Equal(expectedMessage, vm.StatusMessage);
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task QueryExceptionIsErrorAndRetryCallsGetDetailAgain()
    {
        var attempts = 0;
        var vm = CreateVm(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("private database details");
            }

            return OpenResult(42, checkedQty: null);
        });

        await vm.LoadAsync(42);
        Assert.Equal(InspectionDetailPageState.Error, vm.State);
        Assert.Equal("排查详情加载失败", vm.ErrorMessage);
        Assert.True(vm.RetryLoadCommand.CanExecute(null));

        await vm.LoadAsync(42);

        Assert.Equal(InspectionDetailPageState.Open, vm.State);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task OpenDetailRestoresInspectorDateAndNullZeroPositiveQuantities()
    {
        var vm = CreateVm(_ => OpenResult(
            42,
            checkedQty: null,
            draft: new InspectionDraftResult(
                9,
                "  张三 ",
                BusinessDate,
                new[]
                {
                    new InspectionDraftItemResult(101, null),
                    new InspectionDraftItemResult(102, 0),
                    new InspectionDraftItemResult(103, 4)
                },
                false),
            quantities: new int?[] { null, 0, 4 }));

        await vm.LoadAsync(42);

        Assert.True(vm.IsOpen);
        Assert.Equal("张三", vm.InspectorName);
        Assert.Equal(BusinessDate, vm.CheckDate);
        Assert.Equal(new[] { "", "0", "4" }, vm.TaskItems.Select(item => item.CheckedQtyText));
        Assert.Equal(new[] { (int?)null, 0, 4 }, vm.TaskItems.Select(item => item.CheckedQty));
        Assert.True(vm.HasRecoveredDraft);
        Assert.Equal("已恢复未完成草稿", vm.DraftStatusText);
    }

    [Fact]
    public async Task OneDirtyItemSendsPatchWithCurrentCompleteTopSnapshot()
    {
        SaveDraftRequest? request = null;
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null, quantities: new int?[] { null, 3 }),
            saveDraft: value =>
            {
                request = value;
                return new SaveDraftResult(false, 9, new InspectionDraftReadiness(2, 1, 1, 0, true, true, false, false));
            });

        await vm.LoadAsync(42);
        vm.InspectorName = "李四";
        vm.CheckDateValue = BusinessDate.ToDateTime(TimeOnly.MinValue);
        vm.TaskItems[0].CheckedQtyText = "0";
        await vm.WaitForStableSaveAsync();

        Assert.NotNull(request);
        Assert.Equal(42, request!.TaskId);
        Assert.Equal(7, request.ProductId);
        Assert.Equal(BusinessDate, request.BusinessDate);
        Assert.Equal(UtcNow, request.SavedAtUtc);
        Assert.Equal("李四", request.InspectorName);
        Assert.Equal(BusinessDate, request.CheckDate);
        var item = Assert.Single(request.Items!);
        Assert.Equal(101, item.TaskItemId);
        Assert.Equal(201, item.BatchId);
        Assert.Equal(0, item.CheckedQty);
        Assert.Equal(0, item.AttentionVersion);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task FirstSaveCreatesClearableDraftWithoutOverwritingInput()
    {
        var clearCount = 0;
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            saveDraft: _ => new SaveDraftResult(
                true,
                9,
                new InspectionDraftReadiness(1, 1, 0, 0, false, true, true, false)),
            clearDraft: _ =>
            {
                clearCount++;
                return new ClearDraftResult(true);
            },
            confirmClearDraft: () => true);

        await vm.LoadAsync(42);
        vm.TaskItems[0].CheckedQtyText = "0";
        Assert.True(await vm.WaitForStableSaveAsync());

        Assert.True(vm.HasRecoveredDraft);
        Assert.True(vm.ClearDraftCommand.CanExecute(null));
        Assert.Equal("0", vm.TaskItems[0].CheckedQtyText);
        Assert.Equal(0, clearCount);
    }

    [Fact]
    public async Task RapidEditsSerializeSavesAndSecondRequestUsesLatestSnapshot()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<SaveDraftRequest>();
        var active = 0;
        var maximumActive = 0;
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            saveDraft: request =>
            {
                lock (requests)
                {
                    requests.Add(request);
                    active++;
                    maximumActive = Math.Max(maximumActive, active);
                }

                started.TrySetResult();
                if (requests.Count == 1)
                {
                    release.Task.GetAwaiter().GetResult();
                }

                lock (requests)
                {
                    active--;
                }

                return new SaveDraftResult(true, 9, new InspectionDraftReadiness(1, 1, 0, 0, true, true, true, true));
            });

        await vm.LoadAsync(42);
        vm.InspectorName = "第一次";
        vm.CheckDateValue = BusinessDate.ToDateTime(TimeOnly.MinValue);
        vm.TaskItems[0].CheckedQtyText = "1";
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.InspectorName = "最新检查人";
        vm.TaskItems[0].CheckedQtyText = "2";
        release.SetResult();

        Assert.True(await vm.WaitForStableSaveAsync());
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, maximumActive);
        Assert.Equal("第一次", requests[0].InspectorName);
        Assert.Equal(1, requests[0].Items!.Single().CheckedQty);
        Assert.Equal("最新检查人", requests[1].InspectorName);
        Assert.Equal(2, requests[1].Items!.Single().CheckedQty);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task InvalidQuantityIsNotSilentlyConvertedToZero()
    {
        var saveCount = 0;
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            saveDraft: _ =>
            {
                saveCount++;
                return new SaveDraftResult(true, 9, new InspectionDraftReadiness(1, 1, 0, 0, false, false, true, false));
            });

        await vm.LoadAsync(42);
        vm.TaskItems[0].CheckedQtyText = "not-a-number";
        await Task.Delay(700);

        Assert.Equal("not-a-number", vm.TaskItems[0].CheckedQtyText);
        Assert.Null(vm.TaskItems[0].CheckedQty);
        Assert.True(vm.TaskItems[0].HasInputError);
        Assert.Equal(0, saveCount);
    }

    [Fact]
    public async Task SaveFailureReloadsCurrentFactAndKeepsLocalInputAndDirtyState()
    {
        var queryCount = 0;
        var vm = CreateVm(
            _ =>
            {
                queryCount++;
                return OpenResult(42, checkedQty: null);
            },
            saveDraft: _ => throw new IOException("temporary save failure"));

        await vm.LoadAsync(42);
        vm.TaskItems[0].CheckedQtyText = "5";
        Assert.False(await vm.WaitForStableSaveAsync());

        Assert.True(queryCount >= 2);
        Assert.True(vm.SaveFailed);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal("5", vm.TaskItems[0].CheckedQtyText);
        Assert.Equal("保存失败，请重试", vm.SaveStatusText);
    }

    [Fact]
    public async Task RetryBuildsARequestFromCurrentLatestInput()
    {
        var attempts = 0;
        var requests = new List<SaveDraftRequest>();
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            saveDraft: request =>
            {
                requests.Add(request);
                attempts++;
                if (attempts == 1)
                {
                    throw new IOException("temporary save failure");
                }

                return new SaveDraftResult(true, 9, new InspectionDraftReadiness(1, 1, 0, 0, true, true, true, true));
            });

        await vm.LoadAsync(42);
        vm.TaskItems[0].CheckedQtyText = "1";
        Assert.False(await vm.WaitForStableSaveAsync());
        vm.TaskItems[0].CheckedQtyText = "8";
        await vm.RetrySaveAsync();

        Assert.Equal(2, requests.Count);
        Assert.Equal(8, requests[1].Items!.Single().CheckedQty);
        Assert.False(vm.SaveFailed);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task ReconfirmKeepsFlagDuringEditAndReloadsAfterSuccess()
    {
        var reconfirmCount = 0;
        var queryCount = 0;
        var current = OpenResult(42, checkedQty: 3, requiresReconfirmation: true, quantities: new int?[] { 3 });
        var vm = CreateVm(
            _ =>
            {
                queryCount++;
                return current;
            },
            reconfirmItem: request =>
            {
                reconfirmCount++;
                Assert.Equal(42, request.TaskId);
                Assert.Equal(7, request.ProductId);
                Assert.Equal(101, request.TaskItemId);
                Assert.Equal(201, request.BatchId);
                Assert.Equal(0, request.AttentionVersion);
                current = OpenResult(42, checkedQty: 3, requiresReconfirmation: false, quantities: new int?[] { 3 });
                return new ReconfirmItemResult(true, 9, new InspectionDraftReadiness(1, 1, 0, 0, false, false, true, false));
            });

        await vm.LoadAsync(42);
        var row = Assert.Single(vm.TaskItems);
        row.CheckedQtyText = "4";
        Assert.True(row.RequiresReconfirmation);
        Assert.True(row.ReconfirmCommand.CanExecute(null));

        await vm.ReconfirmItemAsync(row);

        Assert.Equal(1, reconfirmCount);
        Assert.Equal(2, queryCount);
        Assert.False(vm.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal("3", vm.TaskItems.Single().CheckedQtyText);
    }

    [Fact]
    public async Task ReconfirmFailureReloadsWithoutLocallyClearingInput()
    {
        var queryCount = 0;
        var vm = CreateVm(
            _ =>
            {
                queryCount++;
                return OpenResult(42, checkedQty: 3, requiresReconfirmation: true, quantities: new int?[] { 3 });
            },
            reconfirmItem: _ => throw new InvalidOperationException("stale confirmation"));

        await vm.LoadAsync(42);
        var row = Assert.Single(vm.TaskItems);
        row.CheckedQtyText = "4";
        await vm.ReconfirmItemAsync(row);

        Assert.True(queryCount >= 2);
        Assert.Equal("4", vm.TaskItems.Single().CheckedQtyText);
        Assert.True(vm.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal("重新确认失败，请重试", vm.ActionErrorMessage);
    }

    [Fact]
    public async Task ClearDraftRequiresConfirmationAndReloadsAfterSuccess()
    {
        var clearCount = 0;
        var queryCount = 0;
        var current = OpenResult(
            42,
            checkedQty: 2,
            draft: new InspectionDraftResult(9, "张三", BusinessDate, new[] { new InspectionDraftItemResult(101, 2) }, false),
            quantities: new int?[] { 2 });
        var vm = CreateVm(
            _ =>
            {
                queryCount++;
                return current;
            },
            clearDraft: _ =>
            {
                clearCount++;
                current = OpenResult(42, checkedQty: null);
                return new ClearDraftResult(true);
            },
            confirmClearDraft: () => true);

        await vm.LoadAsync(42);
        await vm.ClearDraftAsync();

        Assert.Equal(1, clearCount);
        Assert.Equal(2, queryCount);
        Assert.False(vm.HasRecoveredDraft);
        Assert.Equal("草稿已清空", vm.FeedbackMessage);
    }

    [Fact]
    public async Task ClearDraftCancellationDoesNotCallApplication()
    {
        var clearCount = 0;
        var vm = CreateVm(
            _ => OpenResult(
                42,
                checkedQty: 2,
                draft: new InspectionDraftResult(9, "张三", BusinessDate, new[] { new InspectionDraftItemResult(101, 2) }, false),
                quantities: new int?[] { 2 }),
            clearDraft: _ =>
            {
                clearCount++;
                return new ClearDraftResult(true);
            },
            confirmClearDraft: () => false);

        await vm.LoadAsync(42);
        await vm.ClearDraftAsync();

        Assert.Equal(0, clearCount);
        Assert.True(vm.HasRecoveredDraft);
    }

    [Fact]
    public async Task InventoryZeroNeedsSecondConfirmationAndRefreshesAfterTermination()
    {
        var adjustCount = 0;
        var dashboardRefreshes = 0;
        var taskRefreshes = 0;
        var confirmed = false;
        var current = OpenResult(42, checkedQty: null);
        var vm = CreateVm(
            _ => current,
            adjustInventory: request =>
            {
                adjustCount++;
                Assert.Equal(0, request.CorrectedStockQty);
                Assert.True(request.ConfirmProductTermination);
                current = new InspectionTaskDetailResult(42, "system_closed", 7, UtcNow, "product_stock_zero", null);
                return new ManualInventoryAdjustmentResult(true, 13, 5, 0, true);
            },
            refreshDashboard: () =>
            {
                dashboardRefreshes++;
                return Task.CompletedTask;
            },
            refreshPendingTasks: () =>
            {
                taskRefreshes++;
                return Task.CompletedTask;
            },
            confirmZeroInventory: () => confirmed);

        await vm.LoadAsync(42);
        vm.OpenInventoryCommand.Execute(null);
        vm.InventoryText = "0";
        await vm.AdjustInventoryAsync();
        Assert.Equal(0, adjustCount);

        confirmed = true;
        await vm.AdjustInventoryAsync();

        Assert.Equal(1, adjustCount);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, taskRefreshes);
        Assert.Equal(InspectionDetailPageState.SystemClosed, vm.State);
        Assert.False(vm.CanEdit);
    }

    [Fact]
    public async Task PositiveNoChangeShowsFeedbackAndRereadsDetail()
    {
        var adjustCount = 0;
        var queryCount = 0;
        var vm = CreateVm(
            _ =>
            {
                queryCount++;
                return OpenResult(42, checkedQty: null);
            },
            adjustInventory: request =>
            {
                adjustCount++;
                Assert.Equal(5, request.CorrectedStockQty);
                return new ManualInventoryAdjustmentResult(false, null, 5, 5, false);
            });

        await vm.LoadAsync(42);
        vm.OpenInventoryCommand.Execute(null);
        vm.InventoryText = "5";
        await vm.AdjustInventoryAsync();

        Assert.Equal(1, adjustCount);
        Assert.Equal(2, queryCount);
        Assert.Equal("库存未变化", vm.InventoryFeedback);
        Assert.True(vm.IsOpen);
    }

    [Fact]
    public async Task ListEntryCarriesTaskIdAndPendingSourceReturnsWithItsFilters()
    {
        var requestedTaskId = 0L;
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            taskLoader: request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 75, request.Page, request.PageSize),
            detailLoader: taskId =>
            {
                requestedTaskId = taskId;
                return OpenResult(taskId, checkedQty: null);
            });

        shell.NavigateTo(ShellPage.PendingTasks);
        await shell.PendingTasks.LoadAsync();
        shell.PendingTasks.SearchText = "SKU";
        shell.PendingTasks.SelectedStage = "expired";
        await shell.PendingTasks.SearchAsync();
        await shell.PendingTasks.GoToNextPageAsync();
        var sourcePage = shell.PendingTasks.CurrentPage;
        var sourceSearch = shell.PendingTasks.SearchText;
        var sourceStage = shell.PendingTasks.SelectedStage;
        var listItem = new InspectionTaskListItem(1234, 7, "商品", "SKU", "条码", "expired", 1, 5, BusinessDate, false);

        shell.OpenDetailCommand.Execute(listItem);
        await WaitUntil(() => shell.Detail.State != InspectionDetailPageState.Loading);
        Assert.Equal(1234, requestedTaskId);
        Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);

        shell.Detail.BackCommand.Execute(null);
        await WaitUntil(() => shell.CurrentPage == ShellPage.PendingTasks);
        Assert.Equal(sourcePage, shell.PendingTasks.CurrentPage);
        Assert.Equal(sourceSearch, shell.PendingTasks.SearchText);
        Assert.Equal(sourceStage, shell.PendingTasks.SelectedStage);
    }

    [Fact]
    public async Task DashboardEntryCarriesTaskIdAndReturnsToDashboard()
    {
        var requestedTaskId = 0L;
        var item = new InspectionTaskListItem(
            4321,
            7,
            "商品",
            "SKU",
            "条码",
            "expired",
            1,
            5,
            BusinessDate,
            false);
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(1, 1, 0, 0, 0, new[] { item }),
            taskLoader: _ => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, 1, 50),
            detailLoader: taskId =>
            {
                requestedTaskId = taskId;
                return OpenResult(taskId, checkedQty: null);
            });

        await shell.Dashboard.LoadAsync();
        shell.OpenDetailCommand.Execute(item);
        await WaitUntil(() => shell.Detail.State != InspectionDetailPageState.Loading);

        Assert.Equal(item.TaskId, requestedTaskId);
        Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);

        shell.Detail.BackCommand.Execute(null);
        await WaitUntil(() => shell.CurrentPage == ShellPage.Dashboard);
    }

    private static InspectionDetailViewModel CreateVm(
        Func<long, InspectionTaskDetailResult> loadDetail,
        Func<SaveDraftRequest, SaveDraftResult>? saveDraft = null,
        Func<ReconfirmItemRequest, ReconfirmItemResult>? reconfirmItem = null,
        Func<ClearDraftRequest, ClearDraftResult>? clearDraft = null,
        Func<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult>? adjustInventory = null,
        Func<Task>? refreshDashboard = null,
        Func<Task>? refreshPendingTasks = null,
        Func<bool>? confirmClearDraft = null,
        Func<bool>? confirmZeroInventory = null) => new(
        loadDetail,
        saveDraft ?? (_ => new SaveDraftResult(false, 9, new InspectionDraftReadiness(1, 0, 1, 0, false, false, false, false))),
        reconfirmItem ?? (_ => new ReconfirmItemResult(false, 9, new InspectionDraftReadiness(1, 0, 1, 0, false, false, false, false))),
        clearDraft ?? (_ => new ClearDraftResult(false)),
        adjustInventory ?? (_ => new ManualInventoryAdjustmentResult(false, null, 5, 5, false)),
        refreshDashboard,
        refreshPendingTasks,
        utcNow: () => UtcNow,
        businessDate: () => BusinessDate,
        confirmClearDraft: confirmClearDraft,
        confirmZeroInventory: confirmZeroInventory);

    private static InspectionTaskDetailResult OpenResult(
        long taskId,
        int? checkedQty,
        bool requiresReconfirmation = false,
        InspectionDraftResult? draft = null,
        IReadOnlyList<int?>? quantities = null)
    {
        var values = quantities ?? new int?[] { checkedQty };
        var items = values.Select((value, index) => new InspectionTaskItemResult(
            101 + index,
            201 + index,
            index == 0 ? null : new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1 + index),
            index == 0 ? "expired" : "none",
            3 + index,
            0,
            requiresReconfirmation,
            value,
            null)).ToArray();
        var detail = new InspectionTaskDetail(
            taskId,
            7,
            "测试商品",
            "SKU-007",
            "690000000007",
            5,
            "expired",
            items,
            Array.Empty<InspectionNormalBatchResult>(),
            draft);
        return new(taskId, "open", 7, null, null, detail);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(1);
        }

        Assert.True(condition());
    }
}
