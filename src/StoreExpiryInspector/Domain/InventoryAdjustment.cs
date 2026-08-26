namespace StoreExpiryInspector.Domain;

public sealed class InventoryAdjustment
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public int ExcelStockQtySnapshot { get; set; }

    public int AdjustedStockQty { get; set; }

    public DateTime AdjustedAtUtc { get; set; } = DateTime.UtcNow;
}
