using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class InspectionDraftConfiguration : IEntityTypeConfiguration<InspectionDraft>
{
    public void Configure(EntityTypeBuilder<InspectionDraft> entity)
    {
        entity.ToTable("drafts", table =>
        {
            table.HasCheckConstraint(
                "CK_drafts_is_invalid",
                "is_invalid IN (0, 1)");
            table.HasCheckConstraint(
                "CK_drafts_validity_fields",
                "(is_invalid = 0 AND invalid_reason IS NULL AND invalidated_at_utc IS NULL) OR " +
                "(is_invalid = 1 AND invalid_reason IS NOT NULL AND length(trim(invalid_reason)) > 0 AND invalidated_at_utc IS NOT NULL)");
        });

        entity.HasKey(draft => draft.Id);

        entity.Property(draft => draft.Id)
            .HasColumnName("id");
        entity.Property(draft => draft.TaskId)
            .HasColumnName("task_id");
        entity.Property(draft => draft.InspectorName)
            .HasColumnName("inspector_name")
            .HasMaxLength(200);
        entity.Property(draft => draft.CheckDate)
            .HasColumnName("check_date")
            .HasColumnType("TEXT");
        entity.Property(draft => draft.IsInvalid)
            .HasColumnName("is_invalid")
            .HasDefaultValue(false)
            .IsRequired();
        entity.Property(draft => draft.InvalidReason)
            .HasColumnName("invalid_reason")
            .HasMaxLength(200);
        entity.Property(draft => draft.InvalidatedAtUtc)
            .HasColumnName("invalidated_at_utc")
            .HasColumnType("TEXT");
        entity.Property(draft => draft.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(draft => draft.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne(draft => draft.Task)
            .WithOne(task => task.Draft)
            .HasForeignKey<InspectionDraft>(draft => draft.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasAlternateKey(draft => new { draft.Id, draft.TaskId })
            .HasName("AK_drafts_id_task_id");
        entity.HasIndex(draft => draft.TaskId)
            .HasDatabaseName("IX_drafts_task_id")
            .IsUnique();
    }
}
