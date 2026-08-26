using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ImportPersistenceDatabaseTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ImportPersistenceSchemaHasExactColumnsChecksIndexesAndNoActionForeignKeys()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Contains(
            context.Database.GetAppliedMigrations(),
            migration => migration.EndsWith("_AddImportPersistence", StringComparison.Ordinal));

        var tables = SqliteTestDatabase.ReadSchemaNames(context, "table")
            .Where(name => !name.StartsWith("__EF", StringComparison.Ordinal) && !name.StartsWith("sqlite_", StringComparison.Ordinal))
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            new[]
            {
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
                "products",
                "task_items",
                "tasks"
            },
            tables);

        Assert.Equal(
            new[]
            {
                "id",
                "source_file_name",
                "source_file_sha256",
                "parsed_at_utc",
                "confirmed_at_utc",
                "status",
                "product_count",
                "batch_count",
                "new_product_count",
                "new_batch_count",
                "updated_batch_count",
                "issue_count",
                "unsupported_category_count",
                "new_task_product_count",
                "pre_import_snapshot_path",
                "is_undone",
                "undone_at_utc"
            },
            SqliteTestDatabase.ReadTableColumns(context, "imports"));
        Assert.Equal(
            new[] { "id", "import_id", "original_file_name", "content", "sha256", "saved_at_utc" },
            SqliteTestDatabase.ReadTableColumns(context, "import_workbooks"));
        Assert.Equal(
            new[] { "id", "import_id", "row_number", "issue_type", "field_name", "safe_summary" },
            SqliteTestDatabase.ReadTableColumns(context, "import_issues"));

        var importsSql = SqliteTestDatabase.ReadTableSql(context, "imports");
        Assert.Contains("source_file_name = trim(source_file_name)", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_file_sha256", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(source_file_sha256) = 64", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT GLOB '*[^0-9a-f]*'", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status = trim(status)", importsSql, StringComparison.OrdinalIgnoreCase);
        foreach (var countColumn in new[]
        {
            "product_count",
            "batch_count",
            "new_product_count",
            "new_batch_count",
            "updated_batch_count",
            "issue_count",
            "unsupported_category_count",
            "new_task_product_count"
        })
        {
            Assert.Contains($"{countColumn} >= 0", importsSql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("is_undone = 0", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_undone = 1", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("undone_at_utc IS NULL", importsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("undone_at_utc IS NOT NULL", importsSql, StringComparison.OrdinalIgnoreCase);

        var workbooksSql = SqliteTestDatabase.ReadTableSql(context, "import_workbooks");
        Assert.Contains("original_file_name = trim(original_file_name)", workbooksSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(content) > 0", workbooksSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(sha256) = 64", workbooksSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT GLOB '*[^0-9a-f]*'", workbooksSql, StringComparison.OrdinalIgnoreCase);

        var issuesSql = SqliteTestDatabase.ReadTableSql(context, "import_issues");
        Assert.Contains("row_number IS NULL OR row_number > 0", issuesSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("issue_type = trim(issue_type)", issuesSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safe_summary = trim(safe_summary)", issuesSql, StringComparison.OrdinalIgnoreCase);

        var indexes = SqliteTestDatabase.ReadSchemaNames(context, "index");
        Assert.Contains("IX_imports_status_confirmed_at_utc_id", indexes);
        Assert.Contains("IX_imports_source_file_sha256", indexes);
        Assert.Contains("IX_import_workbooks_import_id", indexes);
        Assert.Contains("IX_import_issues_import_id_row_number_id", indexes);
        Assert.Contains("IX_products_last_seen_import_id", indexes);
        Assert.Contains("IX_batches_last_seen_import_id", indexes);
        Assert.Contains(
            "\"status\", \"confirmed_at_utc\", \"id\"",
            SqliteTestDatabase.ReadIndexSql(context, "IX_imports_status_confirmed_at_utc_id"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "\"import_id\", \"row_number\", \"id\"",
            SqliteTestDatabase.ReadIndexSql(context, "IX_import_issues_import_id_row_number_id"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CREATE UNIQUE INDEX",
            SqliteTestDatabase.ReadIndexSql(context, "IX_import_workbooks_import_id"),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "CREATE UNIQUE INDEX",
            SqliteTestDatabase.ReadIndexSql(context, "IX_imports_source_file_sha256"),
            StringComparison.OrdinalIgnoreCase);

        foreach (var table in new[] { "import_workbooks", "import_issues", "products", "batches" })
        {
            Assert.NotEmpty(SqliteTestDatabase.ReadForeignKeyDeleteActions(context, table));
            Assert.All(
                ReadForeignKeys(context, table),
                foreignKey => Assert.Equal("NO ACTION", foreignKey.OnDelete));
        }

        Assert.Contains(
            ReadForeignKeys(context, "import_workbooks"),
            foreignKey => foreignKey.From == "import_id" && foreignKey.Table == "imports" && foreignKey.To == "id");
        Assert.Contains(
            ReadForeignKeys(context, "import_issues"),
            foreignKey => foreignKey.From == "import_id" && foreignKey.Table == "imports" && foreignKey.To == "id");
        Assert.Contains(
            ReadForeignKeys(context, "products"),
            foreignKey => foreignKey.From == "last_seen_import_id" && foreignKey.Table == "imports" && foreignKey.To == "id");
        Assert.Contains(
            ReadForeignKeys(context, "batches"),
            foreignKey => foreignKey.From == "last_seen_import_id" && foreignKey.Table == "imports" && foreignKey.To == "id");
    }

    [Fact]
    public void ImportRecordsNormalizeValidTextAndRejectInvalidShaCountsAndUndoFields()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var import = NewImport("  source.xlsx  ", $"  {ValidSha256}  ", "  future-status  ");
        context.Imports.Add(import);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var saved = context.Imports.AsNoTracking().Single();
        Assert.Equal("source.xlsx", saved.SourceFileName);
        Assert.Equal(ValidSha256, saved.SourceFileSha256);
        Assert.Equal("future-status", saved.Status);
        Assert.Null(saved.ConfirmedAtUtc);
        Assert.False(saved.IsUndone);
        Assert.Null(saved.UndoneAtUtc);

        var sameHash = NewImport("second.xlsx", ValidSha256, "parsed");
        context.Imports.Add(sameHash);
        context.SaveChanges();
        Assert.Equal(2, context.Imports.Count());

        foreach (var sha in new[]
        {
            new string('a', 63),
            new string('a', 65),
            new string('A', 64),
            ValidSha256[..63] + "g"
        })
        {
            AssertRejected(context, NewImport("invalid-sha.xlsx", sha, "parsed"));
        }

        AssertRejected(context, NewImport(" ", ValidSha256, "parsed"));
        AssertRejected(context, NewImport("source.xlsx", ValidSha256, " "));

        var negativeCountSetters = new Action<ImportRecord>[]
        {
            value => value.ProductCount = -1,
            value => value.BatchCount = -1,
            value => value.NewProductCount = -1,
            value => value.NewBatchCount = -1,
            value => value.UpdatedBatchCount = -1,
            value => value.IssueCount = -1,
            value => value.UnsupportedCategoryCount = -1,
            value => value.NewTaskProductCount = -1
        };
        foreach (var setNegative in negativeCountSetters)
        {
            var candidate = NewImport("negative-count.xlsx", ValidSha256, "parsed");
            setNegative(candidate);
            AssertRejected(context, candidate);
        }

        var undoneWithoutTime = NewImport("undone-without-time.xlsx", ValidSha256, "parsed");
        undoneWithoutTime.IsUndone = true;
        AssertRejected(context, undoneWithoutTime);

        var timeWithoutUndone = NewImport("time-without-undone.xlsx", ValidSha256, "parsed");
        timeWithoutUndone.UndoneAtUtc = DateTime.UtcNow;
        AssertRejected(context, timeWithoutUndone);

        var undone = NewImport("undone.xlsx", ValidSha256, "parsed");
        undone.IsUndone = true;
        undone.UndoneAtUtc = DateTime.UtcNow;
        context.Imports.Add(undone);
        context.SaveChanges();
        Assert.True(context.Imports.AsNoTracking().Single(value => value.Id == undone.Id).IsUndone);
    }

    [Fact]
    public void WorkbooksAndIssuesEnforceContentHashTextRowAndRelationshipRules()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = NewImport();
        context.Imports.Add(import);
        context.SaveChanges();

        var workbook = new ImportWorkbook
        {
            ImportId = import.Id,
            OriginalFileName = "  original.xlsx  ",
            Content = new byte[] { 1, 2, 3 },
            Sha256 = $"  {ValidSha256}  ",
            SavedAtUtc = DateTime.UtcNow
        };
        context.ImportWorkbooks.Add(workbook);
        context.ImportIssues.AddRange(
            new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = 2,
                IssueType = "  invalid-date  ",
                FieldName = "expiry_date",
                SafeSummary = "  bad date  "
            },
            new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = null,
                IssueType = "file-error",
                SafeSummary = "file could not be parsed"
            },
            new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = 1,
                IssueType = "missing-column",
                SafeSummary = "column is missing"
            },
            new ImportIssue
            {
                ImportId = import.Id,
                RowNumber = 2,
                IssueType = "duplicate-row",
                SafeSummary = "row is duplicated"
            });
        context.SaveChanges();

        var savedWorkbook = context.ImportWorkbooks.AsNoTracking().Single();
        Assert.Equal("original.xlsx", savedWorkbook.OriginalFileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, savedWorkbook.Content);
        Assert.Equal(ValidSha256, savedWorkbook.Sha256);

        var sortedIssues = context.ImportIssues.AsNoTracking()
            .OrderBy(issue => issue.RowNumber)
            .ThenBy(issue => issue.Id)
            .ToArray();
        Assert.Equal(new int?[] { null, 1, 2, 2 }, sortedIssues.Select(issue => issue.RowNumber));
        Assert.Equal("bad date", sortedIssues[2].SafeSummary);
        Assert.Equal("duplicate-row", sortedIssues[3].IssueType);
        Assert.Null(sortedIssues[0].FieldName);

        AssertRejected(context, new ImportWorkbook
        {
            ImportId = import.Id,
            OriginalFileName = "second.xlsx",
            Content = new byte[] { 1 },
            Sha256 = ValidSha256,
            SavedAtUtc = DateTime.UtcNow
        });
        AssertRejected(context, new ImportWorkbook
        {
            ImportId = 999,
            OriginalFileName = "missing-import.xlsx",
            Content = new byte[] { 1 },
            Sha256 = ValidSha256,
            SavedAtUtc = DateTime.UtcNow
        });
        AssertRejected(context, new ImportWorkbook
        {
            ImportId = import.Id,
            OriginalFileName = " ",
            Content = new byte[] { 1 },
            Sha256 = ValidSha256,
            SavedAtUtc = DateTime.UtcNow
        });
        AssertRejected(context, new ImportWorkbook
        {
            ImportId = import.Id,
            OriginalFileName = "empty.xlsx",
            Content = Array.Empty<byte>(),
            Sha256 = ValidSha256,
            SavedAtUtc = DateTime.UtcNow
        });
        foreach (var sha in new[] { new string('a', 63), new string('A', 64), ValidSha256 + "0" })
        {
            AssertRejected(context, new ImportWorkbook
            {
                ImportId = import.Id,
                OriginalFileName = "invalid-hash.xlsx",
                Content = new byte[] { 1 },
                Sha256 = sha,
                SavedAtUtc = DateTime.UtcNow
            });
        }

        AssertRejected(context, new ImportIssue
        {
            ImportId = import.Id,
            RowNumber = 0,
            IssueType = "row-error",
            SafeSummary = "summary"
        });
        AssertRejected(context, new ImportIssue
        {
            ImportId = import.Id,
            RowNumber = -1,
            IssueType = "row-error",
            SafeSummary = "summary"
        });
        AssertRejected(context, new ImportIssue
        {
            ImportId = 999,
            RowNumber = 1,
            IssueType = "row-error",
            SafeSummary = "summary"
        });
        AssertRejected(context, new ImportIssue
        {
            ImportId = import.Id,
            RowNumber = 1,
            IssueType = " ",
            SafeSummary = "summary"
        });
        AssertRejected(context, new ImportIssue
        {
            ImportId = import.Id,
            RowNumber = 1,
            IssueType = "row-error",
            SafeSummary = " "
        });

        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated(
            $"DELETE FROM imports WHERE id = {import.Id}"));
        Assert.Equal(1, context.ImportWorkbooks.Count());
        Assert.Equal(4, context.ImportIssues.Count());
    }

    [Fact]
    public void LastSeenImportIdsRemainNullableAndOnlyReferenceExistingImports()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-LAST-SEEN");
        var productId = product.Id;
        var batch = AddBatch(context, product.Id);

        Assert.Null(context.Products.AsNoTracking().Single().LastSeenImportId);
        Assert.Null(context.Batches.AsNoTracking().Single().LastSeenImportId);

        var import = NewImport();
        context.Imports.Add(import);
        context.SaveChanges();
        product.LastSeenImportId = import.Id;
        batch.LastSeenImportId = import.Id;
        context.SaveChanges();

        context.ChangeTracker.Clear();
        Assert.Equal(import.Id, context.Products.AsNoTracking().Single().LastSeenImportId);
        Assert.Equal(import.Id, context.Batches.AsNoTracking().Single().LastSeenImportId);

        var invalidProduct = AddProduct(context, "SKU-INVALID-LAST-SEEN");
        invalidProduct.LastSeenImportId = 999;
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        AssertRejected(context, new Batch
        {
            ProductId = product.Id,
            ExpiryDate = new DateOnly(2026, 12, 31),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 1,
            MaxArrivalQty = 1,
            LastSeenImportId = 999
        });

        AssertSqliteRejected(() => context.Database.ExecuteSqlInterpolated(
            $"DELETE FROM imports WHERE id = {import.Id}"));
        Assert.Equal(1, context.Imports.Count());
        Assert.Equal(import.Id, context.Products.AsNoTracking().Single(value => value.Id == productId).LastSeenImportId);
        Assert.Equal(import.Id, context.Batches.AsNoTracking().Single().LastSeenImportId);
    }

    [Fact]
    public void UpgradeFromAddInventoryAdjustmentsPreservesAllTenExistingDataSets()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        string addInventoryAdjustments;

        using (var context = database.Open())
        {
            addInventoryAdjustments = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddInventoryAdjustments", StringComparison.Ordinal));
            context.Database.Migrate(addInventoryAdjustments);

            var product = AddProduct(context, "SKU-UPGRADE-IMPORT");
            var batch = AddBatch(context, product.Id);
            var task = new ProductTask { ProductId = product.Id };
            context.Tasks.Add(task);
            context.SaveChanges();
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
                ExpiryDateSnapshot = batch.ExpiryDate,
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
            Assert.Equal("SKU-UPGRADE-IMPORT", context.Products.AsNoTracking().Single().ProductCode);
            Assert.Equal(8, context.InventoryAdjustments.AsNoTracking().Single().AdjustedStockQty);
            Assert.Contains(
                context.Database.GetAppliedMigrations(),
                migration => migration.EndsWith("_AddImportPersistence", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IncrementalMigrationOnlyCreatesImportTablesAndRebuildsProductsAndBatches()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using var context = database.Open();
        var migrations = context.Database.GetMigrations().ToArray();
        var fromMigration = migrations.Single(migration => migration.EndsWith("_AddInventoryAdjustments", StringComparison.Ordinal));
        var toMigration = migrations.Single(migration => migration.EndsWith("_AddImportPersistence", StringComparison.Ordinal));
        var script = context.Database.GetService<IMigrator>().GenerateScript(fromMigration, toMigration);

        var createdTables = Regex.Matches(script, @"CREATE\s+TABLE\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(
            new[] { "imports", "import_issues", "import_workbooks", "ef_temp_batches", "ef_temp_products" },
            createdTables);

        var droppedTables = Regex.Matches(script, @"DROP\s+TABLE\s+""([^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "batches", "products" }, droppedTables);

        foreach (var table in new[] { "tasks", "task_items", "drafts", "draft_items", "inspections", "inspection_items", "inspection_item_revisions", "inventory_adjustments" })
        {
            Assert.DoesNotContain($"DROP TABLE \"{table}\"", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"ef_temp_{table}", script, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("FK_products_imports_last_seen_import_id", script, StringComparison.Ordinal);
        Assert.Contains("FK_batches_imports_last_seen_import_id", script, StringComparison.Ordinal);
        Assert.Contains("IX_imports_status_confirmed_at_utc_id", script, StringComparison.Ordinal);
        Assert.Contains("IX_imports_source_file_sha256", script, StringComparison.Ordinal);
        Assert.Contains("IX_import_workbooks_import_id", script, StringComparison.Ordinal);
        Assert.Contains("IX_import_issues_import_id_row_number_id", script, StringComparison.Ordinal);
    }

    private static ImportRecord NewImport(
        string sourceFileName = "source.xlsx",
        string sourceFileSha256 = ValidSha256,
        string status = "parsed")
    {
        return new ImportRecord
        {
            SourceFileName = sourceFileName,
            SourceFileSha256 = sourceFileSha256,
            ParsedAtUtc = DateTime.UtcNow,
            Status = status
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

    private static void AssertRejected(StoreDbContext context, object entity)
    {
        context.Add(entity);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
    }

    private static void AssertSqliteRejected(Action action)
    {
        Assert.Throws<SqliteException>(action);
    }

    private static List<(string Table, string From, string To, string OnDelete)> ReadForeignKeys(
        StoreDbContext context,
        string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list({tableName})";
            using var reader = command.ExecuteReader();
            var foreignKeys = new List<(string Table, string From, string To, string OnDelete)>();
            while (reader.Read())
            {
                foreignKeys.Add((
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(6)));
            }

            return foreignKeys;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }
}
