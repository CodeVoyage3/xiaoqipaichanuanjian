using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InventoryAdjustmentDatabaseTests
{
    [Fact]
    public void InventoryAdjustmentSchemaHasApprovedColumnsChecksForeignKeyAndStableIndex()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddInventoryAdjustments", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "id", "product_id", "excel_stock_qty_snapshot", "adjusted_stock_qty", "adjusted_at_utc" },
            SqliteTestDatabase.ReadTableColumns(context, "inventory_adjustments"));

        var tableSql = SqliteTestDatabase.ReadTableSql(context, "inventory_adjustments");
        Assert.Contains("excel_stock_qty_snapshot >= 0", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adjusted_stock_qty >= 0", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            new[] { "NO ACTION" },
            SqliteTestDatabase.ReadForeignKeyDeleteActions(context, "inventory_adjustments"));

        var indexes = SqliteTestDatabase.ReadSchemaNames(context, "index");
        const string indexName = "IX_inventory_adjustments_product_id_adjusted_at_utc_id";
        Assert.Contains(indexName, indexes);
        Assert.Contains(
            "\"product_id\", \"adjusted_at_utc\", \"id\"",
            SqliteTestDatabase.ReadIndexSql(context, indexName),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventoryAdjustmentsAcceptZeroAndPositiveQuantitiesRejectNegativesAndProtectProductHistory()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-INVENTORY-ADJUSTMENT");
        var adjustedAtUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        context.InventoryAdjustments.AddRange(
            new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = 0,
                AdjustedStockQty = 0,
                AdjustedAtUtc = adjustedAtUtc
            },
            new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = 12,
                AdjustedStockQty = 7,
                AdjustedAtUtc = adjustedAtUtc
            });
        context.SaveChanges();

        var adjustments = context.InventoryAdjustments.AsNoTracking()
            .OrderBy(adjustment => adjustment.AdjustedAtUtc)
            .ThenBy(adjustment => adjustment.Id)
            .ToArray();
        Assert.Equal(new[] { 0, 12 }, adjustments.Select(adjustment => adjustment.ExcelStockQtySnapshot));
        Assert.Equal(new[] { 0, 7 }, adjustments.Select(adjustment => adjustment.AdjustedStockQty));

        AssertAdjustmentRejected(context, product.Id, -1, 0);
        AssertAdjustmentRejected(context, product.Id, 0, -1);
        AssertAdjustmentRejected(context, 999, 1, 1);

        context.ChangeTracker.Clear();
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated(
            $"DELETE FROM products WHERE id = {product.Id}"));
        Assert.Equal(2, context.InventoryAdjustments.Count());
    }

    [Fact]
    public void UpgradeFromAddInspectionHistoryPreservesAllNineExistingDataSets()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string addInspectionHistory;

        using (var context = database.Open())
        {
            addInspectionHistory = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddInspectionHistory", StringComparison.Ordinal));
            context.Database.Migrate(addInspectionHistory);

            var product = AddProduct(context, "SKU-INVENTORY-UPGRADE");
            var batch = AddBatch(context, product.Id);
            var task = AddTask(context, product.Id);
            var taskItem = new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = "discount_50"
            };
            context.TaskItems.Add(taskItem);
            context.SaveChanges();

            var draft = new InspectionDraft { TaskId = task.Id };
            context.Drafts.Add(draft);
            context.SaveChanges();
            context.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = taskItem.Id,
                TaskId = task.Id,
                CheckedQty = 0
            });
            context.SaveChanges();

            var inspection = new Inspection
            {
                TaskId = task.Id,
                ProductId = product.Id,
                ProductCodeSnapshot = product.ProductCode,
                StageSnapshot = "discount_50",
                StockQtySnapshot = 0,
                InspectorName = "Inspector",
                CheckDate = new DateOnly(2026, 8, 26)
            };
            context.Inspections.Add(inspection);
            context.SaveChanges();
            var inspectionItem = new InspectionItem
            {
                InspectionId = inspection.Id,
                ProductId = product.Id,
                BatchId = batch.Id,
                ProductionDateSnapshot = new DateOnly(2026, 1, 1),
                ExpiryDateSnapshot = new DateOnly(2026, 12, 31),
                StageSnapshot = "discount_50",
                ArrivalQtySnapshot = 10,
                CheckedQty = 0
            };
            context.InspectionItems.Add(inspectionItem);
            context.SaveChanges();
            context.InspectionItemRevisions.Add(new InspectionItemRevision
            {
                InspectionItemId = inspectionItem.Id,
                PreviousCheckedQty = 0,
                NewCheckedQty = 1
            });
            context.SaveChanges();
        }

        using (var context = database.Open())
        {
            context.Database.Migrate();

            Assert.Equal(1, context.Products.Count());
            Assert.Equal(1, context.Batches.Count());
            Assert.Equal(1, context.Tasks.Count());
            Assert.Equal(1, context.TaskItems.Count());
            Assert.Equal(1, context.Drafts.Count());
            Assert.Equal(1, context.DraftItems.Count());
            Assert.Equal(1, context.Inspections.Count());
            Assert.Equal(1, context.InspectionItems.Count());
            Assert.Equal(1, context.InspectionItemRevisions.Count());

            var product = context.Products.AsNoTracking().Single();
            var batch = context.Batches.AsNoTracking().Single();
            var task = context.Tasks.AsNoTracking().Single();
            var inspection = context.Inspections.AsNoTracking().Single();
            var item = context.InspectionItems.AsNoTracking().Single();
            var revision = context.InspectionItemRevisions.AsNoTracking().Single();
            Assert.Equal("SKU-INVENTORY-UPGRADE", product.ProductCode);
            Assert.Equal(product.Id, batch.ProductId);
            Assert.Equal(product.Id, task.ProductId);
            Assert.Equal(product.Id, inspection.ProductId);
            Assert.Equal(inspection.Id, item.InspectionId);
            Assert.Equal(item.Id, revision.InspectionItemId);
            Assert.Equal(1, revision.NewCheckedQty);
            Assert.Contains(
                context.Database.GetAppliedMigrations(),
                migration => migration.EndsWith("_AddInventoryAdjustments", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IncrementalMigrationScriptOnlyCreatesInventoryAdjustmentTableAndIndex()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using var context = database.Open();
        var migrations = context.Database.GetMigrations().ToArray();
        var fromMigration = migrations.Single(migration => migration.EndsWith("_AddInspectionHistory", StringComparison.Ordinal));
        var toMigration = migrations.Single(migration => migration.EndsWith("_AddInventoryAdjustments", StringComparison.Ordinal));
        var script = context.Database.GetService<IMigrator>().GenerateScript(fromMigration, toMigration);

        Assert.Single(Regex.Matches(script, @"CREATE\s+TABLE", RegexOptions.IgnoreCase));
        Assert.Single(Regex.Matches(script, @"CREATE\s+(?:UNIQUE\s+)?INDEX", RegexOptions.IgnoreCase));
        Assert.Contains("inventory_adjustments", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_inventory_adjustments_product_id_adjusted_at_utc_id", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
    }

    private static Product AddProduct(StoreDbContext context, string code)
    {
        var product = new Product { ProductCode = code };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(StoreDbContext context, long productId)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2026, 12, 31),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static ProductTask AddTask(StoreDbContext context, long productId)
    {
        var task = new ProductTask { ProductId = productId };
        context.Tasks.Add(task);
        context.SaveChanges();
        return task;
    }

    private static void AssertAdjustmentRejected(
        StoreDbContext context,
        long productId,
        int excelStockQtySnapshot,
        int adjustedStockQty)
    {
        context.InventoryAdjustments.Add(new InventoryAdjustment
        {
            ProductId = productId,
            ExcelStockQtySnapshot = excelStockQtySnapshot,
            AdjustedStockQty = adjustedStockQty
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }
}
