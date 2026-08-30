using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record InspectionHistoryEditRequest(
    long InspectionId,
    long InspectionItemId,
    int NewCheckedQty,
    DateTime ChangedAtUtc);

public sealed record InspectionHistoryEditResult(
    long InspectionId,
    long InspectionItemId,
    string Status,
    int? PreviousCheckedQty,
    int? NewCheckedQty,
    long? RevisionId,
    DateTime? UpdatedAtUtc)
{
    public bool Changed => Status == "changed";

    public bool NoChange => Status == "no_change";
}

public sealed class InspectionHistoryEditUseCase
{
    public InspectionHistoryEditResult Execute(
        StoreDbContext context,
        InspectionHistoryEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        EnsureCleanContext(context);

        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "StoreDbContext cannot have an existing transaction during an inspection history edit.");
        }

        using var transaction = context.Database.BeginTransaction();
        try
        {
            // ponytail: clear unchanged tracked entities so the edit always starts from current database values.
            context.ChangeTracker.Clear();
            var item = context.InspectionItems
                .AsTracking()
                .Include(candidate => candidate.Inspection)
                .ThenInclude(inspection => inspection.Task)
                .Include(candidate => candidate.Revisions)
                .SingleOrDefault(candidate =>
                    candidate.Id == request.InspectionItemId &&
                    candidate.InspectionId == request.InspectionId);

            if (item is null || !string.Equals(item.Inspection.Task.Status, "completed", StringComparison.Ordinal))
            {
                transaction.Commit();
                return new(request.InspectionId, request.InspectionItemId, "not_found", null, null, null, null);
            }

            var latestRevisionAtUtc = item.Revisions
                .OrderByDescending(revision => revision.ChangedAtUtc)
                .ThenByDescending(revision => revision.Id)
                .Select(revision => (DateTime?)revision.ChangedAtUtc)
                .FirstOrDefault();
            var minimumChangedAtUtc = item.Inspection.SubmittedAtUtc;
            if (item.UpdatedAtUtc > minimumChangedAtUtc)
            {
                minimumChangedAtUtc = item.UpdatedAtUtc;
            }

            if (latestRevisionAtUtc is DateTime revisionAtUtc && revisionAtUtc > minimumChangedAtUtc)
            {
                minimumChangedAtUtc = revisionAtUtc;
            }

            if (request.ChangedAtUtc < minimumChangedAtUtc)
            {
                throw new ArgumentException(
                    "ChangedAtUtc cannot be earlier than the inspection, item, or revision timestamp.",
                    nameof(request.ChangedAtUtc));
            }

            if (item.CheckedQty == request.NewCheckedQty)
            {
                transaction.Commit();
                return new(
                    request.InspectionId,
                    request.InspectionItemId,
                    "no_change",
                    item.CheckedQty,
                    item.CheckedQty,
                    null,
                    item.UpdatedAtUtc);
            }

            var previousCheckedQty = item.CheckedQty;
            var revision = new InspectionItemRevision
            {
                InspectionItemId = item.Id,
                PreviousCheckedQty = previousCheckedQty,
                NewCheckedQty = request.NewCheckedQty,
                ChangedAtUtc = request.ChangedAtUtc
            };
            item.CheckedQty = request.NewCheckedQty;
            item.UpdatedAtUtc = request.ChangedAtUtc;
            context.InspectionItemRevisions.Add(revision);
            context.SaveChanges();

            transaction.Commit();
            return new(
                request.InspectionId,
                request.InspectionItemId,
                "changed",
                previousCheckedQty,
                item.CheckedQty,
                revision.Id,
                item.UpdatedAtUtc);
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
            }

            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static void ValidateRequest(InspectionHistoryEditRequest request)
    {
        if (request.InspectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InspectionId));
        }

        if (request.InspectionItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InspectionItemId));
        }

        if (request.NewCheckedQty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.NewCheckedQty));
        }

        if (request.ChangedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "ChangedAtUtc must be UTC.",
                nameof(request.ChangedAtUtc));
        }
    }

    private static void EnsureCleanContext(StoreDbContext context)
    {
        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "StoreDbContext must have no pending changes before an inspection history edit.");
        }
    }
}
