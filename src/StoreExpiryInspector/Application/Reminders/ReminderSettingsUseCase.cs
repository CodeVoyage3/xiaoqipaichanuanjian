using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Reminders;

public sealed record ReminderTimeSaveResult(
    bool Succeeded,
    int? ReminderMinuteOfDay,
    string Message);

public sealed class ReminderSettingsUseCase
{
    public int GetReminderMinuteOfDay(StoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Settings
            .AsNoTracking()
            .Select(setting => setting.ReminderMinuteOfDay)
            .Single();
    }

    public ReminderTimeSaveResult SaveReminderTime(StoreDbContext context, string reminderTimeText)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TimeOnly.TryParseExact(
                reminderTimeText?.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var reminderTime))
        {
            return new(false, null, "请输入有效时间，格式为 HH:mm。");
        }

        var minuteOfDay = reminderTime.Hour * 60 + reminderTime.Minute;
        var setting = context.Settings.Single();
        var previous = setting.ReminderMinuteOfDay;
        try
        {
            setting.ReminderMinuteOfDay = minuteOfDay;
            context.SaveChanges();
            return new(true, minuteOfDay, "提醒时间已保存。");
        }
        catch
        {
            setting.ReminderMinuteOfDay = previous;
            context.Entry(setting).Property(item => item.ReminderMinuteOfDay).IsModified = false;
            throw;
        }
    }

    public static string Format(int reminderMinuteOfDay)
    {
        if (reminderMinuteOfDay is < 0 or >= 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(reminderMinuteOfDay));
        }

        return $"{reminderMinuteOfDay / 60:00}:{reminderMinuteOfDay % 60:00}";
    }
}
