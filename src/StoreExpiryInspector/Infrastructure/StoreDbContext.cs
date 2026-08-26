using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure;

public sealed class StoreDbContext : DbContext
{
    public StoreDbContext(DbContextOptions<StoreDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Batch> Batches => Set<Batch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products", table =>
            {
                table.HasCheckConstraint(
                    "CK_products_product_code_not_blank",
                    "length(trim(product_code)) > 0");
                table.HasCheckConstraint(
                    "CK_products_excel_stock_qty_nonnegative",
                    "excel_stock_qty >= 0");
                table.HasCheckConstraint(
                    "CK_products_effective_stock_qty_nonnegative",
                    "effective_stock_qty >= 0");
                table.HasCheckConstraint(
                    "CK_products_category_code",
                    "category_code = 'food'");
                table.HasCheckConstraint(
                    "CK_products_policy_code",
                    "policy_code = 'food_v1'");
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
                .HasDefaultValue("food_v1")
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
        });

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.ToTable("batches", table =>
            {
                table.HasCheckConstraint(
                    "CK_batches_shelf_life_unit",
                    "shelf_life_unit IN ('M', 'D', 'Y')");
                table.HasCheckConstraint(
                    "CK_batches_current_arrival_qty_nonnegative",
                    "current_arrival_qty >= 0");
                table.HasCheckConstraint(
                    "CK_batches_max_arrival_qty_nonnegative",
                    "max_arrival_qty >= 0");
            });

            entity.HasKey(batch => batch.Id);

            entity.Property(batch => batch.Id)
                .HasColumnName("id");
            entity.Property(batch => batch.ProductId)
                .HasColumnName("product_id");
            entity.Property(batch => batch.ProductionDate)
                .HasColumnName("production_date")
                .HasColumnType("TEXT");
            entity.Property(batch => batch.ExpiryDate)
                .HasColumnName("expiry_date")
                .HasColumnType("TEXT");
            entity.Property(batch => batch.ShelfLifeValue)
                .HasColumnName("shelf_life_value");
            entity.Property(batch => batch.ShelfLifeUnit)
                .HasColumnName("shelf_life_unit")
                .HasMaxLength(1)
                .HasDefaultValue("D")
                .IsRequired();
            entity.Property(batch => batch.CurrentArrivalQty)
                .HasColumnName("current_arrival_qty");
            entity.Property(batch => batch.MaxArrivalQty)
                .HasColumnName("max_arrival_qty");
            entity.Property(batch => batch.SourceDiscountReference)
                .HasColumnName("source_discount_reference")
                .HasMaxLength(200);
            entity.Property(batch => batch.LifecycleGeneration)
                .HasColumnName("lifecycle_generation");
            entity.Property(batch => batch.TrackingStatus)
                .HasColumnName("tracking_status")
                .HasMaxLength(50)
                .HasDefaultValue("active")
                .IsRequired();
            entity.Property(batch => batch.StopReason)
                .HasColumnName("stop_reason")
                .HasMaxLength(100);
            entity.Property(batch => batch.StoppedAtUtc)
                .HasColumnName("stopped_at_utc")
                .HasColumnType("TEXT");
            entity.Property(batch => batch.CurrentStage)
                .HasColumnName("current_stage")
                .HasMaxLength(50)
                .HasDefaultValue("none")
                .IsRequired();
            entity.Property(batch => batch.NextTriggerDate)
                .HasColumnName("next_trigger_date")
                .HasColumnType("TEXT");
            entity.Property(batch => batch.AttentionVersion)
                .HasColumnName("attention_version");
            entity.Property(batch => batch.HandledAttentionVersion)
                .HasColumnName("handled_attention_version");
            entity.Property(batch => batch.LastSeenImportId)
                .HasColumnName("last_seen_import_id");
            entity.Property(batch => batch.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("TEXT")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(batch => batch.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .HasColumnType("TEXT")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(batch => batch.Product)
                .WithMany(product => product.Batches)
                .HasForeignKey(batch => batch.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(batch => batch.ProductId)
                .HasDatabaseName("IX_batches_product_id");
            entity.HasIndex(batch => batch.ExpiryDate)
                .HasDatabaseName("IX_batches_expiry_date");
            entity.HasIndex(batch => new { batch.TrackingStatus, batch.NextTriggerDate })
                .HasDatabaseName("IX_batches_tracking_status_next_trigger_date");
            entity.HasIndex(batch => new { batch.ProductId, batch.ProductionDate, batch.ExpiryDate })
                .HasDatabaseName("IX_batches_product_id_production_date_expiry_date")
                .HasFilter("production_date IS NOT NULL")
                .IsUnique();
            entity.HasIndex(batch => new { batch.ProductId, batch.ExpiryDate })
                .HasDatabaseName("IX_batches_product_id_expiry_date")
                .HasFilter("production_date IS NULL")
                .IsUnique();
        });
    }
}
