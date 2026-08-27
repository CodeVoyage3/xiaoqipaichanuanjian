using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;
using Xunit;

namespace StoreExpiryInspector.Tests;

[CollectionDefinition("ImportUndoEligibilitySqlite", DisableParallelization = true)]
public sealed class ImportUndoEligibilitySqliteCollection
{
}

[Collection("ImportUndoEligibilitySqlite")]
public sealed class ImportUndoEligibilityTests
{
    private static readonly DateTime ConfirmationTime =
        new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    private static readonly string[] BusinessTables =
    {
        "products",
        "batches",
        "tasks",
        "task_items",
        "drafts",
        "draft_items",
        "inspections",
        "inspection_items",
        "inspection_item_revisions",
        "inventory_adjustments",
        "imports",
        "import_workbooks",
        "import_issues",
        "backups",
        "settings",
        "app_state",
        "lifecycle_events"
    };

    [Fact]
    public void NoImportReturnsNoCandidateWithoutThrowing()
    {
        using var database = SqliteTestDatabase.Create();

        using var context = database.Open();
        var result = new ImportUndoEligibilityService().Check(context);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.NoCandidate, result.Code);
        Assert.Null(result.CandidateImportId);
        Assert.Null(result.ConfirmedAtUtc);
        Assert.Empty(result.BlockingTables);
    }

    [Fact]
    public void LatestCandidateUsesConfirmedAtThenIdAndDoesNotAcceptAnImportId()
    {
        using var database = SqliteTestDatabase.Create();
        var first = CreateCandidate(database, ConfirmationTime.AddHours(-2));
        var second = CreateCandidate(database, ConfirmationTime);
        var third = CreateCandidate(database, ConfirmationTime);

        using var context = database.Open();
        var result = new ImportUndoEligibilityService().Check(context);

        Assert.True(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.Eligible, result.Code);
        Assert.Equal(third.ImportId, result.CandidateImportId);
        Assert.NotEqual(first.ImportId, result.CandidateImportId);
        Assert.NotEqual(second.ImportId, result.CandidateImportId);
        Assert.Equal(ConfirmationTime, result.ConfirmedAtUtc);
        Assert.DoesNotContain(
            typeof(ImportUndoEligibilityService).GetMethods(),
            method => method.Name is "Check" or "Evaluate" &&
                      method.GetParameters().Any(parameter => parameter.ParameterType == typeof(long)));
    }

    [Fact]
    public void CandidateFilterIgnoresUndoneAndUndoneStatusEvenWhenTheyAreNewest()
    {
        using var database = SqliteTestDatabase.Create();
        var eligible = CreateCandidate(database, ConfirmationTime.AddHours(-2));
        CreateCandidate(database, ConfirmationTime.AddHours(-1), status: ImportStatuses.Undone);
        CreateCandidate(database, ConfirmationTime, isUndone: true);

        using (var context = database.Open())
        {
            var result = new ImportUndoEligibilityService().Check(context);

            Assert.True(result.CanUndo);
            Assert.Equal(ImportUndoEligibilityCodes.Eligible, result.Code);
            Assert.Equal(eligible.ImportId, result.CandidateImportId);
        }

        using (var context = database.Open())
        {
            var import = context.Imports.Single(item => item.Id == eligible.ImportId);
            import.IsUndone = true;
            import.UndoneAtUtc = ConfirmationTime.AddMinutes(1);
            context.SaveChanges();
        }

        using var verify = database.Open();
        var noCandidate = new ImportUndoEligibilityService().Check(verify);

        Assert.False(noCandidate.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.NoCandidate, noCandidate.Code);
        Assert.Null(noCandidate.CandidateImportId);
    }

    [Fact]
    public void EligibleCandidateUsesUniqueVerifiedOriginalSnapshot()
    {
        using var database = SqliteTestDatabase.Create();
        var candidate = CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            var result = new ImportUndoEligibilityService().Check(context);

            Assert.True(result.CanUndo);
            Assert.Equal(ImportUndoEligibilityCodes.Eligible, result.Code);
            Assert.Equal(candidate.ImportId, result.CandidateImportId);
            Assert.Equal(candidate.SnapshotPath, result.SnapshotPath);
            Assert.Equal(candidate.Sha256, result.SnapshotSha256);
            Assert.Equal(candidate.BackupId, result.BackupRecordId);
            Assert.Empty(result.BlockingTables);
        }

        Assert.Equal(candidate.Sha256, Sha256(candidate.SnapshotPath));
    }

    [Fact]
    public void MissingSnapshotPathOrFileIsNotEligible()
    {
        using var database = SqliteTestDatabase.Create();
        var missingPath = Path.Combine(database.Directory, "missing.db");
        var emptyPathCandidate = CreateCandidate(database, ConfirmationTime, snapshotPath: null);
        var missingFileCandidate = CreateCandidate(database, ConfirmationTime.AddMinutes(1), missingPath);

        using var context = database.Open();
        context.ChangeTracker.Clear();
        var result = new ImportUndoEligibilityService().Check(context);

        Assert.Equal(missingFileCandidate.ImportId, result.CandidateImportId);
        Assert.Equal(ImportUndoEligibilityCodes.SnapshotMissing, result.Code);
        Assert.False(result.CanUndo);
        Assert.Equal(string.Empty, emptyPathCandidate.SnapshotPath);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("wrong_type")]
    [InlineData("wrong_status")]
    [InlineData("late")]
    [InlineData("wrong_path")]
    public void InvalidSnapshotAssociationIsConservativelyRejected(string mode)
    {
        using var database = SqliteTestDatabase.Create();
        var candidate = CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            var backup = context.BackupRecords.Single();
            switch (mode)
            {
                case "duplicate":
                    context.BackupRecords.Add(new BackupRecord
                    {
                        BackupType = "pre_import",
                        FilePath = backup.FilePath,
                        Sha256 = backup.Sha256,
                        CreatedAtUtc = backup.CreatedAtUtc,
                        VerificationStatus = "verified"
                    });
                    break;
                case "wrong_type":
                    backup.BackupType = "manual";
                    break;
                case "wrong_status":
                    backup.VerificationStatus = "pending";
                    break;
                case "late":
                    backup.CreatedAtUtc = ConfirmationTime.AddMinutes(1);
                    break;
                case "wrong_path":
                    backup.FilePath = Path.Combine(database.Directory, "another.db");
                    break;
            }

            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(candidate.ImportId, result.CandidateImportId);
        Assert.Equal(ImportUndoEligibilityCodes.SnapshotAssociationInvalid, result.Code);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("sha")]
    [InlineData("not_sqlite")]
    public void InvalidSnapshotContentIsRejected(string mode)
    {
        using var database = SqliteTestDatabase.Create();
        var candidate = CreateCandidate(database, ConfirmationTime);

        if (mode is "bytes" or "not_sqlite")
        {
            File.WriteAllBytes(candidate.SnapshotPath, mode == "bytes" ? new byte[] { 1, 2, 3 } : "not sqlite"u8.ToArray());
        }
        else
        {
            using var context = database.Open();
            context.BackupRecords.Single().Sha256 = new string('b', 64);
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SnapshotInvalid, result.Code);
    }

    [Theory]
    [InlineData("alter_table")]
    [InlineData("migration_history")]
    public void OpenableSnapshotWithAlteredRequiredSchemaOrMigrationsIsRejectedAfterShaUpdate(string mode)
    {
        using var database = SqliteTestDatabase.Create();
        var candidate = CreateCandidate(database, ConfirmationTime);
        var sql = mode == "alter_table"
            ? "ALTER TABLE \"tasks\" RENAME COLUMN \"updated_at_utc\" TO \"updated_at_corrupted\";"
            : "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826170403_AddLifecycleEvents';";

        var updatedSha = MutateSnapshotAndSyncBackupSha(database, candidate, sql);
        Assert.NotEqual(candidate.Sha256, updatedSha);
        Assert.Equal(1L, ReadScalarFromSnapshot(candidate.SnapshotPath, "SELECT 1;"));

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SnapshotInvalid, result.Code);
        Assert.Equal(updatedSha, result.SnapshotSha256);
    }

    [Fact]
    public void ExistingBusinessRowsInTheSnapshotDoNotBlockWhenUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using var context = database.Open();
        var result = new ImportUndoEligibilityService().Check(context);

        Assert.True(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.Eligible, result.Code);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("task_items")]
    [InlineData("inspections")]
    [InlineData("inspection_items")]
    [InlineData("inspection_item_revisions")]
    [InlineData("inventory_adjustments")]
    [InlineData("lifecycle_events")]
    [InlineData("drafts")]
    [InlineData("draft_items")]
    public void EveryFixedBusinessTableAdditionBlocks(string table)
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            AddBusinessRow(context, table);
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SubsequentBusinessChanges, result.Code);
        Assert.Contains(table, result.BlockingTables);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("task_items")]
    [InlineData("inspections")]
    [InlineData("inspection_items")]
    [InlineData("inspection_item_revisions")]
    [InlineData("inventory_adjustments")]
    [InlineData("lifecycle_events")]
    [InlineData("drafts")]
    [InlineData("draft_items")]
    public void EveryFixedBusinessTableModificationBlocks(string table)
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            ModifyBusinessRow(context, table);
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SubsequentBusinessChanges, result.Code);
        Assert.Contains(table, result.BlockingTables);
    }

    [Fact]
    public void DeletingARevisionAndDraftItemBlocks()
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            context.InspectionItemRevisions.Remove(context.InspectionItemRevisions.Single(revision => revision.PreviousCheckedQty == 1));
            context.DraftItems.Remove(context.DraftItems.Single());
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SubsequentBusinessChanges, result.Code);
        Assert.Contains("inspection_item_revisions", result.BlockingTables);
        Assert.Contains("draft_items", result.BlockingTables);
    }

    [Fact]
    public void DeletingAnExistingDraftBlocks()
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            context.Drafts.Remove(context.Drafts.Single(draft =>
                draft.TaskId == context.Tasks.Single(task =>
                    task.ProductId == context.Products.Single(product => product.ProductCode == "P2").Id).Id));
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SubsequentBusinessChanges, result.Code);
        Assert.Contains("drafts", result.BlockingTables);
    }

    [Fact]
    public void ProductAndBatchChangedAfterConfirmationBlock()
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            context.Products.Single(product => product.ProductCode == "P1").UpdatedAtUtc = ConfirmationTime.AddMinutes(1);
            context.Batches.Single(batch => batch.ProductId == context.Products.Single(product => product.ProductCode == "P1").Id)
                .UpdatedAtUtc = ConfirmationTime.AddMinutes(1);
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SubsequentBusinessChanges, result.Code);
        Assert.Contains("products", result.BlockingTables);
        Assert.Contains("batches", result.BlockingTables);
    }

    [Fact]
    public void ProductAndBatchWrittenAtConfirmationTimeDoNotBlock()
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-CONFIRM",
                CreatedAtUtc = ConfirmationTime,
                UpdatedAtUtc = ConfirmationTime
            };
            context.Products.Add(product);
            context.SaveChanges();
            context.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ExpiryDate = new DateOnly(2027, 12, 31),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                CreatedAtUtc = ConfirmationTime,
                UpdatedAtUtc = ConfirmationTime
            });
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.True(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.Eligible, result.Code);
    }

    [Fact]
    public void ImportInfrastructureDifferencesDoNotBlockAndQualificationDoesNotTrackOrWrite()
    {
        using var database = SqliteTestDatabase.Create();
        var candidate = CreateCandidate(database, ConfirmationTime);
        var beforeCounts = ReadTableCounts(database.Path);
        var beforeSnapshotSha = Sha256(candidate.SnapshotPath);

        using var context = database.Open();
        var result = new ImportUndoEligibilityService().Check(context);

        Assert.True(result.CanUndo);
        Assert.Equal(beforeCounts, ReadTableCounts(database.Path));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(beforeSnapshotSha, Sha256(candidate.SnapshotPath));
    }

    [Fact]
    public void PostSnapshotImportInfrastructureAndCroppedOldWorkbookDoNotBlock()
    {
        using var database = SqliteTestDatabase.Create();
        var old = CreateCandidate(database, ConfirmationTime.AddHours(-2));
        var latest = CreateCandidate(database, ConfirmationTime);
        var workbookContent = new byte[] { 1, 2, 3, 4 };

        using (var context = database.Open())
        {
            var oldWorkbook = new ImportWorkbook
            {
                ImportId = old.ImportId,
                OriginalFileName = "old.xlsx",
                Content = workbookContent,
                Sha256 = Sha256(workbookContent),
                SavedAtUtc = ConfirmationTime.AddHours(-1)
            };
            context.ImportWorkbooks.Add(oldWorkbook);
            context.SaveChanges();
            context.ImportWorkbooks.Remove(oldWorkbook);

            context.ImportWorkbooks.Add(new ImportWorkbook
            {
                ImportId = latest.ImportId,
                OriginalFileName = "latest.xlsx",
                Content = workbookContent,
                Sha256 = Sha256(workbookContent),
                SavedAtUtc = ConfirmationTime.AddMinutes(1)
            });
            context.ImportIssues.Add(new ImportIssue
            {
                ImportId = latest.ImportId,
                RowNumber = 2,
                IssueType = "unsupported_category",
                FieldName = "category",
                SafeSummary = "category is not supported"
            });
            context.BackupRecords.Add(new BackupRecord
            {
                BackupType = "manual",
                FilePath = Path.Combine(database.Directory, "manual.db"),
                Sha256 = new string('c', 64),
                CreatedAtUtc = ConfirmationTime.AddMinutes(2),
                VerificationStatus = "verified"
            });
            context.SaveChanges();
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.True(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.Eligible, result.Code);
        Assert.Equal(latest.ImportId, result.CandidateImportId);
        Assert.Empty(verify.ImportWorkbooks.Where(workbook => workbook.ImportId == old.ImportId));
        Assert.Single(verify.ImportWorkbooks.Where(workbook => workbook.ImportId == latest.ImportId));
        Assert.Single(verify.ImportIssues.Where(issue => issue.ImportId == latest.ImportId));
        Assert.Contains(verify.BackupRecords, backup => backup.BackupType == "manual");
    }

    [Fact]
    public void QualificationPreservesAllSeventeenBusinessTableFingerprintsAndSnapshot()
    {
        using var database = SqliteTestDatabase.Create();
        SeedBusinessGraph(database, ConfirmationTime);
        var candidate = CreateCandidate(database, ConfirmationTime);
        var before = ReadTableFingerprints(database.Path);
        var beforeSnapshotSha = Sha256(candidate.SnapshotPath);

        using var context = database.Open();
        var result = new ImportUndoEligibilityService().Check(context);
        var after = ReadTableFingerprints(database.Path);

        Assert.True(result.CanUndo);
        Assert.Equal(17, before.Count);
        Assert.Equal(before, after);
        Assert.Equal(beforeSnapshotSha, Sha256(candidate.SnapshotPath));
        Assert.DoesNotContain(
            context.ChangeTracker.Entries(),
            entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public void CurrentSchemaMismatchReturnsSnapshotInvalid()
    {
        using var database = SqliteTestDatabase.Create();
        CreateCandidate(database, ConfirmationTime);

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
            context.Database.ExecuteSqlRaw("DROP TABLE tasks;");
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        }

        using var verify = database.Open();
        var result = new ImportUndoEligibilityService().Check(verify);

        Assert.False(result.CanUndo);
        Assert.Equal(ImportUndoEligibilityCodes.SnapshotInvalid, result.Code);
    }

    private static ImportCandidate CreateCandidate(
        SqliteTestDatabase database,
        DateTime confirmedAtUtc,
        string? snapshotPath = "default",
        string status = ImportStatuses.Succeeded,
        bool isUndone = false)
    {
        var snapshotDirectory = Path.Combine(database.Directory, "snapshots");
        Directory.CreateDirectory(snapshotDirectory);
        string? actualSnapshotPath = null;
        string sha256 = string.Empty;
        if (snapshotPath == "default")
        {
            var snapshot = new PreImportSnapshotService().Create(database.Path, snapshotDirectory);
            var metadata = Assert.IsType<PreImportSnapshotMetadata>(snapshot.Metadata);
            actualSnapshotPath = metadata.SnapshotPath;
            sha256 = metadata.Sha256;
        }
        else if (snapshotPath is not null)
        {
            actualSnapshotPath = Path.GetFullPath(snapshotPath);
            sha256 = new string('a', 64);
        }

        using var context = database.Open();
        var import = new ImportRecord
        {
            SourceFileName = "import.xlsx",
            SourceFileSha256 = new string('a', 64),
            ParsedAtUtc = confirmedAtUtc.AddHours(-1),
            ConfirmedAtUtc = confirmedAtUtc,
            Status = status,
            PreImportSnapshotPath = actualSnapshotPath,
            IsUndone = isUndone,
            UndoneAtUtc = isUndone ? confirmedAtUtc.AddMinutes(1) : null
        };
        context.Imports.Add(import);
        context.SaveChanges();
        if (actualSnapshotPath is not null)
        {
            context.BackupRecords.Add(new BackupRecord
            {
                BackupType = "pre_import",
                FilePath = actualSnapshotPath,
                Sha256 = sha256,
                CreatedAtUtc = confirmedAtUtc.AddMinutes(-1),
                VerificationStatus = "verified"
            });
            context.SaveChanges();
        }

        return new ImportCandidate(import.Id, actualSnapshotPath ?? string.Empty, sha256, context.BackupRecords.Max(record => (long?)record.Id) ?? 0);
    }

    private static void SeedBusinessGraph(SqliteTestDatabase database, DateTime atUtc)
    {
        var old = atUtc.AddHours(-1);
        using var context = database.Open();
        var products = Enumerable.Range(1, 5).Select(index => new Product
        {
            ProductCode = $"P{index}",
            CurrentName = $"Product {index}",
            CreatedAtUtc = old,
            UpdatedAtUtc = old
        }).ToArray();
        context.Products.AddRange(products);
        context.SaveChanges();

        var batches = new[]
        {
            NewBatch(products[0], 1, old),
            NewBatch(products[1], 2, old),
            NewBatch(products[2], 3, old),
            NewBatch(products[3], 4, old),
            NewBatch(products[4], 5, old),
            NewBatch(products[1], 6, old)
        };
        context.Batches.AddRange(batches);
        context.SaveChanges();

        var tasks = products.Take(4).Select(product => new ProductTask
        {
            ProductId = product.Id,
            CreatedAtUtc = old,
            UpdatedAtUtc = old
        }).ToArray();
        context.Tasks.AddRange(tasks);
        context.SaveChanges();

        var taskItems = tasks.Select((task, index) => new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batches[index].Id,
            ProductId = products[index].Id,
            CreatedAtUtc = old,
            UpdatedAtUtc = old
        }).ToArray();
        context.TaskItems.AddRange(taskItems);
        context.SaveChanges();

        var drafts = new[]
        {
            new InspectionDraft { TaskId = tasks[0].Id, CreatedAtUtc = old, UpdatedAtUtc = old },
            new InspectionDraft { TaskId = tasks[1].Id, CreatedAtUtc = old, UpdatedAtUtc = old }
        };
        context.Drafts.AddRange(drafts);
        context.SaveChanges();
        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = drafts[0].Id,
            TaskItemId = taskItems[0].Id,
            TaskId = tasks[0].Id,
            CheckedQty = 1
        });
        context.SaveChanges();

        var inspections = new[]
        {
            NewInspection(tasks[0], products[0], old),
            NewInspection(tasks[1], products[1], old),
            NewInspection(tasks[2], products[2], old)
        };
        context.Inspections.AddRange(inspections);
        context.SaveChanges();
        var items = new[]
        {
            NewInspectionItem(inspections[0], products[0], batches[0], old),
            NewInspectionItem(inspections[2], products[2], batches[2], old)
        };
        context.InspectionItems.AddRange(items);
        context.SaveChanges();
        context.InspectionItemRevisions.Add(new InspectionItemRevision
        {
            InspectionItemId = items[0].Id,
            PreviousCheckedQty = 1,
            NewCheckedQty = 0,
            ChangedAtUtc = old
        });
        context.InventoryAdjustments.Add(new InventoryAdjustment
        {
            ProductId = products[0].Id,
            ExcelStockQtySnapshot = 10,
            AdjustedStockQty = 9,
            AdjustedAtUtc = old
        });
        context.LifecycleEvents.Add(new LifecycleEvent
        {
            ProductId = products[0].Id,
            BatchId = batches[0].Id,
            EventType = "batch_checked_zero",
            Reason = "seed",
            OccurredAtUtc = old
        });
        context.SaveChanges();
    }

    private static Batch NewBatch(Product product, int offset, DateTime atUtc) => new()
    {
        ProductId = product.Id,
        ExpiryDate = new DateOnly(2027, 1, offset),
        ShelfLifeValue = 12,
        ShelfLifeUnit = "M",
        CurrentArrivalQty = 10,
        MaxArrivalQty = 10,
        CreatedAtUtc = atUtc,
        UpdatedAtUtc = atUtc
    };

    private static Inspection NewInspection(ProductTask task, Product product, DateTime atUtc) => new()
    {
        TaskId = task.Id,
        ProductId = product.Id,
        ProductCodeSnapshot = product.ProductCode,
        StageSnapshot = "discount_50",
        InspectorName = "Inspector",
        CheckDate = new DateOnly(2026, 8, 1),
        SubmittedAtUtc = atUtc
    };

    private static InspectionItem NewInspectionItem(
        Inspection inspection,
        Product product,
        Batch batch,
        DateTime atUtc) => new()
    {
        InspectionId = inspection.Id,
        ProductId = product.Id,
        BatchId = batch.Id,
        ProductionDateSnapshot = new DateOnly(2026, 1, 1),
        ExpiryDateSnapshot = batch.ExpiryDate,
        StageSnapshot = "discount_50",
        ArrivalQtySnapshot = 10,
        CheckedQty = 1,
        UpdatedAtUtc = atUtc
    };

    private static void AddBusinessRow(StoreDbContext context, string table)
    {
        var p2 = context.Products.Single(product => product.ProductCode == "P2");
        var p3 = context.Products.Single(product => product.ProductCode == "P3");
        var p4 = context.Products.Single(product => product.ProductCode == "P4");
        var p5 = context.Products.Single(product => product.ProductCode == "P5");
        var b2 = context.Batches.Single(batch => batch.ProductId == p2.Id && batch.ExpiryDate == new DateOnly(2027, 1, 2));
        var b3 = context.Batches.Single(batch => batch.ProductId == p3.Id);
        var task2 = context.Tasks.Single(task => task.ProductId == p2.Id);
        var task3 = context.Tasks.Single(task => task.ProductId == p3.Id);
        var task4 = context.Tasks.Single(task => task.ProductId == p4.Id);
        var item3 = context.TaskItems.Single(item => item.ProductId == p3.Id);
        var inspection2 = context.Inspections.Single(inspection => inspection.ProductId == p2.Id);
        var inspection3 = context.Inspections.Single(inspection => inspection.ProductId == p3.Id);
        var inspectionItem3 = context.InspectionItems.Single(item => item.ProductId == p3.Id);
        var draft2 = context.Drafts.Single(draft => draft.TaskId == task2.Id);
        var taskItem2 = context.TaskItems.Single(item => item.TaskId == task2.Id);

        switch (table)
        {
            case "tasks":
                context.Tasks.Add(new ProductTask { ProductId = p5.Id });
                break;
            case "task_items":
                var spareBatch = context.Batches.Single(batch => batch.ProductId == p2.Id && batch.ExpiryDate == new DateOnly(2027, 1, 6));
                context.TaskItems.Add(new ProductTaskItem { TaskId = task2.Id, BatchId = spareBatch.Id, ProductId = p2.Id });
                break;
            case "inspections":
                context.Inspections.Add(NewInspection(task4, p4, ConfirmationTime));
                break;
            case "inspection_items":
                context.InspectionItems.Add(NewInspectionItem(inspection2, p2, b2, ConfirmationTime));
                break;
            case "inspection_item_revisions":
                context.InspectionItemRevisions.Add(new InspectionItemRevision
                {
                    InspectionItemId = inspectionItem3.Id,
                    PreviousCheckedQty = 1,
                    NewCheckedQty = 2,
                    ChangedAtUtc = ConfirmationTime
                });
                break;
            case "inventory_adjustments":
                context.InventoryAdjustments.Add(new InventoryAdjustment
                {
                    ProductId = p2.Id,
                    ExcelStockQtySnapshot = 10,
                    AdjustedStockQty = 8,
                    AdjustedAtUtc = ConfirmationTime
                });
                break;
            case "lifecycle_events":
                context.LifecycleEvents.Add(new LifecycleEvent
                {
                    ProductId = p2.Id,
                    BatchId = b2.Id,
                    EventType = "batch_tracking_resumed",
                    Reason = "post import",
                    OccurredAtUtc = ConfirmationTime
                });
                break;
            case "drafts":
                context.Drafts.Add(new InspectionDraft { TaskId = task3.Id });
                break;
            case "draft_items":
                context.DraftItems.Add(new InspectionDraftItem
                {
                    DraftId = draft2.Id,
                    TaskItemId = taskItem2.Id,
                    TaskId = task2.Id,
                    CheckedQty = 1
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(table));
        }
    }

    private static void ModifyBusinessRow(StoreDbContext context, string table)
    {
        switch (table)
        {
            case "tasks":
                context.Tasks.Single(task => task.ProductId == context.Products.Single(product => product.ProductCode == "P1").Id).HighestStage = "discount_20";
                break;
            case "task_items":
                context.TaskItems.Single(item => item.ProductId == context.Products.Single(product => product.ProductCode == "P1").Id).Stage = "discount_20";
                break;
            case "inspections":
                context.Inspections.Single(inspection => inspection.ProductId == context.Products.Single(product => product.ProductCode == "P1").Id).InspectorName = "Changed";
                break;
            case "inspection_items":
                context.InspectionItems.Single(item => item.ProductId == context.Products.Single(product => product.ProductCode == "P1").Id).CheckedQty = 2;
                break;
            case "inspection_item_revisions":
                context.InspectionItemRevisions.Single(revision => revision.PreviousCheckedQty == 1).NewCheckedQty = 2;
                break;
            case "inventory_adjustments":
                context.InventoryAdjustments.Single().AdjustedStockQty = 8;
                break;
            case "lifecycle_events":
                context.LifecycleEvents.Single().Reason = "changed";
                break;
            case "drafts":
                context.Drafts.Single(draft => draft.TaskId == context.Tasks.Single(task => task.ProductId == context.Products.Single(product => product.ProductCode == "P1").Id).Id).InspectorName = "Changed";
                break;
            case "draft_items":
                context.DraftItems.Single().CheckedQty = 2;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(table));
        }
    }

    private static Dictionary<string, long> ReadTableCounts(string databasePath)
    {
        var names = new[]
        {
            "products", "batches", "tasks", "task_items", "drafts", "draft_items", "inspections",
            "inspection_items", "inspection_item_revisions", "inventory_adjustments", "imports",
            "import_workbooks", "import_issues", "backups", "settings", "app_state", "lifecycle_events",
            "__EFMigrationsHistory"
        };
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{name}\";";
            counts[name] = Convert.ToInt64(command.ExecuteScalar());
        }

        return counts;
    }

    private static Dictionary<string, string> ReadTableFingerprints(string databasePath)
    {
        using var connection = OpenReadOnly(databasePath);
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in BusinessTables)
        {
            var columns = ReadColumnNames(connection, table);
            var builder = new StringBuilder(table);
            foreach (var column in columns)
            {
                builder.Append('|').Append(column.Ordinal).Append(':').Append(column.Name);
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)))} " +
                                  $"FROM {QuoteIdentifier(table)} ORDER BY {QuoteIdentifier("id")};";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                builder.Append("|row");
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    AppendValue(builder, reader.GetValue(index));
                }
            }

            fingerprints[table] = Sha256Text(builder.ToString());
        }

        return fingerprints;
    }

    private static List<(int Ordinal, string Name)> ReadColumnNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)});";
        using var reader = command.ExecuteReader();
        var columns = new List<(int Ordinal, string Name)>();
        while (reader.Read())
        {
            columns.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return columns;
    }

    private static string MutateSnapshotAndSyncBackupSha(
        SqliteTestDatabase database,
        ImportCandidate candidate,
        string sql)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = candidate.SnapshotPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        var sha256 = Sha256(candidate.SnapshotPath);
        using var context = database.Open();
        var backup = context.BackupRecords.Single(record => record.Id == candidate.BackupId);
        backup.Sha256 = sha256;
        context.SaveChanges();
        return sha256;
    }

    private static long ReadScalarFromSnapshot(string path, string sql)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void AppendValue(StringBuilder builder, object value)
    {
        if (value is DBNull)
        {
            builder.Append("null;");
            return;
        }

        if (value is byte[] bytes)
        {
            builder.Append("blob:").Append(bytes.Length).Append(':').Append(Convert.ToHexString(bytes)).Append(';');
            return;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        builder.Append(value.GetType().FullName).Append(':').Append(text.Length).Append(':').Append(text).Append(';');
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Sha256Text(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record ImportCandidate(long ImportId, string SnapshotPath, string Sha256, long BackupId);
}
