using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record ProductCatalogRequest(string? SearchText = null, string? CategoryName = null, string? Stage = null, string? TaskStatus = null, int Page = 1, int PageSize = 50);
public sealed record ProductCatalogItem(long ProductId, string? Name, string Code, string? Barcode, string Category, int BatchCount, int EffectiveStockQty, DateOnly? NearestExpiry, string HighestStage, int PendingCount, DateTime? LastImportAtUtc, long? OpenTaskId)
{ public string NearestExpiryText => NearestExpiry?.ToString("yyyy-MM-dd") ?? "—"; public string LastImportText => LastImportAtUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—"; }
public sealed record ProductCatalogPage(IReadOnlyList<ProductCatalogItem> Items, int TotalCount, int Page, int PageSize);
public sealed record ProductCatalogBatch(long BatchId, DateOnly? ProductionDate, DateOnly ExpiryDate, int CurrentArrivalQty, string Stage, bool IsPending)
{ public string TaskStatus => IsPending ? "待排查" : "—"; public string ProductionDateText => ProductionDate?.ToString("yyyy-MM-dd") ?? "—"; public string ExpiryDateText => ExpiryDate.ToString("yyyy-MM-dd"); }
public sealed record ProductCatalogDetail(ProductCatalogItem Product, IReadOnlyList<ProductCatalogBatch> Batches, int? ExcelStockQty, DateTime? LastInspectionAtUtc)
{ public string ExcelStockText => ExcelStockQty?.ToString() ?? "—"; public string LastInspectionText => LastInspectionAtUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—"; }

public sealed class ProductCatalogQuery
{
    public ProductCatalogPage Search(StoreDbContext context, ProductCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(request);
        if (request.Page <= 0 || request.PageSize <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var search = string.IsNullOrWhiteSpace(request.SearchText) ? null : request.SearchText.Trim();
        var query = context.Products.AsNoTracking().AsQueryable();
        if (search is not null) query = query.Where(product => (product.CurrentName != null && product.CurrentName.Contains(search)) || product.ProductCode.Contains(search) || (product.CurrentBarcode != null && product.CurrentBarcode.Contains(search)));
        if (!string.IsNullOrWhiteSpace(request.CategoryName)) query = query.Where(product => product.CategoryCode == CategoryCodeForDisplayName(request.CategoryName));
        if (!string.IsNullOrWhiteSpace(request.Stage)) query = request.Stage switch
        {
            ExpiryStageCalculator.None => query.Where(product => !product.Batches.Any(batch => batch.TrackingStatus == "active" && batch.CurrentStage != ExpiryStageCalculator.None)),
            ExpiryStageCalculator.Discount50 => query.Where(product => product.Batches.Any(batch => batch.TrackingStatus == "active" && batch.CurrentStage == ExpiryStageCalculator.Discount50) && !product.Batches.Any(batch => batch.TrackingStatus == "active" && (batch.CurrentStage == ExpiryStageCalculator.Discount20 || batch.CurrentStage == ExpiryStageCalculator.Withdraw || batch.CurrentStage == ExpiryStageCalculator.Expired))),
            ExpiryStageCalculator.Discount20 => query.Where(product => product.Batches.Any(batch => batch.TrackingStatus == "active" && batch.CurrentStage == ExpiryStageCalculator.Discount20) && !product.Batches.Any(batch => batch.TrackingStatus == "active" && (batch.CurrentStage == ExpiryStageCalculator.Withdraw || batch.CurrentStage == ExpiryStageCalculator.Expired))),
            ExpiryStageCalculator.Withdraw => query.Where(product => product.Batches.Any(batch => batch.TrackingStatus == "active" && batch.CurrentStage == ExpiryStageCalculator.Withdraw) && !product.Batches.Any(batch => batch.TrackingStatus == "active" && batch.CurrentStage == ExpiryStageCalculator.Expired)),
            ExpiryStageCalculator.Expired => query.Where(product => product.Batches.Any(batch => batch.TrackingStatus == "active" && batch.CurrentStage == ExpiryStageCalculator.Expired)),
            _ => throw new ArgumentException("Unknown stage.", nameof(request))
        };
        if (request.TaskStatus == "open") query = query.Where(product => product.Tasks.Any(task => task.Status == "open"));
        if (request.TaskStatus == "none") query = query.Where(product => !product.Tasks.Any(task => task.Status == "open"));
        var total = query.Count();
        if (checked((long)(request.Page - 1) * request.PageSize) > int.MaxValue) return new([], total, request.Page, request.PageSize);
        var rows = query.Select(product => new
        {
            product.Id, product.CurrentName, product.ProductCode, product.CurrentBarcode, product.CategoryCode, product.EffectiveStockQty,
            BatchCount = product.Batches.Count(),
            NearestExpiry = product.Batches.Where(batch => batch.TrackingStatus == "active").Select(batch => (DateOnly?)batch.ExpiryDate).Min(),
            HighestPriority = product.Batches.Where(batch => batch.TrackingStatus == "active").Select(batch => batch.CurrentStage == ExpiryStageCalculator.Expired ? 4 : batch.CurrentStage == ExpiryStageCalculator.Withdraw ? 3 : batch.CurrentStage == ExpiryStageCalculator.Discount20 ? 2 : batch.CurrentStage == ExpiryStageCalculator.Discount50 ? 1 : 0).DefaultIfEmpty().Max(),
            PendingCount = product.Tasks.Where(task => task.Status == "open").SelectMany(task => task.Items).Count(),
            OpenTaskId = product.Tasks.Where(task => task.Status == "open").Select(task => (long?)task.Id).FirstOrDefault(),
            LastImport = product.LastSeenImportId == null ? null : context.Imports.Where(import => import.Id == product.LastSeenImportId && import.Status == ImportStatuses.Succeeded && !import.IsUndone).Select(import => import.ConfirmedAtUtc).FirstOrDefault()
        }).OrderByDescending(row => row.HighestPriority).ThenBy(row => row.NearestExpiry == null).ThenBy(row => row.NearestExpiry).ThenBy(row => row.Id).Skip(PageOffset(request)).Take(request.PageSize).ToArray()
            .Select(row => ToItem(row.Id, row.CurrentName, row.ProductCode, row.CurrentBarcode, row.CategoryCode, row.BatchCount, row.EffectiveStockQty, row.NearestExpiry, row.HighestPriority, row.PendingCount, row.LastImport, row.OpenTaskId)).ToArray();
        return new(rows, total, request.Page, request.PageSize);
    }

    public ProductCatalogDetail? GetDetail(StoreDbContext context, long productId)
    {
        var productRow = context.Products.AsNoTracking().Where(product => product.Id == productId).Select(product => new { product.Id, product.CurrentName, product.ProductCode, product.CurrentBarcode, product.CategoryCode, product.EffectiveStockQty, BatchCount = product.Batches.Count(), NearestExpiry = product.Batches.Where(batch => batch.TrackingStatus == "active").Select(batch => (DateOnly?)batch.ExpiryDate).Min(), HighestPriority = product.Batches.Where(batch => batch.TrackingStatus == "active").Select(batch => batch.CurrentStage == ExpiryStageCalculator.Expired ? 4 : batch.CurrentStage == ExpiryStageCalculator.Withdraw ? 3 : batch.CurrentStage == ExpiryStageCalculator.Discount20 ? 2 : batch.CurrentStage == ExpiryStageCalculator.Discount50 ? 1 : 0).DefaultIfEmpty().Max(), PendingCount = product.Tasks.Where(task => task.Status == "open").SelectMany(task => task.Items).Count(), OpenTaskId = product.Tasks.Where(task => task.Status == "open").Select(task => (long?)task.Id).FirstOrDefault(), LastImport = product.LastSeenImportId == null ? null : context.Imports.Where(import => import.Id == product.LastSeenImportId && import.Status == ImportStatuses.Succeeded && !import.IsUndone).Select(import => import.ConfirmedAtUtc).FirstOrDefault() }).SingleOrDefault();
        var product = productRow is null ? null : ToItem(productRow.Id, productRow.CurrentName, productRow.ProductCode, productRow.CurrentBarcode, productRow.CategoryCode, productRow.BatchCount, productRow.EffectiveStockQty, productRow.NearestExpiry, productRow.HighestPriority, productRow.PendingCount, productRow.LastImport, productRow.OpenTaskId);
        if (product is null) return null;
        var openTask = product.OpenTaskId;
        var batches = context.Batches.AsNoTracking().Where(batch => batch.ProductId == productId).Select(batch => new ProductCatalogBatch(batch.Id, batch.ProductionDate, batch.ExpiryDate, batch.CurrentArrivalQty, batch.CurrentStage, openTask != null && context.TaskItems.Any(item => item.TaskId == openTask && item.BatchId == batch.Id))).ToArray()
            .OrderByDescending(batch => ExpiryStageCalculator.GetStagePriority(batch.Stage)).ThenBy(batch => batch.ExpiryDate).ThenBy(batch => batch.BatchId).ToArray();
        var excel = context.Products.AsNoTracking().Where(product => product.Id == productId).Select(product => (int?)product.ExcelStockQty).Single();
        var lastInspection = context.Inspections.AsNoTracking().Where(inspection => inspection.ProductId == productId).Select(inspection => (DateTime?)inspection.SubmittedAtUtc).Max();
        return new(product, batches, excel, lastInspection);
    }

    private static ProductCatalogItem ToItem(long id, string? name, string code, string? barcode, string category, int batchCount, int stock, DateOnly? nearest, int priority, int pending, DateTime? imported, long? openTask) => new(id, name, code, barcode, ProductCategoryScopes.DisplayNameForCategoryCode(category), batchCount, stock, nearest, priority switch { 4 => ExpiryStageCalculator.Expired, 3 => ExpiryStageCalculator.Withdraw, 2 => ExpiryStageCalculator.Discount20, 1 => ExpiryStageCalculator.Discount50, _ => ExpiryStageCalculator.None }, pending, imported, openTask);
    private static int PageOffset(ProductCatalogRequest request) => (int)checked((long)(request.Page - 1) * request.PageSize);
    private static string CategoryCodeForDisplayName(string name) => name.Trim() switch { "食品" => "food", "宠物" => "pet", "日用" => "daily_use", "美妆" => "beauty", "家居" => "home", "香氛香水" => "fragrance", "文具" => "stationery", "潮流玩具" => "trendy_toys", "应季搭配" => "seasonal_assortment", "赠品小样" => "gift_sample", _ => throw new ArgumentException("Unknown category.", nameof(name)) };
}
