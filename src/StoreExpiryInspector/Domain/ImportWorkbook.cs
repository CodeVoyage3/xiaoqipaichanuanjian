namespace StoreExpiryInspector.Domain;

public sealed class ImportWorkbook
{
    public long Id { get; set; }

    public long ImportId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = Array.Empty<byte>();

    public string Sha256 { get; set; } = string.Empty;

    public DateTime SavedAtUtc { get; set; }
}
