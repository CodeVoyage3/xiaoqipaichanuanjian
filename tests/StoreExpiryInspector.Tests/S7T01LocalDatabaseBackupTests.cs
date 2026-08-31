using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S7T01LocalDatabaseBackupTests
{
    [Fact]
    public void CreatesVerifiedBackupMetadataAndManualBackupRecord()
    {
        using var database = SqliteTestDatabase.Create();
        var backupDirectory = NewBackupDirectory();
        try
        {
            using (var context = database.Open())
            {
                context.Products.Add(new Product { ProductCode = "SKU-S7-COMMITTED" });
                context.SaveChanges();
                Assert.Equal("wal", SqliteTestDatabase.ReadPragma(context, "journal_mode"));
            }

            var result = new LocalDatabaseBackupUseCase().Create(database.Path, backupDirectory);

            Assert.True(result.Succeeded);
            Assert.Equal(LocalDatabaseBackupCodes.Success, result.Code);
            Assert.NotNull(result.BackupRecordId);
            Assert.NotNull(result.BackupId);
            Assert.NotNull(result.BackupPath);
            Assert.NotNull(result.MetadataPath);
            Assert.NotEqual(Path.GetFullPath(database.Path), Path.GetFullPath(result.BackupPath!));
            Assert.StartsWith(Path.GetFullPath(backupDirectory), Path.GetFullPath(result.BackupPath!), StringComparison.OrdinalIgnoreCase);
            Assert.True(result.FileSize > 0);
            Assert.Equal(64, result.Sha256!.Length);
            Assert.Equal(result.Sha256, ComputeSha256(result.BackupPath!));
            Assert.Equal("ok", Scalar(result.BackupPath!, "PRAGMA integrity_check;"));

            using (var source = database.Open())
            {
                var expectedMigrations = source.Database.GetMigrations().ToArray();
                Assert.Equal(expectedMigrations, result.MigrationIds);
                Assert.Equal(expectedMigrations, ReadMigrations(result.BackupPath!));

                var record = source.BackupRecords.AsNoTracking().Single();
                Assert.Equal(result.BackupRecordId, record.Id);
                Assert.Equal("manual", record.BackupType);
                Assert.Equal(result.BackupPath, record.FilePath);
                Assert.Equal(result.Sha256, record.Sha256);
                Assert.Equal("verified", record.VerificationStatus);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(result.MetadataPath!));
            Assert.Equal(result.BackupId, document.RootElement.GetProperty("BackupId").GetString());
            Assert.Equal(result.FileSize, document.RootElement.GetProperty("FileSize").GetInt64());
            Assert.Equal(result.Sha256, document.RootElement.GetProperty("Sha256").GetString());
            Assert.Equal("verified", document.RootElement.GetProperty("ValidationResult").GetString());
            Assert.Empty(Directory.GetFiles(backupDirectory, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteDirectory(backupDirectory);
        }
    }

    [Fact]
    public void OnlineWalSnapshotIncludesCommittedRowsAndExcludesUncommittedRows()
    {
        using var database = SqliteTestDatabase.Create();
        var backupDirectory = NewBackupDirectory();
        try
        {
            using (var context = database.Open())
            {
                context.Products.Add(new Product { ProductCode = "SKU-COMMITTED" });
                context.SaveChanges();
            }

            using var writer = database.Open();
            using var transaction = writer.Database.BeginTransaction();
            writer.Products.Add(new Product { ProductCode = "SKU-UNCOMMITTED" });
            writer.SaveChanges();

            var snapshot = new PreImportSnapshotService().Create(database.Path, backupDirectory);
            Assert.True(snapshot.CanProceed);
            var backupPath = Assert.IsType<PreImportSnapshotMetadata>(snapshot.Metadata).SnapshotPath;
            Assert.Equal(1L, ScalarLong(backupPath, "SELECT COUNT(*) FROM products WHERE product_code = 'SKU-COMMITTED';"));
            Assert.Equal(0L, ScalarLong(backupPath, "SELECT COUNT(*) FROM products WHERE product_code = 'SKU-UNCOMMITTED';"));
            Assert.Equal("ok", Scalar(backupPath, "PRAGMA integrity_check;"));
            transaction.Rollback();
        }
        finally
        {
            DeleteDirectory(backupDirectory);
        }
    }

    [Fact]
    public void MissingSourceAndDatabaseDirectoryDestinationFailClearly()
    {
        using var database = SqliteTestDatabase.Create();
        var useCase = new LocalDatabaseBackupUseCase();

        var missing = useCase.Create(Path.Combine(database.Directory, "missing.db"), NewBackupDirectory());
        var sameDirectory = useCase.Create(database.Path, database.Directory);

        Assert.False(missing.Succeeded);
        Assert.Equal(LocalDatabaseBackupCodes.SourceNotFound, missing.Code);
        Assert.False(sameDirectory.Succeeded);
        Assert.Equal(LocalDatabaseBackupCodes.StorageFailed, sameDirectory.Code);
        Assert.Empty(database.Open().BackupRecords.AsNoTracking());
    }

    [Fact]
    public void ValidationFailureCleansPartialFilesPreservesExistingBackupAndCanRetry()
    {
        using var validDatabase = SqliteTestDatabase.Create();
        using var invalidDatabase = SqliteTestDatabase.CreateEmpty();
        var backupDirectory = NewBackupDirectory();
        try
        {
            using (var connection = Open(invalidDatabase.Path, readOnly: false))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE unexpected (id INTEGER PRIMARY KEY);";
                command.ExecuteNonQuery();
            }

            var invalidHash = ComputeSha256(invalidDatabase.Path);
            var first = new LocalDatabaseBackupUseCase().Create(validDatabase.Path, backupDirectory);
            Assert.True(first.Succeeded);
            var firstHash = ComputeSha256(first.BackupPath!);

            var failed = new LocalDatabaseBackupUseCase().Create(invalidDatabase.Path, backupDirectory);
            Assert.False(failed.Succeeded);
            Assert.Equal(LocalDatabaseBackupCodes.ValidationFailed, failed.Code);
            Assert.Equal(invalidHash, ComputeSha256(invalidDatabase.Path));
            Assert.Equal(firstHash, ComputeSha256(first.BackupPath!));
            Assert.Single(Directory.GetFiles(backupDirectory, "backup-*.db"));
            Assert.Empty(Directory.GetFiles(backupDirectory, "*.tmp", SearchOption.AllDirectories));

            var retry = new LocalDatabaseBackupUseCase().Create(validDatabase.Path, backupDirectory);
            Assert.True(retry.Succeeded);
            Assert.Equal(2, Directory.GetFiles(backupDirectory, "backup-*.db").Length);
            Assert.NotEqual(first.BackupId, retry.BackupId);
        }
        finally
        {
            DeleteDirectory(backupDirectory);
        }
    }

    [Fact]
    public void UnavailableDestinationFailsWithoutChangingSource()
    {
        using var database = SqliteTestDatabase.Create();
        var destinationFile = Path.Combine(database.Directory, "not-a-directory");
        File.WriteAllText(destinationFile, "occupied");
        var sourceHash = ComputeSha256(database.Path);

        var result = new LocalDatabaseBackupUseCase().Create(database.Path, destinationFile);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalDatabaseBackupCodes.StorageFailed, result.Code);
        Assert.Equal(sourceHash, ComputeSha256(database.Path));
        Assert.Empty(database.Open().BackupRecords.AsNoTracking());
    }

    [Fact]
    public void SecondRequestIsRejectedWhileBackupIsInProgress()
    {
        var field = typeof(LocalDatabaseBackupUseCase).GetField(
            "backupInProgress",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(null, 1);
        try
        {
            var result = new LocalDatabaseBackupUseCase().Create("unused.db", "unused");
            Assert.False(result.Succeeded);
            Assert.Equal(LocalDatabaseBackupCodes.BackupInProgress, result.Code);
        }
        finally
        {
            field.SetValue(null, 0);
        }
    }

    private static string NewBackupDirectory() => Path.Combine(
        Path.GetTempPath(),
        "StoreExpiryInspectorBackupTests",
        Guid.NewGuid().ToString("N"));

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

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
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
        using var reader = command.ExecuteReader();
        var migrations = new List<string>();
        while (reader.Read())
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations.ToArray();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
