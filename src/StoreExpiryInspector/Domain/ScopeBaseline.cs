namespace StoreExpiryInspector.Domain;

public sealed class ScopeBaseline
{
    public long Id { get; set; }

    public string ScopeKey { get; set; } = string.Empty;

    public string PolicyCode { get; set; } = string.Empty;

    public int PolicyVersion { get; set; }

    public long CreatedImportId { get; set; }

    public DateOnly BusinessDate { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<BatchBaseline> BatchBaselines { get; } = new List<BatchBaseline>();
}
