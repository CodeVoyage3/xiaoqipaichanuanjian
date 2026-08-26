using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InspectionHistoryDatabaseTests
{
    [Fact]
    public void InspectionHistorySchemaHasRequiredTablesIndexesChecksAndNoCascadeForeignKeys()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddInspectionHistory", StringComparison.Ordinal));

        var tables = SqliteTestDatabase.ReadSchemaNames(context, "table");
        Assert.Contains("inspections", tables);
        Assert.Contains("inspection_items", tables);
        Assert.Contains("inspection_item_revisions", tables);

        var indexes = SqliteTestDatabase.ReadSchemaNames(context, "index");
        Assert.Contains("IX_inspections_task_id", indexes);
        Assert.Contains("IX_inspection_items_inspection_id_batch_id", indexes);
        Assert.Contains("IX_inspection_item_revisions_inspection_item_id_changed_at_utc_id", indexes);
        Assert.Contains(
            "\"inspection_id\", \"batch_id\"",
            SqliteTestDatabase.ReadIndexSql(context, "IX_inspection_items_inspection_id_batch_id"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "\"inspection_item_id\", \"changed_at_utc\", \"id\"",
            SqliteTestDatabase.ReadIndexSql(context, "IX_inspection_item_revisions_inspection_item_id_changed_at_utc_id"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains("product_code_snapshot", ReadTableSql(context, "inspections"));
        Assert.Contains("inspector_name", ReadTableSql(context, "inspections"));
        Assert.Contains("stage_snapshot", ReadTableSql(context, "inspection_items"));
        Assert.Contains("checked_qty", ReadTableSql(context, "inspection_items"));
        Assert.Contains("previous_checked_qty", ReadTableSql(context, "inspection_item_revisions"));
        Assert.Contains("new_checked_qty", ReadTableSql(context, "inspection_item_revisions"));

        Assert.All(
            new[] { "inspections", "inspection_items", "inspection_item_revisions" },
            table => Assert.All(ReadForeignKeyDeleteActions(context, table), action => Assert.Equal("NO ACTION", action)));
    }

    [Fact]
    public void ValidSnapshotsAndQuantitiesAreStoredButNullAndNegativeValuesAreRejected()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-SNAPSHOT");
        var firstBatch = AddBatch(context, product.Id, new DateOnly(2026, 12, 31));
        var secondBatch = AddBatch(context, product.Id, new DateOnly(2026, 12, 30));
        var nullBatch = AddBatch(context, product.Id, new DateOnly(2026, 12, 29));
        var task = AddTask(context, product.Id);
        var inspection = NewInspection(task.Id, product.Id, "  SKU-SNAPSHOT  ", "  Inspector  ", 0);
        context.Inspections.Add(inspection);
        context.SaveChanges();

        context.InspectionItems.AddRange(
            NewInspectionItem(inspection.Id, product.Id, firstBatch.Id, 0),
            NewInspectionItem(inspection.Id, product.Id, secondBatch.Id, 5));
        context.SaveChanges();

        var savedInspection = context.Inspections.AsNoTracking().Single();
        Assert.Equal("SKU-SNAPSHOT", savedInspection.ProductCodeSnapshot);
        Assert.Equal("Inspector", savedInspection.InspectorName);
        Assert.Equal(0, savedInspection.StockQtySnapshot);
        Assert.Equal(new[] { 0, 5 }, context.InspectionItems.AsNoTracking().OrderBy(item => item.Id).Select(item => item.CheckedQty));

        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw(
            "INSERT INTO inspection_items (inspection_id, product_id, batch_id, stage_snapshot, arrival_qty_snapshot, checked_qty) VALUES ({0}, {1}, {2}, 'discount_50', 0, NULL)",
            inspection.Id,
            product.Id,
            nullBatch.Id));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw(
            "INSERT INTO inspection_items (inspection_id, product_id, batch_id, production_date_snapshot, expiry_date_snapshot, stage_snapshot, arrival_qty_snapshot, checked_qty) VALUES ({0}, {1}, {2}, NULL, NULL, 'discount_50', 0, 0)",
            inspection.Id,
            product.Id,
            nullBatch.Id));

        AssertItemRejected(context, NewInspectionItem(inspection.Id, product.Id, nullBatch.Id, -1));
        AssertItemRejected(context, new InspectionItem
        {
            InspectionId = inspection.Id,
            ProductId = product.Id,
            BatchId = nullBatch.Id,
            ArrivalQtySnapshot = -1,
            CheckedQty = 0
        });

        var negativeStock = NewInspection(task.Id, product.Id, "SKU-NEGATIVE-STOCK", "Inspector", -1);
        AssertInspectionRejected(context, negativeStock);

        var blankCode = NewInspection(task.Id, product.Id, " ", "Inspector", 0);
        AssertInspectionRejected(context, blankCode);
        var blankInspector = NewInspection(task.Id, product.Id, "SKU-BLANK-INSPECTOR", " ", 0);
        AssertInspectionRejected(context, blankInspector);

        var invalidStage = NewInspection(task.Id, product.Id, "SKU-INVALID-STAGE", "Inspector", 0);
        invalidStage.StageSnapshot = "unknown";
        AssertInspectionRejected(context, invalidStage);
        var invalidItemStage = NewInspectionItem(inspection.Id, product.Id, nullBatch.Id, 0);
        invalidItemStage.StageSnapshot = "unknown";
        AssertItemRejected(context, invalidItemStage);
    }

    [Fact]
    public void TaskAndBatchUniquenessAndCompositeProductRelationsAreEnforced()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var firstProduct = AddProduct(context, "SKU-RELATION-ONE");
        var secondProduct = AddProduct(context, "SKU-RELATION-TWO");
        var firstBatch = AddBatch(context, firstProduct.Id, new DateOnly(2026, 12, 31));
        var secondBatch = AddBatch(context, secondProduct.Id, new DateOnly(2026, 12, 30));
        var firstTask = AddTask(context, firstProduct.Id);
        var secondTask = AddTask(context, secondProduct.Id);

        var inspection = NewInspection(firstTask.Id, firstProduct.Id, "SKU-RELATION-ONE", "Inspector", 1);
        context.Inspections.Add(inspection);
        context.SaveChanges();

        AssertInspectionRejected(
            context,
            NewInspection(firstTask.Id, firstProduct.Id, "SKU-DUPLICATE-TASK", "Inspector", 1));
        AssertInspectionRejected(
            context,
            NewInspection(secondTask.Id, firstProduct.Id, "SKU-CROSS-TASK", "Inspector", 1));
        AssertInspectionRejected(
            context,
            NewInspection(firstTask.Id, secondProduct.Id, "SKU-CROSS-PRODUCT", "Inspector", 1));

        context.InspectionItems.Add(NewInspectionItem(inspection.Id, firstProduct.Id, firstBatch.Id, 1));
        context.SaveChanges();
        AssertItemRejected(context, NewInspectionItem(inspection.Id, firstProduct.Id, firstBatch.Id, 1));
        AssertItemRejected(context, NewInspectionItem(inspection.Id, firstProduct.Id, secondBatch.Id, 1));

        Assert.Equal(1, context.Inspections.Count());
        Assert.Equal(1, context.InspectionItems.Count());
    }

    [Fact]
    public void RevisionHistoryRequiresDifferentNonnegativeValuesAndSortsByTimestampThenId()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-REVISION");
        var batch = AddBatch(context, product.Id, new DateOnly(2026, 12, 31));
        var task = AddTask(context, product.Id);
        var inspection = AddInspection(context, task.Id, product.Id);
        var item = AddInspectionItem(context, inspection.Id, product.Id, batch.Id, 0);
        var changedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        context.InspectionItemRevisions.AddRange(
            new InspectionItemRevision
            {
                InspectionItemId = item.Id,
                PreviousCheckedQty = 0,
                NewCheckedQty = 5,
                ChangedAtUtc = changedAt
            },
            new InspectionItemRevision
            {
                InspectionItemId = item.Id,
                PreviousCheckedQty = 5,
                NewCheckedQty = 0,
                ChangedAtUtc = changedAt
            });
        context.SaveChanges();

        var revisions = context.InspectionItemRevisions.AsNoTracking()
            .OrderBy(revision => revision.ChangedAtUtc)
            .ThenBy(revision => revision.Id)
            .ToArray();
        Assert.Equal(new[] { 0, 5 }, revisions.Select(revision => revision.PreviousCheckedQty));
        Assert.Equal(new[] { 5, 0 }, revisions.Select(revision => revision.NewCheckedQty));

        AssertRevisionRejected(context, item.Id, -1, 0, changedAt);
        AssertRevisionRejected(context, item.Id, 0, -1, changedAt);
        AssertRevisionRejected(context, item.Id, 2, 2, changedAt);
    }

    [Fact]
    public void ReferencedInspectionHistoryCannotBeDeletedOrCascadeDeleted()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-HISTORY-DELETE");
        var batch = AddBatch(context, product.Id, new DateOnly(2026, 12, 31));
        var task = AddTask(context, product.Id);
        var inspection = AddInspection(context, task.Id, product.Id);
        var item = AddInspectionItem(context, inspection.Id, product.Id, batch.Id, 1);
        var revision = new InspectionItemRevision
        {
            InspectionItemId = item.Id,
            PreviousCheckedQty = 1,
            NewCheckedQty = 0
        };
        context.InspectionItemRevisions.Add(revision);
        context.SaveChanges();

        Assert.Throws<SqliteException>(() => DeleteById(context, "products", product.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "batches", batch.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "tasks", task.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "inspections", inspection.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "inspection_items", item.Id));

        Assert.Equal(1, context.Products.Count());
        Assert.Equal(1, context.Batches.Count());
        Assert.Equal(1, context.Tasks.Count());
        Assert.Equal(1, context.Inspections.Count());
        Assert.Equal(1, context.InspectionItems.Count());
        Assert.Equal(1, context.InspectionItemRevisions.Count());
    }

    [Fact]
    public void UpgradeFromAddTasksAndDraftsPreservesExistingRowsAndAddsHistoryTables()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string addTasksAndDrafts;

        using (var context = database.Open())
        {
            addTasksAndDrafts = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddTasksAndDrafts", StringComparison.Ordinal));
            context.Database.Migrate(addTasksAndDrafts);

            var product = AddProduct(context, "SKU-UPGRADE-HISTORY");
            var batch = AddBatch(context, product.Id, new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 2));
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
            var product = context.Products.AsNoTracking().Single();
            var batch = context.Batches.AsNoTracking().Single();
            Assert.Equal("SKU-UPGRADE-HISTORY", product.ProductCode);
            Assert.Equal(product.Id, batch.ProductId);
            Assert.Equal(new DateOnly(2026, 1, 2), batch.ProductionDate);

            var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();
            Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddTasksAndDrafts", StringComparison.Ordinal));
            Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddInspectionHistory", StringComparison.Ordinal));
            var tables = SqliteTestDatabase.ReadSchemaNames(context, "table");
            Assert.Contains("inspections", tables);
            Assert.Contains("inspection_items", tables);
            Assert.Contains("inspection_item_revisions", tables);
        }
    }

    private static Product AddProduct(StoreDbContext context, string code)
    {
        var product = new Product { ProductCode = code };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(
        StoreDbContext context,
        long productId,
        DateOnly expiryDate,
        DateOnly? productionDate = null)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = productionDate,
            ExpiryDate = expiryDate,
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

    private static Inspection AddInspection(StoreDbContext context, long taskId, long productId)
    {
        var inspection = NewInspection(taskId, productId, "SKU-INSPECTION", "Inspector", 0);
        context.Inspections.Add(inspection);
        context.SaveChanges();
        return inspection;
    }

    private static InspectionItem AddInspectionItem(
        StoreDbContext context,
        long inspectionId,
        long productId,
        long batchId,
        int checkedQty)
    {
        var item = NewInspectionItem(inspectionId, productId, batchId, checkedQty);
        context.InspectionItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static Inspection NewInspection(
        long taskId,
        long productId,
        string productCode,
        string inspectorName,
        int stockQty)
    {
        return new Inspection
        {
            TaskId = taskId,
            ProductId = productId,
            ProductCodeSnapshot = productCode,
            StageSnapshot = "discount_50",
            StockQtySnapshot = stockQty,
            InspectorName = inspectorName,
            CheckDate = new DateOnly(2026, 1, 1)
        };
    }

    private static InspectionItem NewInspectionItem(
        long inspectionId,
        long productId,
        long batchId,
        int checkedQty)
    {
        return new InspectionItem
        {
            InspectionId = inspectionId,
            ProductId = productId,
            BatchId = batchId,
            ProductionDateSnapshot = new DateOnly(2026, 1, 1),
            ExpiryDateSnapshot = new DateOnly(2026, 12, 31),
            StageSnapshot = "discount_50",
            ArrivalQtySnapshot = 10,
            CheckedQty = checkedQty
        };
    }

    private static void AssertInspectionRejected(StoreDbContext context, Inspection inspection)
    {
        context.Inspections.Add(inspection);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertItemRejected(StoreDbContext context, InspectionItem item)
    {
        context.InspectionItems.Add(item);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertRevisionRejected(
        StoreDbContext context,
        long inspectionItemId,
        int previousCheckedQty,
        int newCheckedQty,
        DateTime changedAtUtc)
    {
        context.InspectionItemRevisions.Add(new InspectionItemRevision
        {
            InspectionItemId = inspectionItemId,
            PreviousCheckedQty = previousCheckedQty,
            NewCheckedQty = newCheckedQty,
            ChangedAtUtc = changedAtUtc
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void DeleteById(StoreDbContext context, string table, long id)
    {
        var statement = table switch
        {
            "products" => "DELETE FROM products WHERE id = {0}",
            "batches" => "DELETE FROM batches WHERE id = {0}",
            "tasks" => "DELETE FROM tasks WHERE id = {0}",
            "inspections" => "DELETE FROM inspections WHERE id = {0}",
            "inspection_items" => "DELETE FROM inspection_items WHERE id = {0}",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        context.Database.ExecuteSqlRaw(statement, id);
    }

    private static string ReadTableSql(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            return (string?)command.ExecuteScalar() ?? string.Empty;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static string[] ReadForeignKeyDeleteActions(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list({tableName})";
            using var reader = command.ExecuteReader();
            var actions = new List<string>();
            while (reader.Read())
            {
                actions.Add(reader.GetString(6));
            }

            return actions.ToArray();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }
}
