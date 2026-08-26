namespace StoreExpiryInspector.Domain;

public sealed class InspectionItem
{
    public long Id { get; set; }

    public long InspectionId { get; set; }

    public Inspection Inspection { get; set; } = null!;

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public long BatchId { get; set; }

    public Batch Batch { get; set; } = null!;

    public DateOnly? ProductionDateSnapshot { get; set; }

    public DateOnly? ExpiryDateSnapshot { get; set; }

    public string StageSnapshot { get; set; } = "discount_50";

    public int ArrivalQtySnapshot { get; set; }

    public int CheckedQty { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<InspectionItemRevision> Revisions { get; } = new List<InspectionItemRevision>();
}
