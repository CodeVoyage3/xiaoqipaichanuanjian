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
        Assert.Equal("行错误", vm.PreviewRows[2].StatusText);
        Assert.Contains("可应用 1", vm.PreviewSummaryText);
    }

    [Fact]
    public async Task DraftGateOnlySubmitsCompleteTasksAndRefreshesAfterConfirmation()
    {
        var submissions = 0;
        var refreshes = 0;
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
                return submissions == 1
                    ? new(BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation, [], [new(1, 9, 5, 8)])
                    : new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 101)], []);
            },
            refresh: _ => { refreshes++; return Task.CompletedTask; },
            confirm: _ => true);

        await vm.PreviewAsync("C:\\filled.xlsx");
        vm.InspectorName = "检查员";
        vm.CheckDateText = "2026-09-02";
        await vm.SaveDraftAsync();
        await vm.SubmitAsync();
        await Task.Delay(20);

        Assert.Equal([1], vm.CompleteTaskIds);
        Assert.Contains("仍有未完成", vm.DraftStatusText);
        Assert.Equal(2, submissions);
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

    private static TodayInspectionViewModel Create(
        Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? export = null,
        Func<string, InspectionPlanPreview>? preview = null,
        Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult>? apply = null,
        Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult>? submit = null,
        Func<IReadOnlyCollection<long>, Task>? refresh = null,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirm = null) => new(
            loadTasks: () => new([
                new(1, 9, "商品 A", "A", "001", "expired", 2, 5, Today, false),
                new(2, 10, "商品 B", "B", "002", "withdraw", 1, 4, Today, true)
            ], 2, 1, 50),
            export: export ?? ((path, ids) => new(path, ids.Count, ids.Count)),
            preview: preview ?? (_ => Preview([1])),
            apply: apply ?? (_ => new(true, [new(1, 11, true, new(1, 1, 0, 0, true, true, true, true))])),
            submit: submit ?? (_ => new(BulkInspectionSubmissionOutcome.Submitted, [new(1, 101)], [])),
            refreshAfterSubmit: refresh ?? (_ => Task.CompletedTask),
            confirmOverStock: confirm,
            businessToday: () => Today);

    private static InspectionPlanPreview Preview(IReadOnlyList<long> applicable, IReadOnlyList<InspectionPlanRow>? rows = null, IReadOnlyDictionary<long, string>? reasons = null)
    {
        rows ??= [Row(1, 1)];
        reasons ??= new Dictionary<long, string>();
        return new(new(rows), new(1, rows.Select(row => row.TaskId).Distinct().Count(), rows.Count, rows.Count(row => row.CheckedQty is not null), rows.Count(row => row.CheckedQty is null), rows.Sum(row => row.Errors.Count)), rows.Select(row => new InspectionPlanTaskPreview(row.TaskId!.Value, applicable.Contains(row.TaskId.Value), reasons.TryGetValue(row.TaskId.Value, out var reason) ? reason : null)).ToArray(), applicable, reasons);
    }

    private static InspectionPlanRow Row(long taskId, int? checkedQty, IReadOnlyList<string>? errors = null) =>
        new(2, taskId, taskId, 9, taskId, 1, DateTime.UtcNow, 1, "active", "expired", 1, 1, 5, checkedQty, "A", "商品 A", "2026-09-01", errors ?? []);
}
