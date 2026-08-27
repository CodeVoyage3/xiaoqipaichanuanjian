using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Domain;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ApplicationStartupCoordinatorTests
{
    [Fact]
    public void StartupRecalculatesDueBatchesAndUpdatesLastNormalRunDateAtomically()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-START",
                CurrentName = "启动商品",
                CurrentBarcode = "B-START",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "excel"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ExpiryDate = new DateOnly(2026, 9, 20),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                CurrentStage = ExpiryStageCalculator.None,
                NextTriggerDate = new DateOnly(2026, 8, 27)
            });
            seed.AppStates.Single().LastNormalRunDate = new DateOnly(2026, 8, 26);
            seed.SaveChanges();
        }

        var occurredAtUtc = new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);
        using (var context = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(
                context,
                new DateOnly(2026, 8, 27),
                occurredAtUtc);
            Assert.True(result.Succeeded);
            Assert.False(result.ClockRollback);
            Assert.Equal(1, result.Recalculation.MatchedBatchCount);
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Discount20, batch.CurrentStage);
        Assert.Equal(new DateOnly(2026, 9, 6), batch.NextTriggerDate);
        Assert.Equal(new DateOnly(2026, 8, 27), verify.AppStates.AsNoTracking().Single().LastNormalRunDate);
        Assert.Single(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void ClockRollbackSkipsRecalculationAndLeavesStateUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-ROLLBACK",
                CurrentName = "回拨商品",
                CurrentBarcode = "B-ROLLBACK",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "excel"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ExpiryDate = new DateOnly(2026, 9, 20),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                CurrentStage = ExpiryStageCalculator.None,
                NextTriggerDate = new DateOnly(2026, 8, 27)
            });
            seed.AppStates.Single().LastNormalRunDate = new DateOnly(2026, 8, 28);
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(
                context,
                new DateOnly(2026, 8, 27),
                new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc));
            Assert.True(result.Succeeded);
            Assert.True(result.ClockRollback);
            Assert.Equal(0, result.Recalculation.MatchedBatchCount);
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Equal(new DateOnly(2026, 8, 27), batch.NextTriggerDate);
        Assert.Equal(new DateOnly(2026, 8, 28), verify.AppStates.AsNoTracking().Single().LastNormalRunDate);
        Assert.Empty(verify.Tasks.AsNoTracking());
    }
}
