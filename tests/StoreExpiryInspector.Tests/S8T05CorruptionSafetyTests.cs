using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S8T05CorruptionSafetyTests
{
    [Theory]
    [InlineData("relative.db")]
    [InlineData("..\\outside.db")]
    [InlineData("C:\\temp\\outside.db")]
    public void GuardRejectsUnsafeTargetsBeforeAction(string target)
    {
        var root = NewDirectory();
        var calls = 0;
        Assert.Throws<ArgumentException>(() => ValidateThenAction(root, target, () => calls++));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void GuardRejectsNoGuidRootBeforeAction()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T05", "not-a-guid");
        var calls = 0;
        Assert.ThrowsAny<Exception>(() => ValidateThenAction(root, Path.Combine(root, "current.db"), () => calls++));
        Assert.Equal(0, calls);
    }
    [Fact]
    public void CorruptCurrentDatabaseFailsClosedBeforeStagingOrReplacement()
    {
        using var health = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var backups = Path.Combine(root, "backups");
            var backup = new LocalDatabaseBackupUseCase().Create(health.Path, backups);
            Assert.True(backup.Succeeded);
            var backupHash = Hash(backup.BackupPath!);

            var evidence = new List<object>();
            foreach (var corrupt in new Action<string>[] { CorruptHeader, Truncate, CorruptDataPage, PartialFile, PartialOverwrite })
            {
                var current = Path.Combine(root, Guid.NewGuid().ToString("N"), "current.db");
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                File.Copy(health.Path, current);
                corrupt(current);
                var damagedHash = Hash(current);
                var damagedProbe = Probe(current);
                var checkpoints = new List<string>();

                var result = new DatabaseRestoreUseCase((point, _) => checkpoints.Add(point))
                    .Restore(backup.BackupPath!, true, current, backups);

                Assert.Equal(DatabaseRestoreCodes.PreRestoreBackupFailed, result.Code);
                Assert.Equal(damagedHash, Hash(current));
                Assert.Equal(backupHash, Hash(backup.BackupPath!));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(current)!, "*.restore-*"));
                Assert.Empty(Directory.GetFiles(backups, "pre-restore-*.db"));
                evidence.Add(new { scenario = corrupt.Method.Name, sourceSHA = Hash(health.Path), sourceProbe = Probe(health.Path), damagedSHA = damagedHash, damagedProbe, result = result.Code, resultSucceeded = result.Succeeded, checkpoints, finalSHA = Hash(current), finalProbe = Probe(current), backupSHA = Hash(backup.BackupPath!), pass = !result.Succeeded });
            }
            WriteEvidence(root, "corrupt-current", evidence);
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Fact]
    public void InitializeRejectsCorruptDataPageWithoutChangingTheExistingFile()
    {
        using var health = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            var current = Path.Combine(root, "current.db");
            File.Copy(health.Path, current);
            CorruptDataPage(current);
            var damagedHash = Hash(current);

            Assert.ThrowsAny<Exception>(() => DatabaseInitializer.Initialize(current));
            Assert.Equal(damagedHash, Hash(current));
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Fact]
    public void ExistingEmptyFileIsRejectedWithoutBeingInitialized()
    {
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var current = Path.Combine(root, "current.db");
            File.WriteAllBytes(current, []);

            Assert.ThrowsAny<Exception>(() => DatabaseInitializer.Initialize(current));
            Assert.Equal(0, new FileInfo(current).Length);
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("data_page")]
    public void LegacyInitializeComparisonRecordsOldMigrateWalBehavior(string scenario)
    {
        using var health = CreateRepresentativeDatabase();
        var root = NewDirectory();
        var legacyPath = Path.Combine(root, $"legacy-{scenario}.db");
        var currentPath = Path.Combine(root, $"current-{scenario}.db");
        if (scenario == "empty") { File.WriteAllBytes(legacyPath, []); File.WriteAllBytes(currentPath, []); }
        else { File.Copy(health.Path, legacyPath); File.Copy(health.Path, currentPath); CorruptDataPage(legacyPath); CorruptDataPage(currentPath); }
        var before = Hash(currentPath);
        var legacy = TryLegacyInitialize(legacyPath);
        var current = Assert.ThrowsAny<Exception>(() => DatabaseInitializer.Initialize(currentPath));
        Assert.Equal(before, Hash(currentPath));
        WriteEvidence(root, $"legacy-{scenario}", new { before, legacy, legacyHash = Hash(legacyPath), current = current.GetType().Name, final = Hash(currentPath), pass = true });
    }

    [Fact]
    public void HealthyBackupRestoresRepresentativeFingerprintAfterProtection()
    {
        using var database = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var backups = Path.Combine(root, "backups");
            var before = Fingerprint(database.Path);
            var backup = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
            Assert.True(backup.Succeeded);
            using (var current = database.Open())
            {
                current.Products.Add(new Product { ProductCode = "CURRENT-ONLY", EffectiveStockQty = 1, ExcelStockQty = 1 });
                current.SaveChanges();
            }
            Checkpoint(database.Path);
            var checkpoints = new List<string>();
            var result = new DatabaseRestoreUseCase((point, _) => checkpoints.Add(point))
                .Restore(backup.BackupPath!, true, database.Path, backups);

            Assert.True(result.Succeeded);
            Assert.Contains("staging_validated", checkpoints);
            Assert.Contains("before_replace", checkpoints);
            Assert.Equal(before, Fingerprint(database.Path));
            Assert.Equal("ok", Scalar(database.Path, "PRAGMA integrity_check;"));
            Assert.Equal(0, ForeignKeyViolationCount(database.Path));
            Assert.NotNull(result.PreRestoreBackupPath);
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Fact]
    public void RestoredDatabaseSupportsAuthoritativeBusinessReads()
    {
        using var database = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var backups = Path.Combine(root, "backups");
            var expected = Fingerprint(database.Path);
            var backup = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
            Assert.True(backup.Succeeded);
            Execute(database.Path, "DELETE FROM inspection_item_revisions;");
            Assert.True(new DatabaseRestoreUseCase().Restore(backup.BackupPath!, true, database.Path, backups).Succeeded);
            using var context = database.Open();
            var tasks = new InspectionTaskQuery();
            Assert.NotEmpty(tasks.Dashboard(context).UrgentTasks);
            Assert.NotEmpty(tasks.SearchOpenTasks(context, new()).Items);
            Assert.NotEmpty(tasks.GetOpenTaskIds(context));
            Assert.NotEmpty(tasks.GetReminderCandidates(context));
            var history = new InspectionHistoryQuery();
            var inspectionId = context.Inspections.Single().Id;
            Assert.NotEmpty(history.ListPage(context, new()).Items);
            Assert.Equal("found", history.GetDetail(context, inspectionId).Status);
            Assert.Equal("found", history.GetItemRevisions(context, inspectionId, context.InspectionItems.Single().Id).Status);
            Assert.NotEmpty(new DailyReminderUseCase(tasks).Evaluate(context, DateTime.Today.AddHours(23)).Items);
            Assert.Equal(expected, Fingerprint(database.Path));
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Fact]
    public void LargeS8T01SeedBackupRestoreRecordsActualSizeAndElapsedTime()
    {
        var created = Stopwatch.StartNew();
        var root = NewDirectory();
        AssertSafeSyntheticRoot(root);
        var database = Path.Combine(root, "S8-T05-large.db");
        ValidateThenAction(root, database, () => DatabaseInitializer.Initialize(database));
        S8T01PerformanceBaselineTests.SeedForS8T05(database);
        Checkpoint(database);
        created.Stop();
        try
        {
            Assert.Equal("100000", Scalar(database, "SELECT COUNT(*) FROM products;"));
            Assert.Equal("100000", Scalar(database, "SELECT COUNT(*) FROM batches;"));
            Assert.Equal("300000", Scalar(database, "SELECT COUNT(*) FROM inspections;"));
            Assert.Equal("9", Scalar(database, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
            var backups = Path.Combine(root, "backups");
            var expected = Fingerprint(database);
            var backupWatch = Stopwatch.StartNew();
            var backup = new LocalDatabaseBackupUseCase().Create(database, backups);
            backupWatch.Stop();
            Assert.True(backup.Succeeded);
            Assert.True(new DatabaseRestoreUseCase().ValidateForListing(backup.BackupPath!, database).Succeeded);
            Execute(database, "UPDATE products SET current_name = 'changed current';");
            var restoreWatch = Stopwatch.StartNew();
            var restored = new DatabaseRestoreUseCase().Restore(backup.BackupPath!, true, database, backups);
            restoreWatch.Stop();
            Assert.True(restored.Succeeded);
            var validationWatch = Stopwatch.StartNew();
            Assert.Equal("ok", Scalar(database, "PRAGMA integrity_check;"));
            Assert.Equal(0, ForeignKeyViolationCount(database));
            validationWatch.Stop();
            Assert.Equal(expected, Fingerprint(database));
            var initializeWatch = Stopwatch.StartNew();
            DatabaseInitializer.Initialize(database);
            initializeWatch.Stop();
            WriteEvidence(root, "large-backup-restore", new
            {
                products = 100_000, batches = 100_000, inspections = 300_000,
                databaseBytes = new FileInfo(database).Length,
                backupBytes = new FileInfo(backup.BackupPath!).Length,
                createMilliseconds = created.ElapsedMilliseconds, backupMilliseconds = backupWatch.ElapsedMilliseconds,
                restoreMilliseconds = restoreWatch.ElapsedMilliseconds, finalValidationMilliseconds = validationWatch.ElapsedMilliseconds, initializeMilliseconds = initializeWatch.ElapsedMilliseconds,
                integrity = "ok", foreignKeys = 0, finalFingerprint = Fingerprint(database), pass = true
            });
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Fact]
    public void TamperedBackupIsRejectedBeforeProtectionOrReplacement()
    {
        using var database = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var backups = Path.Combine(root, "backups");
            var backup = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
            Assert.True(backup.Succeeded);
            File.AppendAllText(backup.BackupPath!, "tampered");
            var current = Hash(database.Path);
            var result = new DatabaseRestoreUseCase().Restore(backup.BackupPath!, true, database.Path, backups);

            Assert.Equal(DatabaseRestoreCodes.HashMismatch, result.Code);
            Assert.Equal(current, Hash(database.Path));
            Assert.Empty(Directory.GetFiles(backups, "pre-restore-*.db"));
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("staging-header")]
    [InlineData("staging-fk")]
    [InlineData("staging-migration")]
    [InlineData("staging-identity")]
    [InlineData("replace-before")]
    [InlineData("final-header")]
    [InlineData("final-fk")]
    [InlineData("final-migration")]
    [InlineData("final-identity")]
    public void InjectedRestoreFailurePreservesOrRollsBackTheOriginalDatabase(string failure)
    {
        using var database = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var backups = Path.Combine(root, "backups");
            var original = Fingerprint(database.Path);
            var backup = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
            Assert.True(backup.Succeeded);
            Execute(database.Path, "UPDATE products SET current_name = 'changed current';");
            Checkpoint(database.Path);
            var changed = Fingerprint(database.Path);
            var changedSha = Hash(database.Path);
            var checkpoints = new List<string>();
            string? mutatedSha = null;
            SidecarProbe? mutatedProbe = null;
            string? mutatedPath = null;
            long? mutatedWalBytes = null;
            var result = new DatabaseRestoreUseCase((point, path) =>
            {
                checkpoints.Add(point);
                if (failure == "copy" && point == "before_staging_copy") throw new IOException("injected copy failure");
                if (failure.StartsWith("staging-", StringComparison.Ordinal) && point == "before_staging_validation")
                {
                    MutateValidationTarget(failure[8..], path!);
                    mutatedPath = path; mutatedSha = Hash(path!); mutatedProbe = Probe(path!); mutatedWalBytes = File.Exists(path + "-wal") ? new FileInfo(path + "-wal").Length : null;
                }
                if (failure == "replace-before" && point == "before_replace") throw new IOException("injected replace failure");
                if (failure.StartsWith("final-", StringComparison.Ordinal) && point == "after_replace")
                {
                    MutateValidationTarget(failure[6..], path!);
                    mutatedPath = path; mutatedSha = Hash(path!); mutatedProbe = Probe(path!); mutatedWalBytes = File.Exists(path + "-wal") ? new FileInfo(path + "-wal").Length : null;
                }
            }).Restore(backup.BackupPath!, true, database.Path, backups);

            var final = failure.StartsWith("final-", StringComparison.Ordinal);
            Assert.Equal(final ? DatabaseRestoreCodes.FinalValidationFailed : failure == "replace-before" ? DatabaseRestoreCodes.ReplaceFailed : DatabaseRestoreCodes.StagingFailed, result.Code);
            Assert.Equal(changed, Fingerprint(database.Path));
            Assert.Equal(changedSha, Hash(database.Path));
            Assert.Equal("ok", Scalar(database.Path, "PRAGMA integrity_check;"));
            Assert.Equal(0, ForeignKeyViolationCount(database.Path));
            Assert.NotEqual(original, changed);
            Assert.NotNull(result.PreRestoreBackupPath);
            Assert.True(new DatabaseRestoreUseCase().ValidateForListing(result.PreRestoreBackupPath!, database.Path).Succeeded);
            WriteEvidence(root, $"failure-{failure}", new { result = result.Code, checkpoints, mutatedPath, mutatedMainSHA = mutatedSha, expectedSHA = Hash(backup.BackupPath!), mutatedProbe, mutatedWalBytes, interceptionLayer = mutatedSha is null ? "operation" : string.Equals(mutatedSha, Hash(backup.BackupPath!), StringComparison.OrdinalIgnoreCase) ? "later_not_proven" : "hash_first", original, changed, finalSHA = Hash(database.Path), finalProbe = Probe(database.Path), final = Fingerprint(database.Path), protectionSha = Hash(result.PreRestoreBackupPath!), protectedBackup = true, pass = true });
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("metadata_missing")]
    [InlineData("metadata_incomplete")]
    [InlineData("metadata_filename")]
    [InlineData("metadata_size")]
    [InlineData("sha")]
    [InlineData("header")]
    [InlineData("truncate")]
    [InlineData("integrity")]
    [InlineData("migration")]
    [InlineData("foreign_key")]
    [InlineData("non_sqlite")]
    public void UntrustedBackupIsRejectedBeforeProtection(string scenario)
    {
        using var database = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var backups = Path.Combine(root, "backups");
            var backup = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
            Assert.True(backup.Succeeded);
            var candidate = Path.Combine(root, $"{scenario}.db");
            File.Copy(backup.BackupPath!, candidate);
            File.Copy(backup.BackupPath! + ".metadata.json", candidate + ".metadata.json");
            RewriteMetadata(candidate, metadata => metadata with { FileName = Path.GetFileName(candidate) });
            Assert.True(new DatabaseRestoreUseCase().ValidateForListing(candidate, database.Path).Succeeded);
            MutateBackup(scenario, candidate);
            var before = Fingerprint(database.Path);
            var checkpoints = new List<string>();

            var result = new DatabaseRestoreUseCase((point, _) => checkpoints.Add(point))
                .Restore(candidate, true, database.Path, backups);

            Assert.False(result.Succeeded);
            Assert.Equal(ExpectedCode(scenario), result.Code);
            Assert.Equal(before, Fingerprint(database.Path));
            Assert.Empty(checkpoints);
            Assert.Empty(Directory.GetFiles(backups, "pre-restore-*.db"));
            WriteEvidence(root, $"backup-{scenario}", new { sourceSHA = Hash(backup.BackupPath!), sourceProbe = Probe(database.Path), damagedSHA = File.Exists(candidate) ? Hash(candidate) : null, damagedProbe = Probe(candidate), result = result.Code, checkpoints, finalSHA = Hash(database.Path), finalProbe = Probe(database.Path), before, after = Fingerprint(database.Path), pass = true });
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatched")]
    [InlineData("stale")]
    [InlineData("abnormal")]
    public void WalSidecarVariantsRecordRealReopenBehavior(string scenario)
    {
        using var source = CreateRepresentativeDatabase();
        using var unrelated = CreateRepresentativeDatabase();
        var root = NewDirectory();
        try
        {
            AssertSafeSyntheticRoot(root);
            var copy = Path.Combine(root, scenario, "copy.db");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            Checkpoint(source.Path);
            var sourceBeforeMutation = Fingerprint(source.Path);
            File.Copy(source.Path, copy);
            switch (scenario)
            {
                case "missing": File.Delete(copy + "-wal"); File.Delete(copy + "-shm"); break;
                case "mismatched": CopyUncheckpointedWal(unrelated.Path, copy + "-wal"); break;
                case "stale":
                    var oldWal = Path.Combine(root, "old-generation.wal");
                    CopyUncheckpointedWal(source.Path, oldWal);
                    Checkpoint(source.Path);
                    File.Copy(source.Path, copy, true);
                    File.Copy(oldWal, copy + "-wal", true);
                    break;
                case "abnormal": File.WriteAllBytes(copy + "-journal", [1, 2, 3, 4]); break;
            }
            var probe = Probe(copy);
            if (scenario == "abnormal")
            {
                Assert.False(probe.Opened);
                Assert.Equal("SqliteException", probe.Error);
            }
            else
            {
                Assert.True(probe.Opened);
                Assert.Equal("ok", probe.Integrity);
                Assert.Equal(0, probe.ForeignKeys);
                Assert.Equal(Fingerprint(source.Path), probe.Fingerprint);
            }
            WriteEvidence(root, $"wal-{scenario}", new { sourceBeforeMutation, sourceAfterMutation = Fingerprint(source.Path), probe, pass = true });
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    private static SqliteTestDatabase CreateRepresentativeDatabase(int productCount = 80, int nameLength = 900)
    {
        AssertNoReparseAncestors(Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorTests"), Path.GetTempPath());
        var database = SqliteTestDatabase.CreateEmpty();
        AssertTemporaryTestSource(database.Path);
        DatabaseInitializer.Initialize(database.Path);
        using var context = database.Open();
        for (var index = 0; index < productCount; index++)
        {
            var product = new Product
            {
                ProductCode = $"S8T05-{index:D3}",
                CurrentName = new string('x', nameLength),
                EffectiveStockQty = 1,
                ExcelStockQty = 1
            };
            product.Batches.Add(new Batch
            {
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                ShelfLifeValue = 30,
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1
            });
            context.Products.Add(product);
        }
        context.SaveChanges();
        var firstProduct = context.Products.OrderBy(product => product.Id).First();
        var batch = context.Batches.OrderBy(batch => batch.Id).First();
        var task = new ProductTask { ProductId = firstProduct.Id };
        context.Tasks.Add(task);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = firstProduct.Id, BatchId = batch.Id, Stage = "expired", AttentionVersion = 1 });
        var completed = new ProductTask { ProductId = firstProduct.Id, Status = "completed", HighestStage = "expired", ClosedAtUtc = DateTime.UtcNow };
        context.Tasks.Add(completed);
        context.SaveChanges();
        context.Inspections.Add(new Inspection
        {
            TaskId = completed.Id, ProductId = firstProduct.Id, ProductCodeSnapshot = firstProduct.ProductCode,
            StageSnapshot = "discount_50", StockQtySnapshot = 1, InspectorName = "S8-T05", CheckDate = DateOnly.FromDateTime(DateTime.Today)
        });
        context.SaveChanges();
        var inspection = context.Inspections.Single();
        var item = new InspectionItem { InspectionId = inspection.Id, ProductId = firstProduct.Id, BatchId = batch.Id, ExpiryDateSnapshot = batch.ExpiryDate, StageSnapshot = "expired", ArrivalQtySnapshot = 1, CheckedQty = 1 };
        context.InspectionItems.Add(item);
        context.SaveChanges();
        context.InspectionItemRevisions.Add(new InspectionItemRevision { InspectionItemId = item.Id, PreviousCheckedQty = 0, NewCheckedQty = 1 });
        context.SaveChanges();
        var import = new ImportRecord
        {
            SourceFileName = "S8-T05", SourceFileSha256 = new string('0', 64),
            ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = "succeeded"
        };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline
        {
            ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = ExpiryPolicies.Version1,
            CreatedImportId = import.Id, BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            IsCompleted = true, CompletedAtUtc = DateTime.UtcNow
        });
        context.SaveChanges();
        Checkpoint(database.Path);
        return database;
    }

    private static void CorruptHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = 0;
        stream.WriteByte(0);
    }

    private static void MutateBackup(string scenario, string path)
    {
        switch (scenario)
        {
            case "missing": File.Delete(path); break;
            case "metadata_missing": File.Delete(path + ".metadata.json"); break;
            case "metadata_incomplete": File.WriteAllText(path + ".metadata.json", "{}"); break;
            case "metadata_filename": RewriteMetadata(path, metadata => metadata with { FileName = "other.db" }); break;
            case "metadata_size": RewriteMetadata(path, metadata => metadata with { FileSize = metadata.FileSize + 1 }); break;
            case "sha": File.AppendAllText(path, "tampered"); break;
            case "header": CorruptHeader(path); RefreshMetadata(path); break;
            case "truncate": Truncate(path); break;
            case "integrity": Truncate(path); RefreshMetadata(path); break;
            case "migration": Execute(path, "DELETE FROM __EFMigrationsHistory WHERE MigrationId=(SELECT MAX(MigrationId) FROM __EFMigrationsHistory);"); RefreshMetadata(path); break;
            case "foreign_key": Execute(path, "PRAGMA foreign_keys=OFF; DELETE FROM products WHERE id=(SELECT MIN(id) FROM products);"); RefreshMetadata(path); break;
            case "non_sqlite": File.WriteAllText(path, "not sqlite"); RefreshMetadata(path); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static string ExpectedCode(string scenario) => scenario switch
    {
        "missing" => DatabaseRestoreCodes.BackupNotFound,
        "sha" => DatabaseRestoreCodes.HashMismatch,
        "truncate" or "metadata_size" => DatabaseRestoreCodes.HashMismatch,
        "header" or "integrity" or "non_sqlite" => DatabaseRestoreCodes.IntegrityFailed,
        "migration" => DatabaseRestoreCodes.MigrationIncompatible,
        "foreign_key" => DatabaseRestoreCodes.BackupInvalid,
        _ => DatabaseRestoreCodes.BackupInvalid
    };

    private static void RewriteMetadata(string path, Func<LocalDatabaseBackupMetadata, LocalDatabaseBackupMetadata> update)
    {
        var metadata = JsonSerializer.Deserialize<LocalDatabaseBackupMetadata>(File.ReadAllText(path + ".metadata.json"))!;
        File.WriteAllText(path + ".metadata.json", JsonSerializer.Serialize(update(metadata)));
    }

    private static void RefreshMetadata(string path) => RewriteMetadata(path, metadata => metadata with { FileSize = new FileInfo(path).Length, Sha256 = Hash(path) });

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Truncate(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(stream.Length / 2);
    }

    private static void CorruptDataPage(string path)
    {
        long rootPage;
        long pageSize;
        using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT rootpage FROM sqlite_master WHERE type='table' AND name='products';";
            rootPage = Convert.ToInt64(command.ExecuteScalar());
            command.CommandText = "PRAGMA page_size;";
            pageSize = Convert.ToInt64(command.ExecuteScalar());
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = (rootPage - 1) * pageSize;
        stream.WriteByte(0);
    }

    private static void PartialFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..Math.Min(512, bytes.Length)]);
    }

    private static void PartialOverwrite(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = Math.Max(512, stream.Length / 3);
        stream.Write(new byte[64]);
    }

    private static void MutateValidationTarget(string mutation, string path)
    {
        switch (mutation)
        {
            case "header": CorruptHeader(path); break;
            case "fk": Execute(path, "PRAGMA foreign_keys=OFF; DELETE FROM products WHERE id=(SELECT MIN(id) FROM products);"); break;
            case "migration": Execute(path, "DELETE FROM __EFMigrationsHistory WHERE MigrationId=(SELECT MAX(MigrationId) FROM __EFMigrationsHistory);"); break;
            case "identity": Execute(path, "UPDATE products SET product_code='S8T05-INJECTED' WHERE id=(SELECT MIN(id) FROM products);"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static object TryLegacyInitialize(string path)
    {
        try
        {
            using var context = DatabaseInitializer.CreateContext(path);
            context.Database.Migrate();
            context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
            return new { succeeded = true, error = (string?)null };
        }
        catch (Exception exception) { return new { succeeded = false, error = exception.GetType().Name }; }
    }

    private static void Checkpoint(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static void CopyUncheckpointedWal(string databasePath, string destination)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO products(product_code, category_code, policy_code, policy_version, expiry_management_status, excel_stock_qty, effective_stock_qty, created_at_utc, updated_at_utc) VALUES ('S8T05-WAL-' || lower(hex(randomblob(4))), 'food', 'food_expiry', 1, 'managed', 1, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";
        command.ExecuteNonQuery();
        File.Copy(databasePath + "-wal", destination, true);
    }

    private static SidecarProbe Probe(string path)
    {
        try
        {
            var fingerprint = Fingerprint(path);
            return new(true, Scalar(path, "PRAGMA integrity_check;"), ForeignKeyViolationCount(path), fingerprint, null, null, null, Scalar(path, "SELECT COUNT(*) FROM products;"), Scalar(path, "SELECT COUNT(*) FROM batches;"), Scalar(path, "SELECT COUNT(*) FROM tasks;"), Scalar(path, "SELECT COUNT(*) FROM inspections;"), Scalar(path, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        }
        catch (SqliteException exception)
        {
            return new(false, null, null, null, exception.GetType().Name, exception.SqliteErrorCode, exception.SqliteExtendedErrorCode, null, null, null, null, null);
        }
        catch (Exception exception) { return new(false, null, null, null, exception.GetType().Name, null, null, null, null, null, null, null); }
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // Every persisted user table and field is included, in rowid order, without loading a large table into memory.
    private static string Fingerprint(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var names = new List<string>();
        using (var reader = tables.ExecuteReader()) while (reader.Read()) names.Add(reader.GetString(0));
        return string.Join("|", names.Select(table => $"{table}:{TableDigest(connection, table)}"));
    }

    private static string TableDigest(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (reader.Read())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                AppendValue(hash, reader.IsDBNull(index) ? null : reader.GetValue(index));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendValue(IncrementalHash hash, object? value)
    {
        if (value is null) { hash.AppendData([0]); return; }
        var bytes = value is byte[] blob
            ? blob
            : Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        hash.AppendData(value is byte[] ? [1] : [2]);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static string Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static int ForeignKeyViolationCount(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read()) count++;
        return count;
    }

    private static string NewDirectory()
    {
        var parent = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T05");
        AssertNoReparseAncestors(parent, Path.GetTempPath());
        var path = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        AssertSafeSyntheticRoot(path);
        return path;
    }

    private static void ValidateThenAction(string root, string target, Action action)
    {
        AssertSafeSyntheticRoot(root);
        if (!Path.IsPathFullyQualified(target)) throw new ArgumentException("S8-T05 requires an absolute contained target.");
        var full = Path.GetFullPath(target);
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("S8-T05 target escaped its GUID root.");
        AssertNoReparseAncestors(full, root);
        action();
    }

    private static void WriteEvidence(string root, string scenario, object evidence)
    {
        File.WriteAllText(Path.Combine(root, $"S8-T05-{scenario}.json"), JsonSerializer.Serialize(new
        {
            card = "S8-T05", runId = Environment.GetEnvironmentVariable("S8_T05_RUN_ID") ?? "not_set",
            processId = Environment.ProcessId, root, scenario, evidence
        }));
    }

    private static void AssertSafeSyntheticRoot(string root)
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T05"));
        var actual = Path.GetFullPath(root);
        Assert.StartsWith(expected + Path.DirectorySeparatorChar, actual, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{32}$", Path.GetFileName(actual));
        AssertNoReparseAncestors(actual, expected);
    }

    private static void AssertNoReparseAncestors(string child, string parent)
    {
        var expected = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var current = Path.GetFullPath(child); ; current = Directory.GetParent(current)?.FullName ?? throw new InvalidOperationException("TEMP path escaped."))
        {
            if (Path.Exists(current)) Assert.False(File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint));
            if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)) return;
            Assert.StartsWith(expected + Path.DirectorySeparatorChar, current, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertTemporaryTestSource(string path)
    {
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var actual = Path.GetFullPath(path);
        Assert.StartsWith(temp + Path.DirectorySeparatorChar, actual, StringComparison.OrdinalIgnoreCase);
        AssertNoReparseAncestors(actual, temp);
    }

    private sealed record SidecarProbe(bool Opened, string? Integrity, int? ForeignKeys, string? Fingerprint, string? Error, int? SqliteErrorCode, int? SqliteExtendedErrorCode, string? Products, string? Batches, string? Tasks, string? Inspections, string? Migrations);

}
