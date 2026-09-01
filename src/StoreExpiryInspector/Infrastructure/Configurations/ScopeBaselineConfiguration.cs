using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ScopeBaselineConfiguration : IEntityTypeConfiguration<ScopeBaseline>
{
    public void Configure(EntityTypeBuilder<ScopeBaseline> entity)
    {
        entity.ToTable("scope_baselines", table =>
        {
            table.HasCheckConstraint("CK_scope_baselines_scope_key_not_blank", "length(scope_key) > 0 AND scope_key = trim(scope_key)");
            table.HasCheckConstraint("CK_scope_baselines_policy_code_not_blank", "length(policy_code) > 0 AND policy_code = trim(policy_code)");
            table.HasCheckConstraint("CK_scope_baselines_policy_version_positive", "policy_version > 0");
            table.HasCheckConstraint("CK_scope_baselines_completed_fields", "(is_completed = 0 AND completed_at_utc IS NULL) OR (is_completed = 1 AND completed_at_utc IS NOT NULL)");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.ScopeKey).HasColumnName("scope_key").HasMaxLength(50).HasConversion(value => value.Trim(), value => value).IsRequired();
        entity.Property(value => value.PolicyCode).HasColumnName("policy_code").HasMaxLength(50).HasConversion(value => value.Trim(), value => value).IsRequired();
        entity.Property(value => value.PolicyVersion).HasColumnName("policy_version");
        entity.Property(value => value.CreatedImportId).HasColumnName("created_import_id");
        entity.Property(value => value.BusinessDate).HasColumnName("business_date").HasColumnType("TEXT");
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("TEXT");
        entity.Property(value => value.IsCompleted).HasColumnName("is_completed").HasDefaultValue(false);
        entity.Property(value => value.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("TEXT");
        entity.HasIndex(value => new { value.ScopeKey, value.PolicyCode, value.PolicyVersion }).HasDatabaseName("IX_scope_baselines_scope_key_policy_code_policy_version").IsUnique();
        entity.HasOne<ImportRecord>().WithMany().HasForeignKey(value => value.CreatedImportId).OnDelete(DeleteBehavior.NoAction);
    }
}
