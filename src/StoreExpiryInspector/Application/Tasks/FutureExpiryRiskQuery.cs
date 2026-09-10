using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record FutureExpiryRiskRequest(int Days, string Stage, int Page = 1, int PageSize = 50);
public sealed record FutureExpiryRiskCell(int Days, string Stage, int Count);
public sealed record FutureExpiryRiskOverview(DateOnly BusinessDate, IReadOnlyList<FutureExpiryRiskCell> Cells);
public sealed record FutureExpiryRiskItem(long ProductId, long RepresentativeBatchId, string? ProductName, string? ProductBarcode, string ProductCode, int EffectiveStockQty, string CurrentStage, DateOnly EffectiveDate, int DaysUntil)
{
    public string HighestStage => CurrentStage;
}
public sealed record FutureExpiryRiskPage(IReadOnlyList<FutureExpiryRiskItem> Items, int TotalCount, int Page, int PageSize);

/// <summary>Read-only future stage projection.  The database query is bounded by the existing expiry-date index.</summary>
public sealed class FutureExpiryRiskQuery
{
    private static readonly int[] Windows = [7, 14, 30];
    private static readonly string[] Stages = [ExpiryStageCalculator.Discount50, ExpiryStageCalculator.Discount20, ExpiryStageCalculator.Withdraw, ExpiryStageCalculator.Expired];

    public FutureExpiryRiskOverview Overview(StoreDbContext context, DateOnly businessDate) =>
        new(businessDate, Build(context, businessDate).Select(pair => new FutureExpiryRiskCell(pair.Key.Days, pair.Key.Stage, pair.Value.Count)).ToArray());

    public FutureExpiryRiskPage Search(StoreDbContext context, DateOnly businessDate, FutureExpiryRiskRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (!Windows.Contains(request.Days) || !Stages.Contains(request.Stage) || request.Page <= 0 || request.PageSize <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var rows = Build(context, businessDate)[(request.Days, request.Stage)].Rows;
        var total = rows.Count;
        return new(rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArray(), total, request.Page, request.PageSize);
    }

    private static Dictionary<(int Days, string Stage), Bucket> Build(StoreDbContext context, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(context);
        var maxExpiry = businessDate.AddDays(210);
        var candidates = context.Batches.AsNoTracking()
            .Where(batch => batch.TrackingStatus == "active" && batch.ExpiryDate > businessDate && batch.ExpiryDate <= maxExpiry &&
                batch.Product.EffectiveStockQty > 0 && batch.Product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                batch.Product.PolicyVersion == ExpiryPolicies.Version1 &&
                (batch.Product.PolicyCode == ExpiryPolicies.Food || batch.Product.PolicyCode == ExpiryPolicies.Pet || batch.Product.PolicyCode == ExpiryPolicies.GeneralLong) &&
                context.ScopeBaselines.Any(baseline => baseline.IsCompleted && baseline.ScopeKey == batch.Product.CategoryCode && baseline.PolicyCode == batch.Product.PolicyCode && baseline.PolicyVersion == batch.Product.PolicyVersion))
            .Select(batch => new Candidate(batch.Id, batch.ProductId, batch.Product.CurrentName, batch.Product.CurrentBarcode, batch.Product.ProductCode, batch.Product.EffectiveStockQty, batch.Product.PolicyCode!, batch.Product.PolicyVersion!.Value, batch.ExpiryDate, batch.ShelfLifeValue, batch.ShelfLifeUnit))
            .ToArray();
        var selected = new Dictionary<(int Days, string Stage), Dictionary<long, FutureExpiryRiskItem>>();
        foreach (var days in Windows) foreach (var stage in Stages) selected.Add((days, stage), []);
        foreach (var candidate in candidates)
        {
            var dates = ExpiryPolicyCalculator.CalculateStageDates(candidate.PolicyCode, candidate.PolicyVersion, candidate.ExpiryDate, ShelfLifeDays(candidate));
            if (dates is null) continue;
            foreach (var (stage, date) in new[] { (ExpiryStageCalculator.Discount50, dates.Discount50), (ExpiryStageCalculator.Discount20, dates.Discount20), (ExpiryStageCalculator.Withdraw, dates.Withdraw), (ExpiryStageCalculator.Expired, dates.Expired) })
            foreach (var days in Windows)
                if (date > businessDate && date <= businessDate.AddDays(days))
                {
                    var item = new FutureExpiryRiskItem(candidate.ProductId, candidate.BatchId, candidate.ProductName, candidate.ProductBarcode, candidate.ProductCode, candidate.EffectiveStockQty, CurrentStage(dates, businessDate), date, date.DayNumber - businessDate.DayNumber);
                    var group = selected[(days, stage)];
                    if (!group.TryGetValue(candidate.ProductId, out var current) || item.EffectiveDate < current.EffectiveDate || item.EffectiveDate == current.EffectiveDate && item.RepresentativeBatchId < current.RepresentativeBatchId) group[candidate.ProductId] = item;
                }
        }
        return selected.ToDictionary(pair => pair.Key, pair => new Bucket(pair.Value.Values.OrderBy(item => item.EffectiveDate).ThenBy(item => item.ProductCode, StringComparer.Ordinal).ThenBy(item => item.ProductId).ToArray()));
    }

    private static string CurrentStage(ExpiryPolicyStageDates dates, DateOnly day) => day < dates.Discount50 ? ExpiryStageCalculator.None : day < dates.Discount20 ? ExpiryStageCalculator.Discount50 : day < dates.Withdraw ? ExpiryStageCalculator.Discount20 : day < dates.Expired ? ExpiryStageCalculator.Withdraw : ExpiryStageCalculator.Expired;
    // Keep this aligned with the existing lifecycle input representation; stage thresholds stay in ExpiryPolicyCalculator.
    private static int ShelfLifeDays(Candidate batch) => batch.ShelfLifeUnit switch { "D" => batch.ShelfLifeValue, "M" => checked(batch.ShelfLifeValue * 30), "Y" => checked(batch.ShelfLifeValue * 365), _ => throw new ArgumentException("Invalid shelf life unit.") };
    private sealed record Candidate(long BatchId, long ProductId, string? ProductName, string? ProductBarcode, string ProductCode, int EffectiveStockQty, string PolicyCode, int PolicyVersion, DateOnly ExpiryDate, int ShelfLifeValue, string ShelfLifeUnit);
    private sealed record Bucket(IReadOnlyList<FutureExpiryRiskItem> Rows) { public int Count => Rows.Count; }
}
