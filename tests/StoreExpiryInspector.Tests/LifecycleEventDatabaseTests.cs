using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class LifecycleEventDatabaseTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly string[] EventTypes =
    {
        "product_stock_zero",
        "batch_checked_zero",
        "batch_tracking_resumed",
        "task_auto_closed",
        "draft_invalidated"
    };

    [Fact]
    public void LifecycleEventsSchemaHasExactColumnsChecksIndexesAndNoActionForeignKeys()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Contains(
            context.Database.GetAppliedMigrations(),
            migration => migration.EndsWith("_AddLifecycleEvents", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "id",
                "product_id",
                "batch_id",
                "event_type",
                "reason",
                "occurred_at_utc",
                "source_import_id",
                "source_inspection_id",
                "source_adjustment_id"
            },
            SqliteTestDatabase.ReadTableColumns(context, "lifecycle_events"));

        var tableSql = SqliteTestDatabase.ReadTableSql(context, "lifecycle_events");
        Assert.Contains(
            "event_type IN ('product_stock_zero', 'batch_checked_zero', 'batch_tracking_resumed', 'task_auto_closed', 'draft_invalidated')",
            tableSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(reason) > 0 AND reason = trim(reason)", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(source_import_id IS NOT NULL) + (source_inspection_id IS NOT NULL) + (source_adjustment_id IS NOT NULL) <= 1",
            tableSql,
            StringComparison.OrdinalIgnoreCase);

        var foreignKeys = ReadForeignKeys(context, "lifecycle_events");
        Assert.Equal(7, foreignKeys.Length);
        Assert.All(foreignKeys, foreignKey => Assert.Equal("NO ACTION", foreignKey.OnDelete));
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "product_id" && foreignKey.Table == "products" && foreignKey.To == "id");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "batch_id" && foreignKey.Table == "batches" && foreignKey.To == "id");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "product_id" && foreignKey.Table == "batches" && foreignKey.To == "product_id");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "source_import_id" && foreignKey.Table == "imports" && foreignKey.To == "id");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "source_inspection_id" && foreignKey.Table == "inspections" && foreignKey.To == "id");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "product_id" && foreignKey.Table == "inspections" && foreignKey.To == "product_id");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.From == "source_adjustment_id" && foreignKey.Table == "inventory_adjustments" && foreignKey.To == "id");

        var expectedIndexes = new[]
        {
            "IX_lifecycle_events_batch_id_product_id_occurred_at_utc_id",
            "IX_lifecycle_events_product_id_occurred_at_utc_id",
            "IX_lifecycle_events_source_adjustment_id",
            "IX_lifecycle_events_source_import_id",
            "IX_lifecycle_events_source_inspection_id_product_id"
        };
        Assert.Equal(expectedIndexes, ReadIndexNames(context, "lifecycle_events").OrderBy(name => name));
        foreach (var indexName in expectedIndexes)
        {
            Assert.DoesNotContain("CREATE UNIQUE INDEX", SqliteTestDatabase.ReadIndexSql(context, indexName), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(
            new[] { "batch_id", "product_id", "occurred_at_utc", "id" },
            ReadIndexColumns(context, "IX_lifecycle_events_batch_id_product_id_occurred_at_utc_id"));
        Assert.Equal(
            new[] { "product_id", "occurred_at_utc", "id" },
            ReadIndexColumns(context, "IX_lifecycle_events_product_id_occurred_at_utc_id"));
        Assert.Equal(
            new[] { "source_inspection_id", "product_id" },
            ReadIndexColumns(context, "IX_lifecycle_events_source_inspection_id_product_id"));
        Assert.Equal(new[] { "source_import_id" }, ReadIndexColumns(context, "IX_lifecycle_events_source_import_id"));
        Assert.Equal(new[] { "source_adjustment_id" }, ReadIndexColumns(context, "IX_lifecycle_events_source_adjustment_id"));
    }

    [Fact]
    public void LifecycleEventsAcceptFiveTypesOptionalBatchAndEachSingleSource()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var references = AddReferences(context, "SKU-LIFECYCLE-LEGAL");
        var occurredAtUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

        context.LifecycleEvents.AddRange(
            NewEvent(references.Product.Id, null, "product_stock_zero", "  product reached zero  ", occurredAtUtc),
            NewEvent(references.Product.Id, references.Batch.Id, "batch_checked_zero", "batch check", occurredAtUtc.AddMinutes(1)),
            NewEvent(
                references.Product.Id,
                references.Batch.Id,
                "batch_tracking_resumed",
                "new arrival",
                occurredAtUtc.AddMinutes(2),
                sourceImportId: references.Import.Id),
            NewEvent(
                references.Product.Id,
                null,
                "task_auto_closed",
                "stock zero closed task",
                occurredAtUtc.AddMinutes(3),
                sourceInspectionId: references.Inspection.Id),
            NewEvent(
                references.Product.Id,
                null,
                "draft_invalidated",
                "newer import invalidated draft",
                occurredAtUtc.AddMinutes(4),
                sourceAdjustmentId: references.Adjustment.Id));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var saved = context.LifecycleEvents.AsNoTracking().OrderBy(lifecycleEvent => lifecycleEvent.Id).ToArray();
        Assert.Equal(EventTypes, saved.Select(lifecycleEvent => lifecycleEvent.EventType));
        Assert.Equal("product reached zero", saved[0].Reason);
        Assert.Null(saved[0].BatchId);
        Assert.Null(saved[0].SourceImportId);
        Assert.Equal(references.Batch.Id, saved[1].BatchId);
        Assert.Equal(references.Import.Id, saved[2].SourceImportId);
        Assert.Equal(references.Inspection.Id, saved[3].SourceInspectionId);
        Assert.Equal(references.Adjustment.Id, saved[4].SourceAdjustmentId);
        Assert.All(saved, lifecycleEvent => Assert.Equal(references.Product.Id, lifecycleEvent.ProductId));

        AssertRejected(
            context,
            NewEvent(references.Product.Id, null, "unknown_type", "unknown event", occurredAtUtc));
        AssertRejected(
            context,
            NewEvent(references.Product.Id, null, "product_stock_zero", " ", occurredAtUtc));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated(
            $"INSERT INTO lifecycle_events (event_type, reason, occurred_at_utc) VALUES ('product_stock_zero', 'missing product', {occurredAtUtc})"));
    }

    [Fact]
    public void LifecycleEventsRejectInvalidSourcesAndCrossProductBatchOrInspection()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var references = AddReferences(context, "SKU-LIFECYCLE-PRIMARY");
        var otherReferences = AddReferences(context, "SKU-LIFECYCLE-OTHER");
        var occurredAtUtc = new DateTime(2026, 8, 27, 13, 0, 0, DateTimeKind.Utc);

        AssertRejected(context, NewEvent(999, null, "product_stock_zero", "missing product", occurredAtUtc));
        AssertRejected(context, NewEvent(references.Product.Id, 999, "batch_checked_zero", "missing batch", occurredAtUtc));
        AssertRejected(context, NewEvent(references.Product.Id, otherReferences.Batch.Id, "batch_checked_zero", "other product batch", occurredAtUtc));
        AssertRejected(
            context,
            NewEvent(
                references.Product.Id,
                null,
                "task_auto_closed",
                "missing inspection",
                occurredAtUtc,
                sourceInspectionId: 999));
        AssertRejected(
            context,
            NewEvent(
                references.Product.Id,
                null,
                "task_auto_closed",
                "other product inspection",
                occurredAtUtc,
                sourceInspectionId: otherReferences.Inspection.Id));
        AssertRejected(
            context,
            NewEvent(
                references.Product.Id,
                null,
                "batch_tracking_resumed",
                "missing import",
                occurredAtUtc,
                sourceImportId: 999));
        AssertRejected(
            context,
            NewEvent(
                references.Product.Id,
                null,
                "draft_invalidated",
                "missing adjustment",
                occurredAtUtc,
                sourceAdjustmentId: 999));
        AssertRejected(
            context,
            NewEvent(
                references.Product.Id,
                null,
                "product_stock_zero",
                "two sources",
                occurredAtUtc,
                sourceImportId: references.Import.Id,
                sourceInspectionId: references.Inspection.Id));
        AssertRejected(
            context,
            NewEvent(
                references.Product.Id,
                null,
                "product_stock_zero",
                "three sources",
                occurredAtUtc,
                sourceImportId: references.Import.Id,
                sourceInspectionId: references.Inspection.Id,
                sourceAdjustmentId: references.Adjustment.Id));

        var sourceLess = NewEvent(references.Product.Id, null, "draft_invalidated", "system event", occurredAtUtc);
        context.LifecycleEvents.Add(sourceLess);
        context.SaveChanges();
        Assert.Null(context.LifecycleEvents.AsNoTracking().Single(lifecycleEvent => lifecycleEvent.Id == sourceLess.Id).SourceImportId);
    }

    [Fact]
    public void ReferencedLifecycleRowsCannotBeDeletedAndEventsRemainIntact()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var references = AddReferences(context, "SKU-LIFECYCLE-DELETE");
        context.LifecycleEvents.AddRange(
            NewEvent(references.Product.Id, null, "product_stock_zero", "product source", DateTime.UtcNow),
            NewEvent(references.Product.Id, references.Batch.Id, "batch_checked_zero", "batch source", DateTime.UtcNow.AddSeconds(1)),
            NewEvent(references.Product.Id, null, "batch_tracking_resumed", "import source", DateTime.UtcNow.AddSeconds(2), sourceImportId: references.Import.Id),
            NewEvent(references.Product.Id, null, "task_auto_closed", "inspection source", DateTime.UtcNow.AddSeconds(3), sourceInspectionId: references.Inspection.Id),
            NewEvent(references.Product.Id, null, "draft_invalidated", "adjustment source", DateTime.UtcNow.AddSeconds(4), sourceAdjustmentId: references.Adjustment.Id));
        context.SaveChanges();

        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated($"DELETE FROM products WHERE id = {references.Product.Id}"));
        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated($"DELETE FROM batches WHERE id = {references.Batch.Id}"));
        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated($"DELETE FROM imports WHERE id = {references.Import.Id}"));
        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated($"DELETE FROM inspections WHERE id = {references.Inspection.Id}"));
        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated($"DELETE FROM inventory_adjustments WHERE id = {references.Adjustment.Id}"));
        Assert.Equal(5, context.LifecycleEvents.Count());
    }

    [Fact]
    public void SameProductAndTimeAllowsDuplicateEventsAndSortsById()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-LIFECYCLE-DUPLICATES");
        var occurredAtUtc = new DateTime(2026, 8, 27, 14, 0, 0, DateTimeKind.Utc);
        context.LifecycleEvents.AddRange(
            NewEvent(product.Id, null, "product_stock_zero", "same fact", occurredAtUtc),
            NewEvent(product.Id, null, "product_stock_zero", "same fact", occurredAtUtc));
        context.SaveChanges();

        var saved = context.LifecycleEvents.AsNoTracking()
            .Where(lifecycleEvent => lifecycleEvent.ProductId == product.Id)
            .OrderBy(lifecycleEvent => lifecycleEvent.ProductId)
            .ThenBy(lifecycleEvent => lifecycleEvent.OccurredAtUtc)
            .ThenBy(lifecycleEvent => lifecycleEvent.Id)
            .ToArray();
        Assert.Equal(2, saved.Length);
        Assert.True(saved[0].Id < saved[1].Id);
    }

    [Fact]
    public void UpgradeFromSettingsAndAppStatePreservesAllSixteenExistingDataSets()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string addSettingsAndAppState;

        using (var context = database.Open())
        {
            addSettingsAndAppState = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddSettingsAndAppState", StringComparison.Ordinal));
            context.Database.Migrate(addSettingsAndAppState);

            var product = AddProduct(context, "SKU-UPGRADE-LIFECYCLE");
            var batch = AddBatch(context, product.Id, currentArrivalQty: 7, maxArrivalQty: 9);
            var task = AddTask(context, product.Id);
            var taskItem = new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = "discount_20"
            };
            context.TaskItems.Add(taskItem);
            context.SaveChanges();

            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "Upgrade Inspector",
                CheckDate = new DateOnly(2026, 8, 27)
            };
            context.Drafts.Add(draft);
            context.SaveChanges();
            context.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = taskItem.Id,
                TaskId = task.Id,
                CheckedQty = 2
            });
            context.SaveChanges();

            var inspection = AddInspection(context, task, product);
            var inspectionItem = new InspectionItem
            {
                InspectionId = inspection.Id,
                ProductId = product.Id,
                BatchId = batch.Id,
                ProductionDateSnapshot = new DateOnly(2026, 1, 1),
                ExpiryDateSnapshot = new DateOnly(2026, 12, 31),
                StageSnapshot = "discount_20",
                ArrivalQtySnapshot = 7,
                CheckedQty = 3
            };
            context.InspectionItems.Add(inspectionItem);
            context.SaveChanges();
            context.InspectionItemRevisions.Add(new InspectionItemRevision
            {
                InspectionItemId = inspectionItem.Id,
                PreviousCheckedQty = 2,
                NewCheckedQty = 3
            });

            var import = NewImport();
            context.Imports.Add(import);
            context.SaveChanges();
            context.ImportWorkbooks.Add(new ImportWorkbook
            {
                ImportId = import.Id,
                OriginalFileName = "upgrade.xlsx",
                Content = new byte[] { 1, 2, 3 },
                Sha256 = ValidSha256,
                SavedAtUtc = DateTime.UtcNow
            });
            context.ImportIssues.Add(new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = 2,
                IssueType = "invalid-date",
                SafeSummary = "upgrade issue"
            });
            context.InventoryAdjustments.Add(new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = 9,
                AdjustedStockQty = 8
            });
            context.BackupRecords.Add(new BackupRecord
            {
                BackupType = "manual",
                FilePath = "upgrade.db",
                Sha256 = ValidSha256,
                CreatedAtUtc = DateTime.UtcNow,
                VerificationStatus = "verified"
            });
            context.SaveChanges();
            product.LastSeenImportId = import.Id;
            batch.LastSeenImportId = import.Id;
            context.Settings.Single().ReminderMinuteOfDay = 123;
            context.Settings.Single().AutoStartEnabled = false;
            var state = context.AppStates.Single();
            state.LastReminderDate = new DateOnly(2026, 8, 26);
            state.LastNormalRunDate = new DateOnly(2026, 8, 27);
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
            Assert.Equal(1, context.InventoryAdjustments.Count());
            Assert.Equal(1, context.Imports.Count());
            Assert.Equal(1, context.ImportWorkbooks.Count());
            Assert.Equal(1, context.ImportIssues.Count());
            Assert.Equal(1, context.BackupRecords.Count());
            Assert.Equal(1, context.Settings.Count());
            Assert.Equal(1, context.AppStates.Count());
            Assert.Equal("SKU-UPGRADE-LIFECYCLE", context.Products.AsNoTracking().Single().ProductCode);
            Assert.Equal(7, context.Batches.AsNoTracking().Single().CurrentArrivalQty);
            Assert.Equal("discount_20", context.TaskItems.AsNoTracking().Single().Stage);
            Assert.Equal("Upgrade Inspector", context.Drafts.AsNoTracking().Single().InspectorName);
            Assert.Equal(2, context.DraftItems.AsNoTracking().Single().CheckedQty);
            Assert.Equal(3, context.InspectionItems.AsNoTracking().Single().CheckedQty);
            Assert.Equal(3, context.InspectionItemRevisions.AsNoTracking().Single().NewCheckedQty);
            Assert.Equal(8, context.InventoryAdjustments.AsNoTracking().Single().AdjustedStockQty);
            Assert.Equal(ValidSha256, context.ImportWorkbooks.AsNoTracking().Single().Sha256);
            Assert.Equal("upgrade issue", context.ImportIssues.AsNoTracking().Single().SafeSummary);
            Assert.Equal("manual", context.BackupRecords.AsNoTracking().Single().BackupType);
            Assert.Equal(123, context.Settings.AsNoTracking().Single().ReminderMinuteOfDay);
            Assert.False(context.Settings.AsNoTracking().Single().AutoStartEnabled);
            Assert.Equal(new DateOnly(2026, 8, 26), context.AppStates.AsNoTracking().Single().LastReminderDate);
            Assert.Equal(new DateOnly(2026, 8, 27), context.AppStates.AsNoTracking().Single().LastNormalRunDate);
            Assert.Contains(
                context.Database.GetAppliedMigrations(),
                migration => migration.EndsWith("_AddLifecycleEvents", StringComparison.Ordinal));
            Assert.Empty(context.LifecycleEvents);
        }
    }

    [Fact]
    public void IncrementalMigrationScriptOnlyCreatesLifecycleEventsTableAndIndexes()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using var context = database.Open();
        var migrations = context.Database.GetMigrations().ToArray();
        var fromMigration = migrations.Single(migration => migration.EndsWith("_AddSettingsAndAppState", StringComparison.Ordinal));
        var toMigration = migrations.Single(migration => migration.EndsWith("_AddLifecycleEvents", StringComparison.Ordinal));
        var script = context.Database.GetService<IMigrator>().GenerateScript(fromMigration, toMigration);

        var createdTables = Regex.Matches(script, @"CREATE\s+TABLE\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "lifecycle_events" }, createdTables);
        var createdIndexes = Regex.Matches(script, @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "IX_lifecycle_events_batch_id_product_id_occurred_at_utc_id",
                "IX_lifecycle_events_product_id_occurred_at_utc_id",
                "IX_lifecycle_events_source_adjustment_id",
                "IX_lifecycle_events_source_import_id",
                "IX_lifecycle_events_source_inspection_id_product_id"
            },
            createdIndexes);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ef_temp_", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE \"products\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE \"batches\"", script, StringComparison.OrdinalIgnoreCase);
    }

    private static LifecycleEvent NewEvent(
        long productId,
        long? batchId,
        string eventType,
        string reason,
        DateTime occurredAtUtc,
        long? sourceImportId = null,
        long? sourceInspectionId = null,
        long? sourceAdjustmentId = null)
    {
        return new LifecycleEvent
        {
            ProductId = productId,
            BatchId = batchId,
            EventType = eventType,
            Reason = reason,
            OccurredAtUtc = occurredAtUtc,
            SourceImportId = sourceImportId,
            SourceInspectionId = sourceInspectionId,
            SourceAdjustmentId = sourceAdjustmentId
        };
    }

    private static References AddReferences(StoreDbContext context, string productCode)
    {
        var product = AddProduct(context, productCode);
        var batch = AddBatch(context, product.Id);
        var task = AddTask(context, product.Id);
        var import = NewImport();
        context.Imports.Add(import);
        context.SaveChanges();
        var inspection = AddInspection(context, task, product);
        var adjustment = new InventoryAdjustment
        {
            ProductId = product.Id,
            ExcelStockQtySnapshot = 10,
            AdjustedStockQty = 8
        };
        context.InventoryAdjustments.Add(adjustment);
        context.SaveChanges();
        return new References(product, batch, import, inspection, adjustment);
    }

    private static Product AddProduct(StoreDbContext context, string productCode)
    {
        if (!SqliteTestDatabase.ReadTableColumns(context, "products").Contains("expiry_management_status"))
        {
            context.Database.ExecuteSql($"INSERT INTO products (product_code, excel_stock_qty, effective_stock_qty, lifecycle_generation) VALUES ({productCode}, 0, 0, 0)");
            var legacyProduct = new Product { Id = context.Database.SqlQuery<long>($"SELECT last_insert_rowid() AS Value").Single(), ProductCode = productCode };
            context.Attach(legacyProduct);
            return legacyProduct;
        }
        var product = new Product { ProductCode = productCode };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(
        StoreDbContext context,
        long productId,
        int currentArrivalQty = 10,
        int maxArrivalQty = 10)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2026, 12, 31),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = currentArrivalQty,
            MaxArrivalQty = maxArrivalQty
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static ProductTask AddTask(StoreDbContext context, long productId)
    {
        var task = new ProductTask { ProductId = productId, HighestStage = "discount_50" };
        context.Tasks.Add(task);
        context.SaveChanges();
        return task;
    }

    private static Inspection AddInspection(StoreDbContext context, ProductTask task, Product product)
    {
        var inspection = new Inspection
        {
            TaskId = task.Id,
            ProductId = product.Id,
            ProductCodeSnapshot = product.ProductCode,
            StageSnapshot = "discount_50",
            StockQtySnapshot = 0,
            InspectorName = "Inspector",
            CheckDate = new DateOnly(2026, 8, 27)
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();
        return inspection;
    }

    private static ImportRecord NewImport()
    {
        return new ImportRecord
        {
            SourceFileName = "source.xlsx",
            SourceFileSha256 = ValidSha256,
            ParsedAtUtc = DateTime.UtcNow,
            Status = "confirmed"
        };
    }

    private static void AssertRejected(StoreDbContext context, LifecycleEvent lifecycleEvent)
    {
        context.LifecycleEvents.Add(lifecycleEvent);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertSqliteRejected(Action action)
    {
        Assert.Throws<SqliteException>(action);
    }

    private static string[] ReadIndexNames(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA index_list({tableName})";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(1));
            }

            return names.ToArray();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static string[] ReadIndexColumns(StoreDbContext context, string indexName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA index_info({indexName})";
            using var reader = command.ExecuteReader();
            var columns = new SortedDictionary<int, string>();
            while (reader.Read())
            {
                columns[reader.GetInt32(0)] = reader.GetString(2);
            }

            return columns.Values.ToArray();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static ForeignKey[] ReadForeignKeys(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list({tableName})";
            using var reader = command.ExecuteReader();
            var foreignKeys = new List<ForeignKey>();
            while (reader.Read())
            {
                foreignKeys.Add(new ForeignKey(
                    reader.GetString(3),
                    reader.GetString(2),
                    reader.GetString(4),
                    reader.GetString(6)));
            }

            return foreignKeys.ToArray();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private sealed record References(
        Product Product,
        Batch Batch,
        ImportRecord Import,
        Inspection Inspection,
        InventoryAdjustment Adjustment);

    private sealed record ForeignKey(string From, string Table, string To, string OnDelete);
}
