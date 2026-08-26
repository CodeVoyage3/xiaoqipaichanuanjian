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

    public DbSet<ProductTask> Tasks => Set<ProductTask>();

    public DbSet<ProductTaskItem> TaskItems => Set<ProductTaskItem>();

    public DbSet<InspectionDraft> Drafts => Set<InspectionDraft>();

    public DbSet<InspectionDraftItem> DraftItems => Set<InspectionDraftItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
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
                    "CK_products_policy_code_not_blank",
                    "length(trim(policy_code)) > 0");
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

            entity.HasAlternateKey(batch => new { batch.Id, batch.ProductId })
                .HasName("AK_batches_id_product_id");
        });

        modelBuilder.Entity<ProductTask>(entity =>
        {
            entity.ToTable("tasks", table =>
            {
                table.HasCheckConstraint(
                    "CK_tasks_status",
                    "status IN ('open', 'completed', 'system_closed')");
                table.HasCheckConstraint(
                    "CK_tasks_highest_stage",
                    "highest_stage IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
                table.HasCheckConstraint(
                    "CK_tasks_open_closed_at",
                    "status <> 'open' OR closed_at_utc IS NULL");
                table.HasCheckConstraint(
                    "CK_tasks_closed_closed_at",
                    "status = 'open' OR closed_at_utc IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_tasks_system_closed_reason",
                    "status <> 'system_closed' OR (close_reason IS NOT NULL AND length(trim(close_reason)) > 0)");
            });

            entity.HasKey(task => task.Id);

            entity.Property(task => task.Id)
                .HasColumnName("id");
            entity.Property(task => task.ProductId)
                .HasColumnName("product_id");
            entity.Property(task => task.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasDefaultValue("open")
                .IsRequired();
            entity.Property(task => task.HighestStage)
                .HasColumnName("highest_stage")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(task => task.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("TEXT")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(task => task.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .HasColumnType("TEXT")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(task => task.ClosedAtUtc)
                .HasColumnName("closed_at_utc")
                .HasColumnType("TEXT");
            entity.Property(task => task.CloseReason)
                .HasColumnName("close_reason")
                .HasMaxLength(200);

            entity.HasOne(task => task.Product)
                .WithMany(product => product.Tasks)
                .HasForeignKey(task => task.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasAlternateKey(task => new { task.Id, task.ProductId })
                .HasName("AK_tasks_id_product_id");
            entity.HasIndex(task => new { task.ProductId, task.Status })
                .HasDatabaseName("IX_tasks_product_id_status");
            entity.HasIndex(task => task.ProductId)
                .HasDatabaseName("IX_tasks_product_id_open")
                .HasFilter("status = 'open'")
                .IsUnique();
        });

        modelBuilder.Entity<ProductTaskItem>(entity =>
        {
            entity.ToTable("task_items", table =>
            {
                table.HasCheckConstraint(
                    "CK_task_items_stage",
                    "stage IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
                table.HasCheckConstraint(
                    "CK_task_items_attention_version_nonnegative",
                    "attention_version >= 0");
                table.HasCheckConstraint(
                    "CK_task_items_requires_reconfirmation",
                    "requires_reconfirmation IN (0, 1)");
            });

            entity.HasKey(item => item.Id);

            entity.Property(item => item.Id)
                .HasColumnName("id");
            entity.Property(item => item.TaskId)
                .HasColumnName("task_id");
            entity.Property(item => item.BatchId)
                .HasColumnName("batch_id");
            entity.Property(item => item.ProductId)
                .HasColumnName("product_id");
            entity.Property(item => item.Stage)
                .HasColumnName("stage")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(item => item.AttentionVersion)
                .HasColumnName("attention_version");
            entity.Property(item => item.RequiresReconfirmation)
                .HasColumnName("requires_reconfirmation")
                .HasDefaultValue(false)
                .IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("TEXT")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(item => item.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .HasColumnType("TEXT")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(item => item.Task)
                .WithMany(task => task.Items)
                .HasForeignKey(item => new { item.TaskId, item.ProductId })
                .HasPrincipalKey(task => new { task.Id, task.ProductId })
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(item => item.Batch)
                .WithMany(batch => batch.TaskItems)
                .HasForeignKey(item => new { item.BatchId, item.ProductId })
                .HasPrincipalKey(batch => new { batch.Id, batch.ProductId })
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasAlternateKey(item => new { item.Id, item.TaskId })
                .HasName("AK_task_items_id_task_id");
            entity.HasIndex(item => new { item.TaskId, item.BatchId })
                .HasDatabaseName("IX_task_items_task_id_batch_id")
                .IsUnique();
        });

        modelBuilder.Entity<InspectionDraft>(entity =>
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
        });

        modelBuilder.Entity<InspectionDraftItem>(entity =>
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
        });
    }
}
