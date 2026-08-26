namespace StoreExpiryInspector.Domain;

public sealed class BackupRecord
{
    public long Id { get; set; }

    public string BackupType { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string VerificationStatus { get; set; } = string.Empty;
}
