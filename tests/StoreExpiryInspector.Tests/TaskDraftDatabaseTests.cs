using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class TaskDraftDatabaseTests
{
    [Fact]
    public void InitialMigrationCreatesTablesAndRequiredIndexes()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddTasksAndDrafts", StringComparison.Ordinal));
        Assert.Equal("wal", SqliteTestDatabase.ReadPragma(context, "journal_mode"));
        Assert.Contains("products", SqliteTestDatabase.ReadSchemaNames(context, "table"));
        Assert.Contains("batches", SqliteTestDatabase.ReadSchemaNames(context, "table"));
        Assert.Contains("tasks", SqliteTestDatabase.ReadSchemaNames(context, "table"));
        Assert.Contains("task_items", SqliteTestDatabase.ReadSchemaNames(context, "table"));
        Assert.Contains("drafts", SqliteTestDatabase.ReadSchemaNames(context, "table"));
        Assert.Contains("draft_items", SqliteTestDatabase.ReadSchemaNames(context, "table"));

        var indexes = SqliteTestDatabase.ReadSchemaNames(context, "index");
        Assert.Contains("IX_products_product_code", indexes);
        Assert.Contains("IX_batches_product_id", indexes);
        Assert.Contains("IX_batches_expiry_date", indexes);
        Assert.Contains("IX_batches_tracking_status_next_trigger_date", indexes);
        Assert.Contains("IX_batches_product_id_production_date_expiry_date", indexes);
        Assert.Contains("IX_batches_product_id_expiry_date", indexes);

        Assert.Contains(
            "production_date IS NOT NULL",
            SqliteTestDatabase.ReadIndexSql(context, "IX_batches_product_id_production_date_expiry_date"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "production_date IS NULL",
            SqliteTestDatabase.ReadIndexSql(context, "IX_batches_product_id_expiry_date"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains("IX_tasks_product_id_open", indexes);
        Assert.Contains("IX_tasks_product_id_status", indexes);
        Assert.Contains("IX_task_items_task_id_batch_id", indexes);
        Assert.Contains("IX_drafts_task_id", indexes);
        Assert.Contains("IX_draft_items_draft_id_task_item_id", indexes);
        Assert.DoesNotContain(
            "WHERE",
            SqliteTestDatabase.ReadIndexSql(context, "IX_tasks_product_id_status"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "status = 'open'",
            SqliteTestDatabase.ReadIndexSql(context, "IX_tasks_product_id_open"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationFromInitialCreatePreservesExistingProductAndBatch()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string initialMigration;

        using (var context = database.Open())
        {
            initialMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
            context.Database.Migrate(initialMigration);

            var product = AddProduct(context, "SKU-UPGRADE");
            context.Batches.Add(NewBatch(product.Id, new DateOnly(2026, 1, 2), new DateOnly(2026, 12, 31)));
            context.SaveChanges();
        }

        using (var context = database.Open())
        {
            context.Database.Migrate();

            var product = context.Products.AsNoTracking().Single();
            var batch = context.Batches.AsNoTracking().Single();
            Assert.Equal("SKU-UPGRADE", product.ProductCode);
            Assert.Equal(product.Id, batch.ProductId);
            Assert.Equal(new DateOnly(2026, 1, 2), batch.ProductionDate);
            Assert.Contains(
                context.Database.GetAppliedMigrations(),
                migration => migration.EndsWith("_AddTasksAndDrafts", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OpenTasksAreUniquePerProductButClosedHistoryIsRetained()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-TASK-UNIQUE");

        context.Tasks.Add(NewTask(product.Id));
        context.SaveChanges();
        context.Tasks.Add(NewTask(product.Id));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        var openTask = context.Tasks.Single();
        openTask.Status = "completed";
        openTask.ClosedAtUtc = DateTime.UtcNow;
        context.SaveChanges();

        context.Tasks.Add(NewTask(product.Id, "system_closed", "withdraw", "auto-close"));
        context.SaveChanges();
        Assert.Equal(2, context.Tasks.Count());
    }

    [Fact]
    public void TasksRejectInvalidStatusStageAndCloseFieldCombinations()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-TASK-CHECKS");

        AssertTaskRejected(context, new ProductTask
        {
            ProductId = product.Id,
            Status = "unknown",
            HighestStage = "discount_50"
        });
        AssertTaskRejected(context, new ProductTask
        {
            ProductId = product.Id,
            Status = "open",
            HighestStage = "unknown"
        });
        AssertTaskRejected(context, new ProductTask
        {
            ProductId = product.Id,
            Status = "open",
            HighestStage = "discount_50",
            ClosedAtUtc = DateTime.UtcNow
        });
        AssertTaskRejected(context, new ProductTask
        {
            ProductId = product.Id,
            Status = "completed",
            HighestStage = "discount_50"
        });
        AssertTaskRejected(context, new ProductTask
        {
            ProductId = product.Id,
            Status = "system_closed",
            HighestStage = "discount_50",
            ClosedAtUtc = DateTime.UtcNow,
            CloseReason = " "
        });
    }

    [Fact]
    public void TaskItemsAreUniquePerTaskBatchAndCannotCrossProducts()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var firstProduct = AddProduct(context, "SKU-ITEM-ONE");
        var secondProduct = AddProduct(context, "SKU-ITEM-TWO");
        var firstBatch = AddBatch(context, firstProduct.Id, new DateOnly(2026, 12, 31));
        var secondBatch = AddBatch(context, secondProduct.Id, new DateOnly(2026, 12, 30));
        var task = AddTask(context, firstProduct.Id);

        context.TaskItems.Add(NewTaskItem(task.Id, firstBatch.Id, firstProduct.Id));
        context.SaveChanges();
        context.TaskItems.Add(NewTaskItem(task.Id, firstBatch.Id, firstProduct.Id));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        context.TaskItems.Add(NewTaskItem(task.Id, secondBatch.Id, firstProduct.Id));
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        Assert.Equal(1, context.TaskItems.Count());
    }

    [Fact]
    public void TaskItemsRejectInvalidStageAndNegativeAttentionVersion()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-ITEM-CHECKS");
        var batch = AddBatch(context, product.Id, new DateOnly(2026, 12, 31));
        var task = AddTask(context, product.Id);

        AssertTaskItemRejected(context, new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            Stage = "unknown"
        });
        AssertTaskItemRejected(context, new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            AttentionVersion = -1
        });
    }

    [Fact]
    public void DraftsAreUniquePerTaskAndInvalidationFieldsAreConsistent()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-DRAFT-CHECKS");
        var task = AddTask(context, product.Id);

        context.Drafts.Add(new InspectionDraft { TaskId = task.Id });
        context.SaveChanges();
        Assert.Throws<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolated($"INSERT INTO drafts (task_id, is_invalid) VALUES ({task.Id}, 0)"));

        context.ChangeTracker.Clear();
        AssertDraftRejected(context, new InspectionDraft
        {
            TaskId = AddTask(context, product.Id, "completed", "discount_20", "done").Id,
            InvalidReason = "reason",
            InvalidatedAtUtc = DateTime.UtcNow
        });
        AssertDraftRejected(context, new InspectionDraft
        {
            TaskId = AddTask(context, product.Id, "completed", "withdraw", "done").Id,
            IsInvalid = true,
            InvalidatedAtUtc = DateTime.UtcNow
        });
        AssertDraftRejected(context, new InspectionDraft
        {
            TaskId = AddTask(context, product.Id, "completed", "expired", "done").Id,
            IsInvalid = true,
            InvalidReason = " ",
            InvalidatedAtUtc = DateTime.UtcNow
        });
        AssertDraftRejected(context, new InspectionDraft
        {
            TaskId = AddTask(context, product.Id, "completed", "discount_50", "done").Id,
            IsInvalid = true,
            InvalidReason = "reason"
        });

        context.Drafts.Add(new InspectionDraft
        {
            TaskId = AddTask(context, product.Id, "completed", "discount_20", "done").Id,
            IsInvalid = true,
            InvalidReason = "replaced",
            InvalidatedAtUtc = DateTime.UtcNow
        });
        context.SaveChanges();
        Assert.True(context.Drafts.Single(draft => draft.InvalidReason == "replaced").IsInvalid);
    }

    [Fact]
    public void DraftItemCheckedQuantitySupportsNullZeroAndPositiveButRejectsNegative()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-DRAFT-QTY");
        var task = AddTask(context, product.Id);
        var draft = AddDraft(context, task.Id);
        var firstItem = AddTaskItem(context, task.Id, AddBatch(context, product.Id, new DateOnly(2026, 12, 31)).Id, product.Id);
        var secondItem = AddTaskItem(context, task.Id, AddBatch(context, product.Id, new DateOnly(2026, 12, 30)).Id, product.Id);
        var thirdItem = AddTaskItem(context, task.Id, AddBatch(context, product.Id, new DateOnly(2026, 12, 29)).Id, product.Id);

        context.DraftItems.AddRange(
            new InspectionDraftItem { DraftId = draft.Id, TaskItemId = firstItem.Id, TaskId = task.Id },
            new InspectionDraftItem { DraftId = draft.Id, TaskItemId = secondItem.Id, TaskId = task.Id, CheckedQty = 0 },
            new InspectionDraftItem { DraftId = draft.Id, TaskItemId = thirdItem.Id, TaskId = task.Id, CheckedQty = 5 });
        context.SaveChanges();

        var quantities = context.DraftItems.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.CheckedQty)
            .ToArray();
        Assert.Equal(new int?[] { null, 0, 5 }, quantities);

        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = draft.Id,
            TaskItemId = firstItem.Id,
            TaskId = task.Id,
            CheckedQty = -1
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        var fourthItem = AddTaskItem(
            context,
            task.Id,
            AddBatch(context, product.Id, new DateOnly(2026, 12, 28)).Id,
            product.Id);
        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = draft.Id,
            TaskItemId = fourthItem.Id,
            TaskId = task.Id,
            ConfirmedAttentionVersion = -1
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void DraftItemsAreUniquePerDraftTaskItemAndCannotCrossTasks()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-DRAFT-ITEMS");
        var firstTask = AddTask(context, product.Id);
        var secondTask = AddTask(context, product.Id, "completed", "discount_20", "done");
        var firstItem = AddTaskItem(context, firstTask.Id, AddBatch(context, product.Id, new DateOnly(2026, 12, 31)).Id, product.Id);
        var secondItem = AddTaskItem(context, secondTask.Id, AddBatch(context, product.Id, new DateOnly(2026, 12, 30)).Id, product.Id);
        var firstDraft = AddDraft(context, firstTask.Id);

        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = firstDraft.Id,
            TaskItemId = firstItem.Id,
            TaskId = firstTask.Id
        });
        context.SaveChanges();

        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = firstDraft.Id,
            TaskItemId = firstItem.Id,
            TaskId = firstTask.Id
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = firstDraft.Id,
            TaskItemId = secondItem.Id,
            TaskId = firstTask.Id
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void ReferencedRecordsCannotBeDeletedOrCascadeIntoHistory()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-NO-CASCADE");
        var batch = AddBatch(context, product.Id, new DateOnly(2026, 12, 31));
        var task = AddTask(context, product.Id);
        var taskItem = AddTaskItem(context, task.Id, batch.Id, product.Id);
        var draft = AddDraft(context, task.Id);
        var draftItem = new InspectionDraftItem
        {
            DraftId = draft.Id,
            TaskItemId = taskItem.Id,
            TaskId = task.Id
        };
        context.DraftItems.Add(draftItem);
        context.SaveChanges();

        Assert.Throws<SqliteException>(() => DeleteById(context, "products", product.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "batches", batch.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "tasks", task.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "task_items", taskItem.Id));
        Assert.Throws<SqliteException>(() => DeleteById(context, "drafts", draft.Id));

        Assert.Equal(1, context.Products.Count());
        Assert.Equal(1, context.Batches.Count());
        Assert.Equal(1, context.Tasks.Count());
        Assert.Equal(1, context.TaskItems.Count());
        Assert.Equal(1, context.Drafts.Count());
        Assert.Equal(1, context.DraftItems.Count());
    }

    private static Product AddProduct(StoreDbContext context, string code)
    {
        var product = new Product { ProductCode = code };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(StoreDbContext context, long productId, DateOnly expiryDate)
    {
        var batch = NewBatch(productId, null, expiryDate);
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static Batch NewBatch(long productId, DateOnly? productionDate, DateOnly expiryDate)
    {
        return new Batch
        {
            ProductId = productId,
            ProductionDate = productionDate,
            ExpiryDate = expiryDate,
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10
        };
    }

    private static ProductTask AddTask(
        StoreDbContext context,
        long productId,
        string status = "open",
        string highestStage = "discount_50",
        string? closeReason = null)
    {
        var task = NewTask(productId, status, highestStage, closeReason);
        context.Tasks.Add(task);
        context.SaveChanges();
        return task;
    }

    private static ProductTask NewTask(
        long productId,
        string status = "open",
        string highestStage = "discount_50",
        string? closeReason = null)
    {
        return new ProductTask
        {
            ProductId = productId,
            Status = status,
            HighestStage = highestStage,
            ClosedAtUtc = status == "open" ? null : DateTime.UtcNow,
            CloseReason = closeReason
        };
    }

    private static ProductTaskItem AddTaskItem(StoreDbContext context, long taskId, long batchId, long productId)
    {
        var item = NewTaskItem(taskId, batchId, productId);
        context.TaskItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static ProductTaskItem NewTaskItem(long taskId, long batchId, long productId)
    {
        return new ProductTaskItem
        {
            TaskId = taskId,
            BatchId = batchId,
            ProductId = productId,
            Stage = "discount_50",
            AttentionVersion = 0
        };
    }

    private static InspectionDraft AddDraft(StoreDbContext context, long taskId)
    {
        var draft = new InspectionDraft { TaskId = taskId };
        context.Drafts.Add(draft);
        context.SaveChanges();
        return draft;
    }

    private static void AssertTaskRejected(StoreDbContext context, ProductTask task)
    {
        context.Tasks.Add(task);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertTaskItemRejected(StoreDbContext context, ProductTaskItem item)
    {
        context.TaskItems.Add(item);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertDraftRejected(StoreDbContext context, InspectionDraft draft)
    {
        context.Drafts.Add(draft);
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
            "task_items" => "DELETE FROM task_items WHERE id = {0}",
            "drafts" => "DELETE FROM drafts WHERE id = {0}",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        context.Database.ExecuteSqlRaw(statement, id);
    }
}
