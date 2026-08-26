using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure.Configurations;

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

    public DbSet<Inspection> Inspections => Set<Inspection>();

    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();

    public DbSet<InspectionItemRevision> InspectionItemRevisions => Set<InspectionItemRevision>();

    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();

    public DbSet<ImportRecord> Imports => Set<ImportRecord>();

    public DbSet<ImportWorkbook> ImportWorkbooks => Set<ImportWorkbook>();

    public DbSet<ImportIssue> ImportIssues => Set<ImportIssue>();

    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    public DbSet<AppState> AppStates => Set<AppState>();

    public DbSet<LifecycleEvent> LifecycleEvents => Set<LifecycleEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new BatchConfiguration());
        modelBuilder.ApplyConfiguration(new ProductTaskConfiguration());
        modelBuilder.ApplyConfiguration(new ProductTaskItemConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionDraftConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionDraftItemConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionItemConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionItemRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new InventoryAdjustmentConfiguration());
        modelBuilder.ApplyConfiguration(new ImportRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ImportWorkbookConfiguration());
        modelBuilder.ApplyConfiguration(new ImportIssueConfiguration());
        modelBuilder.ApplyConfiguration(new BackupRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AppSettingConfiguration());
        modelBuilder.ApplyConfiguration(new AppStateConfiguration());
        modelBuilder.ApplyConfiguration(new LifecycleEventConfiguration());
    }
}
