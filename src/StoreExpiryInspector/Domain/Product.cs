namespace StoreExpiryInspector.Domain;

public sealed class Product
{
    public long Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string? CurrentName { get; set; }

    public string? CurrentBarcode { get; set; }

    public string CategoryCode { get; set; } = "food";

    public string PolicyCode { get; set; } = "food_v1";

    public int ExcelStockQty { get; set; }

    public int EffectiveStockQty { get; set; }

    public string? EffectiveStockSource { get; set; }

    public int LifecycleGeneration { get; set; }

    public bool IsStockZeroTerminated { get; set; }

    public long? LastSeenImportId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Batch> Batches { get; } = new List<Batch>();
}
