using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record InspectionPlanPreviewSummary(int ProductCount, int TaskCount, int BatchCount, int FilledCount, int BlankCount, int ErrorCount);
public sealed record InspectionPlanTaskPreview(long TaskId, bool IsApplicable, string? Reason);
public sealed record InspectionPlanPreview(InspectionPlanReadResult File, InspectionPlanPreviewSummary Summary, IReadOnlyList<InspectionPlanTaskPreview> Tasks, IReadOnlyList<long> ApplicableTaskIds, IReadOnlyDictionary<long, string> TaskReasons);
public sealed record ApplyInspectionPlanDraftRequest(InspectionPlanPreview Preview, IReadOnlyCollection<long> TaskIds, string InspectorName, DateOnly CheckDate, DateOnly BusinessDate, DateTime SavedAtUtc);
public sealed record AppliedInspectionPlanDraft(long TaskId, long DraftId, bool Changed, InspectionDraftReadiness Readiness);
public sealed record ApplyInspectionPlanDraftResult(bool Changed, IReadOnlyList<AppliedInspectionPlanDraft> Tasks);

public sealed class InspectionPlanDraftApplyUseCase
{
    private readonly InspectionPlanResultReader reader = new();
    private readonly InspectionDraftUseCase drafts = new();

    public InspectionPlanPreview Preview(StoreDbContext context, string path)
    {
        ArgumentNullException.ThrowIfNull(context);
        var file = reader.Read(path);
        var reasons = Validate(context, file.Rows);
        var tasks = reasons.OrderBy(pair => pair.Key).Select(pair => new InspectionPlanTaskPreview(pair.Key, pair.Value.Length == 0, pair.Value.Length == 0 ? null : pair.Value)).ToArray();
        var summary = new InspectionPlanPreviewSummary(file.Rows.Select(row => row.ProductId).Where(id => id is > 0).Distinct().Count(), tasks.Length, file.Rows.Select(row => row.BatchId).Where(id => id is > 0).Distinct().Count(), file.Rows.Count(row => row.CheckedQty is not null), file.Rows.Count(row => row.CheckedQty is null && row.Errors.All(error => !error.StartsWith("本次排查数量", StringComparison.Ordinal))), file.ErrorCount);
        return new(file, summary, tasks, tasks.Where(task => task.IsApplicable).Select(task => task.TaskId).ToArray(), reasons);
    }

    public ApplyInspectionPlanDraftResult Apply(StoreDbContext context, ApplyInspectionPlanDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(request);
        if (request.Preview is null || request.TaskIds is null || request.TaskIds.Count == 0 || request.TaskIds.Any(id => id <= 0) || request.TaskIds.Distinct().Count() != request.TaskIds.Count)
            throw new ArgumentException("Preview and unique positive TaskIds are required.", nameof(request));
        var inspector = request.InspectorName?.Trim();
        if (string.IsNullOrEmpty(inspector) || inspector.Length > 200 || request.CheckDate == default || request.BusinessDate == default || request.CheckDate > request.BusinessDate || request.SavedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("InspectorName, dates, or SavedAtUtc are invalid.", nameof(request));
        var selected = request.TaskIds.Order().ToArray();
        if (selected.Any(id => !request.Preview.ApplicableTaskIds.Contains(id))) throw new ArgumentException("Every selected task must be applicable in this preview.", nameof(request));
        if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Context has pending changes.");
        using var transaction = context.Database.BeginTransaction();
        try
        {
            context.ChangeTracker.Clear();
            var reasons = Validate(context, request.Preview.File.Rows, selected);
            if (reasons.Any(pair => pair.Value.Length != 0)) throw new InvalidOperationException("Inspection plan is stale: " + string.Join("; ", reasons.Where(pair => pair.Value.Length != 0).Select(pair => $"{pair.Key}: {pair.Value}")));
            var results = new List<AppliedInspectionPlanDraft>();
            foreach (var taskId in selected)
            {
                var rows = request.Preview.File.Rows.Where(row => row.TaskId == taskId).ToArray();
                var result = drafts.SaveDraft(context, new(taskId, rows[0].ProductId!.Value, request.BusinessDate, request.SavedAtUtc, inspector, request.CheckDate,
                    rows.Select(row => new SaveDraftItemRequest(row.TaskItemId!.Value, row.BatchId!.Value, row.AttentionVersion!.Value, row.CheckedQty)).ToArray()));
                results.Add(new(taskId, result.DraftId, result.Changed, result.Readiness));
            }
            transaction.Commit();
            return new(results.Any(result => result.Changed), results);
        }
        catch { transaction.Rollback(); context.ChangeTracker.Clear(); throw; }
    }

    private static Dictionary<long, string> Validate(StoreDbContext context, IReadOnlyList<InspectionPlanRow> rows, IReadOnlyCollection<long>? only = null)
    {
        var taskIds = rows.Where(row => row.TaskId is > 0 && (only is null || only.Contains(row.TaskId.Value))).Select(row => row.TaskId!.Value).Distinct().ToArray();
        var output = taskIds.ToDictionary(id => id, _ => string.Empty);
        foreach (var group in rows.Where(row => row.TaskId is > 0 && output.ContainsKey(row.TaskId.Value)).GroupBy(row => row.TaskId!.Value))
        {
            if (group.Any(row => row.Errors.Count != 0) || group.Select(row => row.TaskItemId).Distinct().Count() != group.Count() || group.Select(row => row.BatchId).Distinct().Count() != group.Count()) { output[group.Key] = "文件行解析或重复身份错误"; continue; }
            var task = context.Tasks.AsNoTracking().SingleOrDefault(t => t.Id == group.Key);
            var row = group.First();
            if (task is null || task.Status != "open" || task.ProductId != row.ProductId || context.Inspections.AsNoTracking().Any(i => i.TaskId == group.Key)) { output[group.Key] = "Task 当前状态不允许应用"; continue; }
            var product = context.Products.AsNoTracking().SingleOrDefault(p => p.Id == task.ProductId);
            if (product is null || product.EffectiveStockQty <= 0 || product.IsStockZeroTerminated || product.ExpiryManagementStatus != ExpiryManagementStatus.Managed || product.PolicyVersion != ExpiryPolicies.Version1 || product.PolicyCode is not (ExpiryPolicies.Food or ExpiryPolicies.Pet or ExpiryPolicies.GeneralLong) || !context.ScopeBaselines.AsNoTracking().Any(b => b.IsCompleted && b.ScopeKey == product.CategoryCode && b.PolicyCode == product.PolicyCode && b.PolicyVersion == product.PolicyVersion)) { output[group.Key] = "Product 或 ScopeBaseline 当前状态不允许应用"; continue; }
            var items = context.TaskItems.AsNoTracking().Where(i => i.TaskId == task.Id).ToArray();
            if (items.Length != row.TaskItemCount || task.UpdatedAtUtc != row.TaskUpdatedAtUtc || items.Any(item => item.RequiresReconfirmation) || group.Any(r => r.TaskItemCount != row.TaskItemCount || r.TaskUpdatedAtUtc != row.TaskUpdatedAtUtc || r.ProductId != row.ProductId || r.ProductId != task.ProductId || r.EffectiveStockQty != row.EffectiveStockQty || r.EffectiveStockQty != product.EffectiveStockQty) || group.Any(r => !items.Any(i => i.Id == r.TaskItemId && i.ProductId == r.ProductId && i.BatchId == r.BatchId && i.AttentionVersion == r.AttentionVersion && i.Stage == r.Stage))) { output[group.Key] = "Task 快照已陈旧或需要重新确认"; continue; }
            var batches = context.Batches.AsNoTracking().Where(b => group.Select(r => r.BatchId).Contains(b.Id)).ToArray();
            if (group.Any(r => !batches.Any(b => b.Id == r.BatchId && b.ProductId == task.ProductId && b.TrackingStatus == "active" && b.TrackingStatus == r.TrackingStatus && b.CurrentStage == r.Stage && b.AttentionVersion == r.AttentionVersion && b.CurrentArrivalQty == r.CurrentArrivalQty && b.MaxArrivalQty == r.MaxArrivalQty))) { output[group.Key] = "Batch 快照已陈旧"; continue; }
            var draft = context.Drafts.AsNoTracking().SingleOrDefault(d => d.TaskId == task.Id);
            if (draft?.IsInvalid == true) output[group.Key] = "Draft 已失效";
        }
        return output;
    }
}
