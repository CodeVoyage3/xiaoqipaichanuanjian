using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;
using System.Xml.Linq;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S5T04InspectionHistoryEditViewModelTests
{
    private static readonly DateTime ChangedAtUtc =
        new(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CompletedDetailCanEnterEditAndCancelWithoutWriting()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var editCalls = 0;
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { item })),
            (_, _) => RevisionResult(42, 4201, 2),
            _ =>
            {
                Interlocked.Increment(ref editCalls);
                return new(42, 4201, "changed", 2, 9, 1, ChangedAtUtc);
            });

        await OpenAndSelectAsync(vm, record);

        vm.BeginEditCommand.Execute(null);
        Assert.True(vm.IsEditing);
        Assert.Equal("2", vm.EditCheckedQtyText);
        Assert.True(vm.CanCancelEdit);
        Assert.False(vm.CanChangeHistorySelection);

        vm.EditCheckedQtyText = "9";
        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("已取消修改，未写入", vm.EditFeedbackMessage);
        Assert.Equal(0, editCalls);
        Assert.Equal(2, vm.DetailItems.Single().CheckedQty);
    }

    [Fact]
    public async Task ChangedResultCallsBackendWithFixedRequestAndRefreshesFormalValueAndRevisions()
    {
        var record = ListItem(42, "HISTORY-42");
        var currentQty = 2;
        var detailCalls = 0;
        var revisionCalls = 0;
        InspectionHistoryEditRequest? requestSeenByBackend = null;
        var vm = CreateVm(
            record,
            _ =>
            {
                Interlocked.Increment(ref detailCalls);
                return new(42, "found", Detail(42, new[] { DetailItem(4201, 42, Volatile.Read(ref currentQty)) }));
            },
            (_, itemId) =>
            {
                Interlocked.Increment(ref revisionCalls);
                return RevisionResult(42, itemId, Volatile.Read(ref currentQty),
                    new[] { new InspectionItemRevisionDetail(1, itemId, 2, Volatile.Read(ref currentQty), ChangedAtUtc) });
            },
            request =>
            {
                requestSeenByBackend = request;
                Volatile.Write(ref currentQty, request.NewCheckedQty);
                return new(request.InspectionId, request.InspectionItemId, "changed", 2, request.NewCheckedQty, 1, request.ChangedAtUtc);
            },
            _ => true,
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "7";
        await vm.SaveEditAsync();

        Assert.NotNull(requestSeenByBackend);
        Assert.Equal(new InspectionHistoryEditRequest(42, 4201, 7, ChangedAtUtc), requestSeenByBackend);
        Assert.True(detailCalls >= 2);
        Assert.True(revisionCalls >= 2);
        Assert.Equal(7, vm.DetailItems.Single().CheckedQty);
        Assert.Equal(7, vm.Revisions.Single().NewCheckedQty);
        Assert.Equal("数量修改成功", vm.EditFeedbackMessage);
        Assert.False(vm.IsEditBusy);
        Assert.False(vm.HasEditError);
    }

    [Fact]
    public async Task NoChangeStillCallsBackendAndRereadsDatabaseAuthoritativeValueWithoutRevision()
    {
        var record = ListItem(42, "HISTORY-42");
        var currentQty = 2;
        var backendCalls = 0;
        var detailCalls = 0;
        var revisionCalls = 0;
        var vm = CreateVm(
            record,
            _ =>
            {
                Interlocked.Increment(ref detailCalls);
                return new(42, "found", Detail(42, new[] { DetailItem(4201, 42, Volatile.Read(ref currentQty)) }));
            },
            (_, itemId) =>
            {
                Interlocked.Increment(ref revisionCalls);
                return RevisionResult(42, itemId, Volatile.Read(ref currentQty), Array.Empty<InspectionItemRevisionDetail>());
            },
            request =>
            {
                Interlocked.Increment(ref backendCalls);
                Volatile.Write(ref currentQty, request.NewCheckedQty);
                return new(request.InspectionId, request.InspectionItemId, "no_change", 5, 5, null, ChangedAtUtc);
            },
            _ => true,
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "5";
        await vm.SaveEditAsync();

        Assert.Equal(1, backendCalls);
        Assert.True(detailCalls >= 2);
        Assert.True(revisionCalls >= 2);
        Assert.Equal(5, vm.DetailItems.Single().CheckedQty);
        Assert.Empty(vm.Revisions);
        Assert.Equal("数量未变化", vm.EditFeedbackMessage);
        Assert.False(vm.HasEditError);
    }

    [Fact]
    public async Task NotFoundRefreshesToSafeStateAndDoesNotReportSuccess()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { item })),
            (_, _) => RevisionResult(42, 4201, 2),
            request => new(request.InspectionId, request.InspectionItemId, "not_found", null, null, null, null),
            _ => true,
            () => ChangedAtUtc,
            detailCalls: new InspectionHistoryDetailResult[]
            {
                new(42, "found", Detail(42, new[] { item })),
                new(42, "not_found", null)
            });

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "9";
        await vm.SaveEditAsync();

        Assert.True(vm.IsDetailNotFound);
        Assert.False(vm.HasDetail);
        Assert.Equal("正式排查批次不存在或已失效", vm.EditFeedbackMessage);
        Assert.DoesNotContain("成功", vm.EditFeedbackMessage, StringComparison.Ordinal);
        Assert.False(vm.HasEditError);
    }

    [Fact]
    public async Task ConfirmationCancelDoesNotCallBackendOrChangeFormalValue()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var confirmCalls = 0;
        var editCalls = 0;
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { item })),
            (_, _) => RevisionResult(42, 4201, 2),
            _ =>
            {
                Interlocked.Increment(ref editCalls);
                return new(42, 4201, "changed", 2, 9, 1, ChangedAtUtc);
            },
            _ =>
            {
                Interlocked.Increment(ref confirmCalls);
                return false;
            });

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "9";
        await vm.SaveEditAsync();

        Assert.Equal(1, confirmCalls);
        Assert.Equal(0, editCalls);
        Assert.Equal(2, vm.DetailItems.Single().CheckedQty);
        Assert.Empty(vm.Revisions);
        Assert.Equal("已取消修改，未写入", vm.EditFeedbackMessage);
        Assert.False(vm.IsEditSubmitting);
    }

    [Fact]
    public async Task NotFoundOrNonCompletedHistoryCannotEnterEdit()
    {
        var record = ListItem(42, "NOT-COMPLETED");
        var vm = CreateVm(
            record,
            _ => new(42, "not_found", null),
            (_, _) => new(42, 4201, "not_found", null),
            _ => throw new InvalidOperationException("must not be called"));

        await vm.LoadAsync();
        vm.OpenDetailCommand.Execute(record);
        await WaitUntil(() => vm.IsDetailNotFound || vm.HasDetailError || vm.HasDetail);

        Assert.True(vm.IsDetailNotFound);
        vm.BeginEditCommand.Execute(null);
        Assert.False(vm.IsEditing);
        Assert.False(vm.CanBeginEdit);
    }

    [Fact]
    public async Task InvalidInputBlocksConfirmationAndBackendAndDirectExecuteIsGuarded()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var confirmCalls = 0;
        var editCalls = 0;
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { item })),
            (_, _) => RevisionResult(42, 4201, 2),
            _ =>
            {
                Interlocked.Increment(ref editCalls);
                return new(42, 4201, "changed", 2, 9, 1, ChangedAtUtc);
            },
            _ =>
            {
                Interlocked.Increment(ref confirmCalls);
                return true;
            });

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        foreach (var invalid in new[] { "", " ", "-1", "1.5", "999999999999999999999" })
        {
            vm.EditCheckedQtyText = invalid;
            Assert.True(vm.HasEditInputError);
            Assert.False(vm.CanSaveEdit);
        }

        vm.SaveEditCommand.Execute(null);
        await Task.Delay(20);
        Assert.Equal(0, confirmCalls);
        Assert.Equal(0, editCalls);

        var selected = vm.SelectedDetailItem;
        vm.BackCommand.Execute(null);
        vm.RefreshCommand.Execute(null);
        vm.RetryCommand.Execute(null);
        vm.RetryDetailCommand.Execute(null);
        vm.RetryRevisionCommand.Execute(null);
        vm.OpenDetail(ListItem(43, "OTHER"));
        vm.SelectedRecord = ListItem(43, "OTHER");
        vm.SelectedDetailItem = DetailItem(4301, 43, 4);

        Assert.True(vm.IsEditing);
        Assert.Same(selected, vm.SelectedDetailItem);
        Assert.Equal(42, vm.SelectedRecord!.InspectionId);
        Assert.True(vm.IsDetailVisible);
    }

    [Fact]
    public async Task ConfirmationAndSubmissionKeepCapturedIdentityAndPreventReentry()
    {
        var firstRecord = ListItem(42, "FIRST");
        var secondRecord = ListItem(43, "SECOND");
        var firstItem = DetailItem(4201, 42, 2);
        var secondItem = DetailItem(4301, 43, 4);
        var confirmCalls = 0;
        var editCalls = 0;
        var confirmBusy = false;
        var backendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBackend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InspectionHistoryEditRequest? confirmedRequest = null;
        InspectionHistoryEditRequest? backendRequest = null;
        InspectionHistoryViewModel? vm = null;
        var currentQty = 2;
        vm = CreateVm(
            firstRecord,
            inspectionId => inspectionId == 42
                ? new(42, "found", Detail(42, new[] { DetailItem(4201, 42, Volatile.Read(ref currentQty)) }))
                : new(43, "found", Detail(43, new[] { secondItem })),
            (_, itemId) => RevisionResult(itemId == 4201 ? 42 : 43, itemId, itemId == 4201 ? Volatile.Read(ref currentQty) : 4),
            request =>
            {
                Interlocked.Increment(ref editCalls);
                backendRequest = request;
                backendStarted.TrySetResult();
                releaseBackend.Task.GetAwaiter().GetResult();
                Volatile.Write(ref currentQty, request.NewCheckedQty);
                return new(request.InspectionId, request.InspectionItemId, "changed", 2, request.NewCheckedQty, 1, request.ChangedAtUtc);
            },
            request =>
            {
                Interlocked.Increment(ref confirmCalls);
                confirmedRequest = request;
                confirmBusy = vm!.IsEditBusy;
                vm.SelectedRecord = secondRecord;
                vm.SelectedDetailItem = secondItem;
                vm.OpenDetail(secondRecord);
                vm.BackCommand.Execute(null);
                vm.RefreshCommand.Execute(null);
                vm.RetryDetailCommand.Execute(null);
                vm.RetryRevisionCommand.Execute(null);
                vm.SaveEditCommand.Execute(null);
                vm.EditCheckedQtyText = "99";
                return true;
            },
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, firstRecord);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "7";
        var firstSave = vm.SaveEditAsync();
        await backendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.True(confirmBusy);
            Assert.Equal(1, confirmCalls);
            Assert.True(vm.IsEditSubmitting);
            Assert.Equal(1, editCalls);
            Assert.Equal(42, vm.SelectedRecord!.InspectionId);
            Assert.Equal(4201, vm.SelectedDetailItem!.InspectionItemId);
            Assert.Equal("7", vm.EditCheckedQtyText);

            await vm.SaveEditAsync();
            Assert.Equal(1, editCalls);
        }
        finally
        {
            releaseBackend.TrySetResult();
            await firstSave;
        }

        var expected = new InspectionHistoryEditRequest(42, 4201, 7, ChangedAtUtc);
        Assert.Equal(expected, confirmedRequest);
        Assert.Equal(expected, backendRequest);
        Assert.False(vm.IsEditBusy);
        Assert.Equal(7, vm.DetailItems.Single().CheckedQty);
    }

    [Fact]
    public async Task FailedSaveKeepsFormalValueAndExistingRevisionsWithoutFakeNewValues()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var existingRevision = new InspectionItemRevisionDetail(1, 4201, 1, 2, ChangedAtUtc.AddHours(-1));
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { item })),
            (_, _) => RevisionResult(42, 4201, 2, new[] { existingRevision }),
            _ => throw new InvalidOperationException("persistence failure"),
            _ => true,
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "9";
        await vm.SaveEditAsync();

        Assert.True(vm.HasEditError);
        Assert.Contains("当前正式数量未更新", vm.EditErrorMessage, StringComparison.Ordinal);
        Assert.Equal(2, vm.DetailItems.Single().CheckedQty);
        Assert.Single(vm.Revisions);
        Assert.Equal(2, vm.Revisions.Single().NewCheckedQty);
        Assert.DoesNotContain(vm.Revisions, revision => revision.NewCheckedQty == 9);
        Assert.False(vm.IsEditSubmitting);
    }

    [Fact]
    public async Task ChangedSaveWithDetailRefreshFailureReportsSavedButNotCurrentValue()
    {
        var record = ListItem(42, "HISTORY-42");
        var detailCalls = 0;
        var editCalls = 0;
        var vm = CreateVm(
            record,
            _ => Interlocked.Increment(ref detailCalls) == 1
                ? new(42, "found", Detail(42, new[] { DetailItem(4201, 42, 2) }))
                : throw new IOException("detail refresh failure"),
            (_, _) => RevisionResult(42, 4201, 2),
            request =>
            {
                Interlocked.Increment(ref editCalls);
                return new(request.InspectionId, request.InspectionItemId, "changed", 2, 9, 1, request.ChangedAtUtc);
            },
            _ => true,
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "9";
        await vm.SaveEditAsync();

        Assert.Equal(1, editCalls);
        Assert.True(vm.HasEditFeedback);
        Assert.Equal("数量已保存，但详情或修改历史刷新失败，请重试", vm.EditFeedbackMessage);
        Assert.False(vm.HasEditError);
        Assert.True(vm.HasDetailError);
        Assert.Empty(vm.DetailItems);
        Assert.Empty(vm.Revisions);
    }

    [Fact]
    public async Task ChangedSaveWithRevisionRefreshFailureReportsSavedAndKeepsRefreshedFormalValue()
    {
        var record = ListItem(42, "HISTORY-42");
        var currentQty = 2;
        var revisionCalls = 0;
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { DetailItem(4201, 42, Volatile.Read(ref currentQty)) })),
            (_, _) =>
            {
                if (Interlocked.Increment(ref revisionCalls) == 2)
                {
                    throw new IOException("revision refresh failure");
                }

                return RevisionResult(42, 4201, Volatile.Read(ref currentQty));
            },
            request =>
            {
                Volatile.Write(ref currentQty, request.NewCheckedQty);
                return new(request.InspectionId, request.InspectionItemId, "changed", 2, request.NewCheckedQty, 1, request.ChangedAtUtc);
            },
            _ => true,
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, record);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "9";
        await vm.SaveEditAsync();

        Assert.True(revisionCalls >= 2);
        Assert.Equal("数量已保存，但详情或修改历史刷新失败，请重试", vm.EditFeedbackMessage);
        Assert.False(vm.HasEditError);
        Assert.Equal(9, vm.DetailItems.Single().CheckedQty);
        Assert.True(vm.HasRevisionError);
        Assert.Empty(vm.Revisions);
    }

    [Fact]
    public async Task OldFeedbackIsClearedWhenOpeningAnotherHistoryRecordOrReturning()
    {
        var first = ListItem(42, "FIRST");
        var second = ListItem(43, "SECOND");
        var currentQty = 2;
        var vm = CreateVm(
            first,
            inspectionId => inspectionId == 42
                ? new(42, "found", Detail(42, new[] { DetailItem(4201, 42, Volatile.Read(ref currentQty)) }))
                : new(43, "found", Detail(43, new[] { DetailItem(4301, 43, 4) })),
            (_, itemId) => RevisionResult(itemId == 4201 ? 42 : 43, itemId, itemId == 4201 ? 2 : 4),
            request =>
            {
                Volatile.Write(ref currentQty, request.NewCheckedQty);
                return new(request.InspectionId, request.InspectionItemId, "changed", 2, 9, 1, request.ChangedAtUtc);
            },
            _ => true,
            () => ChangedAtUtc);

        await OpenAndSelectAsync(vm, first);
        vm.BeginEdit();
        vm.EditCheckedQtyText = "9";
        await vm.SaveEditAsync();
        Assert.Equal("数量修改成功", vm.EditFeedbackMessage);

        vm.OpenDetailCommand.Execute(second);
        await WaitUntil(() => vm.HasDetail && vm.Detail!.InspectionId == 43);
        Assert.False(vm.HasEditFeedback);
        Assert.False(vm.HasEditError);

        vm.BackCommand.Execute(null);
        Assert.False(vm.HasEditFeedback);
        Assert.False(vm.HasEditError);
    }

    [Fact]
    public async Task FailedSaveDuringRevisionLoadLeavesRetryableErrorInsteadOfSpinner()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var revisionGate = new TaskCompletionSource<InspectionItemRevisionHistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var revisionCalls = 0;
        var vm = CreateVm(
            record,
            _ => new(42, "found", Detail(42, new[] { item })),
            (_, _) =>
            {
                if (Interlocked.Increment(ref revisionCalls) == 1)
                {
                    return revisionGate.Task.GetAwaiter().GetResult();
                }

                return RevisionResult(42, 4201, 2);
            },
            _ => throw new InvalidOperationException("transaction failure"),
            _ => true,
            () => ChangedAtUtc);

        try
        {
            await OpenAndSelectAsync(vm, record, waitForRevision: false);
            await WaitUntil(() => vm.IsRevisionLoading);
            vm.BeginEdit();
            vm.EditCheckedQtyText = "9";
            await vm.SaveEditAsync();

            Assert.False(vm.IsRevisionLoading);
            Assert.True(vm.HasRevisionError);
            Assert.Contains("当前正式数量未更新", vm.EditErrorMessage, StringComparison.Ordinal);
            Assert.Equal(2, vm.DetailItems.Single().CheckedQty);

            revisionGate.TrySetResult(RevisionResult(42, 4201, 2));
            vm.CancelEdit();
            vm.RetryRevisionCommand.Execute(null);
            await WaitUntil(() => vm.HasNoRevisions || vm.HasRevisionHistory);
            Assert.True(revisionCalls >= 2);
        }
        finally
        {
            revisionGate.TrySetResult(RevisionResult(42, 4201, 2));
        }
    }

    [Fact]
    public async Task ShellBlocksHistoryNavigationAndDetailOpenWhileEditing()
    {
        var record = ListItem(42, "HISTORY-42");
        var item = DetailItem(4201, 42, 2);
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            taskLoader: request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize),
            logException: _ => { },
            historyListLoader: () => new[] { record },
            historyDetailLoader: _ => new(42, "found", Detail(42, new[] { item })),
            historyRevisionLoader: (_, _) => RevisionResult(42, 4201, 2),
            historyEdit: _ => new(42, 4201, "no_change", 2, 2, null, ChangedAtUtc),
            confirmHistoryEdit: _ => true);

        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => shell.History.HasLoadedResult);
        shell.History.OpenDetailCommand.Execute(record);
        await WaitUntil(() => shell.History.HasDetail);
        shell.History.SelectedDetailItem = shell.History.DetailItems.Single();
        await WaitUntil(() => shell.History.HasNoRevisions || shell.History.HasRevisionHistory);
        shell.History.BeginEdit();

        Assert.False(shell.NavigateHomeCommand.CanExecute(null));
        Assert.False(shell.NavigateTasksCommand.CanExecute(null));
        Assert.False(shell.SearchTasksCommand.CanExecute(null));
        Assert.False(shell.NavigateHistoryCommand.CanExecute(null));
        Assert.False(shell.NavigateImportCommand.CanExecute(null));
        Assert.False(shell.OpenDetailCommand.CanExecute(new InspectionTaskListItem(1, 1, "TASK", "TASK", null, ExpiryStageCalculator.Discount50, 0, 0, null, false)));

        await shell.NavigateToAsync(ShellPage.PendingTasks);
        shell.OpenDetail(123);
        Assert.Equal(ShellPage.History, shell.CurrentPage);

        shell.History.CancelEditCommand.Execute(null);
        Assert.True(shell.NavigateHomeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShellCompositionUsesExistingEditUseCaseAgainstOnlyTemporarySqliteDatabase()
    {
        using var scenario = CreateScenario();
        var query = new InspectionHistoryQuery();
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>()),
            taskLoader: request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize),
            logException: _ => { },
            utcNow: () => ChangedAtUtc,
            historyListLoader: () =>
            {
                using var context = scenario.Database.Open();
                return query.List(context);
            },
            historyDetailLoader: inspectionId =>
            {
                using var context = scenario.Database.Open();
                return query.GetDetail(context, inspectionId);
            },
            historyRevisionLoader: (inspectionId, itemId) =>
            {
                using var context = scenario.Database.Open();
                return query.GetItemRevisions(context, inspectionId, itemId);
            },
            historyEdit: request =>
            {
                using var context = scenario.Database.Open();
                return new InspectionHistoryEditUseCase().Execute(context, request);
            },
            confirmHistoryEdit: _ => true);

        await shell.NavigateToAsync(ShellPage.History);
        await WaitUntil(() => shell.History.HasLoadedResult && shell.History.Items.Count == 1);
        var record = shell.History.Items.Single();
        shell.History.OpenDetailCommand.Execute(record);
        await WaitUntil(() => shell.History.HasDetail);
        shell.History.SelectedDetailItem = shell.History.DetailItems.Single();
        await WaitUntil(() => shell.History.HasNoRevisions);
        shell.History.BeginEdit();
        shell.History.EditCheckedQtyText = "6";
        await shell.History.SaveEditAsync();

        Assert.Equal(6, shell.History.DetailItems.Single().CheckedQty);
        var revision = Assert.Single(shell.History.Revisions);
        Assert.Equal(1, revision.PreviousCheckedQty);
        Assert.Equal(6, revision.NewCheckedQty);

        shell.History.BeginEdit();
        shell.History.EditCheckedQtyText = "6";
        await shell.History.SaveEditAsync();

        Assert.Equal("数量未变化", shell.History.EditFeedbackMessage);
        Assert.Equal(6, shell.History.DetailItems.Single().CheckedQty);
        Assert.Single(shell.History.Revisions);
        using var verify = scenario.Database.Open();
        Assert.Equal(6, verify.InspectionItems.Single().CheckedQty);
        Assert.Single(verify.InspectionItemRevisions);
    }

    [Fact]
    public void FeedbackIsOutsideEditorAndEditorMessagesWrapForNarrowWindows()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var start = window.IndexOf("<!-- 排查历史", StringComparison.Ordinal);
        var end = window.IndexOf("<!-- 排查详情", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var history = window[start..end];
        var editor = history.IndexOf("Visibility=\"{Binding History.IsEditing", StringComparison.Ordinal);
        Assert.True(editor > 0);
        Assert.InRange(history.IndexOf("History.EditFeedbackMessage", StringComparison.Ordinal), 0, editor - 1);
        Assert.InRange(history.IndexOf("History.EditErrorMessage", StringComparison.Ordinal), 0, editor - 1);
        var editorSection = history[editor..];
        Assert.Contains("正在保存…", history, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", editorSection, StringComparison.Ordinal);
        Assert.Contains("CanChangeHistorySelection", history, StringComparison.Ordinal);

        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xaml = System.Xml.Linq.XDocument.Parse(window);
        foreach (var binding in new[] { "History.EditFeedbackMessage", "History.EditErrorMessage" })
        {
            var textBlock = Assert.Single(xaml.Descendants(wpf + "TextBlock"), element =>
                ((string?)element.Attribute("Text"))?.Contains(binding, StringComparison.Ordinal) == true);
            foreach (var ancestor in textBlock.Ancestors())
            {
                var visibility = (string?)ancestor.Attribute("Visibility") ?? string.Empty;
                Assert.DoesNotContain("History.IsEditing", visibility, StringComparison.Ordinal);
                Assert.DoesNotContain("History.HasDetail", visibility, StringComparison.Ordinal);
            }
        }
    }

    private static InspectionHistoryViewModel CreateVm(
        InspectionHistoryListItem record,
        Func<long, InspectionHistoryDetailResult> detail,
        Func<long, long, InspectionItemRevisionHistoryResult> revisions,
        Func<InspectionHistoryEditRequest, InspectionHistoryEditResult> edit,
        Func<InspectionHistoryEditRequest, bool>? confirm = null,
        Func<DateTime>? utcNow = null,
        IReadOnlyList<InspectionHistoryDetailResult>? detailCalls = null)
    {
        var detailCallIndex = 0;
        return new InspectionHistoryViewModel(
            () => new[] { record },
            inspectionId => detailCalls is null
                ? detail(inspectionId)
                : detailCalls[Math.Min(Interlocked.Increment(ref detailCallIndex) - 1, detailCalls.Count - 1)],
            revisions,
            _ => { },
            edit,
            confirm ?? (_ => true),
            utcNow);
    }

    private static async Task OpenAndSelectAsync(
        InspectionHistoryViewModel vm,
        InspectionHistoryListItem record,
        bool waitForRevision = true)
    {
        await vm.LoadAsync();
        vm.OpenDetailCommand.Execute(record);
        await WaitUntil(() => vm.HasDetail || vm.HasDetailError || vm.IsDetailNotFound);
        Assert.True(vm.HasDetail);
        vm.SelectedDetailItem = vm.DetailItems.Single();
        if (waitForRevision)
        {
            await WaitUntil(() => vm.HasNoRevisions || vm.HasRevisionHistory || vm.HasRevisionError || vm.IsRevisionNotFound);
        }
    }

    private static InspectionHistoryListItem ListItem(long id, string code) => new(
        id,
        id + 100,
        id + 200,
        code,
        $"商品-{code}",
        $"BAR-{code}",
        ChangedAtUtc.AddDays(-1),
        1);

    private static InspectionHistoryDetail Detail(
        long id,
        IReadOnlyList<InspectionHistoryItemDetail> items) => new(
        id,
        id + 100,
        id + 200,
        $"HISTORY-{id}",
        $"商品-{id}",
        $"BAR-{id}",
        ExpiryStageCalculator.Discount50,
        20,
        "历史检查员",
        new DateOnly(2026, 8, 29),
        ChangedAtUtc.AddDays(-1),
        items);

    private static InspectionHistoryItemDetail DetailItem(
        long itemId,
        long inspectionId,
        int checkedQty) => new(
        itemId,
        inspectionId,
        inspectionId + 200,
        itemId + 1000,
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 9, 1),
        ExpiryStageCalculator.Discount50,
        10,
        checkedQty,
        ChangedAtUtc.AddDays(-1));

    private static InspectionItemRevisionHistoryResult RevisionResult(
        long inspectionId,
        long itemId,
        int currentQty,
        IReadOnlyList<InspectionItemRevisionDetail>? revisions = null) => new(
        inspectionId,
        itemId,
        "found",
        new InspectionItemRevisionHistory(
            inspectionId,
            itemId,
            currentQty,
            ChangedAtUtc,
            revisions ?? Array.Empty<InspectionItemRevisionDetail>()));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 2500 && !condition(); attempt++)
        {
            await Task.Delay(2);
        }

        Assert.True(condition());
    }

    private static Scenario CreateScenario()
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = new Product
        {
            ProductCode = "S5T04-HISTORY",
            CurrentName = "S5T04 history product",
            CurrentBarcode = "690000000504",
            ExcelStockQty = 20,
            EffectiveStockQty = 20,
            EffectiveStockSource = "excel",
            CreatedAtUtc = ChangedAtUtc.AddDays(-2),
            UpdatedAtUtc = ChangedAtUtc.AddDays(-2)
        };
        context.Products.Add(product);
        context.SaveChanges();

        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = "completed",
            HighestStage = ExpiryStageCalculator.Discount50,
            CreatedAtUtc = ChangedAtUtc.AddDays(-2),
            UpdatedAtUtc = ChangedAtUtc.AddDays(-1),
            ClosedAtUtc = ChangedAtUtc.AddDays(-1)
        };
        context.Tasks.Add(task);
        context.SaveChanges();

        var batch = new Batch
        {
            ProductId = product.Id,
            ProductionDate = new DateOnly(2026, 8, 1),
            ExpiryDate = new DateOnly(2026, 9, 1),
            ShelfLifeValue = 1,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            CurrentStage = ExpiryStageCalculator.Discount50,
            TrackingStatus = "active",
            CreatedAtUtc = ChangedAtUtc.AddDays(-2),
            UpdatedAtUtc = ChangedAtUtc.AddDays(-2)
        };
        context.Batches.Add(batch);
        context.SaveChanges();

        var inspection = new Inspection
        {
            TaskId = task.Id,
            ProductId = product.Id,
            ProductCodeSnapshot = product.ProductCode,
            ProductNameSnapshot = product.CurrentName,
            BarcodeSnapshot = product.CurrentBarcode,
            StageSnapshot = ExpiryStageCalculator.Discount50,
            StockQtySnapshot = 20,
            InspectorName = "S5T04 inspector",
            CheckDate = new DateOnly(2026, 8, 29),
            SubmittedAtUtc = ChangedAtUtc.AddDays(-1)
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();

        var item = new InspectionItem
        {
            InspectionId = inspection.Id,
            ProductId = product.Id,
            BatchId = batch.Id,
            ProductionDateSnapshot = batch.ProductionDate,
            ExpiryDateSnapshot = batch.ExpiryDate,
            StageSnapshot = batch.CurrentStage,
            ArrivalQtySnapshot = batch.CurrentArrivalQty,
            CheckedQty = 1,
            UpdatedAtUtc = ChangedAtUtc.AddDays(-1)
        };
        context.InspectionItems.Add(item);
        context.SaveChanges();
        return new(database, inspection.Id, item.Id);
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

    private sealed record Scenario(
        SqliteTestDatabase Database,
        long InspectionId,
        long InspectionItemId) : IDisposable
    {
        public void Dispose() => Database.Dispose();
    }
}
