using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
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
    IReadOnlyList<ReminderCandidate> Items,
    IReadOnlyList<PreReminderCandidate> PreReminderItems);

public sealed record PreReminderCandidate(long BatchId, long ProductId, string TargetStage);

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
        var preReminderItems = GetPreReminderCandidates(context, businessDate);
        return new(
            items.Count == 0 && preReminderItems.Count == 0 ? DailyReminderStatuses.NoItems : DailyReminderStatuses.Due,
            businessDate,
            reminderMinuteOfDay,
            items,
            preReminderItems);
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
            Array.Empty<ReminderCandidate>(),
            Array.Empty<PreReminderCandidate>());

    private static IReadOnlyList<PreReminderCandidate> GetPreReminderCandidates(StoreDbContext context, DateOnly businessDate)
    {
        var batches = context.Batches
            .AsNoTracking()
            .Where(batch => batch.TrackingStatus == "active" &&
                batch.Product.EffectiveStockQty > 0 &&
                batch.Product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                batch.Product.PolicyVersion == ExpiryPolicies.Version1 &&
                (batch.Product.PolicyCode == ExpiryPolicies.Food ||
                 batch.Product.PolicyCode == ExpiryPolicies.Pet ||
                 batch.Product.PolicyCode == ExpiryPolicies.GeneralLong) &&
                context.ScopeBaselines.Any(baseline => baseline.IsCompleted &&
                    baseline.ScopeKey == batch.Product.CategoryCode &&
                    baseline.PolicyCode == batch.Product.PolicyCode &&
                    baseline.PolicyVersion == batch.Product.PolicyVersion))
            .Select(batch => new
            {
                batch.Id,
                batch.ProductId,
                batch.ExpiryDate,
                batch.ShelfLifeValue,
                batch.ShelfLifeUnit,
                batch.Product.PolicyCode,
                batch.Product.PolicyVersion
            })
            .ToArray();

        return batches.SelectMany(batch =>
            {
                var days = batch.ShelfLifeUnit switch
                {
                    "D" => batch.ShelfLifeValue,
                    "M" => batch.ShelfLifeValue * 30,
                    "Y" => batch.ShelfLifeValue * 365,
                    _ => 0
                };
                var dates = days > 0
                    ? ExpiryPolicyCalculator.CalculateStageDates(batch.PolicyCode!, batch.PolicyVersion!.Value, batch.ExpiryDate, days)
                    : null;
                return dates is null ? Array.Empty<PreReminderCandidate>() :
                    new[]
                    {
                        Node(batch.Id, batch.ProductId, ExpiryStageCalculator.Discount50, dates.Discount50),
                        Node(batch.Id, batch.ProductId, ExpiryStageCalculator.Discount20, dates.Discount20),
                        Node(batch.Id, batch.ProductId, ExpiryStageCalculator.Withdraw, dates.Withdraw),
                        Node(batch.Id, batch.ProductId, ExpiryStageCalculator.Expired, dates.Expired)
                    }.Where(item => item is not null).Select(item => item!).ToArray();
            })
            .ToArray();

        PreReminderCandidate? Node(long batchId, long productId, string stage, DateOnly effectiveDate) =>
            effectiveDate.AddDays(-3) <= businessDate && businessDate < effectiveDate
                ? new(batchId, productId, stage)
                : null;
    }
}
