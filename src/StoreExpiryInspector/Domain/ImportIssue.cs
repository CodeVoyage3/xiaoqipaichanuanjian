namespace StoreExpiryInspector.Domain;

public sealed class ImportIssue
{
    public long Id { get; set; }

    public long ImportId { get; set; }

    public int? RowNumber { get; set; }

    public string IssueType { get; set; } = string.Empty;

    public string? FieldName { get; set; }

    public string SafeSummary { get; set; } = string.Empty;
}
