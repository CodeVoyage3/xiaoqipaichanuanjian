using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> entity)
    {
        entity.ToTable("inspections", table =>
        {
            table.HasCheckConstraint(
                "CK_inspections_product_code_snapshot_not_blank",
                "length(product_code_snapshot) > 0 AND product_code_snapshot = trim(product_code_snapshot)");
            table.HasCheckConstraint(
                "CK_inspections_stage_snapshot",
                "stage_snapshot IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
            table.HasCheckConstraint(
                "CK_inspections_stock_qty_snapshot_nonnegative",
                "stock_qty_snapshot >= 0");
            table.HasCheckConstraint(
                "CK_inspections_inspector_name_not_blank",
                "length(inspector_name) > 0 AND inspector_name = trim(inspector_name)");
        });

        entity.HasKey(inspection => inspection.Id);

        entity.Property(inspection => inspection.Id)
            .HasColumnName("id");
        entity.Property(inspection => inspection.TaskId)
            .HasColumnName("task_id");
        entity.Property(inspection => inspection.ProductId)
            .HasColumnName("product_id");
        entity.Property(inspection => inspection.ProductCodeSnapshot)
            .HasColumnName("product_code_snapshot")
            .HasMaxLength(200)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(inspection => inspection.ProductNameSnapshot)
            .HasColumnName("product_name_snapshot")
            .HasMaxLength(500);
        entity.Property(inspection => inspection.BarcodeSnapshot)
            .HasColumnName("barcode_snapshot")
            .HasMaxLength(200);
        entity.Property(inspection => inspection.StageSnapshot)
            .HasColumnName("stage_snapshot")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(inspection => inspection.StockQtySnapshot)
            .HasColumnName("stock_qty_snapshot");
        entity.Property(inspection => inspection.InspectorName)
            .HasColumnName("inspector_name")
            .HasMaxLength(200)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(inspection => inspection.CheckDate)
            .HasColumnName("check_date")
            .HasColumnType("TEXT");
        entity.Property(inspection => inspection.SubmittedAtUtc)
            .HasColumnName("submitted_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne(inspection => inspection.Product)
            .WithMany()
            .HasForeignKey(inspection => inspection.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(inspection => inspection.Task)
            .WithMany()
            .HasForeignKey(inspection => new { inspection.TaskId, inspection.ProductId })
            .HasPrincipalKey(task => new { task.Id, task.ProductId })
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasAlternateKey(inspection => new { inspection.Id, inspection.ProductId })
            .HasName("AK_inspections_id_product_id");
        entity.HasIndex(inspection => inspection.TaskId)
            .HasDatabaseName("IX_inspections_task_id")
            .IsUnique();
    }
}
