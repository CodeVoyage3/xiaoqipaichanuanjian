namespace StoreExpiryInspector.Domain;

public sealed class Batch
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public DateOnly? ProductionDate { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public int ShelfLifeValue { get; set; }

    public string ShelfLifeUnit { get; set; } = "D";

    public int CurrentArrivalQty { get; set; }

    public int MaxArrivalQty { get; set; }

    public string? SourceDiscountReference { get; set; }

    public int LifecycleGeneration { get; set; }

    public string TrackingStatus { get; set; } = "active";

    public string? StopReason { get; set; }

    public DateTime? StoppedAtUtc { get; set; }

    public string CurrentStage { get; set; } = "none";

    public DateOnly? NextTriggerDate { get; set; }

    public int AttentionVersion { get; set; }

    public int HandledAttentionVersion { get; set; }

    public long? LastSeenImportId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
