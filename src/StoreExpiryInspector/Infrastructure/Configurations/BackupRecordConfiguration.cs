using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> entity)
    {
        entity.ToTable("backups", table =>
        {
            table.HasCheckConstraint(
                "CK_backups_backup_type",
                "backup_type IN ('auto', 'manual', 'pre_import', 'pre_restore', 'pre_upgrade')");
            table.HasCheckConstraint(
                "CK_backups_file_path_not_blank",
                "length(file_path) > 0 AND file_path = trim(file_path)");
            table.HasCheckConstraint(
                "CK_backups_sha256_lower_hex",
                "length(sha256) = 64 AND sha256 NOT GLOB '*[^0-9a-f]*'");
            table.HasCheckConstraint(
                "CK_backups_verification_status_not_blank",
                "length(verification_status) > 0 AND verification_status = trim(verification_status)");
        });

        entity.HasKey(record => record.Id);

        entity.Property(record => record.Id)
            .HasColumnName("id");
        entity.Property(record => record.BackupType)
            .HasColumnName("backup_type")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(record => record.FilePath)
            .HasColumnName("file_path")
            .HasMaxLength(1000)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(record => record.Sha256)
            .HasColumnName("sha256")
            .HasMaxLength(64)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(record => record.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(record => record.VerificationStatus)
            .HasColumnName("verification_status")
            .HasMaxLength(50)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();

        entity.HasIndex(record => new
            {
                record.BackupType,
                record.CreatedAtUtc,
                record.Id
            })
            .HasDatabaseName("IX_backups_backup_type_created_at_utc_id");
    }
}
