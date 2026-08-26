using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class InspectionItemConfiguration : IEntityTypeConfiguration<InspectionItem>
{
    public void Configure(EntityTypeBuilder<InspectionItem> entity)
    {
        entity.ToTable("inspection_items", table =>
        {
            table.HasCheckConstraint(
                "CK_inspection_items_stage_snapshot",
                "stage_snapshot IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
            table.HasCheckConstraint(
                "CK_inspection_items_arrival_qty_snapshot_nonnegative",
                "arrival_qty_snapshot >= 0");
            table.HasCheckConstraint(
                "CK_inspection_items_checked_qty_nonnegative",
                "checked_qty >= 0");
        });

        entity.HasKey(item => item.Id);

        entity.Property(item => item.Id)
            .HasColumnName("id");
        entity.Property(item => item.InspectionId)
            .HasColumnName("inspection_id");
        entity.Property(item => item.ProductId)
            .HasColumnName("product_id");
        entity.Property(item => item.BatchId)
            .HasColumnName("batch_id");
        entity.Property(item => item.ProductionDateSnapshot)
            .HasColumnName("production_date_snapshot")
            .HasColumnType("TEXT");
        entity.Property(item => item.ExpiryDateSnapshot)
            .HasColumnName("expiry_date_snapshot")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(item => item.StageSnapshot)
            .HasColumnName("stage_snapshot")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(item => item.ArrivalQtySnapshot)
            .HasColumnName("arrival_qty_snapshot");
        entity.Property(item => item.CheckedQty)
            .HasColumnName("checked_qty");
        entity.Property(item => item.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(item => item.Inspection)
            .WithMany(inspection => inspection.Items)
            .HasForeignKey(item => new { item.InspectionId, item.ProductId })
            .HasPrincipalKey(inspection => new { inspection.Id, inspection.ProductId })
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(item => item.Batch)
            .WithMany()
            .HasForeignKey(item => new { item.BatchId, item.ProductId })
            .HasPrincipalKey(batch => new { batch.Id, batch.ProductId })
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(item => new { item.InspectionId, item.BatchId })
            .HasDatabaseName("IX_inspection_items_inspection_id_batch_id")
            .IsUnique();
    }
}
