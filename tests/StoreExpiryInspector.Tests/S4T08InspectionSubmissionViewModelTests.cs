using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S4T08InspectionSubmissionViewModelTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 28);
    private static readonly DateTime UtcNow = new(2026, 8, 28, 8, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("completed", InspectionDetailPageState.Completed)]
    [InlineData("system_closed", InspectionDetailPageState.SystemClosed)]
    [InlineData("not_found", InspectionDetailPageState.NotFound)]
    public async Task OnlyOpenDetailsExposeSubmission(string status, InspectionDetailPageState expectedState)
    {
        var vm = CreateVm(_ => new InspectionTaskDetailResult(42, status, 7, null, null, null));

        await vm.LoadAsync(42);

        Assert.Equal(expectedState, vm.State);
        Assert.False(vm.SubmitCommand.CanExecute(null));
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public async Task DetailLoadErrorDoesNotExposeSubmission()
    {
        var vm = CreateVm(_ => throw new IOException("query unavailable"));

        await vm.LoadAsync(42);

        Assert.Equal(InspectionDetailPageState.Error, vm.State);
        Assert.False(vm.SubmitCommand.CanExecute(null));
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public async Task SubmitWaitsForTheSerializedSaveAndUsesTheLatestFullSnapshot()
    {
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveRequests = new List<SaveDraftRequest>();
        var submissionRequests = new List<InspectionSubmissionRequest>();
        var vm = CreateVm(
            _ => saveRequests.Count == 0
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            saveDraft: request =>
            {
                lock (saveRequests)
                {
                    saveRequests.Add(request);
                }

                saveStarted.TrySetResult();
                if (saveRequests.Count == 1)
                {
                    releaseSave.Task.GetAwaiter().GetResult();
                }

                return SavedResult();
            },
            submit: request =>
            {
                submissionRequests.Add(request);
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    901,
                    10,
                    2);
            });

        await vm.LoadAsync(42);
        Assert.True(vm.SubmitCommand.CanExecute(null));
        vm.InspectorName = "第一次";
        vm.TaskItems.Single().CheckedQtyText = "1";
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        vm.InspectorName = "最新检查人";
        vm.TaskItems.Single().CheckedQtyText = "2";
        var submitTask = vm.SubmitAsync();
        await Task.Delay(50);

        Assert.Empty(submissionRequests);
        Assert.True(vm.IsActionBusy);
        Assert.False(vm.CanEdit);

        releaseSave.SetResult();
        await submitTask;

        Assert.Equal(2, saveRequests.Count);
        Assert.Equal("第一次", saveRequests[0].InspectorName);
        Assert.Equal(1, saveRequests[0].Items!.Single().CheckedQty);
        Assert.Equal("最新检查人", saveRequests[1].InspectorName);
        Assert.Equal(2, saveRequests[1].Items!.Single().CheckedQty);
        var submission = Assert.Single(submissionRequests);
        Assert.Equal(42, submission.TaskId);
        Assert.Equal(7, submission.ProductId);
        Assert.Equal(BusinessDate, submission.BusinessDate);
        Assert.Equal(UtcNow, submission.SubmittedAtUtc);
        Assert.Null(submission.ConfirmedEffectiveStockQty);
        Assert.Null(submission.ConfirmedTotalCheckedQty);
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
    }

    [Fact]
    public async Task SaveFailureBlocksSubmissionAndKeepsRetryGateAndInput()
    {
        var submissionCount = 0;
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            saveDraft: _ => throw new IOException("save unavailable"),
            submit: _ =>
            {
                submissionCount++;
                throw new InvalidOperationException("must not be called");
            });

        await vm.LoadAsync(42);
        vm.TaskItems.Single().CheckedQtyText = "5";
        Assert.False(await vm.WaitForStableSaveAsync());
        Assert.True(vm.SaveFailed);
        Assert.Equal("5", vm.TaskItems.Single().CheckedQtyText);

        await vm.SubmitAsync();

        Assert.Equal(0, submissionCount);
        Assert.Equal("草稿尚未保存成功，请先重试保存。", vm.ActionErrorMessage);
        Assert.True(vm.RetrySaveCommand.CanExecute(null));
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task DuplicateSubmitClicksOnlyEnterOneApplicationCallAndLockActions()
    {
        var submitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubmit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissionCount = 0;
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: _ =>
            {
                Interlocked.Increment(ref submissionCount);
                submitStarted.TrySetResult();
                releaseSubmit.Task.GetAwaiter().GetResult();
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    902,
                    10,
                    1);
            });

        await vm.LoadAsync(42);
        var first = vm.SubmitAsync();
        await submitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsSubmitting);
        Assert.Equal("正在提交…", vm.SubmitButtonText);
        Assert.True(vm.IsActionBusy);
        Assert.False(vm.SubmitCommand.CanExecute(null));
        Assert.False(vm.OpenInventoryCommand.CanExecute(null));
        Assert.False(vm.CanEdit);

        await vm.SubmitAsync();
        Assert.Equal(1, submissionCount);

        releaseSubmit.SetResult();
        await first;

        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal(1, submissionCount);
        Assert.Equal("完成排查", vm.SubmitButtonText);
    }

    [Fact]
    public async Task SubmittedRereadsCompletedAndRefreshesBothOpenLists()
    {
        var loadCount = 0;
        var dashboardRefreshes = 0;
        var pendingRefreshes = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: 0)
                : CompletedResult(42),
            submit: _ => new InspectionSubmissionResult(
                InspectionSubmissionOutcome.Submitted,
                903,
                5,
                0),
            refreshDashboard: () =>
            {
                dashboardRefreshes++;
                return Task.CompletedTask;
            },
            refreshPendingTasks: () =>
            {
                pendingRefreshes++;
                return Task.CompletedTask;
            });

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("排查已完成", vm.StatusMessage);
        Assert.True(vm.HasSubmissionCompletedAt);
        Assert.False(vm.CanEdit);
        Assert.False(vm.CanSubmit);
        Assert.False(vm.HasRecoveredDraft);
        Assert.Empty(vm.TaskItems);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, pendingRefreshes);
    }

    [Fact]
    public async Task AlreadySubmittedIsIdempotentAndMapsCompleted()
    {
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: _ => new InspectionSubmissionResult(
                InspectionSubmissionOutcome.AlreadySubmitted,
                904,
                5,
                0));

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("该任务已完成排查", vm.StatusMessage);
        Assert.False(vm.HasSubmissionCompletedAt);
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public async Task CompletedWithoutInspectionIsShownAsIntegrityError()
    {
        var logs = new List<Exception>();
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: _ => throw new InvalidOperationException("completed task has no inspection"),
            logException: logs.Add);

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("任务状态异常，无法完成排查。", vm.StatusMessage);
        Assert.Equal("任务状态异常，无法完成排查。", vm.ActionErrorMessage);
        Assert.Single(logs);
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public async Task OverStockConfirmationUsesReturnedFactsAndReplacesThemWhenBackendChangesFacts()
    {
        var requests = new List<InspectionSubmissionRequest>();
        var results = new Queue<InspectionSubmissionResult>(new InspectionSubmissionResult[]
        {
            new(InspectionSubmissionOutcome.RequiresOverStockConfirmation, null, 10, 12),
            new(InspectionSubmissionOutcome.RequiresOverStockConfirmation, null, 11, 13),
            new(InspectionSubmissionOutcome.Submitted, 905, 11, 13)
        });
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: request =>
            {
                requests.Add(request);
                return results.Dequeue();
            });

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.True(vm.HasOverStockConfirmation);
        Assert.Equal(10, vm.OverStockEffectiveStockQty);
        Assert.Equal(12, vm.OverStockTotalCheckedQty);
        Assert.Equal(2, vm.OverStockExcessQty);
        Assert.Equal("排查件数超过当前库存", vm.OverStockMessage);
        Assert.False(vm.ShowSubmitFooter);
        Assert.False(vm.CanEdit);

        await vm.ConfirmOverStockAsync();

        Assert.Equal(10, requests[1].ConfirmedEffectiveStockQty);
        Assert.Equal(12, requests[1].ConfirmedTotalCheckedQty);
        Assert.True(vm.HasOverStockConfirmation);
        Assert.Equal(11, vm.OverStockEffectiveStockQty);
        Assert.Equal(13, vm.OverStockTotalCheckedQty);
        Assert.Equal(2, vm.OverStockExcessQty);
        Assert.Contains("库存或排查数量已变化，请重新确认", vm.OverStockMessage, StringComparison.Ordinal);

        await vm.ConfirmOverStockAsync();

        Assert.Equal(11, requests[2].ConfirmedEffectiveStockQty);
        Assert.Equal(13, requests[2].ConfirmedTotalCheckedQty);
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("排查已完成", vm.StatusMessage);
    }

    [Fact]
    public async Task ReturningOrEnteringInventoryCorrectionDiscardsOverStockFacts()
    {
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            submit: _ => new InspectionSubmissionResult(
                InspectionSubmissionOutcome.RequiresOverStockConfirmation,
                null,
                10,
                12));

        await vm.LoadAsync(42);
        await vm.SubmitAsync();
        Assert.True(vm.HasOverStockConfirmation);

        vm.ReturnFromOverStockCommand.Execute(null);
        Assert.False(vm.HasOverStockConfirmation);
        Assert.True(vm.CanEdit);

        await vm.SubmitAsync();
        Assert.True(vm.HasOverStockConfirmation);
        vm.OpenInventoryCommand.Execute(null);
        Assert.False(vm.HasOverStockConfirmation);
        Assert.True(vm.IsInventoryEditorVisible);
    }

    [Fact]
    public async Task EditingCheckedQuantityInvalidatesOverStockFactsImmediately()
    {
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            submit: _ => new InspectionSubmissionResult(
                InspectionSubmissionOutcome.RequiresOverStockConfirmation,
                null,
                10,
                12));

        await vm.LoadAsync(42);
        await vm.SubmitAsync();
        vm.TaskItems.Single().CheckedQtyText = "3";

        Assert.False(vm.HasOverStockConfirmation);
        Assert.True(vm.CanEdit);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Theory]
    [InlineData("system_closed", InspectionDetailPageState.SystemClosed)]
    [InlineData("not_found", InspectionDetailPageState.NotFound)]
    public async Task RejectedSubmissionRereadsConcurrentTerminalState(
        string status,
        InspectionDetailPageState expectedState)
    {
        var loadCount = 0;
        var dashboardRefreshes = 0;
        var pendingRefreshes = 0;
        var vm = CreateVm(
            _ =>
            {
                if (Interlocked.Increment(ref loadCount) == 1)
                {
                    return OpenResult(42, checkedQty: null);
                }

                return new InspectionTaskDetailResult(
                    42,
                    status,
                    7,
                    UtcNow,
                    status == "system_closed" ? "product_stock_zero" : null,
                    null);
            },
            submit: _ => throw new InvalidOperationException("state changed"),
            refreshDashboard: () =>
            {
                dashboardRefreshes++;
                return Task.CompletedTask;
            },
            refreshPendingTasks: () =>
            {
                pendingRefreshes++;
                return Task.CompletedTask;
            });

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(expectedState, vm.State);
        Assert.False(vm.CanSubmit);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, pendingRefreshes);
        if (status == "system_closed")
        {
            Assert.Equal("商品库存已归零，全部批次效期跟踪已结束", vm.CloseReasonText);
        }
    }

    [Fact]
    public async Task RejectedStaleSubmissionShowsGetDetailReconfirmationState()
    {
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: 3)
                : OpenResult(42, checkedQty: 3, requiresReconfirmation: true),
            submit: _ => throw new InvalidOperationException("stale attention version"));

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        var row = Assert.Single(vm.TaskItems);
        Assert.Equal(InspectionDetailPageState.Open, vm.State);
        Assert.True(row.RequiresReconfirmation);
        Assert.Equal("当前排查状态已变化，请重新确认", vm.ActionErrorMessage);
        Assert.True(row.ReconfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task SubmissionExceptionWithFailedRereadPreservesVisibleInputAndReportsRetry()
    {
        var loadCount = 0;
        var vm = CreateVm(
            _ =>
            {
                if (Interlocked.Increment(ref loadCount) == 1)
                {
                    return OpenResult(
                        42,
                        checkedQty: 4,
                        draft: new InspectionDraftResult(
                            9,
                            "张三",
                            BusinessDate,
                            new[] { new InspectionDraftItemResult(101, 4) },
                            false),
                        quantities: new int?[] { 4 });
                }

                throw new IOException("detail refresh unavailable");
            },
            submit: _ => throw new IOException("submit unavailable"));

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(InspectionDetailPageState.Error, vm.State);
        Assert.Equal("张三", vm.InspectorName);
        Assert.Equal("4", vm.TaskItems.Single().CheckedQtyText);
        Assert.Equal("排查提交失败，请重试。", vm.ActionErrorMessage);
        Assert.Equal("排查详情加载失败", vm.ErrorMessage);
    }

    [Fact]
    public async Task SuccessfulSubmissionStaysCompletedWhenListRefreshFails()
    {
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: _ => new InspectionSubmissionResult(
                InspectionSubmissionOutcome.Submitted,
                906,
                5,
                1),
            refreshDashboard: () => throw new IOException("dashboard unavailable"));

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("排查已完成", vm.StatusMessage);
        Assert.Equal("排查已完成，但首页或任务列表刷新失败", vm.ActionErrorMessage);
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public async Task ZeroQuantityRemainsAnInputUntilSuccessfulSubmissionAndNoLocalLifecycleFactAppears()
    {
        InspectionSubmissionRequest? request = null;
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: 0)
                : CompletedResult(42),
            submit: value =>
            {
                request = value;
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    907,
                    5,
                    0);
            });

        await vm.LoadAsync(42);
        Assert.Equal(0, vm.TaskItems.Single().CheckedQty);
        Assert.Equal(InspectionDetailPageState.Open, vm.State);
        await vm.SubmitAsync();

        Assert.NotNull(request);
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Empty(vm.TaskItems);
    }

    [Fact]
    public async Task ShellDoesNotNavigateAwayWhileSubmissionIsBusy()
    {
        var submitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubmit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            taskLoader: _ => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, 1, 50),
            detailLoader: _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: _ =>
            {
                submitStarted.TrySetResult();
                releaseSubmit.Task.GetAwaiter().GetResult();
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    908,
                    5,
                    1);
            });

        shell.OpenDetail(42);
        await WaitUntil(() => shell.Detail.State == InspectionDetailPageState.Open);
        var submitTask = shell.Detail.SubmitAsync();
        await submitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await shell.NavigateToAsync(ShellPage.PendingTasks);
        Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);

        releaseSubmit.SetResult();
        await submitTask;
        await shell.NavigateToAsync(ShellPage.PendingTasks);
        Assert.Equal(ShellPage.PendingTasks, shell.CurrentPage);
    }

    [Fact]
    public async Task CompletedAfterSubmissionExceptionIsVerifiedThroughIdempotentApplicationResult()
    {
        var loadCount = 0;
        var submitCount = 0;
        var dashboardRefreshes = 0;
        var pendingRefreshes = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: 1)
                : CompletedResult(42),
            submit: _ =>
            {
                if (Interlocked.Increment(ref submitCount) == 1)
                {
                    throw new IOException("response lost after concurrent completion");
                }

                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.AlreadySubmitted,
                    909,
                    5,
                    0);
            },
            refreshDashboard: () =>
            {
                dashboardRefreshes++;
                return Task.CompletedTask;
            },
            refreshPendingTasks: () =>
            {
                pendingRefreshes++;
                return Task.CompletedTask;
            });

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("该任务已完成排查", vm.StatusMessage);
        Assert.Equal(2, submitCount);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, pendingRefreshes);
        Assert.False(vm.HasActionError);
    }

    [Fact]
    public async Task SuccessfulSubmissionDoesNotReopenWhenDetailRereadIsStillOpen()
    {
        var loadCount = 0;
        var submitCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) == 1
                ? OpenResult(42, checkedQty: 1)
                : OpenResult(42, checkedQty: 1),
            submit: _ =>
            {
                submitCount++;
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    910,
                    5,
                    1);
            });

        await vm.LoadAsync(42);
        await vm.SubmitAsync();

        Assert.Equal(1, submitCount);
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
        Assert.Equal("排查已完成", vm.StatusMessage);
        Assert.Equal("排查已完成，但详情刷新状态异常", vm.ActionErrorMessage);
        Assert.Empty(vm.TaskItems);
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public async Task OverStockInventoryCorrectionDiscardsFactsAndAllowsFreshSubmission()
    {
        var requests = new List<InspectionSubmissionRequest>();
        var loadCount = 0;
        var vm = CreateVm(
            _ => Interlocked.Increment(ref loadCount) <= 2
                ? OpenResult(42, checkedQty: null)
                : CompletedResult(42),
            submit: request =>
            {
                requests.Add(request);
                return requests.Count == 1
                    ? new InspectionSubmissionResult(
                        InspectionSubmissionOutcome.RequiresOverStockConfirmation,
                        null,
                        10,
                        12)
                    : new InspectionSubmissionResult(
                        InspectionSubmissionOutcome.Submitted,
                        911,
                        15,
                        12);
            },
            adjustInventory: request =>
            {
                Assert.Equal(15, request.CorrectedStockQty);
                Assert.False(request.ConfirmProductTermination);
                return new ManualInventoryAdjustmentResult(true, 14, 10, 15, false);
            });

        await vm.LoadAsync(42);
        await vm.SubmitAsync();
        Assert.True(vm.HasOverStockConfirmation);

        vm.OpenInventoryCommand.Execute(null);
        vm.InventoryText = "15";
        await vm.AdjustInventoryAsync();

        Assert.False(vm.HasOverStockConfirmation);
        Assert.True(vm.IsOpen);
        await vm.SubmitAsync();

        Assert.Equal(2, requests.Count);
        Assert.Null(requests[1].ConfirmedEffectiveStockQty);
        Assert.Null(requests[1].ConfirmedTotalCheckedQty);
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
    }

    [Fact]
    public async Task RetrySaveSucceedsBeforeSubmissionIsAllowed()
    {
        var saveAttempts = 0;
        var submissionCount = 0;
        var vm = CreateVm(
            _ => OpenResult(42, checkedQty: null),
            saveDraft: _ =>
            {
                if (Interlocked.Increment(ref saveAttempts) == 1)
                {
                    throw new IOException("temporary save failure");
                }

                return SavedResult();
            },
            submit: _ =>
            {
                submissionCount++;
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    912,
                    5,
                    1);
            });

        await vm.LoadAsync(42);
        vm.TaskItems.Single().CheckedQtyText = "1";
        Assert.False(await vm.WaitForStableSaveAsync());
        Assert.True(vm.SaveFailed);

        await vm.RetrySaveAsync();
        Assert.False(vm.SaveFailed);
        Assert.False(vm.HasUnsavedChanges);

        await vm.SubmitAsync();
        Assert.Equal(1, submissionCount);
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
    }

    [Fact]
    public async Task SubmissionBusyDisablesEditingAndAllDetailActions()
    {
        var submitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubmit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVm(
            _ => OpenResult(
                42,
                checkedQty: 1,
                requiresReconfirmation: true,
                draft: new InspectionDraftResult(
                    9,
                    "检查员",
                    BusinessDate,
                    new[] { new InspectionDraftItemResult(101, 1) },
                    true),
                quantities: new int?[] { 1 }),
            submit: _ =>
            {
                submitStarted.TrySetResult();
                releaseSubmit.Task.GetAwaiter().GetResult();
                return new InspectionSubmissionResult(
                    InspectionSubmissionOutcome.Submitted,
                    913,
                    5,
                    1);
            });

        await vm.LoadAsync(42);
        var row = Assert.Single(vm.TaskItems);
        var inspectorBefore = vm.InspectorName;
        var quantityBefore = row.CheckedQtyText;
        var submitTask = vm.SubmitAsync();
        await submitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.IsActionBusy);
        Assert.False(vm.CanEdit);
        Assert.False(vm.SubmitCommand.CanExecute(null));
        Assert.False(vm.ClearDraftCommand.CanExecute(null));
        Assert.False(vm.OpenInventoryCommand.CanExecute(null));
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(row.ReconfirmCommand.CanExecute(null));

        vm.InspectorName = "不应写入";
        row.CheckedQtyText = "9";
        vm.InventoryText = "99";
        Assert.Equal(inspectorBefore, vm.InspectorName);
        Assert.Equal(quantityBefore, row.CheckedQtyText);
        Assert.Equal(string.Empty, vm.InventoryText);

        releaseSubmit.SetResult();
        await submitTask;
        Assert.Equal(InspectionDetailPageState.Completed, vm.State);
    }

    private static InspectionDetailViewModel CreateVm(
        Func<long, InspectionTaskDetailResult> loadDetail,
        Func<SaveDraftRequest, SaveDraftResult>? saveDraft = null,
        Func<ReconfirmItemRequest, ReconfirmItemResult>? reconfirmItem = null,
        Func<ClearDraftRequest, ClearDraftResult>? clearDraft = null,
        Func<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult>? adjustInventory = null,
        Func<Task>? refreshDashboard = null,
        Func<Task>? refreshPendingTasks = null,
        Action<Exception>? logException = null,
        Func<InspectionSubmissionRequest, InspectionSubmissionResult>? submit = null) => new(
        loadDetail: loadDetail,
        saveDraft: saveDraft ?? (_ => SavedResult()),
        reconfirmItem: reconfirmItem ?? (_ => new ReconfirmItemResult(false, 9, new InspectionDraftReadiness(1, 0, 1, 0, false, false, false, false))),
        clearDraft: clearDraft ?? (_ => new ClearDraftResult(false)),
        adjustInventory: adjustInventory ?? (_ => new ManualInventoryAdjustmentResult(false, null, 5, 5, false)),
        refreshDashboard: refreshDashboard,
        refreshPendingTasks: refreshPendingTasks,
        logException: logException,
        utcNow: () => UtcNow,
        businessDate: () => BusinessDate,
        submit: submit ?? (_ => throw new InvalidOperationException("submission should not be called")));

    private static SaveDraftResult SavedResult() => new(
        true,
        9,
        new InspectionDraftReadiness(1, 1, 0, 0, true, true, true, true));

    private static InspectionTaskDetailResult CompletedResult(long taskId) =>
        new(taskId, "completed", 7, null, null, null);

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
