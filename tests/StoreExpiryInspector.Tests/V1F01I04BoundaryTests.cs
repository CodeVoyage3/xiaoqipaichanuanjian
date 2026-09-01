using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F01I04BoundaryTests
{
    [Fact]
    public void ColdStartDiscountBaselineDoesNotReplayUntilHigherStage()
    {
        using var database = SqliteTestDatabase.Create();
        var day = new DateOnly(2026, 9, 1);
        var utc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        long batchId;
        using (var context = database.Open())
        {
            var import = new ImportRecord { SourceFileName = "i04.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = utc, ConfirmedAtUtc = utc, Status = ImportStatuses.Succeeded };
            context.Imports.Add(import); context.SaveChanges();
            var product = new Product { ProductCode = "I04-BOUNDARY", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = 5, LastSeenImportId = import.Id };
            context.Products.Add(product); context.SaveChanges();
            var batch = new Batch { ProductId = product.Id, ProductionDate = day.AddDays(-280), ExpiryDate = day.AddDays(80), ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 5, MaxArrivalQty = 5, LastSeenImportId = import.Id, NextTriggerDate = day };
            context.Batches.Add(batch); context.SaveChanges(); batchId = batch.Id;
            Assert.True(new ColdStartScopeBaselineUseCase().Execute(context, new("food", ExpiryPolicies.Food, 1, import.Id, day, utc)).Started);
            Assert.Empty(context.Tasks);
            var baseline = context.ScopeBaselines.Single();
            var completed = baseline.CompletedAtUtc;
            var snapshot = context.BatchBaselines.Select(item => new { item.BatchId, item.ColdStartDisposition, item.StageAtBaseline }).ToArray();
            new StartupRecalculationUseCase().Execute(context, day, utc.AddMinutes(1));
            Assert.Empty(context.Tasks);
            Assert.Equal(completed, context.ScopeBaselines.Single().CompletedAtUtc);
            Assert.Equal(snapshot, context.BatchBaselines.Select(item => new { item.BatchId, item.ColdStartDisposition, item.StageAtBaseline }).ToArray());
            new StartupRecalculationUseCase().Execute(context, day.AddDays(30), utc.AddDays(30));
            Assert.Equal(ExpiryStageCalculator.Discount20, context.Batches.Single(item => item.Id == batchId).CurrentStage);
            Assert.Single(context.Tasks);
            Assert.Equal(completed, context.ScopeBaselines.Single().CompletedAtUtc);
        }
    }
}
