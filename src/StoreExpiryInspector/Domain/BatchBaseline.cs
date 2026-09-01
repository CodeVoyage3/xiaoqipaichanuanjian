namespace StoreExpiryInspector.Domain;

public sealed class BatchBaseline
{
    public long Id { get; set; }

    public long BaselineId { get; set; }

    public ScopeBaseline Baseline { get; set; } = null!;

    public long BatchId { get; set; }

    public string StageAtBaseline { get; set; } = ExpiryStageCalculator.None;

    public string ColdStartDisposition { get; set; } = string.Empty;

    public int? CatchupWindowDays { get; set; }

    public long? SourceTaskId { get; set; }

    public string? CatchupSource { get; set; }
}
