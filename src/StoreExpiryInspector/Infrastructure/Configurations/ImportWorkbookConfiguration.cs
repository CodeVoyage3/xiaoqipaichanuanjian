using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ImportWorkbookConfiguration : IEntityTypeConfiguration<ImportWorkbook>
{
    public void Configure(EntityTypeBuilder<ImportWorkbook> entity)
    {
        entity.ToTable("import_workbooks", table =>
        {
            table.HasCheckConstraint(
                "CK_import_workbooks_original_file_name_not_blank",
                "length(original_file_name) > 0 AND original_file_name = trim(original_file_name)");
            table.HasCheckConstraint(
                "CK_import_workbooks_content_not_empty",
                "length(content) > 0");
            table.HasCheckConstraint(
                "CK_import_workbooks_sha256_lower_hex",
                "length(sha256) = 64 AND sha256 NOT GLOB '*[^0-9a-f]*'");
        });

        entity.HasKey(workbook => workbook.Id);

        entity.Property(workbook => workbook.Id)
            .HasColumnName("id");
        entity.Property(workbook => workbook.ImportId)
            .HasColumnName("import_id");
        entity.Property(workbook => workbook.OriginalFileName)
            .HasColumnName("original_file_name")
            .HasMaxLength(500)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(workbook => workbook.Content)
            .HasColumnName("content")
            .HasColumnType("BLOB")
            .IsRequired();
        entity.Property(workbook => workbook.Sha256)
            .HasColumnName("sha256")
            .HasMaxLength(64)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(workbook => workbook.SavedAtUtc)
            .HasColumnName("saved_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        entity.HasOne<ImportRecord>()
            .WithMany()
            .HasForeignKey(workbook => workbook.ImportId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(workbook => workbook.ImportId)
            .HasDatabaseName("IX_import_workbooks_import_id")
            .IsUnique();
    }
}
