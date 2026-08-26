namespace StoreExpiryInspector.Domain;

public sealed class ProductTaskItem
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public ProductTask Task { get; set; } = null!;

    public long BatchId { get; set; }

    public Batch Batch { get; set; } = null!;

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string Stage { get; set; } = "discount_50";

    public int AttentionVersion { get; set; }

    public bool RequiresReconfirmation { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<InspectionDraftItem> DraftItems { get; } = new List<InspectionDraftItem>();
}
