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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new BatchConfiguration());
        modelBuilder.ApplyConfiguration(new ProductTaskConfiguration());
        modelBuilder.ApplyConfiguration(new ProductTaskItemConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionDraftConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionDraftItemConfiguration());
    }
}
