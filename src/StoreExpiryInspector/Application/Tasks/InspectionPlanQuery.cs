using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Tasks;

/// <summary>Projects the existing open tasks plus the same due batches used by startup; never writes future tasks.</summary>
public sealed class InspectionPlanQuery
{
    public InspectionTaskSearchResult Search(StoreDbContext context, DateOnly targetDate, InspectionTaskSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Page <= 0 || request.PageSize <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var open = new InspectionTaskQuery().SearchOpenTasks(context, new(PageSize: int.MaxValue)).Items;
        // ponytail: materializes the open/due working set; add SQL paging only if store-volume profiling warrants it.
        var products = open.ToDictionary(item => item.ProductId, item => item);
        var batches = context.TaskItems.AsNoTracking().Where(item => item.Task.Status == "open")
            .Select(item => new { item.ProductId, item.BatchId, item.Stage, item.Batch.ExpiryDate }).ToArray()
            .ToDictionary(item => (item.ProductId, item.BatchId), item => (item.Stage, item.ExpiryDate));
        // Keep the eligibility and trigger semantics of StartupRecalculationUseCase, including already-open task items.
        var due = context.Batches.AsNoTracking().Include(batch => batch.Product)
            .Where(batch => batch.TrackingStatus == "active" && batch.NextTriggerDate.HasValue && batch.NextTriggerDate <= targetDate &&
                batch.Product.ExpiryManagementStatus == ExpiryManagementStatus.Managed && batch.Product.PolicyVersion == ExpiryPolicies.Version1 &&
                (batch.Product.PolicyCode == ExpiryPolicies.Food || batch.Product.PolicyCode == ExpiryPolicies.Pet || batch.Product.PolicyCode == ExpiryPolicies.GeneralLong) &&
                context.ScopeBaselines.Any(baseline => baseline.IsCompleted && baseline.ScopeKey == batch.Product.CategoryCode &&
                    baseline.PolicyCode == batch.Product.PolicyCode && baseline.PolicyVersion == batch.Product.PolicyVersion)).ToArray();
        foreach (var batch in due)
        {
            var shelfLifeDays = batch.ShelfLifeUnit switch { "D" => batch.ShelfLifeValue, "M" => checked(batch.ShelfLifeValue * 30), "Y" => checked(batch.ShelfLifeValue * 365), _ => throw new ArgumentException("Invalid shelf life unit.") };
            var stage = ExpiryPolicyCalculator.Calculate(batch.Product.PolicyCode!, batch.Product.PolicyVersion!.Value, targetDate, batch.ExpiryDate, shelfLifeDays)
                ?? throw new InvalidOperationException($"Product {batch.ProductId} has an uncovered expiry policy.");
            if (ExpiryStageCalculator.GetStagePriority(stage.CurrentStage) == 0) continue;
            if (batches.TryGetValue((batch.ProductId, batch.Id), out var previous) && ExpiryStageCalculator.CompareStages(stage.CurrentStage, previous.Stage) < 0)
                throw new InvalidOperationException("Projected stage cannot downgrade an open task item.");
            batches[(batch.ProductId, batch.Id)] = (stage.CurrentStage, batch.ExpiryDate);
            var product = batch.Product;
            products.TryAdd(product.Id, new(-product.Id, product.Id, product.CurrentName, product.ProductCode, product.CurrentBarcode,
                stage.CurrentStage, 0, product.EffectiveStockQty, batch.ExpiryDate, false, ProductCategoryScopes.DisplayNameForCategoryCode(product.CategoryCode)));
        }
        var byProduct = batches.GroupBy(pair => pair.Key.ProductId).ToDictionary(group => group.Key, group => group.Select(pair => pair.Value).ToArray());
        var rows = products.Values.Select(item =>
        {
            var pending = byProduct.GetValueOrDefault(item.ProductId) ?? [];
            return item with
            {
                TaskId = -item.ProductId, // Projection identity is not a formal TaskId and cannot enter existing inspection commands.
                HighestStage = pending.Length == 0 ? item.HighestStage : pending.OrderByDescending(batch => ExpiryStageCalculator.GetStagePriority(batch.Stage)).First().Stage,
                PendingBatchCount = pending.Length, NearestExpiryDate = pending.Length == 0 ? item.NearestExpiryDate : pending.Min(batch => batch.ExpiryDate),
                HasValidDraft = false, PlannedInspectionDate = targetDate
            };
        });
        if (!string.IsNullOrWhiteSpace(request.CategoryName)) rows = rows.Where(item => item.CategoryName == request.CategoryName.Trim());
        if (!string.IsNullOrWhiteSpace(request.Stage)) rows = rows.Where(item => item.HighestStage == request.Stage);
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.Trim();
            rows = rows.Where(item => item.ProductCode.Contains(search) || item.ProductName?.Contains(search) == true || item.ProductBarcode?.Contains(search) == true);
        }
        var ordered = rows.OrderByDescending(item => ExpiryStageCalculator.GetStagePriority(item.HighestStage)).ThenBy(item => item.NearestExpiryDate).ThenBy(item => item.ProductId).ToArray();
        var offset = checked((long)(request.Page - 1) * request.PageSize);
        return new(offset > int.MaxValue ? [] : ordered.Skip((int)offset).Take(request.PageSize).ToArray(), ordered.Length, request.Page, request.PageSize);
    }
}
