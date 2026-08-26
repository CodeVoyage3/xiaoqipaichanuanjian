using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class BackupMetadataDatabaseTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void BackupMetadataSchemaHasExactColumnsChecksAndStableNonUniqueIndex()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Contains(
            context.Database.GetAppliedMigrations(),
            migration => migration.EndsWith("_AddBackupMetadata", StringComparison.Ordinal));

        var tables = SqliteTestDatabase.ReadSchemaNames(context, "table")
            .Where(name => !name.StartsWith("__EF", StringComparison.Ordinal) && !name.StartsWith("sqlite_", StringComparison.Ordinal))
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "app_state",
                "backups",
                "batches",
                "draft_items",
                "drafts",
                "import_issues",
                "import_workbooks",
                "imports",
                "inspection_item_revisions",
                "inspection_items",
                "inspections",
                "inventory_adjustments",
                "lifecycle_events",
                "products",
                "settings",
                "task_items",
                "tasks"
            },
            tables);
        Assert.Equal(
            new[] { "id", "backup_type", "file_path", "sha256", "created_at_utc", "verification_status" },
            SqliteTestDatabase.ReadTableColumns(context, "backups"));

        var tableSql = SqliteTestDatabase.ReadTableSql(context, "backups");
        Assert.Contains(
            "backup_type IN ('auto', 'manual', 'pre_import', 'pre_restore', 'pre_upgrade')",
            tableSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file_path = trim(file_path)", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verification_status = trim(verification_status)", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(sha256) = 64", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT GLOB '*[^0-9a-f]*'", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(SqliteTestDatabase.ReadForeignKeyDeleteActions(context, "backups"));

        const string indexName = "IX_backups_backup_type_created_at_utc_id";
        Assert.Contains(indexName, SqliteTestDatabase.ReadSchemaNames(context, "index"));
        var indexSql = SqliteTestDatabase.ReadIndexSql(context, indexName);
        Assert.Contains(
            "\"backup_type\", \"created_at_utc\", \"id\"",
            indexSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", indexSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackupRecordsNormalizeTextAcceptFiveTypesAndSortStableDuplicates()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var createdAtUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        var records = new[]
        {
            NewBackup("auto", "  auto.db  ", $"  {ValidSha256}  ", createdAtUtc, "  pending  "),
            NewBackup("manual", "same.db", ValidSha256, createdAtUtc, "verified"),
            NewBackup("pre_import", "import.db", ValidSha256, createdAtUtc, "future-status"),
            NewBackup("pre_restore", "restore.db", ValidSha256, createdAtUtc, "verified"),
            NewBackup("pre_upgrade", "upgrade.db", ValidSha256, createdAtUtc, "verified"),
            NewBackup("manual", "same.db", ValidSha256, createdAtUtc, "verified")
        };
        context.BackupRecords.AddRange(records);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var saved = context.BackupRecords.AsNoTracking().OrderBy(record => record.Id).ToArray();
        Assert.Equal(6, saved.Length);
        Assert.Equal(
            new[] { "auto", "manual", "pre_import", "pre_restore", "pre_upgrade", "manual" },
            saved.Select(record => record.BackupType));
        Assert.Equal("auto.db", saved[0].FilePath);
        Assert.Equal(ValidSha256, saved[0].Sha256);
        Assert.Equal("pending", saved[0].VerificationStatus);
        Assert.Equal(2, saved.Count(record => record.FilePath == "same.db" && record.Sha256 == ValidSha256));

        var manualRecords = context.BackupRecords.AsNoTracking()
            .Where(record => record.BackupType == "manual")
            .OrderBy(record => record.BackupType)
            .ThenBy(record => record.CreatedAtUtc)
            .ThenBy(record => record.Id)
            .ToArray();
        Assert.Equal(new[] { records[1].Id, records[5].Id }, manualRecords.Select(record => record.Id));
    }

    [Fact]
    public void BackupRecordsRejectUnknownTypesBlankTextAndInvalidSha256()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        AssertRejected(context, NewBackup("scheduled"));
        AssertRejected(context, NewBackup(" auto "));

        foreach (var path in new[] { "", " ", "\t", "\r\n" })
        {
            AssertRejected(context, NewBackup(filePath: path));
        }

        foreach (var status in new[] { "", " ", "\t", "\r\n" })
        {
            AssertRejected(context, NewBackup(verificationStatus: status));
        }

        foreach (var sha256 in new[]
        {
            new string('a', 63),
            new string('a', 65),
            new string('A', 64),
            ValidSha256[..63] + "g"
        })
        {
            AssertRejected(context, NewBackup(sha256: sha256));
        }

        AssertRawRejected(context, "scheduled", "backup.db", ValidSha256, "verified");
        AssertRawRejected(context, "auto", " ", ValidSha256, "verified");
        AssertRawRejected(context, "auto", "backup.db", new string('A', 64), "verified");
        AssertRawRejected(context, "auto", "backup.db", ValidSha256, " ");
    }

    [Fact]
    public void UpgradeFromAddImportPersistencePreservesAllThirteenExistingDataSets()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string addImportPersistence;

        using (var context = database.Open())
        {
            addImportPersistence = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddImportPersistence", StringComparison.Ordinal));
            context.Database.Migrate(addImportPersistence);

            var product = AddProduct(context, "SKU-UPGRADE-BACKUP");
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
            Assert.Equal("SKU-UPGRADE-BACKUP", context.Products.AsNoTracking().Single().ProductCode);
            Assert.Equal(8, context.InventoryAdjustments.AsNoTracking().Single().AdjustedStockQty);
            Assert.Equal(ValidSha256, context.ImportWorkbooks.AsNoTracking().Single().Sha256);
            Assert.Equal("bad date", context.ImportIssues.AsNoTracking().Single().SafeSummary);
            Assert.Equal(
                context.Imports.AsNoTracking().Single().Id,
                context.Products.AsNoTracking().Single().LastSeenImportId);
            Assert.Contains(
                context.Database.GetAppliedMigrations(),
                migration => migration.EndsWith("_AddBackupMetadata", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IncrementalMigrationScriptOnlyCreatesBackupsTableAndIndex()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using var context = database.Open();
        var migrations = context.Database.GetMigrations().ToArray();
        var fromMigration = migrations.Single(migration => migration.EndsWith("_AddImportPersistence", StringComparison.Ordinal));
        var toMigration = migrations.Single(migration => migration.EndsWith("_AddBackupMetadata", StringComparison.Ordinal));
        var script = context.Database.GetService<IMigrator>().GenerateScript(fromMigration, toMigration);

        var createdTables = Regex.Matches(script, @"CREATE\s+TABLE\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "backups" }, createdTables);

        var createdIndexes = Regex.Matches(script, @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "IX_backups_backup_type_created_at_utc_id" }, createdIndexes);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ef_temp_", script, StringComparison.OrdinalIgnoreCase);
    }

    private static BackupRecord NewBackup(
        string backupType = "auto",
        string filePath = "backup.db",
        string sha256 = ValidSha256,
        DateTime? createdAtUtc = null,
        string verificationStatus = "verified")
    {
        return new BackupRecord
        {
            BackupType = backupType,
            FilePath = filePath,
            Sha256 = sha256,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            VerificationStatus = verificationStatus
        };
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

    private static void AssertRejected(StoreDbContext context, BackupRecord record)
    {
        context.BackupRecords.Add(record);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertRawRejected(
        StoreDbContext context,
        string backupType,
        string filePath,
        string sha256,
        string verificationStatus)
    {
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated(
            $"INSERT INTO backups (backup_type, file_path, sha256, created_at_utc, verification_status) VALUES ({backupType}, {filePath}, {sha256}, {DateTime.UtcNow}, {verificationStatus})"));
    }
}
