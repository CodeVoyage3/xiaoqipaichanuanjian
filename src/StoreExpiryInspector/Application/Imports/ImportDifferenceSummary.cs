using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Imports;

public sealed record ImportDifferenceSummary(
    int NewProductCount,
    int NewBatchCount,
    int? StockIncreaseCount,
    int? StockDecreaseCount,
    int? StockBecameZeroCount,
    int? MissingBatchCount,
    int? MissingProductCount,
    int? MissingOpenTaskBatchCount,
    int? MissingOpenTaskProductCount,
    int IssueCount);

public sealed class ImportDifferenceSummaryQuery
{
    public ImportDifferenceSummary Read(StoreDbContext context, ImportPlan plan, long importId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);

        var issueCount = context.ImportIssues.AsNoTracking().Count(issue => issue.ImportId == importId);
        var currentConfirmedAtUtc = context.Imports.AsNoTracking()
            .Where(import => import.Id == importId)
            .Select(import => import.ConfirmedAtUtc)
            .SingleOrDefault();
        if (currentConfirmedAtUtc is null)
        {
            return new(plan.NewProductCount, plan.NewBatchCount, null, null, null, null, null, null, null, issueCount);
        }

        var baselineId = context.Imports.AsNoTracking()
            .Where(import => import.ConfirmedAtUtc != null &&
                import.Status == ImportStatuses.Succeeded && !import.IsUndone &&
                (import.ConfirmedAtUtc < currentConfirmedAtUtc ||
                 import.ConfirmedAtUtc == currentConfirmedAtUtc && import.Id < importId))
            .OrderByDescending(import => import.ConfirmedAtUtc)
            .ThenByDescending(import => import.Id)
            .Select(import => (long?)import.Id)
            .FirstOrDefault();
        if (baselineId is null)
        {
            return new(plan.NewProductCount, plan.NewBatchCount, null, null, null, null, null, null, null, issueCount);
        }

        var stockChanges = plan.UpdatedProducts
            .SelectMany(product => product.FieldChanges)
            .Where(change => change.FieldName == "ExcelStockQty" && change.Before is int && change.After is int)
            .Select(change => (Before: (int)change.Before!, After: (int)change.After!))
            .ToArray();
        var missingBatches = context.Batches.AsNoTracking().Where(batch => batch.LastSeenImportId == baselineId);
        var missingOpenTaskItems = context.TaskItems.AsNoTracking()
            .Where(item => item.Task.Status == "open" && item.Batch.LastSeenImportId == baselineId);

        return new(
            plan.NewProductCount,
            plan.NewBatchCount,
            stockChanges.Count(change => change.After > change.Before),
            stockChanges.Count(change => change.After < change.Before),
            stockChanges.Count(change => change.Before > 0 && change.After == 0),
            missingBatches.Select(batch => batch.Id).Distinct().Count(),
            missingBatches.Select(batch => batch.ProductId).Distinct().Count(),
            missingOpenTaskItems.Select(item => item.BatchId).Distinct().Count(),
            missingOpenTaskItems.Select(item => item.ProductId).Distinct().Count(),
            issueCount);
    }
}
