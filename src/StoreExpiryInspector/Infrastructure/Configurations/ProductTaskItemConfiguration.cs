using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ProductTaskItemConfiguration : IEntityTypeConfiguration<ProductTaskItem>
{
    public void Configure(EntityTypeBuilder<ProductTaskItem> entity)
    {
        entity.ToTable("task_items", table =>
        {
            table.HasCheckConstraint(
                "CK_task_items_stage",
                "stage IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
            table.HasCheckConstraint(
                "CK_task_items_attention_version_nonnegative",
                "attention_version >= 0");
            table.HasCheckConstraint(
                "CK_task_items_requires_reconfirmation",
                "requires_reconfirmation IN (0, 1)");
        });

        entity.HasKey(item => item.Id);

        entity.Property(item => item.Id)
            .HasColumnName("id");
        entity.Property(item => item.TaskId)
            .HasColumnName("task_id");
        entity.Property(item => item.BatchId)
            .HasColumnName("batch_id");
        entity.Property(item => item.ProductId)
            .HasColumnName("product_id");
        entity.Property(item => item.Stage)
            .HasColumnName("stage")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(item => item.AttentionVersion)
            .HasColumnName("attention_version");
        entity.Property(item => item.RequiresReconfirmation)
            .HasColumnName("requires_reconfirmation")
            .HasDefaultValue(false)
            .IsRequired();
        entity.Property(item => item.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(item => item.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(item => item.Task)
            .WithMany(task => task.Items)
            .HasForeignKey(item => new { item.TaskId, item.ProductId })
            .HasPrincipalKey(task => new { task.Id, task.ProductId })
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(item => item.Batch)
            .WithMany(batch => batch.TaskItems)
            .HasForeignKey(item => new { item.BatchId, item.ProductId })
            .HasPrincipalKey(batch => new { batch.Id, batch.ProductId })
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasAlternateKey(item => new { item.Id, item.TaskId })
            .HasName("AK_task_items_id_task_id");
        entity.HasIndex(item => new { item.TaskId, item.BatchId })
            .HasDatabaseName("IX_task_items_task_id_batch_id")
            .IsUnique();
    }
}
