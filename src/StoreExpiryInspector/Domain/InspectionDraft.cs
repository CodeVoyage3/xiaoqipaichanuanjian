namespace StoreExpiryInspector.Domain;

public sealed class InspectionDraft
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public ProductTask Task { get; set; } = null!;

    public string? InspectorName { get; set; }

    public DateOnly? CheckDate { get; set; }

    public bool IsInvalid { get; set; }

    public string? InvalidReason { get; set; }

    public DateTime? InvalidatedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<InspectionDraftItem> Items { get; } = new List<InspectionDraftItem>();
}
