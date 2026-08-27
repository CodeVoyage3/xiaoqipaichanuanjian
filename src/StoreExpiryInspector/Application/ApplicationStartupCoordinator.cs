using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public sealed record ApplicationStartupRequest(
    DateOnly BusinessDate,
    DateTime OccurredAtUtc);

public sealed record ApplicationStartupResult(
    bool Succeeded,
    bool ClockRollback,
    DateOnly BusinessDate,
    StartupRecalculationResult Recalculation)
{
    public bool Success => Succeeded;
}

public sealed class ApplicationStartupCoordinator
{
    private readonly StartupRecalculationUseCase _recalculation;

    public ApplicationStartupCoordinator(StartupRecalculationUseCase? recalculation = null)
    {
        _recalculation = recalculation ?? new StartupRecalculationUseCase();
    }

    public ApplicationStartupResult Execute(
        StoreDbContext context,
        ApplicationStartupRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (request.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(request));
        }

        using var transaction = context.Database.BeginTransaction();
        try
        {
            var state = context.AppStates.Single();
            if (state.LastNormalRunDate.HasValue &&
                request.BusinessDate < state.LastNormalRunDate.Value)
            {
                transaction.Commit();
                context.ChangeTracker.Clear();
                return new(
                    true,
                    true,
                    request.BusinessDate,
                    new StartupRecalculationResult(0, 0, 0, 0));
            }

            var result = _recalculation.Execute(
                context,
                new StartupRecalculationRequest(
                    request.BusinessDate,
                    request.OccurredAtUtc));
            state.LastNormalRunDate = request.BusinessDate;
            context.SaveChanges();
            transaction.Commit();
            context.ChangeTracker.Clear();
            return new(true, false, request.BusinessDate, result);
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
            }

            context.ChangeTracker.Clear();
            throw;
        }
    }

    public ApplicationStartupResult Execute(
        StoreDbContext context,
        DateOnly businessDate,
        DateTime occurredAtUtc) => Execute(
            context,
            new ApplicationStartupRequest(businessDate, occurredAtUtc));
}
