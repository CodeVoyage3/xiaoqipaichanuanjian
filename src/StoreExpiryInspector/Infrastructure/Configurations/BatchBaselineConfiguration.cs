using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class BatchBaselineConfiguration : IEntityTypeConfiguration<BatchBaseline>
{
    public void Configure(EntityTypeBuilder<BatchBaseline> entity)
    {
        entity.ToTable("batch_baselines", table =>
        {
            table.HasCheckConstraint("CK_batch_baselines_stage", "stage_at_baseline IN ('none', 'discount_50', 'discount_20', 'withdraw', 'expired')");
            table.HasCheckConstraint("CK_batch_baselines_disposition", "cold_start_disposition IN ('discount50_baseline', 'discount20_baseline', 'withdraw_task', 'expired_today_task', 'expired_catchup_task', 'expired_historical_baseline', 'stock_zero_baseline')");
            table.HasCheckConstraint("CK_batch_baselines_catchup_window", "(cold_start_disposition = 'expired_catchup_task' AND catchup_window_days BETWEEN 3 AND 30) OR (cold_start_disposition <> 'expired_catchup_task' AND catchup_window_days IS NULL)");
            table.HasCheckConstraint("CK_batch_baselines_sources", "(cold_start_disposition IN ('withdraw_task', 'expired_today_task', 'expired_catchup_task') AND source_task_id IS NOT NULL) OR (cold_start_disposition NOT IN ('withdraw_task', 'expired_today_task', 'expired_catchup_task') AND source_task_id IS NULL AND catchup_source IS NULL)");
            table.HasCheckConstraint("CK_batch_baselines_catchup_source", "(cold_start_disposition = 'expired_catchup_task' AND length(catchup_source) > 0 AND catchup_source = trim(catchup_source)) OR (cold_start_disposition <> 'expired_catchup_task' AND catchup_source IS NULL)");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.BaselineId).HasColumnName("baseline_id");
        entity.Property(value => value.BatchId).HasColumnName("batch_id");
        entity.Property(value => value.StageAtBaseline).HasColumnName("stage_at_baseline").HasMaxLength(20).IsRequired();
        entity.Property(value => value.ColdStartDisposition).HasColumnName("cold_start_disposition").HasMaxLength(50).IsRequired();
        entity.Property(value => value.CatchupWindowDays).HasColumnName("catchup_window_days");
        entity.Property(value => value.SourceTaskId).HasColumnName("source_task_id");
        entity.Property(value => value.CatchupSource).HasColumnName("catchup_source").HasMaxLength(100).HasConversion(value => value == null ? null : value.Trim(), value => value);
        entity.HasIndex(value => new { value.BaselineId, value.BatchId }).HasDatabaseName("IX_batch_baselines_baseline_id_batch_id").IsUnique();
        entity.HasOne(value => value.Baseline).WithMany(value => value.BatchBaselines).HasForeignKey(value => value.BaselineId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Batch>().WithMany().HasForeignKey(value => value.BatchId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<ProductTask>().WithMany().HasForeignKey(value => value.SourceTaskId).OnDelete(DeleteBehavior.NoAction);
    }
}
