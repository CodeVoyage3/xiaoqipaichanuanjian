using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public readonly record struct ProductTaskBatchResult(
    long BatchId,
    string Stage,
    int AttentionVersion,
    bool RequiresReconfirmation);

public sealed record ProductTaskAggregationRequest(
    long ProductId,
    IReadOnlyList<ProductTaskBatchResult> BatchResults,
    DateTime UpdatedAtUtc);

public sealed record ProductTaskAggregationResult(
    bool Changed,
    long? TaskId,
    string? HighestStage,
    int AddedItemCount,
    int UpdatedItemCount);

public sealed class ProductTaskAggregator
{
    public ProductTaskAggregationResult Aggregate(
        StoreDbContext context,
        ProductTaskAggregationRequest request)
    {
        ValidateRequest(context, request);

        var batchResults = request.BatchResults;
        var batchIds = batchResults.Select(result => result.BatchId).ToArray();
        var productExists = context.Products
            .AsNoTracking()
            .Any(product => product.Id == request.ProductId);
        if (!productExists)
        {
            throw new KeyNotFoundException($"Product {request.ProductId} does not exist.");
        }

        var batchesById = context.Batches
            .AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionary(batch => batch.Id);
        foreach (var result in batchResults)
        {
            if (!batchesById.TryGetValue(result.BatchId, out var batch))
            {
                throw new KeyNotFoundException($"Batch {result.BatchId} does not exist.");
            }

            if (batch.ProductId != request.ProductId)
            {
                throw new ArgumentException(
                    $"Batch {result.BatchId} does not belong to product {request.ProductId}.",
                    nameof(request));
            }

            if (batch.AttentionVersion != result.AttentionVersion)
            {
                throw new ArgumentException(
                    $"Batch {result.BatchId} attention version is stale.",
                    nameof(request));
            }
        }

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        var journal = new MutationJournal(context);
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            var task = context.Tasks
                .Include(candidate => candidate.Items)
                .Include(candidate => candidate.Draft)
                .SingleOrDefault(candidate =>
                    candidate.ProductId == request.ProductId &&
                    candidate.Status == "open");
            var actionableResults = batchResults
                .Where(result => ExpiryStageCalculator.GetStagePriority(result.Stage) > 0)
                .ToArray();

            if (task is null)
            {
                if (actionableResults.Length == 0)
                {
                    transaction?.Commit();
                    return new(false, null, null, 0, 0);
                }

                var newTask = new ProductTask
                {
                    ProductId = request.ProductId,
                    Status = "open",
                    HighestStage = HighestStage(actionableResults.Select(result => result.Stage)),
                    CreatedAtUtc = request.UpdatedAtUtc,
                    UpdatedAtUtc = request.UpdatedAtUtc
                };
                context.Tasks.Add(newTask);
                journal.TrackAdded(newTask);

                foreach (var result in actionableResults)
                {
                    var item = new ProductTaskItem
                    {
                        Task = newTask,
                        BatchId = result.BatchId,
                        ProductId = request.ProductId,
                        Stage = result.Stage,
                        AttentionVersion = result.AttentionVersion,
                        RequiresReconfirmation = result.RequiresReconfirmation,
                        CreatedAtUtc = request.UpdatedAtUtc,
                        UpdatedAtUtc = request.UpdatedAtUtc
                    };
                    context.TaskItems.Add(item);
                    journal.TrackAdded(item);
                }

                context.SaveChanges();
                transaction?.Commit();
                return new(true, newTask.Id, newTask.HighestStage, actionableResults.Length, 0);
            }

            var itemByBatchId = task.Items.ToDictionary(item => item.BatchId);
            var hasValidDraft = task.Draft is { IsInvalid: false };
            var addedItemCount = 0;
            var updatedItemCount = 0;
            var changed = false;

            foreach (var result in actionableResults)
            {
                if (!itemByBatchId.TryGetValue(result.BatchId, out var item))
                {
                    item = new ProductTaskItem
                    {
                        Task = task,
                        BatchId = result.BatchId,
                        ProductId = request.ProductId,
                        Stage = result.Stage,
                        AttentionVersion = result.AttentionVersion,
                        RequiresReconfirmation = result.RequiresReconfirmation,
                        CreatedAtUtc = request.UpdatedAtUtc,
                        UpdatedAtUtc = request.UpdatedAtUtc
                    };
                    context.TaskItems.Add(item);
                    journal.TrackAdded(item);
                    itemByBatchId.Add(result.BatchId, item);
                    addedItemCount++;
                    changed = true;
                    continue;
                }

                var stageComparison = ExpiryStageCalculator.CompareStages(result.Stage, item.Stage);
                if (stageComparison < 0 || result.AttentionVersion < item.AttentionVersion)
                {
                    throw new ArgumentException(
                        $"Batch {result.BatchId} contains a stage or attention version downgrade.",
                        nameof(request));
                }

                var stageChanged = stageComparison > 0;
                var versionChanged = result.AttentionVersion > item.AttentionVersion;
                var reconfirmationChanged = result.RequiresReconfirmation && !item.RequiresReconfirmation;
                if (!stageChanged && !versionChanged && !reconfirmationChanged)
                {
                    continue;
                }

                journal.Capture(item);
                if (stageChanged)
                {
                    item.Stage = result.Stage;
                }

                if (versionChanged)
                {
                    item.AttentionVersion = result.AttentionVersion;
                }

                if (reconfirmationChanged || (hasValidDraft && (stageChanged || versionChanged)))
                {
                    item.RequiresReconfirmation = true;
                }

                item.UpdatedAtUtc = request.UpdatedAtUtc;
                updatedItemCount++;
                changed = true;
            }

            if (!changed)
            {
                transaction?.Commit();
                return new(false, task.Id, task.HighestStage, 0, 0);
            }

            var highestStage = HighestStage(task.Items.Select(item => item.Stage));
            if (!string.Equals(task.HighestStage, highestStage, StringComparison.Ordinal))
            {
                journal.Capture(task);
                task.HighestStage = highestStage;
            }

            journal.Capture(task);
            task.UpdatedAtUtc = request.UpdatedAtUtc;

            context.SaveChanges();
            transaction?.Commit();
            return new(true, task.Id, task.HighestStage, addedItemCount, updatedItemCount);
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

            journal.Restore();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static void ValidateRequest(
        StoreDbContext context,
        ProductTaskAggregationRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BatchResults);
        if (request.ProductId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ProductId));
        }

        if (request.UpdatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("UpdatedAtUtc must be UTC.", nameof(request));
        }

        var batchIds = new HashSet<long>();
        foreach (var result in request.BatchResults)
        {
            if (result.BatchId <= 0 || !batchIds.Add(result.BatchId))
            {
                throw new ArgumentException(
                    "Batch results must contain unique positive batch ids.",
                    nameof(request));
            }

            ExpiryStageCalculator.GetStagePriority(result.Stage);
            if (result.AttentionVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(result.AttentionVersion));
            }
        }
    }

    private static string HighestStage(IEnumerable<string> stages)
    {
        var highest = ExpiryStageCalculator.None;
        foreach (var stage in stages)
        {
            if (ExpiryStageCalculator.CompareStages(stage, highest) > 0)
            {
                highest = stage;
            }
        }

        return highest == ExpiryStageCalculator.None
            ? throw new InvalidOperationException("A task must contain a trackable stage.")
            : highest;
    }

    private sealed class MutationJournal
    {
        private readonly StoreDbContext _context;
        private readonly Dictionary<ProductTask, (string HighestStage, DateTime UpdatedAtUtc, EntityState State)> _tasks = new();
        private readonly Dictionary<ProductTaskItem, (string Stage, int AttentionVersion, bool RequiresReconfirmation, DateTime UpdatedAtUtc, EntityState State)> _items = new();
        private readonly List<object> _added = new();

        public MutationJournal(StoreDbContext context)
        {
            _context = context;
        }

        public void Capture(ProductTask task)
        {
            if (!_tasks.ContainsKey(task))
            {
                _tasks.Add(task, (task.HighestStage, task.UpdatedAtUtc, _context.Entry(task).State));
            }
        }

        public void Capture(ProductTaskItem item)
        {
            if (!_items.ContainsKey(item))
            {
                _items.Add(item, (
                    item.Stage,
                    item.AttentionVersion,
                    item.RequiresReconfirmation,
                    item.UpdatedAtUtc,
                    _context.Entry(item).State));
            }
        }

        public void TrackAdded(object entity) => _added.Add(entity);

        public void Restore()
        {
            foreach (var (task, values) in _tasks)
            {
                task.HighestStage = values.HighestStage;
                task.UpdatedAtUtc = values.UpdatedAtUtc;
                _context.Entry(task).State = values.State;
            }

            foreach (var (item, values) in _items)
            {
                item.Stage = values.Stage;
                item.AttentionVersion = values.AttentionVersion;
                item.RequiresReconfirmation = values.RequiresReconfirmation;
                item.UpdatedAtUtc = values.UpdatedAtUtc;
                _context.Entry(item).State = values.State;
            }

            for (var index = _added.Count - 1; index >= 0; index--)
            {
                _context.Entry(_added[index]).State = EntityState.Detached;
            }
        }
    }
}
