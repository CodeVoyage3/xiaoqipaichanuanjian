namespace StoreExpiryInspector.Domain;

public sealed class InspectionDraftItem
{
    public long Id { get; set; }

    public long DraftId { get; set; }

    public InspectionDraft Draft { get; set; } = null!;

    public long TaskItemId { get; set; }

    public ProductTaskItem TaskItem { get; set; } = null!;

    public long TaskId { get; set; }

    public int? CheckedQty { get; set; }

    public int ConfirmedAttentionVersion { get; set; }
}
