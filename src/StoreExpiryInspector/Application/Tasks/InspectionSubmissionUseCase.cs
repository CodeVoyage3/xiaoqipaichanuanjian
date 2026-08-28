using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public enum InspectionSubmissionOutcome
{
    Submitted,
    AlreadySubmitted,
    RequiresOverStockConfirmation
}

public sealed record InspectionSubmissionRequest(
    long TaskId,
    long ProductId,
    DateOnly BusinessDate,
    DateTime SubmittedAtUtc,
    int? ConfirmedEffectiveStockQty = null,
    int? ConfirmedTotalCheckedQty = null);

public sealed record InspectionSubmissionResult(
    InspectionSubmissionOutcome Outcome,
    long? InspectionId,
    int EffectiveStockQty,
    int TotalCheckedQty)
{
    public string Status => Outcome switch
    {
        InspectionSubmissionOutcome.Submitted => "submitted",
        InspectionSubmissionOutcome.AlreadySubmitted => "already_submitted",
        InspectionSubmissionOutcome.RequiresOverStockConfirmation => "requires_over_stock_confirmation",
        _ => throw new InvalidOperationException("Unknown inspection submission outcome.")
    };

    public bool Submitted => Outcome == InspectionSubmissionOutcome.Submitted;

    public bool AlreadySubmitted => Outcome == InspectionSubmissionOutcome.AlreadySubmitted;

    public bool RequiresOverStockConfirmation =>
        Outcome == InspectionSubmissionOutcome.RequiresOverStockConfirmation;
}

public sealed class InspectionSubmissionUseCase
{
    private readonly BatchCheckedZeroLifecycleUseCase _batchCheckedZeroLifecycle = new();

    public InspectionSubmissionResult Submit(
        StoreDbContext context,
        InspectionSubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        EnsureCleanContext(context);

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            context.ChangeTracker.Clear();
            var task = LoadTask(context, request.TaskId);
            var product = context.Products
                .AsTracking()
                .SingleOrDefault(candidate => candidate.Id == request.ProductId);
            if (product is null)
            {
                throw new KeyNotFoundException($"Product {request.ProductId} does not exist.");
            }

            if (task.ProductId != product.Id)
            {
                throw new ArgumentException(
                    $"Task {task.Id} does not belong to product {request.ProductId}.",
                    nameof(request));
            }

            var existingInspection = context.Inspections
                .AsNoTracking()
                .SingleOrDefault(inspection => inspection.TaskId == task.Id);

            if (string.Equals(task.Status, "completed", StringComparison.Ordinal))
            {
                if (existingInspection is null)
                {
                    throw new InvalidOperationException(
                        $"Completed task {task.Id} has no formal inspection.");
                }

                ValidateInspectionOwnership(existingInspection, task, product);
                if (task.Draft is { IsInvalid: false })
                {
                    throw new InvalidOperationException(
                        $"Completed task {task.Id} retains an effective draft.");
                }

                transaction?.Commit();
                return new(
                    InspectionSubmissionOutcome.AlreadySubmitted,
                    existingInspection.Id,
                    product.EffectiveStockQty,
                    0);
            }

            if (string.Equals(task.Status, "system_closed", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Task {task.Id} is system closed.");
            }

            if (!string.Equals(task.Status, "open", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Task {task.Id} has an unsupported status.");
            }

            if (existingInspection is not null)
            {
                throw new InvalidOperationException(
                    $"Open task {task.Id} already has a formal inspection.");
            }

            var draftFacts = ValidateOpenTask(task, product, request.BusinessDate);
            if (draftFacts.TotalCheckedQty > product.EffectiveStockQty)
            {
                if (request.ConfirmedEffectiveStockQty != product.EffectiveStockQty ||
                    request.ConfirmedTotalCheckedQty != draftFacts.TotalCheckedQty)
                {
                    transaction?.Commit();
                    return new(
                        InspectionSubmissionOutcome.RequiresOverStockConfirmation,
                        null,
                        product.EffectiveStockQty,
                        draftFacts.TotalCheckedQty);
                }
            }

            var inspection = new Inspection
            {
                TaskId = task.Id,
                ProductId = product.Id,
                ProductCodeSnapshot = product.ProductCode,
                ProductNameSnapshot = product.CurrentName,
                BarcodeSnapshot = product.CurrentBarcode,
                StageSnapshot = task.HighestStage,
                StockQtySnapshot = product.EffectiveStockQty,
                InspectorName = draftFacts.InspectorName,
                CheckDate = draftFacts.CheckDate,
                SubmittedAtUtc = request.SubmittedAtUtc
            };
            foreach (var item in task.Items.OrderBy(item => item.Id))
            {
                var draftItem = draftFacts.ItemsByTaskItemId[item.Id];
                var inspectionItem = new InspectionItem
                {
                    Inspection = inspection,
                    ProductId = product.Id,
                    BatchId = item.BatchId,
                    ProductionDateSnapshot = item.Batch.ProductionDate,
                    ExpiryDateSnapshot = item.Batch.ExpiryDate,
                    StageSnapshot = item.Stage,
                    ArrivalQtySnapshot = item.Batch.CurrentArrivalQty,
                    CheckedQty = draftItem.CheckedQty!.Value,
                    UpdatedAtUtc = request.SubmittedAtUtc
                };
                inspection.Items.Add(inspectionItem);
            }

            context.Inspections.Add(inspection);
            context.SaveChanges();

            foreach (var item in task.Items.OrderBy(item => item.Id))
            {
                var inspectionItem = inspection.Items.Single(inspectionItem =>
                    inspectionItem.BatchId == item.BatchId);
                if (inspectionItem.CheckedQty != 0)
                {
                    continue;
                }

                _batchCheckedZeroLifecycle.Execute(
                    context,
                    new BatchCheckedZeroLifecycleRequest(
                        product.Id,
                        item.BatchId,
                        inspection.Id,
                        inspectionItem.Id,
                        inspectionItem.CheckedQty,
                        request.SubmittedAtUtc));
            }

            foreach (var item in task.Items)
            {
                item.Batch.HandledAttentionVersion = item.Batch.AttentionVersion;
            }

            task.Status = "completed";
            task.ClosedAtUtc = request.SubmittedAtUtc;
            task.UpdatedAtUtc = request.SubmittedAtUtc;

            context.DraftItems.RemoveRange(task.Draft!.Items.ToArray());
            context.Drafts.Remove(task.Draft);
            context.SaveChanges();

            transaction?.Commit();
            return new(
                InspectionSubmissionOutcome.Submitted,
                inspection.Id,
                product.EffectiveStockQty,
                draftFacts.TotalCheckedQty);
        }
        catch
        {
            if (ownsTransaction)
            {
                try
                {
                    transaction?.Rollback();
                }
                catch
                {
                }
            }

            // The caller owns an outer transaction; it must decide whether to commit or roll back.
            // Clearing tracked state prevents a failed submission from being accidentally saved later.
            context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    public InspectionSubmissionResult Execute(
        StoreDbContext context,
        InspectionSubmissionRequest request) => Submit(context, request);

    private static ProductTask LoadTask(StoreDbContext context, long taskId)
    {
        var task = context.Tasks
            .Include(candidate => candidate.Items)
            .ThenInclude(item => item.Batch)
            .Include(candidate => candidate.Draft)
            .ThenInclude(draft => draft!.Items)
            .SingleOrDefault(candidate => candidate.Id == taskId);
        return task ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");
    }

    private static DraftFacts ValidateOpenTask(
        ProductTask task,
        Product product,
        DateOnly businessDate)
    {
        if (task.Items.Count == 0)
        {
            throw new InvalidOperationException($"Task {task.Id} has no current task items.");
        }

        var taskItemsById = task.Items.ToDictionary(item => item.Id);
        foreach (var item in task.Items)
        {
            if (item.TaskId != task.Id || item.ProductId != product.Id || item.Batch is null)
            {
                throw new InvalidOperationException(
                    $"Task item {item.Id} has inconsistent task, product, or batch ownership.");
            }

            if (item.Batch.ProductId != product.Id)
            {
                throw new InvalidOperationException(
                    $"Batch {item.BatchId} does not belong to product {product.Id}.");
            }

            if (item.AttentionVersion < 0 || item.Batch.AttentionVersion < 0 ||
                item.AttentionVersion != item.Batch.AttentionVersion)
            {
                throw new InvalidOperationException(
                    $"Task item {item.Id} and batch {item.BatchId} have inconsistent attention versions.");
            }

            if (item.Batch.HandledAttentionVersion < 0 ||
                item.Batch.HandledAttentionVersion > item.Batch.AttentionVersion)
            {
                throw new InvalidOperationException(
                    $"Batch {item.BatchId} has an invalid handled attention version.");
            }
        }

        var draft = task.Draft;
        if (draft is null)
        {
            throw new InvalidOperationException($"Task {task.Id} has no current valid draft.");
        }

        if (draft.TaskId != task.Id ||
            draft.IsInvalid || draft.InvalidReason is not null || draft.InvalidatedAtUtc is not null)
        {
            throw new InvalidOperationException($"Draft {draft.Id} is invalid and cannot be submitted.");
        }

        var normalizedInspectorName = draft.InspectorName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedInspectorName))
        {
            throw new InvalidOperationException("InspectorName is required for submission.");
        }

        if (draft.CheckDate is not DateOnly checkDate)
        {
            throw new InvalidOperationException("CheckDate is required for submission.");
        }

        if (checkDate > businessDate)
        {
            throw new InvalidOperationException("CheckDate cannot be later than BusinessDate.");
        }

        var draftItemsByTaskItemId = draft.Items.ToDictionary(item => item.TaskItemId);
        if (draftItemsByTaskItemId.Count != taskItemsById.Count ||
            draft.Items.Any(item => item.DraftId != draft.Id || item.TaskId != task.Id))
        {
            throw new InvalidOperationException($"Draft {draft.Id} does not match the current task items.");
        }

        var totalCheckedQty = 0;
        foreach (var taskItem in task.Items)
        {
            if (!draftItemsByTaskItemId.TryGetValue(taskItem.Id, out var draftItem))
            {
                throw new InvalidOperationException(
                    $"Task item {taskItem.Id} has no complete draft input.");
            }

            if (draftItem.CheckedQty is not int checkedQty)
            {
                throw new InvalidOperationException(
                    $"Task item {taskItem.Id} has no checked quantity.");
            }

            if (taskItem.RequiresReconfirmation)
            {
                throw new InvalidOperationException(
                    $"Task item {taskItem.Id} requires reconfirmation.");
            }

            if (draftItem.ConfirmedAttentionVersion != taskItem.AttentionVersion ||
                draftItem.ConfirmedAttentionVersion != taskItem.Batch.AttentionVersion)
            {
                throw new InvalidOperationException(
                    $"Draft item {draftItem.Id} has a stale attention version.");
            }

            totalCheckedQty = checked(totalCheckedQty + checkedQty);
        }

        return new(
            normalizedInspectorName,
            checkDate,
            totalCheckedQty,
            draftItemsByTaskItemId);
    }

    private static void ValidateInspectionOwnership(
        Inspection inspection,
        ProductTask task,
        Product product)
    {
        if (inspection.ProductId != product.Id || inspection.TaskId != task.Id)
        {
            throw new InvalidOperationException(
                $"Inspection {inspection.Id} does not belong to task {task.Id} and product {product.Id}.");
        }
    }

    private static void ValidateRequest(InspectionSubmissionRequest request)
    {
        if (request.TaskId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TaskId));
        }

        if (request.ProductId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ProductId));
        }

        if (request.SubmittedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "SubmittedAtUtc must be UTC.",
                nameof(request.SubmittedAtUtc));
        }

        if (request.ConfirmedEffectiveStockQty is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ConfirmedEffectiveStockQty));
        }

        if (request.ConfirmedTotalCheckedQty is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ConfirmedTotalCheckedQty));
        }
    }

    private static void EnsureCleanContext(StoreDbContext context)
    {
        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "StoreDbContext must have no pending changes before an inspection submission.");
        }
    }

    private sealed record DraftFacts(
        string InspectorName,
        DateOnly CheckDate,
        int TotalCheckedQty,
        IReadOnlyDictionary<long, InspectionDraftItem> ItemsByTaskItemId);
}
