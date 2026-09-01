using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public sealed record StartupRecalculationRequest(
    DateOnly BusinessDate,
    DateTime UpdatedAtUtc);

public sealed record StartupRecalculationResult(
    int MatchedBatchCount,
    int ChangedBatchCount,
    int AggregatedBatchCount,
    int AggregatedProductCount);

public sealed class StartupRecalculationUseCase
{
    private readonly ProductTaskAggregator _taskAggregator;

    public StartupRecalculationUseCase(ProductTaskAggregator? taskAggregator = null)
    {
        _taskAggregator = taskAggregator ?? new ProductTaskAggregator();
    }

    public StartupRecalculationResult Execute(
        StoreDbContext context,
        StartupRecalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (request.UpdatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("UpdatedAtUtc must be UTC.", nameof(request));
        }

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            var batches = context.Batches
                .AsTracking()
                .Include(batch => batch.Product)
                .Where(batch =>
                    batch.TrackingStatus == "active" &&
                    batch.NextTriggerDate.HasValue &&
                    batch.NextTriggerDate.Value <= request.BusinessDate &&
                    context.Products.Any(product =>
                        product.Id == batch.ProductId &&
                        product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                        product.PolicyVersion == ExpiryPolicies.Version1 &&
                        (product.PolicyCode == ExpiryPolicies.Food ||
                         product.PolicyCode == ExpiryPolicies.Pet ||
                         product.PolicyCode == ExpiryPolicies.GeneralLong) &&
                        context.ScopeBaselines.Any(baseline =>
                            baseline.IsCompleted &&
                            baseline.ScopeKey == product.CategoryCode &&
                            baseline.PolicyCode == product.PolicyCode &&
                            baseline.PolicyVersion == product.PolicyVersion)))
                .ToArray();

            if (batches.Length == 0)
            {
                transaction?.Commit();
                return new(0, 0, 0, 0);
            }

            var calculated = new List<(Batch Batch, ExpiryStageResult Result)>(batches.Length);
            var changedBatchCount = 0;
            foreach (var batch in batches)
            {
                var result = ExpiryPolicyCalculator.Calculate(
                    batch.Product.PolicyCode!,
                    batch.Product.PolicyVersion!.Value,
                    request.BusinessDate,
                    batch.ExpiryDate,
                    ShelfLifeDays(batch));
                if (result is null)
                {
                    throw new InvalidOperationException(
                        $"Product {batch.ProductId} has an uncovered expiry policy.");
                }
                calculated.Add((batch, result));

                if (string.Equals(batch.CurrentStage, result.CurrentStage, StringComparison.Ordinal) &&
                    batch.NextTriggerDate == result.NextTriggerDate)
                {
                    continue;
                }

                batch.CurrentStage = result.CurrentStage;
                batch.NextTriggerDate = result.NextTriggerDate;
                batch.UpdatedAtUtc = request.UpdatedAtUtc;
                changedBatchCount++;
            }

            var aggregatedBatchCount = 0;
            var aggregatedProductCount = 0;
            foreach (var productGroup in calculated
                .Where(item => ExpiryStageCalculator.GetStagePriority(item.Result.CurrentStage) > 0)
                .GroupBy(item => item.Batch.ProductId))
            {
                var batchResults = productGroup
                    .Select(item => new ProductTaskBatchResult(
                        item.Batch.Id,
                        item.Result.CurrentStage,
                        item.Batch.AttentionVersion,
                        false))
                    .ToArray();
                _taskAggregator.Aggregate(
                    context,
                    new ProductTaskAggregationRequest(
                        productGroup.Key,
                        batchResults,
                        request.UpdatedAtUtc));
                aggregatedBatchCount += batchResults.Length;
                aggregatedProductCount++;
            }

            context.SaveChanges();
            transaction?.Commit();
            return new(
                batches.Length,
                changedBatchCount,
                aggregatedBatchCount,
                aggregatedProductCount);
        }
        catch
        {
            if (ownsTransaction)
            {
                try
                {
                    transaction?.Rollback();
                }
                catch
                {
                }
            }

            context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    public StartupRecalculationResult Execute(
        StoreDbContext context,
        DateOnly businessDate,
        DateTime updatedAtUtc) =>
        Execute(context, new StartupRecalculationRequest(businessDate, updatedAtUtc));

    private static int ShelfLifeDays(Batch batch) => batch.ShelfLifeUnit switch
    {
        "D" => batch.ShelfLifeValue,
        "M" => checked(batch.ShelfLifeValue * 30),
        "Y" => checked(batch.ShelfLifeValue * 365),
        _ => throw new ArgumentException("Invalid shelf life unit.")
    };
}
