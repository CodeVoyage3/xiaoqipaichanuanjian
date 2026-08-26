namespace StoreExpiryInspector.Domain;

public sealed class AppState
{
    public long Id { get; set; }

    public DateOnly? LastReminderDate { get; set; }

    public DateOnly? LastNormalRunDate { get; set; }
}
