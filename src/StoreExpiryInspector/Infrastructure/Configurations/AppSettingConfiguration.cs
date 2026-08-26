using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> entity)
    {
        entity.ToTable("settings", table =>
        {
            table.HasCheckConstraint(
                "CK_settings_id_singleton",
                "id = 1");
            table.HasCheckConstraint(
                "CK_settings_reminder_minute_of_day_range",
                "reminder_minute_of_day BETWEEN 0 AND 1439");
            table.HasCheckConstraint(
                "CK_settings_auto_start_enabled",
                "auto_start_enabled IN (0, 1)");
        });

        entity.HasKey(setting => setting.Id);

        entity.Property(setting => setting.Id)
            .HasColumnName("id");
        entity.Property(setting => setting.ReminderMinuteOfDay)
            .HasColumnName("reminder_minute_of_day")
            .HasDefaultValue(600)
            .IsRequired();
        entity.Property(setting => setting.AutoStartEnabled)
            .HasColumnName("auto_start_enabled")
            .HasDefaultValue(true)
            .IsRequired();
    }
}
