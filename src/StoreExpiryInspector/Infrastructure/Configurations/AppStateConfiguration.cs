using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class AppStateConfiguration : IEntityTypeConfiguration<AppState>
{
    public void Configure(EntityTypeBuilder<AppState> entity)
    {
        entity.ToTable("app_state", table =>
        {
            table.HasCheckConstraint(
                "CK_app_state_id_singleton",
                "id = 1");
        });

        entity.HasKey(state => state.Id);

        entity.Property(state => state.Id)
            .HasColumnName("id");
        entity.Property(state => state.LastReminderDate)
            .HasColumnName("last_reminder_date")
            .HasColumnType("TEXT");
        entity.Property(state => state.LastNormalRunDate)
            .HasColumnName("last_normal_run_date")
            .HasColumnType("TEXT");
    }
}
