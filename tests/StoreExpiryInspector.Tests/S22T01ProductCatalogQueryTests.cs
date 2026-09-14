using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S22T01ProductCatalogQueryTests
{
    [Fact]
    public void UsesProductEffectiveStockAndOnlyActiveBatchRisk()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var importTime = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var import = new ImportRecord { SourceFileName = "s22.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = importTime, Status = ImportStatuses.Succeeded, ConfirmedAtUtc = importTime };
        context.Add(import); context.SaveChanges();
        var product = new Product { ProductCode = "S22", CurrentName = "商品", CategoryCode = "food", EffectiveStockQty = 9, ExcelStockQty = 12, LastSeenImportId = import.Id };
        context.Add(product); context.SaveChanges();
        context.Batches.AddRange(new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 9, 20), TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount50, CurrentArrivalQty = 99 }, new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 9, 1), TrackingStatus = "closed", CurrentStage = ExpiryStageCalculator.Expired, CurrentArrivalQty = 99 });
        context.SaveChanges();
        var pending = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Discount50 };
        context.Add(pending); context.SaveChanges();
        context.Add(new ProductTaskItem { TaskId = pending.Id, ProductId = product.Id, BatchId = context.Batches.First().Id, Stage = ExpiryStageCalculator.Discount50 }); context.SaveChanges();
        var item = Assert.Single(new ProductCatalogQuery().Search(context, new()).Items);
        Assert.Equal(9, item.EffectiveStockQty); Assert.Equal(ExpiryStageCalculator.Discount50, item.HighestStage); Assert.Equal(new DateOnly(2026, 9, 20), item.NearestExpiry);
        Assert.Equal("2026-09-20", item.NearestExpiryText); Assert.Equal("5折", new ProductCatalogViewModel(_ => new([], 0, 1, 50), _ => null).Stages.Single(option => option.Value == ExpiryStageCalculator.Discount50).Label);
        Assert.Equal("2026-09-14", item.LastImportText); Assert.Empty(new ProductCatalogQuery().Search(context, new(Stage: ExpiryStageCalculator.Expired)).Items);
        context.Batches.Add(new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 9, 18), TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount20 }); context.SaveChanges();
        Assert.Empty(new ProductCatalogQuery().Search(context, new(Stage: ExpiryStageCalculator.Discount50)).Items);
        Assert.Equal(ExpiryStageCalculator.Discount20, Assert.Single(new ProductCatalogQuery().Search(context, new(Stage: ExpiryStageCalculator.Discount20)).Items).HighestStage);
        var detail = new ProductCatalogQuery().GetDetail(context, product.Id)!;
        Assert.Equal("12", detail.ExcelStockText); Assert.True(detail.Batches.Single(batch => batch.IsPending).IsPending); Assert.Contains(detail.Batches, batch => !batch.IsPending && batch.TaskStatus == "—");
    }
}
