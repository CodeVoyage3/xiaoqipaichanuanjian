using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record InspectionHistoryListItem(
    long InspectionId,
    long TaskId,
    long ProductId,
    string ProductCode,
    string? ProductName,
    string? ProductBarcode,
    DateTime SubmittedAtUtc,
    int ItemCount);

public sealed record InspectionHistoryItemDetail(
    long InspectionItemId,
    long InspectionId,
    long ProductId,
    long BatchId,
    DateOnly? ProductionDateSnapshot,
    DateOnly ExpiryDateSnapshot,
    string StageSnapshot,
    int ArrivalQtySnapshot,
    int CheckedQty,
    DateTime UpdatedAtUtc);

public sealed record InspectionHistoryDetail(
    long InspectionId,
    long TaskId,
    long ProductId,
    string ProductCodeSnapshot,
    string? ProductNameSnapshot,
    string? BarcodeSnapshot,
    string StageSnapshot,
    int StockQtySnapshot,
    string InspectorName,
    DateOnly CheckDate,
    DateTime SubmittedAtUtc,
    IReadOnlyList<InspectionHistoryItemDetail> Items);

public sealed record InspectionHistoryDetailResult(
    long InspectionId,
    string Status,
    InspectionHistoryDetail? Detail);

public sealed record InspectionItemRevisionDetail(
    long RevisionId,
    long InspectionItemId,
    int PreviousCheckedQty,
    int NewCheckedQty,
    DateTime ChangedAtUtc);

public sealed record InspectionItemRevisionHistory(
    long InspectionId,
    long InspectionItemId,
    int CurrentCheckedQty,
    DateTime UpdatedAtUtc,
    IReadOnlyList<InspectionItemRevisionDetail> Revisions);

public sealed record InspectionItemRevisionHistoryResult(
    long InspectionId,
    long InspectionItemId,
    string Status,
    InspectionItemRevisionHistory? History);

public sealed class InspectionHistoryQuery
{
    public IReadOnlyList<InspectionHistoryListItem> List(StoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var records = context.Inspections
            .AsNoTracking()
            .Where(inspection => inspection.Task.Status == "completed")
            .OrderByDescending(inspection => inspection.SubmittedAtUtc)
            .ThenByDescending(inspection => inspection.Id)
            .Select(inspection => new InspectionHistoryListItem(
                inspection.Id,
                inspection.TaskId,
                inspection.ProductId,
                inspection.ProductCodeSnapshot,
                inspection.ProductNameSnapshot,
                inspection.BarcodeSnapshot,
                inspection.SubmittedAtUtc,
                inspection.Items.Count()))
            .ToArray();
        return Array.AsReadOnly(records);
    }

    public InspectionHistoryDetailResult GetDetail(StoreDbContext context, long inspectionId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (inspectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inspectionId));
        }

        var inspection = context.Inspections
            .AsNoTracking()
            .Where(candidate => candidate.Id == inspectionId && candidate.Task.Status == "completed")
            .Select(candidate => new InspectionHistoryHeader(
                candidate.Id,
                candidate.TaskId,
                candidate.ProductId,
                candidate.ProductCodeSnapshot,
                candidate.ProductNameSnapshot,
                candidate.BarcodeSnapshot,
                candidate.StageSnapshot,
                candidate.StockQtySnapshot,
                candidate.InspectorName,
                candidate.CheckDate,
                candidate.SubmittedAtUtc))
            .SingleOrDefault();
        if (inspection is null)
        {
            return new(inspectionId, "not_found", null);
        }

        var items = context.InspectionItems
            .AsNoTracking()
            .Where(item => item.InspectionId == inspection.Id)
            .OrderBy(item => item.Id)
            .Select(item => new InspectionHistoryItemDetail(
                item.Id,
                item.InspectionId,
                item.ProductId,
                item.BatchId,
                item.ProductionDateSnapshot,
                item.ExpiryDateSnapshot,
                item.StageSnapshot,
                item.ArrivalQtySnapshot,
                item.CheckedQty,
                item.UpdatedAtUtc))
            .ToArray();

        return new(
            inspection.Id,
            "found",
            new(
                inspection.Id,
                inspection.TaskId,
                inspection.ProductId,
                inspection.ProductCodeSnapshot,
                inspection.ProductNameSnapshot,
                inspection.BarcodeSnapshot,
                inspection.StageSnapshot,
                inspection.StockQtySnapshot,
                inspection.InspectorName,
                inspection.CheckDate,
                inspection.SubmittedAtUtc,
                Array.AsReadOnly(items)));
    }

    public InspectionItemRevisionHistoryResult GetItemRevisions(
        StoreDbContext context,
        long inspectionId,
        long inspectionItemId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (inspectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inspectionId));
        }

        if (inspectionItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inspectionItemId));
        }

        var item = context.InspectionItems
            .AsNoTracking()
            .Include(candidate => candidate.Revisions)
            .AsSingleQuery()
            .Where(candidate =>
                candidate.Id == inspectionItemId &&
                candidate.InspectionId == inspectionId &&
                candidate.Inspection.Task.Status == "completed")
            .SingleOrDefault();
        if (item is null)
        {
            return new(inspectionId, inspectionItemId, "not_found", null);
        }

        var revisions = item.Revisions
            .OrderBy(revision => revision.ChangedAtUtc)
            .ThenBy(revision => revision.Id)
            .Select(revision => new InspectionItemRevisionDetail(
                revision.Id,
                revision.InspectionItemId,
                revision.PreviousCheckedQty,
                revision.NewCheckedQty,
                revision.ChangedAtUtc))
            .ToArray();
        return new(
            inspectionId,
            inspectionItemId,
            "found",
            new(
                inspectionId,
                inspectionItemId,
                item.CheckedQty,
                item.UpdatedAtUtc,
                Array.AsReadOnly(revisions)));
    }

    private sealed record InspectionHistoryHeader(
        long Id,
        long TaskId,
        long ProductId,
        string ProductCodeSnapshot,
        string? ProductNameSnapshot,
        string? BarcodeSnapshot,
        string StageSnapshot,
        int StockQtySnapshot,
        string InspectorName,
        DateOnly CheckDate,
        DateTime SubmittedAtUtc);
}
