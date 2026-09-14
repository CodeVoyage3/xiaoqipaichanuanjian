using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S22T01ProductCatalogQueryTests
{
    [Fact]
    public void UsesProductEffectiveStockAndOnlyActiveBatchRisk()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = new ImportRecord { SourceFileName = "s22.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, Status = ImportStatuses.Succeeded, ConfirmedAtUtc = DateTime.UtcNow };
        context.Add(import); context.SaveChanges();
        var product = new Product { ProductCode = "S22", CurrentName = "商品", CategoryCode = "food", EffectiveStockQty = 9, ExcelStockQty = 12, LastSeenImportId = import.Id };
        context.Add(product); context.SaveChanges();
        context.Batches.AddRange(new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 9, 20), TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount50, CurrentArrivalQty = 99 }, new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 9, 1), TrackingStatus = "closed", CurrentStage = ExpiryStageCalculator.Expired, CurrentArrivalQty = 99 });
        context.SaveChanges();
        var item = Assert.Single(new ProductCatalogQuery().Search(context, new()).Items);
        Assert.Equal(9, item.EffectiveStockQty); Assert.Equal(ExpiryStageCalculator.Discount50, item.HighestStage); Assert.Equal(new DateOnly(2026, 9, 20), item.NearestExpiry);
    }
}
