using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class LifecycleEventConfiguration : IEntityTypeConfiguration<LifecycleEvent>
{
    public void Configure(EntityTypeBuilder<LifecycleEvent> entity)
    {
        entity.ToTable("lifecycle_events", table =>
        {
            table.HasCheckConstraint(
                "CK_lifecycle_events_event_type",
                "event_type IN ('product_stock_zero', 'batch_checked_zero', 'batch_tracking_resumed', 'task_auto_closed', 'draft_invalidated')");
            table.HasCheckConstraint(
                "CK_lifecycle_events_reason_not_blank",
                "length(reason) > 0 AND reason = trim(reason)");
            table.HasCheckConstraint(
                "CK_lifecycle_events_single_source",
                "(source_import_id IS NOT NULL) + (source_inspection_id IS NOT NULL) + (source_adjustment_id IS NOT NULL) <= 1");
        });

        entity.HasKey(lifecycleEvent => lifecycleEvent.Id);

        entity.Property(lifecycleEvent => lifecycleEvent.Id)
            .HasColumnName("id");
        entity.Property(lifecycleEvent => lifecycleEvent.ProductId)
            .HasColumnName("product_id");
        entity.Property(lifecycleEvent => lifecycleEvent.BatchId)
            .HasColumnName("batch_id");
        entity.Property(lifecycleEvent => lifecycleEvent.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(lifecycleEvent => lifecycleEvent.Reason)
            .HasColumnName("reason")
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(lifecycleEvent => lifecycleEvent.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(lifecycleEvent => lifecycleEvent.SourceImportId)
            .HasColumnName("source_import_id");
        entity.Property(lifecycleEvent => lifecycleEvent.SourceInspectionId)
            .HasColumnName("source_inspection_id");
        entity.Property(lifecycleEvent => lifecycleEvent.SourceAdjustmentId)
            .HasColumnName("source_adjustment_id");

        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(lifecycleEvent => lifecycleEvent.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(lifecycleEvent => new { lifecycleEvent.BatchId, lifecycleEvent.ProductId })
            .HasPrincipalKey(batch => new { batch.Id, batch.ProductId })
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<ImportRecord>()
            .WithMany()
            .HasForeignKey(lifecycleEvent => lifecycleEvent.SourceImportId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Inspection>()
            .WithMany()
            .HasForeignKey(lifecycleEvent => new { lifecycleEvent.SourceInspectionId, lifecycleEvent.ProductId })
            .HasPrincipalKey(inspection => new { inspection.Id, inspection.ProductId })
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<InventoryAdjustment>()
            .WithMany()
            .HasForeignKey(lifecycleEvent => lifecycleEvent.SourceAdjustmentId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(lifecycleEvent => new
            {
                lifecycleEvent.ProductId,
                lifecycleEvent.OccurredAtUtc,
                lifecycleEvent.Id
            })
            .HasDatabaseName("IX_lifecycle_events_product_id_occurred_at_utc_id");
        entity.HasIndex(lifecycleEvent => new
            {
                lifecycleEvent.BatchId,
                lifecycleEvent.ProductId,
                lifecycleEvent.OccurredAtUtc,
                lifecycleEvent.Id
            })
            .HasDatabaseName("IX_lifecycle_events_batch_id_product_id_occurred_at_utc_id");
        entity.HasIndex(lifecycleEvent => new
            {
                lifecycleEvent.SourceInspectionId,
                lifecycleEvent.ProductId
            })
            .HasDatabaseName("IX_lifecycle_events_source_inspection_id_product_id");
        entity.HasIndex(lifecycleEvent => lifecycleEvent.SourceImportId)
            .HasDatabaseName("IX_lifecycle_events_source_import_id");
        entity.HasIndex(lifecycleEvent => lifecycleEvent.SourceAdjustmentId)
            .HasDatabaseName("IX_lifecycle_events_source_adjustment_id");
    }
}
