namespace StoreExpiryInspector.Domain;

public sealed class InspectionItemRevision
{
    public long Id { get; set; }

    public long InspectionItemId { get; set; }

    public InspectionItem InspectionItem { get; set; } = null!;

    public int PreviousCheckedQty { get; set; }

    public int NewCheckedQty { get; set; }

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
