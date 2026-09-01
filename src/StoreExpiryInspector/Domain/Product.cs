namespace StoreExpiryInspector.Domain;

public sealed class Product
{
    public long Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string? CurrentName { get; set; }

    public string? CurrentBarcode { get; set; }

    public string CategoryCode { get; set; } = "food";

    public string? PolicyCode { get; set; }

    public int? PolicyVersion { get; set; }

    public ExpiryManagementStatus ExpiryManagementStatus { get; set; } = ExpiryManagementStatus.Managed;

    public int ExcelStockQty { get; set; }

    public int EffectiveStockQty { get; set; }

    public string? EffectiveStockSource { get; set; }

    public int LifecycleGeneration { get; set; }

    public bool IsStockZeroTerminated { get; set; }

    public long? LastSeenImportId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Batch> Batches { get; } = new List<Batch>();

    public ICollection<ProductTask> Tasks { get; } = new List<ProductTask>();

    public void EnsureExpiryManagementContract()
    {
        var hasV1Policy = PolicyVersion == ExpiryPolicies.Version1 && PolicyCode is ExpiryPolicies.Food or ExpiryPolicies.Pet or ExpiryPolicies.GeneralLong;
        if (ExpiryManagementStatus == ExpiryManagementStatus.Managed && hasV1Policy ||
            ExpiryManagementStatus is not ExpiryManagementStatus.Managed && PolicyCode is null && PolicyVersion is null)
        {
            return;
        }

        throw new InvalidOperationException("Managed products require a policy; excluded and unresolved products must not have one.");
    }
}
