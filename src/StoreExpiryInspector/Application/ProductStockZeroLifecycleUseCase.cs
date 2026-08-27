using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public sealed record ProductStockZeroRequest(
    long ProductId,
    DateTime OccurredAtUtc,
    long? SourceImportId = null,
    long? SourceAdjustmentId = null);

public sealed record ProductStockZeroResult(
    bool ProductTerminated,
    int StoppedBatchCount,
    bool TaskClosed,
    bool DraftInvalidated,
    int LifecycleEventCount);

public sealed class ProductStockZeroLifecycleUseCase
{
    public ProductStockZeroResult Execute(
        StoreDbContext context,
        ProductStockZeroRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        var journal = new MutationJournal(context);
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            var product = context.Products
                .AsTracking()
                .SingleOrDefault(candidate => candidate.Id == request.ProductId);
            if (product is null)
            {
                throw new KeyNotFoundException($"Product {request.ProductId} does not exist.");
            }

            ValidateSource(context, product.Id, request);
            if (product.EffectiveStockQty != 0)
            {
                throw new InvalidOperationException(
                    $"Product {product.Id} must have an effective stock quantity of zero.");
            }

            var nextLifecycleGeneration = product.IsStockZeroTerminated
                ? product.LifecycleGeneration
                : checked(product.LifecycleGeneration + 1);

            var batches = context.Batches
                .AsTracking()
                .Where(batch => batch.ProductId == product.Id)
                .ToArray();
            var task = context.Tasks
                .Include(candidate => candidate.Items)
                .Include(candidate => candidate.Draft)
                .SingleOrDefault(candidate =>
                    candidate.ProductId == product.Id &&
                    candidate.Status == "open");

            var productTerminated = !product.IsStockZeroTerminated;
            if (productTerminated)
            {
                journal.Capture(product);
                product.IsStockZeroTerminated = true;
                product.LifecycleGeneration = nextLifecycleGeneration;
                product.UpdatedAtUtc = request.OccurredAtUtc;
                var lifecycleEvent = new LifecycleEvent
                {
                    ProductId = product.Id,
                    EventType = "product_stock_zero",
                    Reason = "product_stock_zero",
                    OccurredAtUtc = request.OccurredAtUtc,
                    SourceImportId = request.SourceImportId,
                    SourceAdjustmentId = request.SourceAdjustmentId
                };
                context.LifecycleEvents.Add(lifecycleEvent);
                journal.Track(lifecycleEvent);
            }

            var stoppedBatchCount = 0;
            foreach (var batch in batches)
            {
                var hasProductZeroTerminalState =
                    batch.TrackingStatus == "stopped" &&
                    batch.StopReason == "product_stock_zero" &&
                    batch.StoppedAtUtc.HasValue &&
                    batch.NextTriggerDate is null;
                if (hasProductZeroTerminalState)
                {
                    continue;
                }

                journal.Capture(batch);
                batch.TrackingStatus = "stopped";
                batch.StopReason = "product_stock_zero";
                batch.StoppedAtUtc = request.OccurredAtUtc;
                batch.NextTriggerDate = null;
                batch.UpdatedAtUtc = request.OccurredAtUtc;
                stoppedBatchCount++;
            }

            var taskClosed = false;
            var draftInvalidated = false;
            if (task is not null)
            {
                journal.Capture(task);
                task.Status = "system_closed";
                task.ClosedAtUtc = request.OccurredAtUtc;
                task.CloseReason = "product_stock_zero";
                task.UpdatedAtUtc = request.OccurredAtUtc;
                taskClosed = true;
                var lifecycleEvent = new LifecycleEvent
                {
                    ProductId = product.Id,
                    EventType = "task_auto_closed",
                    Reason = "product_stock_zero",
                    OccurredAtUtc = request.OccurredAtUtc,
                    SourceImportId = request.SourceImportId,
                    SourceAdjustmentId = request.SourceAdjustmentId
                };
                context.LifecycleEvents.Add(lifecycleEvent);
                journal.Track(lifecycleEvent);

                if (task.Draft is { IsInvalid: false } draft)
                {
                    journal.Capture(draft);
                    draft.IsInvalid = true;
                    draft.InvalidReason = "product_stock_zero";
                    draft.InvalidatedAtUtc = request.OccurredAtUtc;
                    draft.UpdatedAtUtc = request.OccurredAtUtc;
                    draftInvalidated = true;
                    lifecycleEvent = new LifecycleEvent
                    {
                        ProductId = product.Id,
                        EventType = "draft_invalidated",
                        Reason = "product_stock_zero",
                        OccurredAtUtc = request.OccurredAtUtc,
                        SourceImportId = request.SourceImportId,
                        SourceAdjustmentId = request.SourceAdjustmentId
                    };
                    context.LifecycleEvents.Add(lifecycleEvent);
                    journal.Track(lifecycleEvent);
                }
            }

            context.SaveChanges();
            transaction?.Commit();
            return new(
                productTerminated,
                stoppedBatchCount,
                taskClosed,
                draftInvalidated,
                (productTerminated ? 1 : 0) + (taskClosed ? 1 : 0) + (draftInvalidated ? 1 : 0));
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

    public ProductStockZeroResult Execute(
        StoreDbContext context,
        long productId,
        DateTime occurredAtUtc,
        long? sourceImportId = null,
        long? sourceAdjustmentId = null) =>
        Execute(
            context,
            new ProductStockZeroRequest(
                productId,
                occurredAtUtc,
                sourceImportId,
                sourceAdjustmentId));

    private static void ValidateRequest(ProductStockZeroRequest request)
    {
        if (request.ProductId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ProductId));
        }

        if (request.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(request));
        }

        if (request.SourceImportId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SourceImportId));
        }

        if (request.SourceAdjustmentId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SourceAdjustmentId));
        }

        if (request.SourceImportId.HasValue && request.SourceAdjustmentId.HasValue)
        {
            throw new ArgumentException(
                "At most one lifecycle source may be provided.",
                nameof(request));
        }
    }

    private static void ValidateSource(
        StoreDbContext context,
        long productId,
        ProductStockZeroRequest request)
    {
        if (request.SourceImportId is long importId &&
            !context.Imports.AsNoTracking().Any(import => import.Id == importId))
        {
            throw new KeyNotFoundException($"Import {importId} does not exist.");
        }

        if (request.SourceAdjustmentId is not long adjustmentId)
        {
            return;
        }

        var adjustment = context.InventoryAdjustments
            .AsNoTracking()
            .SingleOrDefault(candidate => candidate.Id == adjustmentId);
        if (adjustment is null)
        {
            throw new KeyNotFoundException($"Inventory adjustment {adjustmentId} does not exist.");
        }

        if (adjustment.ProductId != productId)
        {
            throw new ArgumentException(
                $"Inventory adjustment {adjustmentId} does not belong to product {productId}.",
                nameof(request));
        }

        if (adjustment.AdjustedStockQty != 0)
        {
            throw new ArgumentException(
                $"Inventory adjustment {adjustmentId} does not confirm zero stock.",
                nameof(request));
        }
    }

    private sealed class MutationJournal
    {
        private readonly StoreDbContext _context;
        private readonly Dictionary<Product, ProductState> _products = new();
        private readonly Dictionary<Batch, BatchState> _batches = new();
        private readonly Dictionary<ProductTask, ProductTaskState> _tasks = new();
        private readonly Dictionary<InspectionDraft, DraftState> _drafts = new();
        private readonly List<LifecycleEvent> _events = new();

        public MutationJournal(StoreDbContext context)
        {
            _context = context;
        }

        public void Capture(Product product)
        {
            if (!_products.ContainsKey(product))
            {
                _products.Add(product, new(product.IsStockZeroTerminated, product.LifecycleGeneration, product.UpdatedAtUtc, _context.Entry(product).State));
            }
        }

        public void Capture(Batch batch)
        {
            if (!_batches.ContainsKey(batch))
            {
                _batches.Add(batch, new(batch.TrackingStatus, batch.StopReason, batch.StoppedAtUtc, batch.NextTriggerDate, batch.UpdatedAtUtc, _context.Entry(batch).State));
            }
        }

        public void Capture(ProductTask task)
        {
            if (!_tasks.ContainsKey(task))
            {
                _tasks.Add(task, new(task.Status, task.ClosedAtUtc, task.CloseReason, task.UpdatedAtUtc, _context.Entry(task).State));
            }
        }

        public void Capture(InspectionDraft draft)
        {
            if (!_drafts.ContainsKey(draft))
            {
                _drafts.Add(draft, new(draft.IsInvalid, draft.InvalidReason, draft.InvalidatedAtUtc, draft.UpdatedAtUtc, _context.Entry(draft).State));
            }
        }

        public void Track(LifecycleEvent lifecycleEvent) => _events.Add(lifecycleEvent);

        public void Restore()
        {
            foreach (var (product, state) in _products)
            {
                product.IsStockZeroTerminated = state.IsStockZeroTerminated;
                product.LifecycleGeneration = state.LifecycleGeneration;
                product.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(product).State = state.EntityState;
            }

            foreach (var (batch, state) in _batches)
            {
                batch.TrackingStatus = state.TrackingStatus;
                batch.StopReason = state.StopReason;
                batch.StoppedAtUtc = state.StoppedAtUtc;
                batch.NextTriggerDate = state.NextTriggerDate;
                batch.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(batch).State = state.EntityState;
            }

            foreach (var (task, state) in _tasks)
            {
                task.Status = state.Status;
                task.ClosedAtUtc = state.ClosedAtUtc;
                task.CloseReason = state.CloseReason;
                task.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(task).State = state.EntityState;
            }

            foreach (var (draft, state) in _drafts)
            {
                draft.IsInvalid = state.IsInvalid;
                draft.InvalidReason = state.InvalidReason;
                draft.InvalidatedAtUtc = state.InvalidatedAtUtc;
                draft.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(draft).State = state.EntityState;
            }

            foreach (var lifecycleEvent in _events)
            {
                if (_context.Entry(lifecycleEvent).State == EntityState.Added)
                {
                    _context.Entry(lifecycleEvent).State = EntityState.Detached;
                }
            }
        }

        private sealed record ProductState(
            bool IsStockZeroTerminated,
            int LifecycleGeneration,
            DateTime UpdatedAtUtc,
            EntityState EntityState);

        private sealed record BatchState(
            string TrackingStatus,
            string? StopReason,
            DateTime? StoppedAtUtc,
            DateOnly? NextTriggerDate,
            DateTime UpdatedAtUtc,
            EntityState EntityState);

        private sealed record ProductTaskState(
            string Status,
            DateTime? ClosedAtUtc,
            string? CloseReason,
            DateTime UpdatedAtUtc,
            EntityState EntityState);

        private sealed record DraftState(
            bool IsInvalid,
            string? InvalidReason,
            DateTime? InvalidatedAtUtc,
            DateTime UpdatedAtUtc,
            EntityState EntityState);
    }
}
