namespace StoreExpiryInspector.Domain;

public sealed class Inspection
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public ProductTask Task { get; set; } = null!;

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string ProductCodeSnapshot { get; set; } = string.Empty;

    public string? ProductNameSnapshot { get; set; }

    public string? BarcodeSnapshot { get; set; }

    public string StageSnapshot { get; set; } = "discount_50";

    public int StockQtySnapshot { get; set; }

    public string InspectorName { get; set; } = string.Empty;

    public DateOnly CheckDate { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<InspectionItem> Items { get; } = new List<InspectionItem>();
}
