namespace StoreExpiryInspector.Domain;

public sealed class ImportRecord
{
    public long Id { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public string SourceFileSha256 { get; set; } = string.Empty;

    public DateTime ParsedAtUtc { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public int ProductCount { get; set; }

    public int BatchCount { get; set; }

    public int NewProductCount { get; set; }

    public int NewBatchCount { get; set; }

    public int UpdatedBatchCount { get; set; }

    public int IssueCount { get; set; }

    public int UnsupportedCategoryCount { get; set; }

    public int NewTaskProductCount { get; set; }

    public string? PreImportSnapshotPath { get; set; }

    public bool IsUndone { get; set; }

    public DateTime? UndoneAtUtc { get; set; }
}
