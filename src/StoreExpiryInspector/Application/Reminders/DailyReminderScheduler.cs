using System.Windows.Threading;
using StoreExpiryInspector.Infrastructure.Logging;

namespace StoreExpiryInspector.Application.Reminders;

public sealed class DailyReminderScheduler : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaximumWakeDelay = TimeSpan.FromMinutes(1);

    private int _reminderMinuteOfDay;
    private readonly Func<DateTime, DailyReminderRuntimeResult> _runReminder;
    private readonly Func<DateTime> _localNow;
    private readonly LocalFileLogger _logger;
    private readonly DispatcherTimer _timer;

    public DailyReminderScheduler(
        int reminderMinuteOfDay,
        Func<DateTime, DailyReminderRuntimeResult> runReminder,
        LocalFileLogger logger,
        Func<DateTime>? localNow = null)
    {
        if (reminderMinuteOfDay is < 0 or >= 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(reminderMinuteOfDay));
        }

        _reminderMinuteOfDay = reminderMinuteOfDay;
        _runReminder = runReminder ?? throw new ArgumentNullException(nameof(runReminder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localNow = localNow ?? new Func<DateTime>(() => TimeProvider.System.GetLocalNow().DateTime);
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += Timer_Tick;
    }

    public bool IsRunning { get; private set; }

    public DateTime NextCheckAt { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        RunAndSchedule(_localNow());
    }

    public void Stop()
    {
        IsRunning = false;
        _timer.Stop();
    }

    public void Reschedule(int reminderMinuteOfDay)
    {
        if (reminderMinuteOfDay is < 0 or >= 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(reminderMinuteOfDay));
        }

        _reminderMinuteOfDay = reminderMinuteOfDay;
        if (IsRunning)
        {
            RunAndSchedule(_localNow());
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = _localNow();
        if (now >= NextCheckAt)
        {
            RunAndSchedule(now);
            return;
        }

        SetTimer(now);
    }

    private void RunAndSchedule(DateTime now)
    {
        try
        {
            var result = _runReminder(now);
            NextCheckAt = CalculateNextCheck(now, _reminderMinuteOfDay, result);
        }
        catch (Exception exception)
        {
            _logger.TryWrite(
                "error",
                "daily_reminder_scheduler_failed",
                "每日提醒调度单次执行失败，应用继续运行并稍后重试。",
                exception.ToString());
            NextCheckAt = now + RetryDelay;
        }

        SetTimer(now);
    }

    private void SetTimer(DateTime now)
    {
        if (!IsRunning)
        {
            return;
        }

        var delay = NextCheckAt - now;
        _timer.Interval = delay <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : delay < MaximumWakeDelay ? delay : MaximumWakeDelay;
        _timer.Start();
    }

    public static DateTime CalculateNextCheck(
        DateTime now,
        int reminderMinuteOfDay,
        DailyReminderRuntimeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (reminderMinuteOfDay is < 0 or >= 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(reminderMinuteOfDay));
        }

        if (result.Status == DailyReminderStatuses.NotDue)
        {
            return now.Date.AddMinutes(reminderMinuteOfDay);
        }

        if (result.Status == "error"
            || (result.Status == DailyReminderStatuses.Due && !result.ReminderRecorded))
        {
            return now + RetryDelay;
        }

        return now.Date.AddDays(1).AddMinutes(reminderMinuteOfDay);
    }
}
