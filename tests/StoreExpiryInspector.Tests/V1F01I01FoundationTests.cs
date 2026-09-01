using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F01I01FoundationTests
{
    [Theory]
    [InlineData(ExpiryPolicies.Food, 270, 30, "discount_50")]
    [InlineData(ExpiryPolicies.Food, 271, 90, "discount_50")]
    [InlineData(ExpiryPolicies.Pet, 30, 90, "discount_50")]
    [InlineData(ExpiryPolicies.GeneralLong, 181, 180, "discount_50")]
    public void PoliciesUseTheApprovedThresholds(string code, int shelfLifeDays, int remainingDays, string stage)
    {
        var expiry = new DateOnly(2026, 12, 31);
        var result = ExpiryPolicyCalculator.Calculate(code, 1, expiry.AddDays(-remainingDays), expiry, shelfLifeDays);
        Assert.Equal(stage, result!.CurrentStage);
        Assert.Equal(ExpiryStageCalculator.Expired, ExpiryPolicyCalculator.Calculate(code, 1, expiry, expiry, shelfLifeDays)!.CurrentStage);
    }

    [Theory]
    [InlineData(ExpiryPolicies.Food, 270, 31, "none")]
    [InlineData(ExpiryPolicies.Food, 270, 30, "discount_50")]
    [InlineData(ExpiryPolicies.Food, 270, 14, "discount_20")]
    [InlineData(ExpiryPolicies.Food, 270, 7, "withdraw")]
    [InlineData(ExpiryPolicies.Food, 271, 91, "none")]
    [InlineData(ExpiryPolicies.Food, 271, 90, "discount_50")]
    [InlineData(ExpiryPolicies.Food, 271, 60, "discount_20")]
    [InlineData(ExpiryPolicies.Food, 271, 14, "withdraw")]
    [InlineData(ExpiryPolicies.GeneralLong, 181, 181, "none")]
    [InlineData(ExpiryPolicies.GeneralLong, 181, 180, "discount_50")]
    [InlineData(ExpiryPolicies.GeneralLong, 181, 90, "discount_20")]
    [InlineData(ExpiryPolicies.GeneralLong, 181, 14, "withdraw")]
    public void PoliciesCoverEveryThresholdBoundary(string code, int shelfLifeDays, int remainingDays, string expectedStage)
    {
        var expiry = new DateOnly(2026, 12, 31);
        Assert.Equal(expectedStage, ExpiryPolicyCalculator.Calculate(code, 1, expiry.AddDays(-remainingDays), expiry, shelfLifeDays)!.CurrentStage);
        Assert.Equal(ExpiryStageCalculator.Expired, ExpiryPolicyCalculator.Calculate(code, 1, expiry, expiry, shelfLifeDays)!.CurrentStage);
    }

    [Fact]
    public void GeneralShortPolicyIsUncoveredAndUnknownInputsAreRejected()
    {
        var date = new DateOnly(2026, 12, 31);
        Assert.Null(ExpiryPolicyCalculator.Calculate(ExpiryPolicies.GeneralLong, 1, date.AddDays(-30), date, 180));
        Assert.Throws<ArgumentException>(() => ExpiryPolicyCalculator.Calculate("unknown", 1, date, date, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpiryPolicyCalculator.Calculate(ExpiryPolicies.Food, 2, date, date, 1));
    }

    [Fact]
    public void ProductAndBaselineConstraintsArePersistedBySqlite()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = new ImportRecord { SourceFileName = "source.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, Status = "confirmed" };
        context.Imports.Add(import);
        context.SaveChanges();
        var product = new Product { ProductCode = "SKU-I01" };
        context.Products.Add(product);
        context.SaveChanges();
        var batch = new Batch { ProductId = product.Id, ExpiryDate = new DateOnly(2026, 12, 31), ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 1, MaxArrivalQty = 1 };
        context.Batches.Add(batch);
        context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id };
        context.Tasks.Add(task);
        context.SaveChanges();

        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw("INSERT INTO products (product_code, policy_code, policy_version, expiry_management_status, excel_stock_qty, effective_stock_qty) VALUES ('invalid', NULL, NULL, 'managed', 0, 0)"));
        var baseline = new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = new DateOnly(2026, 9, 1) };
        context.ScopeBaselines.Add(baseline);
        context.SaveChanges();
        context.BatchBaselines.Add(new BatchBaseline { BaselineId = baseline.Id, BatchId = batch.Id, StageAtBaseline = ExpiryStageCalculator.Expired, ColdStartDisposition = ColdStartDispositions.ExpiredCatchupTask, CatchupWindowDays = 3, SourceTaskId = task.Id, CatchupSource = "historical_window" });
        context.SaveChanges();

        Assert.Throws<SqliteException>(() => context.Database.ExecuteSql($"INSERT INTO scope_baselines (scope_key, policy_code, policy_version, created_import_id, business_date, created_at_utc, is_completed) VALUES ('food', 'food_expiry', 1, {import.Id}, '2026-09-01', '2026-09-01T00:00:00Z', 0)"));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSql($"INSERT INTO scope_baselines (scope_key, policy_code, policy_version, created_import_id, business_date, created_at_utc, is_completed) VALUES ('food', 'not_v1', 1, {import.Id}, '2026-09-01', '2026-09-01T00:00:00Z', 0)"));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSql($"INSERT INTO batch_baselines (baseline_id, batch_id, stage_at_baseline, cold_start_disposition, catchup_window_days) VALUES ({baseline.Id}, {batch.Id}, 'expired', 'expired_catchup_task', 2)"));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSql($"INSERT INTO batch_baselines (baseline_id, batch_id, stage_at_baseline, cold_start_disposition) VALUES ({baseline.Id}, {batch.Id}, 'withdraw', 'withdraw_task')"));
        Assert.Equal(1, context.ScopeBaselines.Count());
        Assert.Equal(1, context.BatchBaselines.Count());
    }

    [Fact]
    public void UpgradeFromTheEighthMigrationNormalizesProductsWithoutCreatingFacts()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using (var context = database.Open())
        {
            var prior = context.Database.GetMigrations().Single(value => value.EndsWith("_AddLifecycleEvents", StringComparison.Ordinal));
            context.Database.Migrate(prior);
            context.Database.ExecuteSqlRaw("INSERT INTO products (product_code, policy_code, excel_stock_qty, effective_stock_qty, lifecycle_generation) VALUES ('SKU-OLD', 'food_v1', 0, 0, 0)");
        }
        using (var context = database.Open())
        {
            context.Database.Migrate();
            var product = context.Products.Single();
            Assert.Equal(ExpiryPolicies.Food, product.PolicyCode);
            Assert.Equal(1, product.PolicyVersion);
            Assert.Equal(ExpiryManagementStatus.Managed, product.ExpiryManagementStatus);
            Assert.Empty(context.Tasks);
            Assert.Empty(context.Inspections);
            Assert.Empty(context.ScopeBaselines);
            Assert.Empty(context.BatchBaselines);
            Assert.Equal(9, context.Database.GetAppliedMigrations().Count());
        }
    }
}
