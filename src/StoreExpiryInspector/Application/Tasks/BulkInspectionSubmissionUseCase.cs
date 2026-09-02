using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public enum BulkInspectionSubmissionOutcome
{
    Submitted,
    AlreadySubmitted,
    RequiresOverStockConfirmation,
    OverStockConfirmationStale
}

public sealed record OverStockConfirmation(long TaskId, int EffectiveStockQty, int TotalCheckedQty);

public sealed record BulkInspectionSubmissionRequest(
    IReadOnlyCollection<long> TaskIds,
    string InspectorName,
    DateOnly CheckDate,
    DateOnly BusinessDate,
    DateTime SubmittedAtUtc,
    IReadOnlyCollection<OverStockConfirmation>? OverStockConfirmations = null);

public sealed record BulkInspectionSubmissionTaskResult(long TaskId, long InspectionId);

public sealed record BulkInspectionSubmissionResult(
    BulkInspectionSubmissionOutcome Outcome,
    IReadOnlyList<BulkInspectionSubmissionTaskResult> Tasks,
    IReadOnlyList<OverStockConfirmation> OverStockConfirmations)
{
    public bool Submitted => Outcome == BulkInspectionSubmissionOutcome.Submitted;
}

public sealed class BulkInspectionSubmissionUseCase
{
    private readonly InspectionSubmissionUseCase _submissions = new();

    public BulkInspectionSubmissionResult Submit(StoreDbContext context, BulkInspectionSubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var taskIds = ValidateRequest(request, out var inspectorName, out var confirmations);
        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("StoreDbContext must have no pending changes before a bulk inspection submission.");
        }

        using var transaction = context.Database.BeginTransaction();
        try
        {
            context.ChangeTracker.Clear();
            var tasks = LoadAndValidate(context, taskIds, request, inspectorName);
            var completed = tasks.Where(task => task.Status == "completed").ToArray();
            if (completed.Length != 0)
            {
                if (completed.Length != tasks.Count)
                {
                    throw new InvalidOperationException("Open and completed tasks cannot be submitted together.");
                }

                var alreadySubmitted = completed.Select(task => ValidateCompleted(context, task, request, inspectorName))
                    .OrderBy(result => result.TaskId).ToArray();
                transaction.Rollback();
                context.ChangeTracker.Clear();
                return new(BulkInspectionSubmissionOutcome.AlreadySubmitted, alreadySubmitted, Array.Empty<OverStockConfirmation>());
            }

            var submitted = new List<BulkInspectionSubmissionTaskResult>();
            var warnings = new List<OverStockConfirmation>();
            foreach (var task in tasks)
            {
                var result = _submissions.Submit(context, new(task.Id, task.ProductId, request.BusinessDate, request.SubmittedAtUtc));
                if (result.RequiresOverStockConfirmation)
                {
                    warnings.Add(new(task.Id, result.EffectiveStockQty, result.TotalCheckedQty));
                }
                else if (result.Submitted && result.InspectionId is long inspectionId)
                {
                    submitted.Add(new(task.Id, inspectionId));
                }
                else
                {
                    throw new InvalidOperationException($"Task {task.Id} did not produce a new formal inspection.");
                }
            }

            if (warnings.Count != 0)
            {
                var currentWarnings = warnings.OrderBy(item => item.TaskId).ToArray();
                if (confirmations.Length == 0)
                {
                    return Rollback(context, transaction, BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation, currentWarnings);
                }

                if (!confirmations.SequenceEqual(currentWarnings))
                {
                    return Rollback(context, transaction, BulkInspectionSubmissionOutcome.OverStockConfirmationStale, currentWarnings);
                }

                foreach (var warning in currentWarnings)
                {
                    var task = tasks.Single(candidate => candidate.Id == warning.TaskId);
                    var result = _submissions.Submit(context, new(task.Id, task.ProductId, request.BusinessDate, request.SubmittedAtUtc, warning.EffectiveStockQty, warning.TotalCheckedQty));
                    if (!result.Submitted || result.InspectionId is not long inspectionId)
                    {
                        throw new InvalidOperationException($"Task {task.Id} did not accept its current over-stock confirmation.");
                    }

                    submitted.Add(new(task.Id, inspectionId));
                }
            }
            else if (confirmations.Length != 0)
            {
                return Rollback(context, transaction, BulkInspectionSubmissionOutcome.OverStockConfirmationStale, Array.Empty<OverStockConfirmation>());
            }

            var ordered = submitted.OrderBy(item => item.TaskId).ToArray();
            if (ordered.Length != tasks.Count || ordered.Select(item => item.TaskId).Distinct().Count() != tasks.Count)
            {
                throw new InvalidOperationException("Every task must produce exactly one new formal inspection.");
            }

            transaction.Commit();
            context.ChangeTracker.Clear();
            return new(BulkInspectionSubmissionOutcome.Submitted, ordered, Array.Empty<OverStockConfirmation>());
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public BulkInspectionSubmissionResult Execute(StoreDbContext context, BulkInspectionSubmissionRequest request) => Submit(context, request);

    private static BulkInspectionSubmissionResult Rollback(StoreDbContext context, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, BulkInspectionSubmissionOutcome outcome, IReadOnlyList<OverStockConfirmation> warnings)
    {
        transaction.Rollback();
        context.ChangeTracker.Clear();
        return new(outcome, Array.Empty<BulkInspectionSubmissionTaskResult>(), warnings);
    }

    private static IReadOnlyList<ProductTask> LoadAndValidate(StoreDbContext context, long[] taskIds, BulkInspectionSubmissionRequest request, string inspectorName)
    {
        var tasks = context.Tasks.AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .Include(task => task.Product)
            .Include(task => task.Items).ThenInclude(item => item.Batch)
            .Include(task => task.Draft).ThenInclude(draft => draft!.Items)
            .OrderBy(task => task.Id)
            .AsSplitQuery()
            .ToArray();
        if (tasks.Length != taskIds.Length)
        {
            throw new KeyNotFoundException("Every requested task must exist.");
        }

        var inspectedTaskIds = context.Inspections.AsNoTracking()
            .Where(inspection => taskIds.Contains(inspection.TaskId))
            .Select(inspection => inspection.TaskId)
            .ToHashSet();

        foreach (var task in tasks)
        {
            if (task.Status is not ("open" or "completed"))
            {
                throw new InvalidOperationException($"Task {task.Id} has an unsupported status.");
            }

            if (task.Product is null || task.ProductId != task.Product.Id)
            {
                throw new InvalidOperationException($"Task {task.Id} has an invalid product relationship.");
            }

            ValidateProduct(context, task.Product);
            if (task.Status == "open")
            {
                if (inspectedTaskIds.Contains(task.Id))
                {
                    throw new InvalidOperationException($"Open task {task.Id} already has a formal inspection.");
                }

                ValidateOpenTask(task, request, inspectorName);
            }
        }

        return tasks;
    }

    private static void ValidateProduct(StoreDbContext context, Product product)
    {
        if (product.ExpiryManagementStatus != ExpiryManagementStatus.Managed || product.PolicyVersion != ExpiryPolicies.Version1 ||
            product.PolicyCode is not (ExpiryPolicies.Food or ExpiryPolicies.Pet or ExpiryPolicies.GeneralLong) ||
            product.EffectiveStockQty < 0 || product.IsStockZeroTerminated ||
            !context.ScopeBaselines.AsNoTracking().Any(baseline => baseline.IsCompleted && baseline.ScopeKey == product.CategoryCode && baseline.PolicyCode == product.PolicyCode && baseline.PolicyVersion == product.PolicyVersion))
        {
            throw new InvalidOperationException($"Product {product.Id} is not eligible for formal inspection submission.");
        }
    }

    private static void ValidateOpenTask(ProductTask task, BulkInspectionSubmissionRequest request, string inspectorName)
    {
        if (task.Items.Count == 0 || task.Draft is null || task.Draft.IsInvalid || task.Draft.InvalidReason is not null || task.Draft.InvalidatedAtUtc is not null ||
            task.Draft.TaskId != task.Id || !string.Equals(task.Draft.InspectorName?.Trim(), inspectorName, StringComparison.Ordinal) || task.Draft.CheckDate != request.CheckDate)
        {
            throw new InvalidOperationException($"Task {task.Id} has no current valid matching draft.");
        }

        var items = task.Items.ToDictionary(item => item.Id);
        var draftItems = task.Draft.Items.ToDictionary(item => item.TaskItemId);
        if (draftItems.Count != items.Count || task.Draft.Items.Any(item => item.DraftId != task.Draft.Id || item.TaskId != task.Id))
        {
            throw new InvalidOperationException($"Draft {task.Draft.Id} does not exactly cover task {task.Id}.");
        }

        foreach (var item in task.Items)
        {
            var batch = item.Batch;
            if (item.TaskId != task.Id || item.ProductId != task.ProductId || batch is null || batch.ProductId != task.ProductId ||
                batch.TrackingStatus != "active" || batch.CurrentArrivalQty < 0 || batch.MaxArrivalQty < 0 ||
                item.AttentionVersion < 0 || batch.AttentionVersion < 0 || batch.HandledAttentionVersion < 0 || batch.HandledAttentionVersion > batch.AttentionVersion ||
                item.AttentionVersion != batch.AttentionVersion || item.Stage != batch.CurrentStage || !IsInspectableStage(item.Stage) || item.RequiresReconfirmation ||
                !draftItems.TryGetValue(item.Id, out var draftItem) || draftItem.CheckedQty is not int checkedQty || checkedQty < 0 || draftItem.ConfirmedAttentionVersion != item.AttentionVersion || draftItem.ConfirmedAttentionVersion != batch.AttentionVersion)
            {
                throw new InvalidOperationException($"Task {task.Id} has stale or invalid current inspection facts.");
            }
        }

        _ = task.Draft.Items.Aggregate(0, (total, item) => checked(total + item.CheckedQty!.Value));
    }

    private static BulkInspectionSubmissionTaskResult ValidateCompleted(StoreDbContext context, ProductTask task, BulkInspectionSubmissionRequest request, string inspectorName)
    {
        if (task.Draft is { IsInvalid: false })
        {
            throw new InvalidOperationException($"Completed task {task.Id} retains an effective draft.");
        }

        var inspections = context.Inspections.AsNoTracking().Where(inspection => inspection.TaskId == task.Id).Take(2).ToArray();
        if (inspections.Length != 1)
        {
            throw new InvalidOperationException($"Completed task {task.Id} must have exactly one formal inspection.");
        }

        var inspection = inspections[0];
        if (inspection.ProductId != task.ProductId || !string.Equals(inspection.InspectorName.Trim(), inspectorName, StringComparison.Ordinal) || inspection.CheckDate != request.CheckDate || inspection.SubmittedAtUtc != request.SubmittedAtUtc)
        {
            throw new InvalidOperationException($"Completed task {task.Id} does not match this submission request.");
        }

        var expected = task.Items.Select(item => new InspectionCoverage(item.BatchId, item.ProductId)).OrderBy(item => item.BatchId).ToArray();
        var actual = context.InspectionItems.AsNoTracking().Where(item => item.InspectionId == inspection.Id)
            .OrderBy(item => item.BatchId).Select(item => new InspectionCoverage(item.BatchId, item.ProductId)).ToArray();
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"Inspection {inspection.Id} does not cover the completed task facts.");
        }

        return new(task.Id, inspection.Id);
    }

    private static long[] ValidateRequest(BulkInspectionSubmissionRequest request, out string inspectorName, out OverStockConfirmation[] confirmations)
    {
        if (request.TaskIds is null || request.TaskIds.Count == 0 || request.TaskIds.Any(id => id <= 0) || request.TaskIds.Distinct().Count() != request.TaskIds.Count)
        {
            throw new ArgumentException("TaskIds must be a non-empty set of unique positive IDs.", nameof(request));
        }

        inspectorName = request.InspectorName?.Trim() ?? string.Empty;
        if (inspectorName.Length == 0 || inspectorName.Length > 200 || request.CheckDate == default || request.BusinessDate == default || request.CheckDate > request.BusinessDate || request.SubmittedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("InspectorName, dates, or SubmittedAtUtc are invalid.", nameof(request));
        }

        var supplied = request.OverStockConfirmations ?? Array.Empty<OverStockConfirmation>();
        if (supplied.Any(item => item is null || item.TaskId <= 0 || item.EffectiveStockQty < 0 || item.TotalCheckedQty < 0) || supplied.Select(item => item.TaskId).Distinct().Count() != supplied.Count || supplied.Any(item => !request.TaskIds.Contains(item.TaskId)))
        {
            throw new ArgumentException("Over-stock confirmations must be unique, complete positive task facts for requested tasks.", nameof(request));
        }

        confirmations = supplied.OrderBy(item => item.TaskId).ToArray();
        return request.TaskIds.Order().ToArray();
    }

    private static bool IsInspectableStage(string stage)
    {
        try { return ExpiryStageCalculator.GetStagePriority(stage) > 0; }
        catch (ArgumentException) { return false; }
    }

    private sealed record InspectionCoverage(long BatchId, long ProductId);
}
