using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Reminders;

public static class DailyReminderStatuses
{
    public const string NotDue = "not_due";
    public const string Due = "due";
    public const string AlreadyRemindedToday = "already_reminded_today";
    public const string NoItems = "no_items";
    public const string ClockRollback = "clock_rollback";
}

public sealed record DailyReminderResult(
    string Status,
    DateOnly BusinessDate,
    int ReminderMinuteOfDay,
    IReadOnlyList<ReminderCandidate> Items);

public sealed class DailyReminderUseCase
{
    private readonly InspectionTaskQuery _taskQuery;

    public DailyReminderUseCase(InspectionTaskQuery? taskQuery = null)
    {
        _taskQuery = taskQuery ?? new InspectionTaskQuery();
    }

    public DailyReminderResult Evaluate(StoreDbContext context, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (localNow.Kind == DateTimeKind.Utc)
        {
            throw new ArgumentException("Local time must not be UTC.", nameof(localNow));
        }

        var reminderMinuteOfDay = context.Settings
            .AsNoTracking()
            .Select(setting => setting.ReminderMinuteOfDay)
            .Single();
        var businessDate = DateOnly.FromDateTime(localNow);
        if (localNow.Hour * 60 + localNow.Minute < reminderMinuteOfDay)
        {
            return Result(DailyReminderStatuses.NotDue, businessDate, reminderMinuteOfDay);
        }

        var lastReminderDate = context.AppStates
            .AsNoTracking()
            .Select(state => state.LastReminderDate)
            .Single();
        if (lastReminderDate == businessDate)
        {
            return Result(DailyReminderStatuses.AlreadyRemindedToday, businessDate, reminderMinuteOfDay);
        }

        if (lastReminderDate > businessDate)
        {
            return Result(DailyReminderStatuses.ClockRollback, businessDate, reminderMinuteOfDay);
        }

        var items = _taskQuery.GetReminderCandidates(context);
        return new(
            items.Count == 0 ? DailyReminderStatuses.NoItems : DailyReminderStatuses.Due,
            businessDate,
            reminderMinuteOfDay,
            items);
    }

    public bool RecordSuccessfulReminder(
        StoreDbContext context,
        DateOnly businessDate,
        bool notificationSucceeded)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!notificationSucceeded)
        {
            return false;
        }

        var state = context.AppStates.Single();
        if (state.LastReminderDate == businessDate)
        {
            return true;
        }

        if (state.LastReminderDate > businessDate)
        {
            return false;
        }

        var reminderProperty = context.Entry(state).Property(item => item.LastReminderDate);
        var previousDate = state.LastReminderDate;
        var wasModified = reminderProperty.IsModified;
        try
        {
            state.LastReminderDate = businessDate;
            context.SaveChanges();
            return true;
        }
        catch
        {
            state.LastReminderDate = previousDate;
            reminderProperty.IsModified = wasModified;
            throw;
        }
    }

    private static DailyReminderResult Result(
        string status,
        DateOnly businessDate,
        int reminderMinuteOfDay) => new(
            status,
            businessDate,
            reminderMinuteOfDay,
            Array.Empty<ReminderCandidate>());
}
