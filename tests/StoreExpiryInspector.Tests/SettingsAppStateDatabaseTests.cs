using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class SettingsAppStateDatabaseTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void SettingsAndAppStateHaveExactSchemaDefaultsChecksAndNoIndexes()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Contains(
            context.Database.GetAppliedMigrations(),
            migration => migration.EndsWith("_AddSettingsAndAppState", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "id", "reminder_minute_of_day", "auto_start_enabled" },
            SqliteTestDatabase.ReadTableColumns(context, "settings"));
        Assert.Equal(
            new[] { "id", "last_reminder_date", "last_normal_run_date" },
            SqliteTestDatabase.ReadTableColumns(context, "app_state"));

        var settingsSql = SqliteTestDatabase.ReadTableSql(context, "settings");
        Assert.Contains("id = 1", settingsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reminder_minute_of_day BETWEEN 0 AND 1439", settingsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auto_start_enabled IN (0, 1)", settingsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEFAULT 600", settingsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEFAULT 1", settingsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ReadIndexNames(context, "settings"));

        var appStateSql = SqliteTestDatabase.ReadTableSql(context, "app_state");
        Assert.Contains("id = 1", appStateSql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ReadIndexNames(context, "app_state"));

        var settings = context.Settings.AsNoTracking().Single();
        Assert.Equal(1, settings.Id);
        Assert.Equal(600, settings.ReminderMinuteOfDay);
        Assert.True(settings.AutoStartEnabled);

        var state = context.AppStates.AsNoTracking().Single();
        Assert.Equal(1, state.Id);
        Assert.Null(state.LastReminderDate);
        Assert.Null(state.LastNormalRunDate);
    }

    [Fact]
    public void SettingsEnforceReminderRangeBooleanAndSingletonConstraintsInSqlite()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var settings = context.Settings.Single();

        foreach (var minute in new[] { 0, 600, 1439 })
        {
            settings.ReminderMinuteOfDay = minute;
            settings.AutoStartEnabled = minute != 0;
            context.SaveChanges();
        }

        foreach (var minute in new[] { -1, 1440 })
        {
            settings.ReminderMinuteOfDay = minute;
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            context.ChangeTracker.Clear();
            settings = context.Settings.Single();
        }

        settings.AutoStartEnabled = false;
        context.SaveChanges();
        settings.AutoStartEnabled = true;
        context.SaveChanges();

        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw(
            "UPDATE settings SET auto_start_enabled = 2 WHERE id = 1"));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw(
            "INSERT INTO settings (id, reminder_minute_of_day, auto_start_enabled) VALUES (2, 600, 1)"));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlRaw(
            "INSERT INTO app_state (id) VALUES (2)"));

        context.ChangeTracker.Clear();
        settings = context.Settings.AsNoTracking().Single();
        Assert.Equal(1439, settings.ReminderMinuteOfDay);
        Assert.True(settings.AutoStartEnabled);
        Assert.Equal(1, context.AppStates.Count());
    }

    [Fact]
    public void AppStateDatesPersistIndependentlyAndCanBeCleared()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var state = context.AppStates.Single();
        var reminderDate = new DateOnly(2026, 8, 27);
        var normalRunDate = new DateOnly(2026, 8, 28);

        state.LastReminderDate = reminderDate;
        context.SaveChanges();
        context.ChangeTracker.Clear();
        state = context.AppStates.Single();
        Assert.Equal(reminderDate, state.LastReminderDate);
        Assert.Null(state.LastNormalRunDate);

        state.LastNormalRunDate = normalRunDate;
        state.LastReminderDate = null;
        context.SaveChanges();
        context.ChangeTracker.Clear();
        state = context.AppStates.AsNoTracking().Single();
        Assert.Null(state.LastReminderDate);
        Assert.Equal(normalRunDate, state.LastNormalRunDate);

        state = context.AppStates.Single();
        state.LastNormalRunDate = null;
        context.SaveChanges();
        Assert.Null(context.AppStates.AsNoTracking().Single().LastNormalRunDate);
    }

    [Fact]
    public void UpgradeFromAddBackupMetadataPreservesAllFourteenExistingDataSets()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string addBackupMetadata;

        using (var context = database.Open())
        {
            addBackupMetadata = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddBackupMetadata", StringComparison.Ordinal));
            context.Database.Migrate(addBackupMetadata);

            var product = AddProduct(context, "SKU-UPGRADE-SETTINGS");
            var batch = AddBatch(context, product.Id);
            var task = AddTask(context, product.Id);
            var taskItem = new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id
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
                CheckDate = new DateOnly(2026, 8, 27)
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
            context.InventoryAdjustments.Add(new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = 10,
                AdjustedStockQty = 8
            });

            var import = NewImport();
            context.Imports.Add(import);
            context.SaveChanges();
            context.ImportWorkbooks.Add(new ImportWorkbook
            {
                ImportId = import.Id,
                OriginalFileName = "source.xlsx",
                Content = new byte[] { 1, 2, 3 },
                Sha256 = ValidSha256,
                SavedAtUtc = DateTime.UtcNow
            });
            context.ImportIssues.Add(new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = 1,
                IssueType = "invalid-date",
                SafeSummary = "bad date"
            });
            context.BackupRecords.Add(new BackupRecord
            {
                BackupType = "manual",
                FilePath = "backup.db",
                Sha256 = ValidSha256,
                CreatedAtUtc = DateTime.UtcNow,
                VerificationStatus = "verified"
            });
            product.LastSeenImportId = import.Id;
            batch.LastSeenImportId = import.Id;
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
            Assert.Equal("SKU-UPGRADE-SETTINGS", context.Products.AsNoTracking().Single().ProductCode);
            Assert.Equal(8, context.InventoryAdjustments.AsNoTracking().Single().AdjustedStockQty);
            Assert.Equal(ValidSha256, context.ImportWorkbooks.AsNoTracking().Single().Sha256);
            Assert.Equal("bad date", context.ImportIssues.AsNoTracking().Single().SafeSummary);
            Assert.Equal("manual", context.BackupRecords.AsNoTracking().Single().BackupType);
            Assert.Equal(
                context.Imports.AsNoTracking().Single().Id,
                context.Products.AsNoTracking().Single().LastSeenImportId);
            Assert.Equal(600, context.Settings.AsNoTracking().Single().ReminderMinuteOfDay);
            Assert.True(context.Settings.AsNoTracking().Single().AutoStartEnabled);
            Assert.Null(context.AppStates.AsNoTracking().Single().LastReminderDate);
            Assert.Null(context.AppStates.AsNoTracking().Single().LastNormalRunDate);
            Assert.Contains(
                context.Database.GetAppliedMigrations(),
                migration => migration.EndsWith("_AddSettingsAndAppState", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IncrementalMigrationScriptOnlyCreatesAndSeedsSettingsAndAppState()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using var context = database.Open();
        var migrations = context.Database.GetMigrations().ToArray();
        var fromMigration = migrations.Single(migration => migration.EndsWith("_AddBackupMetadata", StringComparison.Ordinal));
        var toMigration = migrations.Single(migration => migration.EndsWith("_AddSettingsAndAppState", StringComparison.Ordinal));
        var script = context.Database.GetService<IMigrator>().GenerateScript(fromMigration, toMigration);

        var createdTables = Regex.Matches(script, @"CREATE\s+TABLE\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "app_state", "settings" }, createdTables);
        Assert.Contains("INSERT INTO \"app_state\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO \"settings\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("600", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ef_temp_", script, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ReadIndexNames(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA index_list({tableName})";
            using var reader = command.ExecuteReader();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                names.Add(reader.GetString(1));
            }

            return names;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static Product AddProduct(StoreDbContext context, string code)
    {
        if (!SqliteTestDatabase.ReadTableColumns(context, "products").Contains("expiry_management_status"))
        {
            context.Database.ExecuteSql($"INSERT INTO products (product_code, excel_stock_qty, effective_stock_qty, lifecycle_generation) VALUES ({code}, 0, 0, 0)");
            var legacyProduct = new Product { Id = context.Database.SqlQuery<long>($"SELECT last_insert_rowid() AS Value").Single(), ProductCode = code };
            context.Attach(legacyProduct);
            return legacyProduct;
        }
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
}
