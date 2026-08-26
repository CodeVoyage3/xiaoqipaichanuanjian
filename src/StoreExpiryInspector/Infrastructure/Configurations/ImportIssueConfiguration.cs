using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ImportIssueConfiguration : IEntityTypeConfiguration<ImportIssue>
{
    public void Configure(EntityTypeBuilder<ImportIssue> entity)
    {
        entity.ToTable("import_issues", table =>
        {
            table.HasCheckConstraint(
                "CK_import_issues_row_number_positive",
                "row_number IS NULL OR row_number > 0");
            table.HasCheckConstraint(
                "CK_import_issues_issue_type_not_blank",
                "length(issue_type) > 0 AND issue_type = trim(issue_type)");
            table.HasCheckConstraint(
                "CK_import_issues_safe_summary_not_blank",
                "length(safe_summary) > 0 AND safe_summary = trim(safe_summary)");
        });

        entity.HasKey(issue => issue.Id);

        entity.Property(issue => issue.Id)
            .HasColumnName("id");
        entity.Property(issue => issue.ImportId)
            .HasColumnName("import_id");
        entity.Property(issue => issue.RowNumber)
            .HasColumnName("row_number");
        entity.Property(issue => issue.IssueType)
            .HasColumnName("issue_type")
            .HasMaxLength(100)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(issue => issue.FieldName)
            .HasColumnName("field_name")
            .HasMaxLength(200);
        entity.Property(issue => issue.SafeSummary)
            .HasColumnName("safe_summary")
            .HasMaxLength(1000)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();

        entity.HasOne<ImportRecord>()
            .WithMany()
            .HasForeignKey(issue => issue.ImportId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(issue => new
            {
                issue.ImportId,
                issue.RowNumber,
                issue.Id
            })
            .HasDatabaseName("IX_import_issues_import_id_row_number_id");
    }
}
