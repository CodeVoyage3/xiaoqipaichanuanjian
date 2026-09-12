using StoreExpiryInspector.Application.Updates;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UpdateSafety;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V110InstallerE2EBusinessDataTests
{
    [Fact]
    public void SeedOrFingerprintIsLimitedToTheExplicitTemporaryInstallerDatabase()
    {
        var database = Environment.GetEnvironmentVariable("S9_T07_E2E_DATABASE");
        var evidence = Environment.GetEnvironmentVariable("S9_T07_E2E_BUSINESS_FINGERPRINT");
        var mode = Environment.GetEnvironmentVariable("S9_T07_E2E_BUSINESS_MODE");
        Assert.False(string.IsNullOrWhiteSpace(database));
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Assert.True(mode is "seed" or "fingerprint" or "validate");

        database = Path.GetFullPath(database!);
        var dataRoot = Path.GetDirectoryName(Path.GetDirectoryName(database))!;
        Assert.True(Guid.TryParse(Path.GetRelativePath(Path.GetTempPath(), dataRoot), out _));
        Assert.True(File.Exists(database));

        using var context = new StoreDbContext(new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite($"Data Source={database};Foreign Keys=True;Pooling=False")
            .Options);
        if (mode == "seed") Seed(context);
        var fingerprint = S8T03ImportPerformanceTests.BusinessFingerprint(database);
        if (mode == "validate")
        {
            var migrations = UpgradeHealthAck.VerifyDatabase(database, includeWal: true);
            Assert.True(migrations.SequenceEqual(CurrentSchemaIdentity.Migrations, StringComparer.Ordinal));
            File.WriteAllText(evidence!, System.Text.Json.JsonSerializer.Serialize(new { migrationCount = migrations.Count, integrity = "ok", foreignKeys = 0, businessFingerprint = fingerprint }));
            return;
        }

        File.WriteAllText(evidence!, fingerprint);
    }

    private static void Seed(StoreDbContext context)
    {
        Assert.Empty(context.Products);
        var now = new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc);
        var product = new Product { ProductCode = "V110-E2E-SKU", CurrentName = "覆盖升级真实商品", CurrentBarcode = "6900000001109", ExcelStockQty = 12, EffectiveStockQty = 12, EffectiveStockSource = "excel", LifecycleGeneration = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        context.Products.Add(product); context.SaveChanges();
        var batch = new Batch { ProductId = product.Id, ProductionDate = new DateOnly(2026, 8, 1), ExpiryDate = new DateOnly(2026, 10, 1), ShelfLifeValue = 60, ShelfLifeUnit = "D", CurrentArrivalQty = 12, MaxArrivalQty = 12, LifecycleGeneration = 1, TrackingStatus = "active", CurrentStage = "discount_50", NextTriggerDate = new DateOnly(2026, 9, 13), AttentionVersion = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        context.Batches.Add(batch); context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = "discount_50", CreatedAtUtc = now, UpdatedAtUtc = now };
        context.Tasks.Add(task); context.SaveChanges();
        context.Inspections.Add(new Inspection { TaskId = task.Id, ProductId = product.Id, ProductCodeSnapshot = product.ProductCode, ProductNameSnapshot = product.CurrentName, BarcodeSnapshot = product.CurrentBarcode, StageSnapshot = "discount_50", StockQtySnapshot = 12, InspectorName = "E2E", CheckDate = new DateOnly(2026, 9, 12), SubmittedAtUtc = now });
        context.BackupRecords.Add(new BackupRecord { BackupType = "auto", FilePath = "v110-e2e-backup.db", Sha256 = new string('a', 64), CreatedAtUtc = now, VerificationStatus = "verified" });
        context.Settings.Single().ReminderMinuteOfDay = 601;
        context.SaveChanges();
    }
}
