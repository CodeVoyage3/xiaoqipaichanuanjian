using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public sealed record ColdStartScopeBaselineRequest(
    string ScopeKey,
    string PolicyCode,
    int PolicyVersion,
    long CreatedImportId,
    DateOnly BusinessDate,
    DateTime OccurredAtUtc);

public sealed record ColdStartScopeBaselineResult(bool Started, bool AlreadyCompleted, long? BaselineId);

public sealed class ColdStartScopeBaselineUseCase
{
    private readonly ProductTaskAggregator _taskAggregator;

    public ColdStartScopeBaselineUseCase(ProductTaskAggregator? taskAggregator = null) =>
        _taskAggregator = taskAggregator ?? new ProductTaskAggregator();

    public ColdStartScopeBaselineResult Execute(StoreDbContext context, ColdStartScopeBaselineRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (request.PolicyVersion != ExpiryPolicies.Version1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PolicyVersion), "V1 only supports policy version 1.");
        }
        if (request.CreatedImportId <= 0 || request.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(request.ScopeKey) || request.ScopeKey != request.ScopeKey.Trim() ||
            string.IsNullOrWhiteSpace(request.PolicyCode) || request.PolicyCode != request.PolicyCode.Trim() ||
            request.PolicyCode is not (ExpiryPolicies.Food or ExpiryPolicies.Pet or ExpiryPolicies.GeneralLong))
        {
            throw new ArgumentException("Cold-start request is invalid.", nameof(request));
        }

        if (!context.Imports.AsNoTracking().Any(import => import.Id == request.CreatedImportId && import.Status == ImportStatuses.Succeeded && !import.IsUndone) ||
            !context.Products.AsNoTracking().Any(product => product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                product.CategoryCode == request.ScopeKey && product.PolicyCode == request.PolicyCode && product.PolicyVersion == request.PolicyVersion))
        {
            throw new InvalidOperationException("Cold-start scope or import is not valid.");
        }

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction) transaction = context.Database.BeginTransaction();
            var baseline = context.ScopeBaselines.SingleOrDefault(item =>
                item.ScopeKey == request.ScopeKey && item.PolicyCode == request.PolicyCode && item.PolicyVersion == request.PolicyVersion);
            if (baseline is { IsCompleted: true })
            {
                transaction?.Commit();
                return new(false, true, baseline.Id);
            }
            if (baseline is not null && (context.BatchBaselines.Any(item => item.BaselineId == baseline.Id) || baseline.CompletedAtUtc is not null))
            {
                throw new InvalidOperationException("Incomplete scope baseline contains persisted facts.");
            }
            if (baseline is null)
            {
                baseline = new ScopeBaseline { ScopeKey = request.ScopeKey, PolicyCode = request.PolicyCode, PolicyVersion = request.PolicyVersion, CreatedImportId = request.CreatedImportId, BusinessDate = request.BusinessDate, CreatedAtUtc = request.OccurredAtUtc };
                context.ScopeBaselines.Add(baseline);
                context.SaveChanges();
            }

            var batches = context.Batches
                .Include(batch => batch.Product)
                .Where(batch => batch.Product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                    batch.Product.CategoryCode == request.ScopeKey && batch.Product.PolicyCode == request.PolicyCode && batch.Product.PolicyVersion == request.PolicyVersion)
                .OrderBy(batch => batch.Product.ProductCode).ThenBy(batch => batch.Id).ToArray();
            var facts = batches.Select(batch => Classify(batch, request)).ToArray();
            var taskFacts = facts.Where(fact => fact.NeedsTask).ToArray();

            foreach (var fact in facts.Where(fact => !fact.NeedsTask && fact.Disposition is not null)) context.BatchBaselines.Add(ToBaseline(baseline.Id, fact, null));
            foreach (var fact in facts)
            {
                if (fact.Stage is not null)
                {
                    fact.Batch.CurrentStage = fact.Stage.CurrentStage;
                    fact.Batch.NextTriggerDate = fact.Stage.NextTriggerDate;
                    fact.Batch.UpdatedAtUtc = request.OccurredAtUtc;
                }
                if (fact.ShelfLifeUnavailable)
                {
                    context.ImportIssues.Add(new ImportIssue { ImportId = request.CreatedImportId, IssueType = "cold_start_actual_shelf_life_unavailable", FieldName = "ProductionDate", SafeSummary = $"Cold start cannot calculate actual shelf life for product {fact.Batch.ProductId}, batch {fact.Batch.Id}." });
                }
            }
            context.SaveChanges();

            foreach (var group in taskFacts.GroupBy(fact => fact.Batch.ProductId).OrderBy(group => group.Key))
            {
                var result = _taskAggregator.Aggregate(context, new ProductTaskAggregationRequest(group.Key,
                    group.Select(fact => new ProductTaskBatchResult(fact.Batch.Id, fact.Stage!.CurrentStage, fact.Batch.AttentionVersion, false)).ToArray(), request.OccurredAtUtc));
                if (result.TaskId is not long taskId) throw new InvalidOperationException("Cold-start task aggregation did not return an open task.");
                foreach (var fact in group) context.BatchBaselines.Add(ToBaseline(baseline.Id, fact, taskId));
            }
            context.SaveChanges();
            baseline.IsCompleted = true;
            baseline.CompletedAtUtc = request.OccurredAtUtc;
            context.SaveChanges();
            transaction?.Commit();
            return new(true, false, baseline.Id);
        }
        catch
        {
            if (ownsTransaction) { try { transaction?.Rollback(); } catch { } context.ChangeTracker.Clear(); }
            throw;
        }
        finally { transaction?.Dispose(); }
    }

    private static ClassifiedBatch Classify(Batch batch, ColdStartScopeBaselineRequest request)
    {
        if (batch.Product.EffectiveStockQty == 0) return new(batch, null, ColdStartDispositions.StockZeroBaseline, null, false, false);
        if (batch.TrackingStatus != "active") return new(batch, null, null, null, false, false);
        var stage = ExpiryPolicyCalculator.Calculate(request.PolicyCode, request.PolicyVersion, request.BusinessDate, batch.ExpiryDate, ShelfLifeDays(batch));
        if (stage is null || stage.CurrentStage == ExpiryStageCalculator.None) return new(batch, stage, null, null, false, false);
        if (stage.CurrentStage == ExpiryStageCalculator.Discount50) return new(batch, stage, ColdStartDispositions.Discount50Baseline, null, false, false);
        if (stage.CurrentStage == ExpiryStageCalculator.Discount20) return new(batch, stage, ColdStartDispositions.Discount20Baseline, null, false, false);
        if (stage.CurrentStage == ExpiryStageCalculator.Withdraw) return new(batch, stage, ColdStartDispositions.WithdrawTask, null, true, false);
        if (batch.ExpiryDate == request.BusinessDate) return new(batch, stage, ColdStartDispositions.ExpiredTodayTask, null, true, false);
        if (batch.ProductionDate is not DateOnly production || batch.ExpiryDate <= production)
            return new(batch, stage, ColdStartDispositions.ExpiredHistoricalBaseline, null, false, true);
        var window = Math.Clamp((int)((3L * (batch.ExpiryDate.DayNumber - production.DayNumber) + 99) / 100), 3, 30);
        return request.BusinessDate.DayNumber - batch.ExpiryDate.DayNumber <= window
            ? new(batch, stage, ColdStartDispositions.ExpiredCatchupTask, window, true, false)
            : new(batch, stage, ColdStartDispositions.ExpiredHistoricalBaseline, null, false, false);
    }

    private static int ShelfLifeDays(Batch batch) => batch.ShelfLifeUnit switch
    {
        "D" => batch.ShelfLifeValue, "M" => checked(batch.ShelfLifeValue * 30), "Y" => checked(batch.ShelfLifeValue * 365), _ => throw new ArgumentException("Invalid shelf life unit.")
    };

    private static BatchBaseline ToBaseline(long baselineId, ClassifiedBatch fact, long? taskId) => new()
    {
        BaselineId = baselineId, BatchId = fact.Batch.Id, StageAtBaseline = fact.Stage?.CurrentStage ?? ExpiryStageCalculator.None,
        ColdStartDisposition = fact.Disposition!, SourceTaskId = taskId,
        CatchupWindowDays = fact.Disposition == ColdStartDispositions.ExpiredCatchupTask ? fact.CatchupWindowDays : null,
        CatchupSource = fact.Disposition == ColdStartDispositions.ExpiredCatchupTask ? "historical_window" : null
    };

    private sealed record ClassifiedBatch(Batch Batch, ExpiryStageResult? Stage, string? Disposition, int? CatchupWindowDays, bool NeedsTask, bool ShelfLifeUnavailable);
}
