using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ImportRecordConfiguration : IEntityTypeConfiguration<ImportRecord>
{
    public void Configure(EntityTypeBuilder<ImportRecord> entity)
    {
        entity.ToTable("imports", table =>
        {
            table.HasCheckConstraint(
                "CK_imports_source_file_name_not_blank",
                "length(source_file_name) > 0 AND source_file_name = trim(source_file_name)");
            table.HasCheckConstraint(
                "CK_imports_source_file_sha256_lower_hex",
                "length(source_file_sha256) = 64 AND source_file_sha256 NOT GLOB '*[^0-9a-f]*'");
            table.HasCheckConstraint(
                "CK_imports_status_not_blank",
                "length(status) > 0 AND status = trim(status)");
            table.HasCheckConstraint(
                "CK_imports_product_count_nonnegative",
                "product_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_batch_count_nonnegative",
                "batch_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_new_product_count_nonnegative",
                "new_product_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_new_batch_count_nonnegative",
                "new_batch_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_updated_batch_count_nonnegative",
                "updated_batch_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_issue_count_nonnegative",
                "issue_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_unsupported_category_count_nonnegative",
                "unsupported_category_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_new_task_product_count_nonnegative",
                "new_task_product_count >= 0");
            table.HasCheckConstraint(
                "CK_imports_undone_fields",
                "(is_undone = 0 AND undone_at_utc IS NULL) OR " +
                "(is_undone = 1 AND undone_at_utc IS NOT NULL)");
        });

        entity.HasKey(importRecord => importRecord.Id);

        entity.Property(importRecord => importRecord.Id)
            .HasColumnName("id");
        entity.Property(importRecord => importRecord.SourceFileName)
            .HasColumnName("source_file_name")
            .HasMaxLength(500)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(importRecord => importRecord.SourceFileSha256)
            .HasColumnName("source_file_sha256")
            .HasMaxLength(64)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(importRecord => importRecord.ParsedAtUtc)
            .HasColumnName("parsed_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();
        entity.Property(importRecord => importRecord.ConfirmedAtUtc)
            .HasColumnName("confirmed_at_utc")
            .HasColumnType("TEXT");
        entity.Property(importRecord => importRecord.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(importRecord => importRecord.ProductCount)
            .HasColumnName("product_count");
        entity.Property(importRecord => importRecord.BatchCount)
            .HasColumnName("batch_count");
        entity.Property(importRecord => importRecord.NewProductCount)
            .HasColumnName("new_product_count");
        entity.Property(importRecord => importRecord.NewBatchCount)
            .HasColumnName("new_batch_count");
        entity.Property(importRecord => importRecord.UpdatedBatchCount)
            .HasColumnName("updated_batch_count");
        entity.Property(importRecord => importRecord.IssueCount)
            .HasColumnName("issue_count");
        entity.Property(importRecord => importRecord.UnsupportedCategoryCount)
            .HasColumnName("unsupported_category_count");
        entity.Property(importRecord => importRecord.NewTaskProductCount)
            .HasColumnName("new_task_product_count");
        entity.Property(importRecord => importRecord.PreImportSnapshotPath)
            .HasColumnName("pre_import_snapshot_path")
            .HasMaxLength(1000);
        entity.Property(importRecord => importRecord.IsUndone)
            .HasColumnName("is_undone")
            .HasDefaultValue(false)
            .IsRequired();
        entity.Property(importRecord => importRecord.UndoneAtUtc)
            .HasColumnName("undone_at_utc")
            .HasColumnType("TEXT");

        entity.HasIndex(importRecord => new
            {
                importRecord.Status,
                importRecord.ConfirmedAtUtc,
                importRecord.Id
            })
            .HasDatabaseName("IX_imports_status_confirmed_at_utc_id");
        entity.HasIndex(importRecord => importRecord.SourceFileSha256)
            .HasDatabaseName("IX_imports_source_file_sha256");
    }
}
