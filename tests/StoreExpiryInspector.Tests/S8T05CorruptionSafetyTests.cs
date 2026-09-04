using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S8T05CorruptionSafetyTests
{
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
            foreach (var corrupt in new Action<string>[] { CorruptHeader, Truncate, CorruptDataPage, PartialFile })
            {
                var current = Path.Combine(root, Guid.NewGuid().ToString("N"), "current.db");
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                File.Copy(health.Path, current);
                corrupt(current);
                var damagedHash = Hash(current);

                var result = new DatabaseRestoreUseCase().Restore(backup.BackupPath!, true, current, backups);

                Assert.Equal(DatabaseRestoreCodes.PreRestoreBackupFailed, result.Code);
                Assert.Equal(damagedHash, Hash(current));
                Assert.Equal(backupHash, Hash(backup.BackupPath!));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(current)!, "*.restore-*"));
                Assert.Empty(Directory.GetFiles(backups, "pre-restore-*.db"));
                evidence.Add(new { scenario = corrupt.Method.Name, damagedSha256 = damagedHash, result = result.Code, staging = false, replace = false });
            }
            File.WriteAllText(Path.Combine(root, "S8-T05-corruption-evidence.json"), JsonSerializer.Serialize(evidence));
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

    private static SqliteTestDatabase CreateRepresentativeDatabase()
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        for (var index = 0; index < 80; index++)
        {
            var product = new Product
            {
                ProductCode = $"S8T05-{index:D3}",
                CurrentName = new string('x', 900),
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
        context.Inspections.Add(new Inspection
        {
            TaskId = task.Id, ProductId = firstProduct.Id, ProductCodeSnapshot = firstProduct.ProductCode,
            StageSnapshot = "discount_50", StockQtySnapshot = 1, InspectorName = "S8-T05", CheckDate = DateOnly.FromDateTime(DateTime.Today)
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

    private static void Truncate(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(stream.Length / 2);
    }

    private static void CorruptDataPage(string path)
    {
        long rootPage;
        using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT rootpage FROM sqlite_master WHERE type='table' AND name='products';";
            rootPage = Convert.ToInt64(command.ExecuteScalar());
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = (rootPage - 1) * 4096;
        stream.WriteByte(0);
    }

    private static void PartialFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..Math.Min(512, bytes.Length)]);
    }

    private static void Checkpoint(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T05", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertSafeSyntheticRoot(string root)
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T05"));
        var actual = Path.GetFullPath(root);
        Assert.StartsWith(expected + Path.DirectorySeparatorChar, actual, StringComparison.OrdinalIgnoreCase);
        Assert.False((File.GetAttributes(actual) & FileAttributes.ReparsePoint) != 0);
    }

}
