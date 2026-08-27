using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record SaveDraftItemRequest(
    long TaskItemId,
    long BatchId,
    int AttentionVersion,
    int? CheckedQty);

public sealed record SaveDraftRequest(
    long TaskId,
    long ProductId,
    DateOnly BusinessDate,
    DateTime SavedAtUtc,
    string? InspectorName = null,
    DateOnly? CheckDate = null,
    IReadOnlyList<SaveDraftItemRequest>? Items = null);

public sealed record ReconfirmItemRequest(
    long TaskId,
    long ProductId,
    long TaskItemId,
    long BatchId,
    int AttentionVersion,
    DateTime ConfirmedAtUtc);

public sealed record ClearDraftRequest(long TaskId, long ProductId);

public sealed record InspectionDraftReadiness(
    int CurrentItemCount,
    int FilledItemCount,
    int MissingItemCount,
    int RequiresReconfirmationCount,
    bool HasInspectorName,
    bool HasCheckDate,
    bool AllItemsFilled,
    bool IsDraftComplete);

public sealed record SaveDraftResult(
    bool Changed,
    long DraftId,
    InspectionDraftReadiness Readiness)
{
    public bool NoChange => !Changed;

    public int CurrentItemCount => Readiness.CurrentItemCount;

    public int FilledItemCount => Readiness.FilledItemCount;

    public int MissingItemCount => Readiness.MissingItemCount;

    public int RequiresReconfirmationCount => Readiness.RequiresReconfirmationCount;

    public bool HasInspectorName => Readiness.HasInspectorName;

    public bool HasCheckDate => Readiness.HasCheckDate;

    public bool AllItemsFilled => Readiness.AllItemsFilled;

    public bool IsDraftComplete => Readiness.IsDraftComplete;
}

public sealed record ReconfirmItemResult(
    bool Changed,
    long DraftId,
    InspectionDraftReadiness Readiness)
{
    public bool IdempotentReplay => !Changed;

    public bool NoChange => !Changed;

    public int CurrentItemCount => Readiness.CurrentItemCount;

    public int FilledItemCount => Readiness.FilledItemCount;

    public int MissingItemCount => Readiness.MissingItemCount;

    public int RequiresReconfirmationCount => Readiness.RequiresReconfirmationCount;

    public bool HasInspectorName => Readiness.HasInspectorName;

    public bool HasCheckDate => Readiness.HasCheckDate;

    public bool AllItemsFilled => Readiness.AllItemsFilled;

    public bool IsDraftComplete => Readiness.IsDraftComplete;
}

public sealed record ClearDraftResult(bool Changed)
{
    public bool NoChange => !Changed;
}

public sealed class InspectionDraftUseCase
{
    public SaveDraftResult SaveDraft(
        StoreDbContext context,
        SaveDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateSaveDraftRequest(request);
        EnsureCleanContext(context);

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            // ponytail: reject pending work, then clear unchanged tracked entities so every operation reloads the database.
            context.ChangeTracker.Clear();
            var task = LoadCurrentTask(context, request.TaskId, request.ProductId, true);
            var requestedItems = request.Items ?? Array.Empty<SaveDraftItemRequest>();
            var taskItemsById = task.Items.ToDictionary(item => item.Id);
            ValidateSaveItemObservations(taskItemsById, requestedItems);

            var draft = task.Draft;
            if (draft is { IsInvalid: true })
            {
                throw new InvalidOperationException(
                    $"Draft {draft.Id} is invalid and cannot be edited.");
            }

            var normalizedInspectorName = NormalizeInspectorName(request.InspectorName);
            var changed = false;
            if (draft is null)
            {
                draft = new InspectionDraft
                {
                    TaskId = task.Id,
                    InspectorName = normalizedInspectorName,
                    CheckDate = request.CheckDate,
                    CreatedAtUtc = request.SavedAtUtc,
                    UpdatedAtUtc = request.SavedAtUtc
                };
                context.Drafts.Add(draft);
                task.Draft = draft;
                changed = true;
            }
            else
            {
                if (!string.Equals(draft.InspectorName, normalizedInspectorName, StringComparison.Ordinal))
                {
                    draft.InspectorName = normalizedInspectorName;
                    changed = true;
                }

                if (draft.CheckDate != request.CheckDate)
                {
                    draft.CheckDate = request.CheckDate;
                    changed = true;
                }
            }

            var draftItemsByTaskItemId = draft.Items.ToDictionary(item => item.TaskItemId);
            foreach (var input in requestedItems)
            {
                var taskItem = taskItemsById[input.TaskItemId];
                if (!draftItemsByTaskItemId.TryGetValue(input.TaskItemId, out var draftItem))
                {
                    if (input.CheckedQty is null)
                    {
                        continue;
                    }

                    draftItem = new InspectionDraftItem
                    {
                        Draft = draft,
                        DraftId = draft.Id,
                        TaskItemId = taskItem.Id,
                        TaskId = task.Id,
                        CheckedQty = input.CheckedQty,
                        ConfirmedAttentionVersion = input.AttentionVersion
                    };
                    context.DraftItems.Add(draftItem);
                    draftItemsByTaskItemId.Add(input.TaskItemId, draftItem);
                    changed = true;
                    continue;
                }

                if (draftItem.CheckedQty != input.CheckedQty)
                {
                    draftItem.CheckedQty = input.CheckedQty;
                    changed = true;
                }
            }

            if (changed && context.Entry(draft).State != EntityState.Added)
            {
                draft.UpdatedAtUtc = request.SavedAtUtc;
            }

            var readiness = BuildReadiness(task, draft, draftItemsByTaskItemId);
            if (changed)
            {
                context.SaveChanges();
            }

            transaction?.Commit();
            return new(changed, draft.Id, readiness);
        }
        catch
        {
            RollbackAndClear(context, ownsTransaction, transaction);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    public ReconfirmItemResult ReconfirmItem(
        StoreDbContext context,
        ReconfirmItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateReconfirmItemRequest(request);
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
            var task = LoadCurrentTask(context, request.TaskId, request.ProductId, true);
            var taskItem = task.Items.SingleOrDefault(item => item.Id == request.TaskItemId);
            if (taskItem is null)
            {
                throw new ArgumentException(
                    $"Task item {request.TaskItemId} does not belong to task {request.TaskId}.",
                    nameof(request));
            }

            if (taskItem.BatchId != request.BatchId)
            {
                throw new ArgumentException(
                    $"Batch {request.BatchId} does not belong to task item {request.TaskItemId}.",
                    nameof(request));
            }

            if (task.Draft is null)
            {
                throw new InvalidOperationException(
                    $"Task {request.TaskId} has no current valid draft.");
            }

            if (task.Draft.IsInvalid)
            {
                throw new InvalidOperationException(
                    $"Draft {task.Draft.Id} is invalid and cannot be confirmed.");
            }

            var draftItem = task.Draft.Items.SingleOrDefault(item => item.TaskItemId == request.TaskItemId);
            if (draftItem is null)
            {
                throw new InvalidOperationException(
                    $"Task item {request.TaskItemId} has no draft input to confirm.");
            }

            if (request.AttentionVersion != taskItem.AttentionVersion)
            {
                throw new ArgumentException(
                    $"Task item {request.TaskItemId} attention version is stale.",
                    nameof(request));
            }

            if (!taskItem.RequiresReconfirmation)
            {
                if (draftItem.ConfirmedAttentionVersion != taskItem.AttentionVersion)
                {
                    throw new InvalidOperationException(
                        $"Task item {request.TaskItemId} confirmation state is inconsistent.");
                }

                var replayReadiness = BuildReadiness(
                    task,
                    task.Draft,
                    task.Draft.Items.ToDictionary(item => item.TaskItemId));
                transaction?.Commit();
                return new(false, task.Draft.Id, replayReadiness);
            }

            if (draftItem.CheckedQty is null)
            {
                throw new InvalidOperationException(
                    $"Task item {request.TaskItemId} has no checked quantity to confirm.");
            }

            draftItem.ConfirmedAttentionVersion = taskItem.AttentionVersion;
            taskItem.RequiresReconfirmation = false;
            taskItem.UpdatedAtUtc = request.ConfirmedAtUtc;
            task.Draft.UpdatedAtUtc = request.ConfirmedAtUtc;

            var readiness = BuildReadiness(
                task,
                task.Draft,
                task.Draft.Items.ToDictionary(item => item.TaskItemId));
            context.SaveChanges();
            transaction?.Commit();
            return new(true, task.Draft.Id, readiness);
        }
        catch
        {
            RollbackAndClear(context, ownsTransaction, transaction);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    public ClearDraftResult ClearDraft(
        StoreDbContext context,
        ClearDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateClearDraftRequest(request);
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
            var task = LoadCurrentTask(context, request.TaskId, request.ProductId, false);
            if (task.Draft is null)
            {
                transaction?.Commit();
                return new(false);
            }

            if (task.Draft.IsInvalid)
            {
                throw new InvalidOperationException(
                    $"Draft {task.Draft.Id} is invalid and cannot be cleared.");
            }

            context.DraftItems.RemoveRange(task.Draft.Items.ToArray());
            context.Drafts.Remove(task.Draft);
            context.SaveChanges();
            transaction?.Commit();
            return new(true);
        }
        catch
        {
            RollbackAndClear(context, ownsTransaction, transaction);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static ProductTask LoadCurrentTask(
        StoreDbContext context,
        long taskId,
        long productId,
        bool requireVersionConsistency)
    {
        var task = context.Tasks
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.Draft)
            .ThenInclude(draft => draft!.Items)
            .SingleOrDefault(candidate => candidate.Id == taskId);
        if (task is null)
        {
            throw new KeyNotFoundException($"Task {taskId} does not exist.");
        }

        if (!string.Equals(task.Status, "open", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Task {taskId} is not open.");
        }

        if (task.ProductId != productId)
        {
            throw new ArgumentException(
                $"Task {taskId} does not belong to product {productId}.",
                nameof(productId));
        }

        if (!context.Products.AsNoTracking().Any(product => product.Id == task.ProductId))
        {
            throw new KeyNotFoundException($"Product {task.ProductId} does not exist.");
        }

        var batchIds = task.Items.Select(item => item.BatchId).Distinct().ToArray();
        var batches = batchIds.Length == 0
            ? new Dictionary<long, Batch>()
            : context.Batches
                .AsNoTracking()
                .Where(batch => batchIds.Contains(batch.Id))
                .ToDictionary(batch => batch.Id);
        foreach (var item in task.Items)
        {
            if (item.ProductId != productId)
            {
                throw new InvalidOperationException(
                    $"Task item {item.Id} does not belong to product {productId}.");
            }

            if (!batches.TryGetValue(item.BatchId, out var batch))
            {
                throw new InvalidOperationException(
                    $"Batch {item.BatchId} for task item {item.Id} does not exist.");
            }

            if (batch.ProductId != productId)
            {
                throw new InvalidOperationException(
                    $"Batch {batch.Id} does not belong to product {productId}.");
            }

            if (requireVersionConsistency && item.AttentionVersion != batch.AttentionVersion)
            {
                throw new InvalidOperationException(
                    $"Task item {item.Id} and batch {batch.Id} attention versions differ.");
            }
        }

        if (task.Draft is not null)
        {
            var taskItemIds = task.Items.Select(item => item.Id).ToHashSet();
            if (task.Draft.Items.Any(item => !taskItemIds.Contains(item.TaskItemId)))
            {
                throw new InvalidOperationException(
                    $"Draft {task.Draft.Id} contains an item outside the current task.");
            }
        }

        return task;
    }

    private static InspectionDraftReadiness BuildReadiness(
        ProductTask task,
        InspectionDraft draft,
        IReadOnlyDictionary<long, InspectionDraftItem> draftItems)
    {
        var filledItemCount = task.Items.Count(item =>
            draftItems.TryGetValue(item.Id, out var draftItem) &&
            draftItem.CheckedQty is not null);
        var currentItemCount = task.Items.Count;
        var missingItemCount = currentItemCount - filledItemCount;
        var requiresReconfirmationCount = task.Items.Count(item => item.RequiresReconfirmation);
        var hasInspectorName = !string.IsNullOrWhiteSpace(draft.InspectorName);
        var hasCheckDate = draft.CheckDate is not null;
        var allItemsFilled = missingItemCount == 0;
        return new(
            currentItemCount,
            filledItemCount,
            missingItemCount,
            requiresReconfirmationCount,
            hasInspectorName,
            hasCheckDate,
            allItemsFilled,
            allItemsFilled && hasInspectorName && hasCheckDate && requiresReconfirmationCount == 0);
    }

    private static void ValidateSaveDraftRequest(SaveDraftRequest request)
    {
        ValidateTaskAndProductIds(request.TaskId, request.ProductId);
        ValidateUtc(request.SavedAtUtc, nameof(request.SavedAtUtc));
        if (request.CheckDate is DateOnly checkDate && checkDate > request.BusinessDate)
        {
            throw new ArgumentException(
                "CheckDate cannot be later than BusinessDate.",
                nameof(request.CheckDate));
        }

        var normalizedInspectorName = NormalizeInspectorName(request.InspectorName);
        if (normalizedInspectorName is not null && normalizedInspectorName.Length > 200)
        {
            throw new ArgumentException(
                "InspectorName cannot exceed 200 characters.",
                nameof(request.InspectorName));
        }

        var taskItemIds = new HashSet<long>();
        var batchIds = new HashSet<long>();
        foreach (var item in request.Items ?? Array.Empty<SaveDraftItemRequest>())
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "Draft items cannot contain null values.",
                    nameof(request.Items));
            }

            if (item.TaskItemId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(item.TaskItemId));
            }

            if (item.BatchId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(item.BatchId));
            }

            if (item.AttentionVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(item.AttentionVersion));
            }

            if (item.CheckedQty is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(item.CheckedQty));
            }

            if (!taskItemIds.Add(item.TaskItemId) || !batchIds.Add(item.BatchId))
            {
                throw new ArgumentException(
                    "Draft items must contain unique TaskItemId and BatchId values.",
                    nameof(request.Items));
            }
        }
    }

    private static void ValidateSaveItemObservations(
        IReadOnlyDictionary<long, ProductTaskItem> taskItemsById,
        IReadOnlyList<SaveDraftItemRequest> requestedItems)
    {
        foreach (var input in requestedItems)
        {
            if (!taskItemsById.TryGetValue(input.TaskItemId, out var taskItem))
            {
                throw new ArgumentException(
                    $"Task item {input.TaskItemId} does not belong to the current task.",
                    nameof(requestedItems));
            }

            if (taskItem.BatchId != input.BatchId)
            {
                throw new ArgumentException(
                    $"Batch {input.BatchId} does not belong to task item {input.TaskItemId}.",
                    nameof(requestedItems));
            }

            if (input.AttentionVersion > taskItem.AttentionVersion)
            {
                throw new ArgumentException(
                    $"Task item {input.TaskItemId} attention version is from the future.",
                    nameof(requestedItems));
            }
        }
    }

    private static void ValidateReconfirmItemRequest(ReconfirmItemRequest request)
    {
        ValidateTaskAndProductIds(request.TaskId, request.ProductId);
        if (request.TaskItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TaskItemId));
        }

        if (request.BatchId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BatchId));
        }

        if (request.AttentionVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.AttentionVersion));
        }

        ValidateUtc(request.ConfirmedAtUtc, nameof(request.ConfirmedAtUtc));
    }

    private static void ValidateClearDraftRequest(ClearDraftRequest request)
    {
        ValidateTaskAndProductIds(request.TaskId, request.ProductId);
    }

    private static void ValidateTaskAndProductIds(long taskId, long productId)
    {
        if (taskId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskId));
        }

        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId));
        }
    }

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException($"{parameterName} must be UTC.", parameterName);
        }
    }

    private static string? NormalizeInspectorName(string? inspectorName)
    {
        return string.IsNullOrWhiteSpace(inspectorName)
            ? null
            : inspectorName.Trim();
    }

    private static void EnsureCleanContext(StoreDbContext context)
    {
        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "StoreDbContext must have no pending changes before a draft operation.");
        }
    }

    private static void RollbackAndClear(
        StoreDbContext context,
        bool ownsTransaction,
        IDbContextTransaction? transaction)
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

        context.ChangeTracker.Clear();
    }
}
