using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public sealed record BatchCheckedZeroLifecycleRequest(
    long ProductId,
    long BatchId,
    long InspectionId,
    long InspectionItemId,
    int CheckedQty,
    DateTime OccurredAtUtc);

public sealed record BatchCheckedZeroLifecycleResult(
    bool BatchStopped,
    bool IdempotentReplay,
    int LifecycleEventCount);

public sealed class BatchCheckedZeroLifecycleUseCase
{
    public BatchCheckedZeroLifecycleResult Execute(
        StoreDbContext context,
        BatchCheckedZeroLifecycleRequest request)
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
                .AsNoTracking()
                .SingleOrDefault(candidate => candidate.Id == request.ProductId);
            if (product is null)
            {
                throw new KeyNotFoundException($"Product {request.ProductId} does not exist.");
            }

            var batch = context.Batches
                .AsTracking()
                .SingleOrDefault(candidate => candidate.Id == request.BatchId);
            if (batch is null)
            {
                throw new KeyNotFoundException($"Batch {request.BatchId} does not exist.");
            }

            var inspection = context.Inspections
                .AsNoTracking()
                .SingleOrDefault(candidate => candidate.Id == request.InspectionId);
            if (inspection is null)
            {
                throw new KeyNotFoundException($"Inspection {request.InspectionId} does not exist.");
            }

            var inspectionItem = context.InspectionItems
                .AsNoTracking()
                .SingleOrDefault(candidate => candidate.Id == request.InspectionItemId);
            if (inspectionItem is null)
            {
                throw new KeyNotFoundException(
                    $"Inspection item {request.InspectionItemId} does not exist.");
            }

            ValidateFacts(request, batch, inspection, inspectionItem);

            var matchingEvents = context.LifecycleEvents
                .AsNoTracking()
                .Where(lifecycleEvent =>
                    lifecycleEvent.EventType == "batch_checked_zero" &&
                    lifecycleEvent.ProductId == request.ProductId &&
                    lifecycleEvent.BatchId == request.BatchId &&
                    lifecycleEvent.SourceInspectionId == request.InspectionId)
                .Take(2)
                .ToArray();
            if (matchingEvents.Length > 1)
            {
                throw new InvalidOperationException(
                    "Multiple matching batch_checked_zero lifecycle events exist.");
            }

            if (matchingEvents.Length == 1)
            {
                ValidateMatchingEvent(matchingEvents[0]);
                transaction?.Commit();
                return new(false, true, 0);
            }

            ValidateBatchState(batch);

            journal.Capture(batch);
            batch.TrackingStatus = "stopped";
            batch.StopReason = "batch_checked_zero";
            batch.StoppedAtUtc = request.OccurredAtUtc;
            batch.NextTriggerDate = null;
            batch.UpdatedAtUtc = request.OccurredAtUtc;

            var lifecycleEvent = new LifecycleEvent
            {
                ProductId = request.ProductId,
                BatchId = request.BatchId,
                EventType = "batch_checked_zero",
                Reason = "batch_checked_zero",
                OccurredAtUtc = request.OccurredAtUtc,
                SourceInspectionId = request.InspectionId
            };
            context.LifecycleEvents.Add(lifecycleEvent);
            journal.TrackAdded(lifecycleEvent);

            if (context.ChangeTracker.HasChanges())
            {
                context.SaveChanges();
            }

            transaction?.Commit();
            return new(true, false, 1);
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

    private static void ValidateRequest(BatchCheckedZeroLifecycleRequest request)
    {
        if (request.ProductId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ProductId));
        }

        if (request.BatchId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BatchId));
        }

        if (request.InspectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InspectionId));
        }

        if (request.InspectionItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InspectionItemId));
        }

        if (request.CheckedQty != 0)
        {
            throw new ArgumentException("CheckedQty must be exactly zero.", nameof(request));
        }

        if (request.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(request));
        }
    }

    private static void ValidateFacts(
        BatchCheckedZeroLifecycleRequest request,
        Batch batch,
        Inspection inspection,
        InspectionItem inspectionItem)
    {
        if (batch.ProductId != request.ProductId)
        {
            throw new ArgumentException(
                $"Batch {batch.Id} does not belong to product {request.ProductId}.",
                nameof(request));
        }

        if (inspection.ProductId != request.ProductId)
        {
            throw new ArgumentException(
                $"Inspection {inspection.Id} does not belong to product {request.ProductId}.",
                nameof(request));
        }

        if (inspectionItem.ProductId != request.ProductId)
        {
            throw new ArgumentException(
                $"Inspection item {inspectionItem.Id} does not belong to product {request.ProductId}.",
                nameof(request));
        }

        if (inspectionItem.InspectionId != request.InspectionId)
        {
            throw new ArgumentException(
                $"Inspection item {inspectionItem.Id} does not belong to inspection {request.InspectionId}.",
                nameof(request));
        }

        if (inspectionItem.BatchId != request.BatchId)
        {
            throw new ArgumentException(
                $"Inspection item {inspectionItem.Id} does not belong to batch {request.BatchId}.",
                nameof(request));
        }

        if (inspectionItem.CheckedQty != 0)
        {
            throw new InvalidOperationException(
                $"Inspection item {inspectionItem.Id} does not persist a zero checked quantity.");
        }

        if (request.OccurredAtUtc < inspection.SubmittedAtUtc)
        {
            throw new ArgumentException(
                "OccurredAtUtc cannot be earlier than the formal inspection submission.",
                nameof(request));
        }

    }

    private static void ValidateBatchState(Batch batch)
    {
        if (batch.TrackingStatus == "active")
        {
            if (batch.StopReason is not null || batch.StoppedAtUtc.HasValue)
            {
                throw new InvalidOperationException(
                    $"Active batch {batch.Id} contains stopped-state fields.");
            }

            return;
        }

        throw new InvalidOperationException(
            batch.TrackingStatus == "stopped"
                ? $"Batch {batch.Id} is already stopped without its matching lifecycle event."
                : $"Batch {batch.Id} has an invalid lifecycle state.");
    }

    private static void ValidateMatchingEvent(LifecycleEvent lifecycleEvent)
    {
        if (lifecycleEvent.Reason != "batch_checked_zero" ||
            lifecycleEvent.SourceImportId is not null ||
            lifecycleEvent.SourceAdjustmentId is not null)
        {
            throw new InvalidOperationException(
                "The matching lifecycle event does not describe a canonical batch_checked_zero fact.");
        }
    }

    private sealed class MutationJournal
    {
        private readonly StoreDbContext _context;
        private readonly Dictionary<Batch, BatchState> _batches = new();
        private readonly List<LifecycleEvent> _addedEvents = new();

        public MutationJournal(StoreDbContext context)
        {
            _context = context;
        }

        public void Capture(Batch batch)
        {
            if (_batches.ContainsKey(batch))
            {
                return;
            }

            _batches.Add(batch, new(
                batch.TrackingStatus,
                batch.StopReason,
                batch.StoppedAtUtc,
                batch.NextTriggerDate,
                batch.UpdatedAtUtc,
                _context.Entry(batch).State));
        }

        public void TrackAdded(LifecycleEvent lifecycleEvent) => _addedEvents.Add(lifecycleEvent);

        public void Restore()
        {
            foreach (var (batch, state) in _batches)
            {
                batch.TrackingStatus = state.TrackingStatus;
                batch.StopReason = state.StopReason;
                batch.StoppedAtUtc = state.StoppedAtUtc;
                batch.NextTriggerDate = state.NextTriggerDate;
                batch.UpdatedAtUtc = state.UpdatedAtUtc;
                _context.Entry(batch).State = state.EntityState;
            }

            foreach (var lifecycleEvent in _addedEvents)
            {
                if (_context.Entry(lifecycleEvent).State == EntityState.Added)
                {
                    _context.Entry(lifecycleEvent).State = EntityState.Detached;
                }
            }
        }

        private sealed record BatchState(
            string TrackingStatus,
            string? StopReason,
            DateTime? StoppedAtUtc,
            DateOnly? NextTriggerDate,
            DateTime UpdatedAtUtc,
            EntityState EntityState);
    }
}
