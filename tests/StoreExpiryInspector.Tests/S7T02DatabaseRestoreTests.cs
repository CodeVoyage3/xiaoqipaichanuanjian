using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Domain;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace StoreExpiryInspector.Tests;

public sealed class S7T02DatabaseRestoreTests
{
    [Fact]
    public void ValidBackupUsesStagingProtectsCurrentDatabaseAndRestoresExactBytes()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        string? stagingSeen = null;
        try
        {
            AddProduct(database, "RESTORE-TARGET");
            var target = CreateBackup(database, backups);
            AddProduct(database, "CURRENT-ONLY");

            var restore = new DatabaseRestoreUseCase((point, path) =>
            {
                if (point == "staging_validated")
                {
                    stagingSeen = path;
                    Assert.NotEqual(database.Path, path);
                    Assert.Equal(target.Sha256, Hash(path!));
                }
            }).Restore(target.BackupPath!, true, database.Path, backups);

            Assert.True(restore.Succeeded);
            Assert.Equal(DatabaseRestoreCodes.Restored, restore.Code);
            Assert.Equal(target.BackupId, restore.RestoredBackupId);
            Assert.NotNull(restore.PreRestoreBackupId);
            Assert.True(File.Exists(restore.PreRestoreBackupPath));
            Assert.True(File.Exists(restore.PreRestoreBackupPath + ".metadata.json"));
            var protectionMetadata = JsonSerializer.Deserialize<LocalDatabaseBackupMetadata>(
                File.ReadAllText(restore.PreRestoreBackupPath + ".metadata.json"));
            Assert.NotNull(protectionMetadata);
            Assert.Equal(LocalDatabaseBackupCodes.Success, protectionMetadata.ValidationResult);
            Assert.Equal(Hash(restore.PreRestoreBackupPath!), protectionMetadata.Sha256);
            Assert.Equal(target.FileSize, restore.RestoredFileSize);
            Assert.Equal(target.Sha256, restore.RestoredSha256);
            Assert.Equal(target.Sha256, Hash(database.Path));
            Assert.Equal("ok", Scalar(database.Path, "PRAGMA integrity_check;"));
            Assert.Equal(target.MigrationIds, ReadMigrations(database.Path));
            Assert.Equal(1L, ScalarLong(database.Path, "SELECT COUNT(*) FROM products WHERE product_code='RESTORE-TARGET';"));
            Assert.Equal(0L, ScalarLong(database.Path, "SELECT COUNT(*) FROM products WHERE product_code='CURRENT-ONLY';"));
            Assert.Equal(1L, ScalarLong(restore.PreRestoreBackupPath!, "SELECT COUNT(*) FROM products WHERE product_code='CURRENT-ONLY';"));
            Assert.NotNull(stagingSeen);
            AssertNoRestoreArtifacts(database.Directory);
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void RuntimeGateSamePathAndMissingBackupDoNotChangeDatabase()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var target = CreateBackup(database, backups);
            Checkpoint(database.Path);
            var originalHash = Hash(database.Path);
            var useCase = new DatabaseRestoreUseCase();

            Assert.Equal(DatabaseRestoreCodes.DatabaseInUse,
                useCase.Restore(target.BackupPath!, false, database.Path, backups).Code);
            Assert.Equal(DatabaseRestoreCodes.BackupInvalid,
                useCase.Restore(database.Path, true, database.Path, backups).Code);
            Assert.Equal(DatabaseRestoreCodes.BackupNotFound,
                useCase.Restore(Path.Combine(backups, "missing.db"), true, database.Path, backups).Code);

            var temporaryBackup = target.BackupPath + ".tmp";
            File.Copy(target.BackupPath!, temporaryBackup);
            Assert.Equal(DatabaseRestoreCodes.BackupInvalid,
                useCase.Restore(temporaryBackup, true, database.Path, backups).Code);

            using (var heldBackup = new FileStream(target.BackupPath!, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Equal(DatabaseRestoreCodes.BackupInvalid,
                    useCase.Restore(target.BackupPath!, true, database.Path, backups).Code);
            }

            Assert.Equal(originalHash, Hash(database.Path));
            Assert.Empty(Directory.GetFiles(backups, "pre-restore-*.db"));
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void HashIntegrityAndMigrationFailuresAreDistinctAndPreserveFormalDatabase()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var originalHash = Hash(database.Path);

            var hashTarget = CreateBackup(database, backups);
            File.AppendAllText(hashTarget.BackupPath!, "tampered");
            Assert.Equal(DatabaseRestoreCodes.HashMismatch,
                Restore(hashTarget, database, backups).Code);

            var integrityTarget = CreateBackup(database, backups);
            using (var stream = new FileStream(integrityTarget.BackupPath!, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(Math.Max(4096, stream.Length / 2));
            }
            RefreshMetadata(integrityTarget.BackupPath!);
            Assert.Equal(DatabaseRestoreCodes.IntegrityFailed,
                Restore(integrityTarget, database, backups).Code);

            var migrationTarget = CreateBackup(database, backups);
            Execute(migrationTarget.BackupPath!,
                "PRAGMA journal_mode=DELETE; DELETE FROM __EFMigrationsHistory WHERE MigrationId=(SELECT MAX(MigrationId) FROM __EFMigrationsHistory);");
            RefreshMetadata(migrationTarget.BackupPath!);
            Assert.Equal(DatabaseRestoreCodes.MigrationIncompatible,
                Restore(migrationTarget, database, backups).Code);

            var malformedMetadataTarget = CreateBackup(database, backups);
            File.WriteAllText(malformedMetadataTarget.MetadataPath!, "{\"BackupId\":\"broken\"}");
            Assert.Equal(DatabaseRestoreCodes.BackupInvalid,
                Restore(malformedMetadataTarget, database, backups).Code);

            Assert.Equal(originalHash, Hash(database.Path));
            Assert.Empty(Directory.GetFiles(backups, "pre-restore-*.db"));
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void ProtectionFailureAndOccupiedDatabaseStopBeforeReplacement()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var target = CreateBackup(database, backups);
            Checkpoint(database.Path);
            var originalHash = Hash(database.Path);
            var unusableDirectory = Path.Combine(database.Directory, "not-a-directory");
            File.WriteAllText(unusableDirectory, "occupied");

            var protectionFailed = new DatabaseRestoreUseCase().Restore(
                target.BackupPath!, true, database.Path, unusableDirectory);
            Assert.Equal(DatabaseRestoreCodes.PreRestoreBackupFailed, protectionFailed.Code);
            Assert.Equal(originalHash, Hash(database.Path));

            using var held = new FileStream(database.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var occupied = Restore(target, database, backups);
            Assert.Equal(DatabaseRestoreCodes.DatabaseInUse, occupied.Code);
            Assert.NotNull(occupied.PreRestoreBackupPath);
            Assert.Equal(originalHash, Hash(database.Path));
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void ReplaceFailurePreservesOriginalAndFailureCanRetry()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var target = CreateBackup(database, backups);
            AddProduct(database, "CURRENT");
            Checkpoint(database.Path);
            var originalHash = Hash(database.Path);
            var failed = new DatabaseRestoreUseCase((point, _) =>
            {
                if (point == "before_replace") throw new IOException("injected replace failure");
            }).Restore(target.BackupPath!, true, database.Path, backups);

            Assert.Equal(DatabaseRestoreCodes.ReplaceFailed, failed.Code);
            Assert.Equal(originalHash, Hash(database.Path));
            Assert.True(File.Exists(failed.PreRestoreBackupPath));
            AssertNoRestoreArtifacts(database.Directory);

            var retry = Restore(target, database, backups);
            Assert.True(retry.Succeeded);
            Assert.Equal(target.Sha256, Hash(database.Path));
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void FinalValidationFailureRollsBackAndRollbackFailureIsCritical()
    {
        using var rollbackDatabase = SqliteTestDatabase.Create();
        using var criticalDatabase = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var rollbackTarget = CreateBackup(rollbackDatabase, backups);
            AddProduct(rollbackDatabase, "CURRENT-ROLLBACK");
            Checkpoint(rollbackDatabase.Path);
            var rollbackOriginalHash = Hash(rollbackDatabase.Path);
            var rolledBack = new DatabaseRestoreUseCase((point, path) =>
            {
                if (point == "after_replace") File.WriteAllText(path!, "invalid restored bytes");
            }).Restore(rollbackTarget.BackupPath!, true, rollbackDatabase.Path, backups);

            Assert.Equal(DatabaseRestoreCodes.FinalValidationFailed, rolledBack.Code);
            Assert.Equal(rollbackOriginalHash, Hash(rollbackDatabase.Path));
            Assert.Equal("ok", Scalar(rollbackDatabase.Path, "PRAGMA integrity_check;"));
            Assert.True(File.Exists(rolledBack.PreRestoreBackupPath));

            var criticalTarget = CreateBackup(criticalDatabase, backups);
            AddProduct(criticalDatabase, "CURRENT-CRITICAL");
            var critical = new DatabaseRestoreUseCase((point, path) =>
            {
                if (point == "after_replace") File.WriteAllText(path!, "invalid restored bytes");
                if (point == "before_rollback") throw new IOException("injected rollback failure");
            }).Restore(criticalTarget.BackupPath!, true, criticalDatabase.Path, backups);

            Assert.Equal(DatabaseRestoreCodes.CriticalRestoreFailure, critical.Code);
            Assert.False(critical.Succeeded);
            Assert.True(File.Exists(critical.PreRestoreBackupPath));
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void SidecarsAreQuarantinedAndDeletedOnlyForTheFormalDatabase()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var target = CreateBackup(database, backups);
            var unrelated = Path.Combine(database.Directory, "other.db-wal");
            File.WriteAllText(unrelated, "keep");
            var restore = new DatabaseRestoreUseCase((point, _) =>
            {
                if (point != "staging_validated") return;
                foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                {
                    File.WriteAllText(database.Path + suffix, "old sidecar");
                }
            }).Restore(target.BackupPath!, true, database.Path, backups);

            Assert.True(restore.Succeeded);
            Assert.All(new[] { "-wal", "-shm", "-journal" }, suffix => Assert.False(File.Exists(database.Path + suffix)));
            Assert.True(File.Exists(unrelated));
            Assert.Empty(Directory.GetFiles(database.Directory, "*.restore-*"));
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    [Fact]
    public void RestoreExcludesSecondRestoreAndOrdinaryBackup()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = NewDirectory();
        try
        {
            var target = CreateBackup(database, backups);
            DatabaseRestoreResult? secondRestore = null;
            LocalDatabaseBackupResult? ordinaryBackup = null;
            var first = new DatabaseRestoreUseCase((point, _) =>
            {
                if (point != "staging_validated") return;
                secondRestore = new DatabaseRestoreUseCase().Restore(target.BackupPath!, true, database.Path, backups);
                ordinaryBackup = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
            }).Restore(target.BackupPath!, true, database.Path, backups);

            Assert.True(first.Succeeded);
            Assert.Equal(DatabaseRestoreCodes.DatabaseInUse, secondRestore!.Code);
            Assert.Equal(LocalDatabaseBackupCodes.BackupInProgress, ordinaryBackup!.Code);
        }
        finally
        {
            DeleteDirectory(backups);
        }
    }

    private static DatabaseRestoreResult Restore(
        LocalDatabaseBackupResult target,
        SqliteTestDatabase database,
        string backups) => new DatabaseRestoreUseCase().Restore(target.BackupPath!, true, database.Path, backups);

    private static LocalDatabaseBackupResult CreateBackup(SqliteTestDatabase database, string backups)
    {
        var result = new LocalDatabaseBackupUseCase().Create(database.Path, backups);
        Assert.True(result.Succeeded);
        return result;
    }

    private static void AddProduct(SqliteTestDatabase database, string productCode)
    {
        using var context = database.Open();
        context.Products.Add(new Product { ProductCode = productCode });
        context.SaveChanges();
    }

    private static void RefreshMetadata(string backupPath)
    {
        var metadataPath = backupPath + ".metadata.json";
        var metadata = JsonSerializer.Deserialize<LocalDatabaseBackupMetadata>(File.ReadAllText(metadataPath))!;
        metadata = metadata with { FileSize = new FileInfo(backupPath).Length, Sha256 = Hash(backupPath) };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata));
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Open(path, readOnly: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Checkpoint(string path) => Execute(path, "PRAGMA wal_checkpoint(TRUNCATE);");

    private static string Scalar(string path, string sql)
    {
        using var connection = Open(path, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static long ScalarLong(string path, string sql)
    {
        using var connection = Open(path, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string[] ReadMigrations(string path)
    {
        using var connection = Open(path, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var migrations = new List<string>();
        while (reader.Read()) migrations.Add(reader.GetString(0));
        return migrations.ToArray();
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorRestoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertNoRestoreArtifacts(string directory)
    {
        var files = Directory.GetFiles(directory, "*.restore-*");
        Assert.True(files.Length == 0, string.Join(Environment.NewLine, files));
    }

    private static void DeleteDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
