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
        var baselineId = context.Imports.AsNoTracking()
            .Where(import => import.Id != importId && import.Status == ImportStatuses.Succeeded && !import.IsUndone)
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
        var missingBatches = context.Batches.AsNoTracking()
            .Where(batch => batch.LastSeenImportId == baselineId)
            .Select(batch => new { batch.Id, batch.ProductId })
            .ToArray();
        var missingBatchIds = missingBatches.Select(batch => batch.Id).ToArray();
        var missingOpenTaskBatchIds = context.TaskItems.AsNoTracking()
            .Where(item => missingBatchIds.Contains(item.BatchId) && item.Task.Status == "open")
            .Select(item => item.BatchId)
            .Distinct()
            .ToArray();
        var missingById = missingBatches.ToDictionary(batch => batch.Id);

        return new(
            plan.NewProductCount,
            plan.NewBatchCount,
            stockChanges.Count(change => change.After > change.Before),
            stockChanges.Count(change => change.After < change.Before),
            stockChanges.Count(change => change.Before > 0 && change.After == 0),
            missingBatches.Length,
            missingBatches.Select(batch => batch.ProductId).Distinct().Count(),
            missingOpenTaskBatchIds.Length,
            missingOpenTaskBatchIds.Select(id => missingById[id].ProductId).Distinct().Count(),
            issueCount);
    }
}
