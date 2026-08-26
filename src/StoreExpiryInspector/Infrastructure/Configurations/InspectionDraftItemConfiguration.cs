using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class InspectionDraftItemConfiguration : IEntityTypeConfiguration<InspectionDraftItem>
{
    public void Configure(EntityTypeBuilder<InspectionDraftItem> entity)
    {
        entity.ToTable("draft_items", table =>
        {
            table.HasCheckConstraint(
                "CK_draft_items_checked_qty_nonnegative",
                "checked_qty IS NULL OR checked_qty >= 0");
            table.HasCheckConstraint(
                "CK_draft_items_confirmed_attention_version_nonnegative",
                "confirmed_attention_version >= 0");
        });

        entity.HasKey(item => item.Id);

        entity.Property(item => item.Id)
            .HasColumnName("id");
        entity.Property(item => item.DraftId)
            .HasColumnName("draft_id");
        entity.Property(item => item.TaskItemId)
            .HasColumnName("task_item_id");
        entity.Property(item => item.TaskId)
            .HasColumnName("task_id");
        entity.Property(item => item.CheckedQty)
            .HasColumnName("checked_qty");
        entity.Property(item => item.ConfirmedAttentionVersion)
            .HasColumnName("confirmed_attention_version");

        entity.HasOne(item => item.Draft)
            .WithMany(draft => draft.Items)
            .HasForeignKey(item => new { item.DraftId, item.TaskId })
            .HasPrincipalKey(draft => new { draft.Id, draft.TaskId })
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(item => item.TaskItem)
            .WithMany(taskItem => taskItem.DraftItems)
            .HasForeignKey(item => new { item.TaskItemId, item.TaskId })
            .HasPrincipalKey(taskItem => new { taskItem.Id, taskItem.TaskId })
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(item => new { item.DraftId, item.TaskItemId })
            .HasDatabaseName("IX_draft_items_draft_id_task_item_id")
            .IsUnique();
    }
}
