using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        entity.ToTable("products", table =>
        {
            table.HasCheckConstraint(
                "CK_products_product_code_not_blank",
                "length(product_code) > 0 AND product_code = trim(product_code)");
            table.HasCheckConstraint(
                "CK_products_excel_stock_qty_nonnegative",
                "excel_stock_qty >= 0");
            table.HasCheckConstraint(
                "CK_products_effective_stock_qty_nonnegative",
                "effective_stock_qty >= 0");
            table.HasCheckConstraint(
                "CK_products_category_code_not_blank",
                "length(trim(category_code)) > 0");
            table.HasCheckConstraint(
                "CK_products_expiry_management_policy",
                "(expiry_management_status = 'managed' AND policy_code IN ('food_expiry', 'pet_expiry', 'general_long_expiry') AND policy_version = 1) OR " +
                "(expiry_management_status IN ('excluded', 'unresolved') AND policy_code IS NULL AND policy_version IS NULL)");
        });

        entity.HasKey(product => product.Id);

        entity.Property(product => product.Id)
            .HasColumnName("id");
        entity.Property(product => product.ProductCode)
            .HasColumnName("product_code")
            .HasMaxLength(200)
            .HasConversion(value => value.Trim(), value => value)
            .IsRequired();
        entity.Property(product => product.CurrentName)
            .HasColumnName("current_name")
            .HasMaxLength(500);
        entity.Property(product => product.CurrentBarcode)
            .HasColumnName("current_barcode")
            .HasMaxLength(200);
        entity.Property(product => product.CategoryCode)
            .HasColumnName("category_code")
            .HasMaxLength(50)
            .HasDefaultValue("food")
            .IsRequired();
        entity.Property(product => product.PolicyCode)
            .HasColumnName("policy_code")
            .HasMaxLength(50)
            .HasDefaultValue(ExpiryPolicies.Food);
        entity.Property(product => product.PolicyVersion)
            .HasColumnName("policy_version")
            .HasDefaultValue(ExpiryPolicies.Version1);
        entity.Property(product => product.ExpiryManagementStatus)
            .HasColumnName("expiry_management_status")
            .HasMaxLength(20)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<ExpiryManagementStatus>(value, ignoreCase: true))
            .HasDefaultValue(ExpiryManagementStatus.Managed)
            .IsRequired();
        entity.Property(product => product.ExcelStockQty)
            .HasColumnName("excel_stock_qty");
        entity.Property(product => product.EffectiveStockQty)
            .HasColumnName("effective_stock_qty");
        entity.Property(product => product.EffectiveStockSource)
            .HasColumnName("effective_stock_source")
            .HasMaxLength(50);
        entity.Property(product => product.LifecycleGeneration)
            .HasColumnName("lifecycle_generation");
        entity.Property(product => product.IsStockZeroTerminated)
            .HasColumnName("is_stock_zero_terminated")
            .HasDefaultValue(false);
        entity.Property(product => product.LastSeenImportId)
            .HasColumnName("last_seen_import_id");
        entity.Property(product => product.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(product => product.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasIndex(product => product.ProductCode)
            .HasDatabaseName("IX_products_product_code")
            .IsUnique();

        entity.HasOne<ImportRecord>()
            .WithMany()
            .HasForeignKey(product => product.LastSeenImportId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
