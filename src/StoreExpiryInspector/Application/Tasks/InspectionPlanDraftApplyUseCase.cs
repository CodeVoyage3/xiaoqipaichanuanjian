using System.Globalization;
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
        var resolved = Resolve(context, reader.Read(path).Rows);
        var applicable = resolved.Where(row => row.CheckedQty is not null && row.Errors.Count == 0 && row.TaskId is > 0).GroupBy(row => row.TaskId!.Value).ToArray();
        var reasons = applicable.ToDictionary(group => group.Key, _ => string.Empty);
        var tasks = applicable.Select(group => new InspectionPlanTaskPreview(group.Key, true, null)).OrderBy(task => task.TaskId).ToArray();
        var summary = new InspectionPlanPreviewSummary(resolved.Where(row => row.ProductId is > 0).Select(row => row.ProductId).Distinct().Count(), tasks.Length, resolved.Where(row => row.BatchId is > 0).Select(row => row.BatchId).Distinct().Count(), resolved.Count(row => row.CheckedQty is not null && row.Errors.Count == 0), resolved.Count(row => row.CheckedQty is null && row.Errors.Count == 0), resolved.Sum(row => row.Errors.Count));
        return new(new(resolved), summary, tasks, tasks.Select(task => task.TaskId).ToArray(), reasons);
    }
    public ApplyInspectionPlanDraftResult Apply(StoreDbContext context, ApplyInspectionPlanDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(request);
        if (request.Preview is null || request.TaskIds is null || request.TaskIds.Count == 0 || request.TaskIds.Any(id => id <= 0) || request.TaskIds.Distinct().Count() != request.TaskIds.Count || request.SavedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Preview, task ids, and UTC save time are required.", nameof(request));
        var inspector = request.InspectorName?.Trim(); if (string.IsNullOrEmpty(inspector) || request.CheckDate == default || request.CheckDate > request.BusinessDate) throw new ArgumentException("Inspector and dates are invalid.", nameof(request));
        using var transaction = context.Database.BeginTransaction();
        try
        {
            context.ChangeTracker.Clear(); var current = Resolve(context, request.Preview.File.Rows); var selected = request.TaskIds.ToHashSet(); var results = new List<AppliedInspectionPlanDraft>();
            foreach (var group in current.Where(row => row.CheckedQty is not null && row.Errors.Count == 0 && row.TaskId is > 0 && selected.Contains(row.TaskId.Value)).GroupBy(row => row.TaskId!.Value))
            {
                var first = group.First(); var result = drafts.SaveDraft(context, new(group.Key, first.ProductId!.Value, request.BusinessDate, request.SavedAtUtc, inspector, request.CheckDate, group.Select(row => new SaveDraftItemRequest(row.TaskItemId!.Value, row.BatchId!.Value, row.AttentionVersion!.Value, row.CheckedQty)).ToArray(), true));
                results.Add(new(group.Key, result.DraftId, result.Changed, result.Readiness));
            }
            transaction.Commit(); return new(results.Any(result => result.Changed), results);
        }
        catch { transaction.Rollback(); context.ChangeTracker.Clear(); throw; }
    }
    private static IReadOnlyList<InspectionPlanRow> Resolve(StoreDbContext context, IReadOnlyList<InspectionPlanRow> rows)
    {
        var result = new List<InspectionPlanRow>();
        foreach (var source in rows)
        {
            var errors = source.Errors.ToList(); if (source.CheckedQty is null || errors.Count != 0) { result.Add(source with { Errors = errors }); continue; }
            if (!DateOnly.TryParseExact(source.ExpiryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)) { errors.Add("有效日期格式不正确。"); result.Add(source with { Errors = errors }); continue; }
            var product = context.Products.AsNoTracking().SingleOrDefault(item => item.ProductCode == source.ProductCode);
            if (product is null) { errors.Add("该行商品编码无法匹配当前商品，已跳过。"); result.Add(source with { Errors = errors }); continue; }
            DateOnly? production = null; if (!string.IsNullOrWhiteSpace(source.ProductionDate)) { if (!DateOnly.TryParseExact(source.ProductionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedProduction)) { errors.Add("生产日期格式不正确。"); result.Add(source with { Errors = errors }); continue; } production = parsedProduction; }
            var batches = context.Batches.AsNoTracking().Where(batch => batch.ProductId == product.Id && batch.ExpiryDate == expiry && batch.ProductionDate == production).ToArray();
            if (batches.Length != 1) { errors.Add("该行批次无法唯一匹配当前数据，已跳过。"); result.Add(source with { Errors = errors }); continue; }
            var batch = batches[0]; var item = context.TaskItems.AsNoTracking().Where(candidate => candidate.BatchId == batch.Id).Join(context.Tasks.AsNoTracking().Where(task => task.Status == "open"), candidate => candidate.TaskId, task => task.Id, (candidate, task) => new { candidate, task }).SingleOrDefault();
            if (item is null || product.EffectiveStockQty <= 0 || product.IsStockZeroTerminated || product.ExpiryManagementStatus != ExpiryManagementStatus.Managed || batch.TrackingStatus != "active" || batch.CurrentStage != item.candidate.Stage || batch.AttentionVersion != item.candidate.AttentionVersion || batch.HandledAttentionVersion >= batch.AttentionVersion || item.candidate.RequiresReconfirmation) { errors.Add("该行对应批次状态已经变化，本次已跳过，请重新导出最新计划。"); result.Add(source with { Errors = errors }); continue; }
            result.Add(source with { ProductId = product.Id, BatchId = batch.Id, TaskId = item.task.Id, TaskItemId = item.candidate.Id, AttentionVersion = item.candidate.AttentionVersion, Stage = item.candidate.Stage, TrackingStatus = batch.TrackingStatus, CurrentArrivalQty = batch.CurrentArrivalQty, MaxArrivalQty = batch.MaxArrivalQty, EffectiveStockQty = product.EffectiveStockQty, Errors = errors });
        }
        return result;
    }
}
