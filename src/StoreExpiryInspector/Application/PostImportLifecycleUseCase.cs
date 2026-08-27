using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public static class PostImportBatchFactKinds
{
    public const string New = "new";

    public const string Existing = "existing";
}

public sealed record PostImportBatchFact(
    long BatchId,
    string Kind,
    int PreviousMaxArrivalQty,
    int CurrentArrivalQty,
    int? ExpectedAttentionVersionBefore = null,
    int? TargetAttentionVersionAfter = null);

public sealed record PostImportProductGroup
{
    public PostImportProductGroup(
        long productId,
        IReadOnlyList<PostImportBatchFact> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ProductId = productId;
        Batches = Array.AsReadOnly(batches.ToArray());
    }

    public long ProductId { get; }

    public IReadOnlyList<PostImportBatchFact> Batches { get; }
}

public sealed record PostImportLifecycleRequest
{
    public PostImportLifecycleRequest(
        long importId,
        DateOnly businessDate,
        DateTime occurredAtUtc,
        IReadOnlyList<PostImportProductGroup> productGroups)
    {
        ArgumentNullException.ThrowIfNull(productGroups);
        ImportId = importId;
        BusinessDate = businessDate;
        OccurredAtUtc = occurredAtUtc;
        ProductGroups = Array.AsReadOnly(productGroups.ToArray());
    }

    public long ImportId { get; }

    public DateOnly BusinessDate { get; }

    public DateTime OccurredAtUtc { get; }

    public IReadOnlyList<PostImportProductGroup> ProductGroups { get; }
}

public sealed record PostImportLifecycleResult(
    int StartedBatchCount,
    int NewArrivalBatchCount,
    int ResumedBatchCount,
    int AggregatedProductCount,
    int LifecycleEventCount)
{
    public int ChangedBatchCount =>
        StartedBatchCount + NewArrivalBatchCount + ResumedBatchCount;
}

public sealed class PostImportLifecycleUseCase
{
    private readonly ProductTaskAggregator _taskAggregator;

    public PostImportLifecycleUseCase(ProductTaskAggregator? taskAggregator = null)
    {
        _taskAggregator = taskAggregator ?? new ProductTaskAggregator();
    }

    public PostImportLifecycleResult Execute(
        StoreDbContext context,
        PostImportLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestShape(request);

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        var journal = new MutationJournal(context);
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            var productIds = request.ProductGroups
                .Select(group => group.ProductId)
                .ToArray();
            var batchIds = request.ProductGroups
                .SelectMany(group => group.Batches)
                .Select(fact => fact.BatchId)
                .ToArray();

            var import = context.Imports
                .AsNoTracking()
                .SingleOrDefault(candidate => candidate.Id == request.ImportId);
            if (import is null)
            {
                throw new KeyNotFoundException($"Import {request.ImportId} does not exist.");
            }

            if (!string.Equals(import.Status, ImportStatuses.Succeeded, StringComparison.Ordinal) ||
                import.IsUndone)
            {
                throw new InvalidOperationException(
                    $"Import {request.ImportId} must be succeeded and not undone.");
            }

            var products = context.Products
                .AsTracking()
                .Where(product => productIds.Contains(product.Id))
                .ToDictionary(product => product.Id);
            if (products.Count != productIds.Length)
            {
                var missingProductId = productIds.First(productId => !products.ContainsKey(productId));
                throw new KeyNotFoundException($"Product {missingProductId} does not exist.");
            }

            var batches = context.Batches
                .AsTracking()
                .Where(batch => batchIds.Contains(batch.Id))
                .ToDictionary(batch => batch.Id);
            if (batches.Count != batchIds.Length)
            {
                var missingBatchId = batchIds.First(batchId => !batches.ContainsKey(batchId));
                throw new KeyNotFoundException($"Batch {missingBatchId} does not exist.");
            }

            ValidateDatabaseFacts(request, products, batches);
            var prepared = PrepareFacts(request, products, batches);
            journal.CaptureOpenTasks(context, productIds);

            var pendingByProduct = new Dictionary<long, List<ProductTaskBatchResult>>();
            var startedBatchCount = 0;
            var newArrivalBatchCount = 0;
            var resumedBatchCount = 0;
            var lifecycleEventCount = 0;

            foreach (var item in prepared)
            {
                if (item.Action == PreparedAction.None)
                {
                    continue;
                }

                var batch = item.Batch;
                var stage = item.Stage!;
                if (item.Action == PreparedAction.New)
                {
                    if (!IsUnprocessedNewBatch(batch))
                    {
                        continue;
                    }

                    journal.Capture(batch);
                    batch.LifecycleGeneration = item.Product.LifecycleGeneration;
                    batch.TrackingStatus = "active";
                    batch.StopReason = null;
                    batch.StoppedAtUtc = null;
                    batch.CurrentStage = stage.CurrentStage;
                    batch.NextTriggerDate = stage.NextTriggerDate;
                    batch.UpdatedAtUtc = request.OccurredAtUtc;
                    startedBatchCount++;
                }
                else
                {
                    journal.Capture(batch);
                    ApplyAttentionCompareAndSet(
                        context,
                        batch,
                        item.Fact.ExpectedAttentionVersionBefore!.Value,
                        item.Fact.TargetAttentionVersionAfter!.Value);

                    if (item.Action == PreparedAction.Resume)
                    {
                        batch.TrackingStatus = "active";
                        batch.StopReason = null;
                        batch.StoppedAtUtc = null;
                        resumedBatchCount++;
                        var lifecycleEvent = new LifecycleEvent
                        {
                            ProductId = item.Product.Id,
                            BatchId = batch.Id,
                            EventType = "batch_tracking_resumed",
                            Reason = "new_arrival_after_batch_checked_zero",
                            OccurredAtUtc = request.OccurredAtUtc,
                            SourceImportId = request.ImportId
                        };
                        context.LifecycleEvents.Add(lifecycleEvent);
                        journal.TrackAdded(lifecycleEvent);
                        lifecycleEventCount++;
                    }
                    else
                    {
                        newArrivalBatchCount++;
                    }

                    batch.CurrentStage = stage.CurrentStage;
                    batch.NextTriggerDate = stage.NextTriggerDate;
                    batch.UpdatedAtUtc = request.OccurredAtUtc;
                }

                if (ExpiryStageCalculator.GetStagePriority(stage.CurrentStage) > 0)
                {
                    if (!pendingByProduct.TryGetValue(item.Product.Id, out var results))
                    {
                        results = new List<ProductTaskBatchResult>();
                        pendingByProduct.Add(item.Product.Id, results);
                    }

                    results.Add(new ProductTaskBatchResult(
                        batch.Id,
                        stage.CurrentStage,
                        batch.AttentionVersion,
                        item.Action is PreparedAction.Arrival or PreparedAction.Resume));
                }
            }

            if (context.ChangeTracker.HasChanges())
            {
                context.SaveChanges();
            }

            var aggregatedProductCount = 0;
            foreach (var productGroup in pendingByProduct)
            {
                _taskAggregator.Aggregate(
                    context,
                    new ProductTaskAggregationRequest(
                        productGroup.Key,
                        productGroup.Value,
                        request.OccurredAtUtc));
                journal.ObserveAdded(context, productIds);
                aggregatedProductCount++;
            }

            if (context.ChangeTracker.HasChanges())
            {
                context.SaveChanges();
            }

            transaction?.Commit();
            return new(
                startedBatchCount,
                newArrivalBatchCount,
                resumedBatchCount,
                aggregatedProductCount,
                lifecycleEventCount);
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

                context.ChangeTracker.Clear();
            }
            else
            {
                journal.Restore();
            }

            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static void ValidateRequestShape(PostImportLifecycleRequest request)
    {
        if (request.ImportId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ImportId));
        }

        if (request.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.ProductGroups);
        if (request.ProductGroups.Count == 0)
        {
            throw new ArgumentException("At least one product group is required.", nameof(request));
        }

        var productIds = new HashSet<long>();
        var batchIds = new HashSet<long>();
        foreach (var group in request.ProductGroups)
        {
            ArgumentNullException.ThrowIfNull(group);
            if (group.ProductId <= 0 || !productIds.Add(group.ProductId))
            {
                throw new ArgumentException(
                    "Product groups must contain unique positive product ids.",
                    nameof(request));
            }

            ArgumentNullException.ThrowIfNull(group.Batches);
            if (group.Batches.Count == 0)
            {
                throw new ArgumentException(
                    "Every product group must contain at least one batch fact.",
                    nameof(request));
            }

            foreach (var fact in group.Batches)
            {
                ArgumentNullException.ThrowIfNull(fact);
                if (fact.BatchId <= 0 || !batchIds.Add(fact.BatchId))
                {
                    throw new ArgumentException(
                        "Batch facts must contain unique positive batch ids.",
                        nameof(request));
                }

                if (fact.PreviousMaxArrivalQty < 0 || fact.CurrentArrivalQty < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(fact));
                }

                if (string.Equals(fact.Kind, PostImportBatchFactKinds.New, StringComparison.Ordinal))
                {
                    if (fact.ExpectedAttentionVersionBefore.HasValue ||
                        fact.TargetAttentionVersionAfter.HasValue)
                    {
                        throw new ArgumentException(
                            "New batch facts must not contain attention versions.",
                            nameof(request));
                    }

                    continue;
                }

                if (!string.Equals(fact.Kind, PostImportBatchFactKinds.Existing, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Batch fact kind must be 'new' or 'existing'.",
                        nameof(request));
                }

                if (fact.ExpectedAttentionVersionBefore is not int expected || expected < 0 ||
                    fact.TargetAttentionVersionAfter is not int target || target < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(fact));
                }

                var hasNewArrival = fact.CurrentArrivalQty > fact.PreviousMaxArrivalQty;
                var expectedTarget = hasNewArrival
                    ? checked(expected + 1)
                    : expected;
                if (target != expectedTarget)
                {
                    throw new ArgumentException(
                        "Existing batch attention target does not match the frozen arrival fact.",
                        nameof(request));
                }
            }
        }
    }

    private static void ValidateDatabaseFacts(
        PostImportLifecycleRequest request,
        IReadOnlyDictionary<long, Product> products,
        IReadOnlyDictionary<long, Batch> batches)
    {
        foreach (var group in request.ProductGroups)
        {
            var product = products[group.ProductId];
            if (product.EffectiveStockQty <= 0)
            {
                throw new InvalidOperationException(
                    $"Product {product.Id} must have positive effective stock quantity.");
            }

            if (product.LastSeenImportId != request.ImportId)
            {
                throw new InvalidOperationException(
                    $"Product {product.Id} was not seen by import {request.ImportId}.");
            }

            foreach (var fact in group.Batches)
            {
                var batch = batches[fact.BatchId];
                if (batch.ProductId != product.Id)
                {
                    throw new ArgumentException(
                        $"Batch {batch.Id} does not belong to product {product.Id}.",
                        nameof(request));
                }

                if (batch.LastSeenImportId != request.ImportId)
                {
                    throw new InvalidOperationException(
                        $"Batch {batch.Id} was not seen by import {request.ImportId}.");
                }

                var expectedMaxArrivalQty = Math.Max(
                    fact.PreviousMaxArrivalQty,
                    fact.CurrentArrivalQty);
                if (batch.CurrentArrivalQty != fact.CurrentArrivalQty ||
                    batch.MaxArrivalQty != expectedMaxArrivalQty)
                {
                    throw new InvalidOperationException(
                        $"Batch {batch.Id} does not match the frozen import after values.");
                }

                if (batch.AttentionVersion < 0 || batch.HandledAttentionVersion < 0)
                {
                    throw new InvalidOperationException(
                        $"Batch {batch.Id} contains a negative attention version.");
                }
            }
        }
    }

    private static IReadOnlyList<PreparedBatch> PrepareFacts(
        PostImportLifecycleRequest request,
        IReadOnlyDictionary<long, Product> products,
        IReadOnlyDictionary<long, Batch> batches)
    {
        var prepared = new List<PreparedBatch>();
        foreach (var group in request.ProductGroups)
        {
            var product = products[group.ProductId];
            foreach (var fact in group.Batches)
            {
                var batch = batches[fact.BatchId];
                if (string.Equals(fact.Kind, PostImportBatchFactKinds.New, StringComparison.Ordinal))
                {
                    prepared.Add(new(
                        product,
                        batch,
                        fact,
                        ExpiryStageCalculator.Calculate(
                            request.BusinessDate,
                            batch.ExpiryDate,
                            batch.ShelfLifeValue,
                            batch.ShelfLifeUnit),
                        PreparedAction.New));
                    continue;
                }

                var expected = fact.ExpectedAttentionVersionBefore!.Value;
                var target = fact.TargetAttentionVersionAfter!.Value;
                if (batch.AttentionVersion != expected && batch.AttentionVersion != target)
                {
                    throw new InvalidOperationException(
                        $"Batch {batch.Id} attention version is stale or conflicting.");
                }

                if (fact.CurrentArrivalQty <= fact.PreviousMaxArrivalQty ||
                    batch.AttentionVersion == target ||
                    product.IsStockZeroTerminated)
                {
                    prepared.Add(new(product, batch, fact, null, PreparedAction.None));
                    continue;
                }

                if (batch.TrackingStatus == "stopped" &&
                    batch.StopReason == "batch_checked_zero")
                {
                    if (batch.LifecycleGeneration != product.LifecycleGeneration)
                    {
                        throw new InvalidOperationException(
                            $"Batch {batch.Id} is not in the current product lifecycle generation.");
                    }

                    prepared.Add(new(
                        product,
                        batch,
                        fact,
                        ExpiryStageCalculator.Calculate(
                            request.BusinessDate,
                            batch.ExpiryDate,
                            batch.ShelfLifeValue,
                            batch.ShelfLifeUnit),
                        PreparedAction.Resume));
                    continue;
                }

                if (batch.TrackingStatus != "active")
                {
                    prepared.Add(new(product, batch, fact, null, PreparedAction.None));
                    continue;
                }

                if (batch.StopReason is not null || batch.StoppedAtUtc.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Active batch {batch.Id} contains stopped-state fields.");
                }

                prepared.Add(new(
                    product,
                    batch,
                    fact,
                    ExpiryStageCalculator.Calculate(
                        request.BusinessDate,
                        batch.ExpiryDate,
                        batch.ShelfLifeValue,
                        batch.ShelfLifeUnit),
                    PreparedAction.Arrival));
            }
        }

        return prepared;
    }

    private static bool IsUnprocessedNewBatch(Batch batch) =>
        batch.LifecycleGeneration == 0 &&
        batch.TrackingStatus == "active" &&
        batch.StopReason is null &&
        batch.StoppedAtUtc is null &&
        batch.CurrentStage == ExpiryStageCalculator.None &&
        batch.NextTriggerDate is null &&
        batch.AttentionVersion == 0 &&
        batch.HandledAttentionVersion == 0;

    private static void ApplyAttentionCompareAndSet(
        StoreDbContext context,
        Batch batch,
        int expected,
        int target)
    {
        var changedRows = context.Database.ExecuteSqlInterpolated($"""
            UPDATE batches
            SET attention_version = {target}
            WHERE id = {batch.Id} AND attention_version = {expected}
            """);
        if (changedRows != 1)
        {
            throw new InvalidOperationException(
                $"Batch {batch.Id} attention version compare-and-set failed.");
        }

        batch.AttentionVersion = target;
    }

    private enum PreparedAction
    {
        None,
        New,
        Arrival,
        Resume
    }

    private sealed record PreparedBatch(
        Product Product,
        Batch Batch,
        PostImportBatchFact Fact,
        ExpiryStageResult? Stage,
        PreparedAction Action);

    private sealed class MutationJournal
    {
        private readonly StoreDbContext _context;
        private readonly Dictionary<Batch, BatchState> _batches = new();
        private readonly Dictionary<ProductTask, TaskState> _tasks = new();
        private readonly Dictionary<ProductTaskItem, ItemState> _items = new();
        private readonly HashSet<object> _known = new();
        private readonly List<object> _added = new();

        public MutationJournal(StoreDbContext context)
        {
            _context = context;
            foreach (var entry in context.ChangeTracker.Entries())
            {
                _known.Add(entry.Entity);
            }
        }

        public void CaptureOpenTasks(StoreDbContext context, IReadOnlyList<long> productIds)
        {
            var tasks = context.Tasks
                .Include(task => task.Items)
                .Include(task => task.Draft)
                .Where(task =>
                    productIds.Contains(task.ProductId) &&
                    task.Status == "open")
                .ToArray();
            foreach (var task in tasks)
            {
                Capture(task);
                foreach (var item in task.Items)
                {
                    Capture(item);
                }
            }
        }

        public void Capture(Batch batch)
        {
            if (_batches.ContainsKey(batch))
            {
                return;
            }

            _known.Add(batch);
            _batches.Add(batch, new(
                batch.LifecycleGeneration,
                batch.TrackingStatus,
                batch.StopReason,
                batch.StoppedAtUtc,
                batch.CurrentStage,
                batch.NextTriggerDate,
                batch.AttentionVersion,
                batch.UpdatedAtUtc,
                _context.Entry(batch).State));
        }

        public void Capture(ProductTask task)
        {
            if (_tasks.ContainsKey(task))
            {
                return;
            }

            _known.Add(task);
            _tasks.Add(task, new(
                task.HighestStage,
                task.UpdatedAtUtc,
                _context.Entry(task).State));
        }

        public void Capture(ProductTaskItem item)
        {
            if (_items.ContainsKey(item))
            {
                return;
            }

            _known.Add(item);
            _items.Add(item, new(
                item.Stage,
                item.AttentionVersion,
                item.RequiresReconfirmation,
                item.UpdatedAtUtc,
                _context.Entry(item).State));
        }

        public void TrackAdded(object entity)
        {
            _known.Add(entity);
            _added.Add(entity);
        }

        public void ObserveAdded(StoreDbContext context, IReadOnlyList<long> productIds)
        {
            foreach (var entry in context.ChangeTracker.Entries<ProductTask>())
            {
                if (productIds.Contains(entry.Entity.ProductId) && _known.Add(entry.Entity))
                {
                    _added.Add(entry.Entity);
                }
            }

            foreach (var entry in context.ChangeTracker.Entries<ProductTaskItem>())
            {
                if (productIds.Contains(entry.Entity.ProductId) && _known.Add(entry.Entity))
                {
                    _added.Add(entry.Entity);
                }
            }
        }

        public void Restore()
        {
            foreach (var (batch, state) in _batches)
            {
                batch.LifecycleGeneration = state.LifecycleGeneration;
                batch.TrackingStatus = state.TrackingStatus;
                batch.StopReason = state.StopReason;
                batch.StoppedAtUtc = state.StoppedAtUtc;
                batch.CurrentStage = state.CurrentStage;
                batch.NextTriggerDate = state.NextTriggerDate;
                batch.AttentionVersion = state.AttentionVersion;
                batch.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(batch).State = state.EntityState;
            }

            foreach (var (task, state) in _tasks)
            {
                task.HighestStage = state.HighestStage;
                task.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(task).State = state.EntityState;
            }

            foreach (var (item, state) in _items)
            {
                item.Stage = state.Stage;
                item.AttentionVersion = state.AttentionVersion;
                item.RequiresReconfirmation = state.RequiresReconfirmation;
                item.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(item).State = state.EntityState;
            }

            for (var index = _added.Count - 1; index >= 0; index--)
            {
                _context.Entry(_added[index]).State = EntityState.Detached;
            }
        }

        private sealed record BatchState(
            int LifecycleGeneration,
            string TrackingStatus,
            string? StopReason,
            DateTime? StoppedAtUtc,
            string CurrentStage,
            DateOnly? NextTriggerDate,
            int AttentionVersion,
            DateTime UpdatedAtUtc,
            EntityState EntityState);

        private sealed record TaskState(
            string HighestStage,
            DateTime UpdatedAtUtc,
            EntityState EntityState);

        private sealed record ItemState(
            string Stage,
            int AttentionVersion,
            bool RequiresReconfirmation,
            DateTime UpdatedAtUtc,
            EntityState EntityState);
    }
}
