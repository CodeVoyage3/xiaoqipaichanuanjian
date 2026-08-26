namespace StoreExpiryInspector.Domain;

public sealed class AppSetting
{
    public long Id { get; set; }

    public int ReminderMinuteOfDay { get; set; } = 600;

    public bool AutoStartEnabled { get; set; } = true;
}
