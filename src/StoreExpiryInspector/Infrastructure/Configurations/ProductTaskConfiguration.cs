using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ProductTaskConfiguration : IEntityTypeConfiguration<ProductTask>
{
    public void Configure(EntityTypeBuilder<ProductTask> entity)
    {
        entity.ToTable("tasks", table =>
        {
            table.HasCheckConstraint(
                "CK_tasks_status",
                "status IN ('open', 'completed', 'system_closed')");
            table.HasCheckConstraint(
                "CK_tasks_highest_stage",
                "highest_stage IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
            table.HasCheckConstraint(
                "CK_tasks_open_closed_at",
                "status <> 'open' OR closed_at_utc IS NULL");
            table.HasCheckConstraint(
                "CK_tasks_closed_closed_at",
                "status = 'open' OR closed_at_utc IS NOT NULL");
            table.HasCheckConstraint(
                "CK_tasks_system_closed_reason",
                "status <> 'system_closed' OR (close_reason IS NOT NULL AND length(trim(close_reason)) > 0)");
        });

        entity.HasKey(task => task.Id);

        entity.Property(task => task.Id)
            .HasColumnName("id");
        entity.Property(task => task.ProductId)
            .HasColumnName("product_id");
        entity.Property(task => task.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .HasDefaultValue("open")
            .IsRequired();
        entity.Property(task => task.HighestStage)
            .HasColumnName("highest_stage")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(task => task.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(task => task.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(task => task.ClosedAtUtc)
            .HasColumnName("closed_at_utc")
            .HasColumnType("TEXT");
        entity.Property(task => task.CloseReason)
            .HasColumnName("close_reason")
            .HasMaxLength(200);

        entity.HasOne(task => task.Product)
            .WithMany(product => product.Tasks)
            .HasForeignKey(task => task.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasAlternateKey(task => new { task.Id, task.ProductId })
            .HasName("AK_tasks_id_product_id");
        entity.HasIndex(task => new { task.ProductId, task.Status })
            .HasDatabaseName("IX_tasks_product_id_status");
        entity.HasIndex(task => task.ProductId)
            .HasDatabaseName("IX_tasks_product_id_open")
            .HasFilter("status = 'open'")
            .IsUnique();
    }
}
