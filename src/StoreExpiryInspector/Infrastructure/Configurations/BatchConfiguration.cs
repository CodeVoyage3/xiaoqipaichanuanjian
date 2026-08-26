using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> entity)
    {
        entity.ToTable("batches", table =>
        {
            table.HasCheckConstraint(
                "CK_batches_shelf_life_unit",
                "shelf_life_unit IN ('M', 'D', 'Y')");
            table.HasCheckConstraint(
                "CK_batches_current_arrival_qty_nonnegative",
                "current_arrival_qty >= 0");
            table.HasCheckConstraint(
                "CK_batches_max_arrival_qty_nonnegative",
                "max_arrival_qty >= 0");
        });

        entity.HasKey(batch => batch.Id);

        entity.Property(batch => batch.Id)
            .HasColumnName("id");
        entity.Property(batch => batch.ProductId)
            .HasColumnName("product_id");
        entity.Property(batch => batch.ProductionDate)
            .HasColumnName("production_date")
            .HasColumnType("TEXT");
        entity.Property(batch => batch.ExpiryDate)
            .HasColumnName("expiry_date")
            .HasColumnType("TEXT");
        entity.Property(batch => batch.ShelfLifeValue)
            .HasColumnName("shelf_life_value");
        entity.Property(batch => batch.ShelfLifeUnit)
            .HasColumnName("shelf_life_unit")
            .HasMaxLength(1)
            .HasDefaultValue("D")
            .IsRequired();
        entity.Property(batch => batch.CurrentArrivalQty)
            .HasColumnName("current_arrival_qty");
        entity.Property(batch => batch.MaxArrivalQty)
            .HasColumnName("max_arrival_qty");
        entity.Property(batch => batch.SourceDiscountReference)
            .HasColumnName("source_discount_reference")
            .HasMaxLength(200);
        entity.Property(batch => batch.LifecycleGeneration)
            .HasColumnName("lifecycle_generation");
        entity.Property(batch => batch.TrackingStatus)
            .HasColumnName("tracking_status")
            .HasMaxLength(50)
            .HasDefaultValue("active")
            .IsRequired();
        entity.Property(batch => batch.StopReason)
            .HasColumnName("stop_reason")
            .HasMaxLength(100);
        entity.Property(batch => batch.StoppedAtUtc)
            .HasColumnName("stopped_at_utc")
            .HasColumnType("TEXT");
        entity.Property(batch => batch.CurrentStage)
            .HasColumnName("current_stage")
            .HasMaxLength(50)
            .HasDefaultValue("none")
            .IsRequired();
        entity.Property(batch => batch.NextTriggerDate)
            .HasColumnName("next_trigger_date")
            .HasColumnType("TEXT");
        entity.Property(batch => batch.AttentionVersion)
            .HasColumnName("attention_version");
        entity.Property(batch => batch.HandledAttentionVersion)
            .HasColumnName("handled_attention_version");
        entity.Property(batch => batch.LastSeenImportId)
            .HasColumnName("last_seen_import_id");
        entity.Property(batch => batch.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(batch => batch.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne(batch => batch.Product)
            .WithMany(product => product.Batches)
            .HasForeignKey(batch => batch.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<ImportRecord>()
            .WithMany()
            .HasForeignKey(batch => batch.LastSeenImportId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(batch => batch.ProductId)
            .HasDatabaseName("IX_batches_product_id");
        entity.HasIndex(batch => batch.ExpiryDate)
            .HasDatabaseName("IX_batches_expiry_date");
        entity.HasIndex(batch => new { batch.TrackingStatus, batch.NextTriggerDate })
            .HasDatabaseName("IX_batches_tracking_status_next_trigger_date");
        entity.HasIndex(batch => new { batch.ProductId, batch.ProductionDate, batch.ExpiryDate })
            .HasDatabaseName("IX_batches_product_id_production_date_expiry_date")
            .HasFilter("production_date IS NOT NULL")
            .IsUnique();
        entity.HasIndex(batch => new { batch.ProductId, batch.ExpiryDate })
            .HasDatabaseName("IX_batches_product_id_expiry_date")
            .HasFilter("production_date IS NULL")
            .IsUnique();

        entity.HasAlternateKey(batch => new { batch.Id, batch.ProductId })
            .HasName("AK_batches_id_product_id");
    }
}
