using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class InspectionItemRevisionConfiguration : IEntityTypeConfiguration<InspectionItemRevision>
{
    public void Configure(EntityTypeBuilder<InspectionItemRevision> entity)
    {
        entity.ToTable("inspection_item_revisions", table =>
        {
            table.HasCheckConstraint(
                "CK_inspection_item_revisions_previous_checked_qty_nonnegative",
                "previous_checked_qty >= 0");
            table.HasCheckConstraint(
                "CK_inspection_item_revisions_new_checked_qty_nonnegative",
                "new_checked_qty >= 0");
            table.HasCheckConstraint(
                "CK_inspection_item_revisions_checked_qty_changed",
                "previous_checked_qty <> new_checked_qty");
        });

        entity.HasKey(revision => revision.Id);

        entity.Property(revision => revision.Id)
            .HasColumnName("id");
        entity.Property(revision => revision.InspectionItemId)
            .HasColumnName("inspection_item_id");
        entity.Property(revision => revision.PreviousCheckedQty)
            .HasColumnName("previous_checked_qty");
        entity.Property(revision => revision.NewCheckedQty)
            .HasColumnName("new_checked_qty");
        entity.Property(revision => revision.ChangedAtUtc)
            .HasColumnName("changed_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasOne(revision => revision.InspectionItem)
            .WithMany(item => item.Revisions)
            .HasForeignKey(revision => revision.InspectionItemId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(revision => new
            {
                revision.InspectionItemId,
                revision.ChangedAtUtc,
                revision.Id
            })
            .HasDatabaseName("IX_inspection_item_revisions_inspection_item_id_changed_at_utc_id");
    }
}
