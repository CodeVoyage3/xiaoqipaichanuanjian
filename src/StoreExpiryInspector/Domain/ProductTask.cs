namespace StoreExpiryInspector.Domain;

public sealed class ProductTask
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string Status { get; set; } = "open";

    public string HighestStage { get; set; } = "discount_50";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAtUtc { get; set; }

    public string? CloseReason { get; set; }

    public ICollection<ProductTaskItem> Items { get; } = new List<ProductTaskItem>();

    public InspectionDraft? Draft { get; set; }
}
