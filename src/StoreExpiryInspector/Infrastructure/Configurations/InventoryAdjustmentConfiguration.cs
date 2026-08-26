using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> entity)
    {
        entity.ToTable("inventory_adjustments", table =>
        {
            table.HasCheckConstraint(
                "CK_inventory_adjustments_excel_stock_qty_snapshot_nonnegative",
                "excel_stock_qty_snapshot >= 0");
            table.HasCheckConstraint(
                "CK_inventory_adjustments_adjusted_stock_qty_nonnegative",
                "adjusted_stock_qty >= 0");
        });

        entity.HasKey(adjustment => adjustment.Id);

        entity.Property(adjustment => adjustment.Id)
            .HasColumnName("id");
        entity.Property(adjustment => adjustment.ProductId)
            .HasColumnName("product_id");
        entity.Property(adjustment => adjustment.ExcelStockQtySnapshot)
            .HasColumnName("excel_stock_qty_snapshot");
        entity.Property(adjustment => adjustment.AdjustedStockQty)
            .HasColumnName("adjusted_stock_qty");
        entity.Property(adjustment => adjustment.AdjustedAtUtc)
            .HasColumnName("adjusted_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(adjustment => new
            {
                adjustment.ProductId,
                adjustment.AdjustedAtUtc,
                adjustment.Id
            })
            .HasDatabaseName("IX_inventory_adjustments_product_id_adjusted_at_utc_id");
    }
}
