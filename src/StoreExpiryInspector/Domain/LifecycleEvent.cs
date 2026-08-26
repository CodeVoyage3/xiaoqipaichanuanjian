namespace StoreExpiryInspector.Domain;

public sealed class LifecycleEvent
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public long? BatchId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public long? SourceImportId { get; set; }

    public long? SourceInspectionId { get; set; }

    public long? SourceAdjustmentId { get; set; }
}
