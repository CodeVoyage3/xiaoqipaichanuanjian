using System.Globalization;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class PreImportSnapshotServiceTests
{
    private static readonly string[] ExpectedTables =
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
        "tasks",
        "__EFMigrationsHistory"
    };

    private static readonly string[] ExpectedMigrations =
    {
        "20260826123739_InitialCreate",
        "20260826130822_AddTasksAndDrafts",
        "20260826135612_AddInspectionHistory",
        "20260826142429_AddInventoryAdjustments",
        "20260826152131_AddImportPersistence",
        "20260826155455_AddBackupMetadata",
        "20260826162033_AddSettingsAndAppState",
        "20260826170403_AddLifecycleEvents"
    };

    [Fact]
    public void CreatesIndependentVerifiedWalSnapshotWithoutChangingSourceRows()
    {
        using var database = SqliteTestDatabase.Create();
        using var walConnection = Open(database.Path, SqliteOpenMode.ReadWrite);
        Assert.Equal("wal", ExecuteScalar(walConnection, "PRAGMA journal_mode;"));
        SeedRepresentativeRows(database);
        var before = ReadRows(database.Path);
        var walPath = database.Path + "-wal";
        Assert.True(File.Exists(walPath));
        Assert.True(new FileInfo(walPath).Length > 0);

        var result = new PreImportSnapshotService().Create(
            database.Path,
            Path.Combine(database.Directory, "snapshots"));

        Assert.True(result.CanProceed, $"{result.Code}: {result.SafeSummary}");
        Assert.Equal(PreImportSnapshotCodes.Verified, result.Code);
        Assert.NotEmpty(result.SafeSummary);
        var metadata = Assert.IsType<PreImportSnapshotMetadata>(result.Metadata);
        Assert.Equal("pre_import", metadata.BackupType);
        Assert.Equal(Path.GetFullPath(database.Path), metadata.SourceDatabasePath);
        Assert.Equal(Path.GetFullPath(metadata.SnapshotPath), metadata.SnapshotPath);
        Assert.Equal(metadata.SnapshotPath, metadata.FilePath);
        Assert.Equal("verified", metadata.VerificationStatus);
        Assert.Equal(64, metadata.Sha256.Length);
        Assert.Equal(metadata.Sha256, ComputeSha256(metadata.SnapshotPath));
        Assert.True(metadata.FileSize > 0);
        Assert.Equal(ExpectedMigrations, metadata.MigrationIds);
        Assert.True(File.Exists(metadata.SnapshotPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(metadata.SnapshotPath)!, "*.tmp"));
        Assert.True(new PreImportSnapshotService().ValidateSnapshot(metadata));

        using var snapshot = Open(metadata.SnapshotPath, SqliteOpenMode.ReadOnly);
        Assert.Equal("ok", ExecuteScalar(snapshot, "PRAGMA quick_check;"));
        Assert.Empty(ReadRowsFromQuery(snapshot, "PRAGMA foreign_key_check;"));
        Assert.True(Convert.ToInt64(ExecuteScalar(snapshot, "PRAGMA page_count;"), CultureInfo.InvariantCulture) > 0);
        Assert.Equal(
            ExpectedTables.Append("__EFMigrationsLock").OrderBy(name => name, StringComparer.Ordinal),
            ReadTableNames(snapshot));
        Assert.Equal(ExpectedMigrations, ReadMigrationIds(snapshot));
        AssertRowsEqual(before, ReadRows(database.Path));
        Assert.Equal("SKU-SNAPSHOT-1", ReadSingleValue(snapshot, "SELECT product_code FROM products;"));
        Assert.Equal(37L, ReadSingleValue(snapshot, "SELECT current_arrival_qty FROM batches;"));
        Assert.Equal("source.xlsx", ReadSingleValue(snapshot, "SELECT source_file_name FROM imports;"));
        Assert.Equal("pre-existing", ReadSingleValue(snapshot, "SELECT verification_status FROM backups;"));
        Assert.Equal("open", ReadSingleValue(snapshot, "SELECT status FROM tasks;"));
        Assert.Equal("product_stock_zero", ReadSingleValue(snapshot, "SELECT event_type FROM lifecycle_events;"));
    }

    [Fact]
    public void ValidatesSnapshotAfterSourceFilesAreRemoved()
    {
        using var database = SqliteTestDatabase.Create();
        var service = new PreImportSnapshotService();
        PreImportSnapshotMetadata metadata;

        using (var walConnection = Open(database.Path, SqliteOpenMode.ReadWrite))
        {
            Assert.Equal("wal", ExecuteScalar(walConnection, "PRAGMA journal_mode;"));
            SeedRepresentativeRows(database);
            metadata = Assert.IsType<PreImportSnapshotMetadata>(service.Create(
                database.Path,
                Path.Combine(database.Directory, "snapshots")).Metadata);
        }

        RemoveSourceFiles(database);

        Assert.Equal(Path.GetFullPath(database.Path), metadata.SourceDatabasePath);
        Assert.False(File.Exists(metadata.SourceDatabasePath));
        Assert.True(service.ValidateSnapshot(metadata));
        using var snapshot = Open(metadata.SnapshotPath, SqliteOpenMode.ReadOnly);
        Assert.Equal("SKU-SNAPSHOT-1", ReadSingleValue(snapshot, "SELECT product_code FROM products;"));
        Assert.Equal(ExpectedMigrations, ReadMigrationIds(snapshot));
    }

    [Fact]
    public void RejectsMissingLockedAndCorruptSourcesWithoutPublishing()
    {
        using (var missing = SqliteTestDatabase.CreateEmpty())
        {
            var result = new PreImportSnapshotService().Create(
                missing.Path,
                Path.Combine(missing.Directory, "snapshots"));

            AssertFailure(result, PreImportSnapshotCodes.SourceMissing, missing.Directory);
        }

        using (var locked = SqliteTestDatabase.Create())
        {
            SqliteConnection.ClearAllPools();
            using var lockStream = new FileStream(locked.Path, FileMode.Open, FileAccess.Read, FileShare.None);
            var result = new PreImportSnapshotService().Create(
                locked.Path,
                Path.Combine(locked.Directory, "snapshots"));

            Assert.False(result.CanProceed);
            Assert.Null(result.Metadata);
            Assert.Equal(PreImportSnapshotCodes.SourceUnavailable, result.Code);
            Assert.Empty(Directory.GetFiles(locked.Directory, "pre-import-*.db", SearchOption.AllDirectories));
        }

        using (var corrupt = SqliteTestDatabase.CreateEmpty())
        {
            File.WriteAllBytes(corrupt.Path, new byte[] { 0x53, 0x51, 0x4c, 0x69, 0x74, 0x65 });
            var result = new PreImportSnapshotService().Create(
                corrupt.Path,
                Path.Combine(corrupt.Directory, "snapshots"));

            Assert.False(result.CanProceed);
            Assert.Null(result.Metadata);
            Assert.Equal(PreImportSnapshotCodes.SourceUnavailable, result.Code);
            Assert.Empty(Directory.GetFiles(corrupt.Directory, "*.tmp", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void RejectsUnavailableDestinationAndPreservesExistingFiles()
    {
        using var database = SqliteTestDatabase.Create();
        var existing = Path.Combine(database.Directory, "keep.db");
        File.WriteAllBytes(existing, new byte[] { 1, 2, 3, 4 });
        var destinationFile = Path.Combine(database.Directory, "not-a-directory");
        File.WriteAllText(destinationFile, "keep");

        var result = new PreImportSnapshotService().Create(database.Path, destinationFile);

        AssertFailure(result, PreImportSnapshotCodes.DestinationUnavailable, database.Directory);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(existing));
        Assert.Equal("keep", File.ReadAllText(destinationFile));
    }

    [Fact]
    public void ValidatesMissingSchemaAsBlockedAndCleansOnlyItsTemporaryFile()
    {
        using var database = SqliteTestDatabase.CreateEmpty();
        using (var connection = Open(database.Path, SqliteOpenMode.ReadWriteCreate))
        {
        }

        var destination = Path.Combine(database.Directory, "snapshots");
        Directory.CreateDirectory(destination);
        var existingPath = Path.Combine(destination, "keep.db");
        var existingBytes = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(existingPath, existingBytes);

        var result = new PreImportSnapshotService().Create(database.Path, destination);

        AssertFailure(result, PreImportSnapshotCodes.VerificationFailed, database.Directory);
        Assert.Equal(existingBytes, File.ReadAllBytes(existingPath));
        Assert.Empty(Directory.GetFiles(destination, "*.tmp"));
        Assert.Empty(Directory.GetFiles(destination, "pre-import-*.db"));
    }

    [Fact]
    public void CreatesTwoNonOverwritingSnapshotsAndDetectsTampering()
    {
        using var database = SqliteTestDatabase.Create();
        SeedRepresentativeRows(database);
        var destination = Path.Combine(database.Directory, "snapshots");
        var service = new PreImportSnapshotService();

        var first = Assert.IsType<PreImportSnapshotMetadata>(service.Create(database.Path, destination).Metadata);
        var second = Assert.IsType<PreImportSnapshotMetadata>(service.Create(database.Path, destination).Metadata);

        Assert.NotEqual(first.SnapshotPath, second.SnapshotPath);
        Assert.True(File.Exists(first.SnapshotPath));
        Assert.True(File.Exists(second.SnapshotPath));
        Assert.Equal(2, Directory.GetFiles(destination, "pre-import-*.db").Length);
        Assert.Empty(Directory.GetFiles(destination, "*.tmp"));
        Assert.True(service.ValidateSnapshot(first));

        using (var stream = new FileStream(first.SnapshotPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            stream.Position = stream.Length - 1;
            var original = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(original ^ 0xff));
        }

        Assert.False(service.ValidateSnapshot(first));
        Assert.True(service.ValidateSnapshot(second));

        RemoveSourceFiles(database);
        Assert.False(File.Exists(second.SourceDatabasePath));
        using (var stream = new FileStream(second.SnapshotPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            stream.Position = stream.Length - 1;
            var original = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(original ^ 0xff));
        }

        Assert.False(service.ValidateSnapshot(second));
    }

    private static void SeedRepresentativeRows(SqliteTestDatabase database)
    {
        using var context = database.Open();
        var product = new Product
        {
            ProductCode = "SKU-SNAPSHOT-1",
            CurrentName = "Snapshot product",
            CurrentBarcode = "690000000001",
            ExcelStockQty = 37,
            EffectiveStockQty = 37,
            EffectiveStockSource = "excel"
        };
        context.Products.Add(product);
        context.SaveChanges();

        var batch = new Batch
        {
            ProductId = product.Id,
            ProductionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2026, 12, 31),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 37,
            MaxArrivalQty = 40,
            SourceDiscountReference = "discount",
            CurrentStage = "discount_50"
        };
        context.Batches.Add(batch);
        context.SaveChanges();

        var task = new ProductTask { ProductId = product.Id };
        context.Tasks.Add(task);
        context.SaveChanges();

        var import = new ImportRecord
        {
            SourceFileName = "source.xlsx",
            SourceFileSha256 = new string('a', 64),
            ParsedAtUtc = new DateTime(2026, 8, 27, 1, 2, 3, DateTimeKind.Utc),
            Status = "Succeeded",
            ProductCount = 1,
            BatchCount = 1,
            NewProductCount = 1,
            NewBatchCount = 1
        };
        context.Imports.Add(import);
        context.SaveChanges();

        product.LastSeenImportId = import.Id;
        batch.LastSeenImportId = import.Id;
        context.SaveChanges();

        context.BackupRecords.Add(new BackupRecord
        {
            BackupType = "manual",
            FilePath = "pre-existing.db",
            Sha256 = new string('b', 64),
            CreatedAtUtc = new DateTime(2026, 8, 27, 1, 2, 3, DateTimeKind.Utc),
            VerificationStatus = "pre-existing"
        });
        context.LifecycleEvents.Add(new LifecycleEvent
        {
            ProductId = product.Id,
            BatchId = batch.Id,
            EventType = "product_stock_zero",
            Reason = "test",
            OccurredAtUtc = new DateTime(2026, 8, 27, 1, 2, 3, DateTimeKind.Utc),
            SourceImportId = import.Id
        });
        context.SaveChanges();
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static object ReadSingleValue(SqliteConnection connection, string sql) =>
        ExecuteScalar(connection, sql) ?? throw new InvalidOperationException("Expected one value.");

    private static string[] ReadTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static string[] ReadMigrationIds(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids.ToArray();
    }

    private static List<string> ReadRowsFromQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join(
                "\u001f",
                Enumerable.Range(0, reader.FieldCount).Select(index => ToStableValue(reader.GetValue(index)))));
        }

        return rows;
    }

    private static Dictionary<string, string[]> ReadRows(string path)
    {
        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var table in ReadTableNames(connection))
        {
            var rows = ReadRowsFromQuery(connection, $"SELECT * FROM {SqliteIdentifier(table)} ORDER BY rowid;");
            result.Add(table, rows.ToArray());
        }

        return result;
    }

    private static string SqliteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string ToStableValue(object value) => value switch
    {
        DBNull => "<null>",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static void AssertRowsEqual(
        IReadOnlyDictionary<string, string[]> expected,
        IReadOnlyDictionary<string, string[]> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(key => key, StringComparer.Ordinal), actual.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (var pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out var rows));
            Assert.NotNull(rows);
            Assert.Equal(pair.Value, rows!);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RemoveSourceFiles(SqliteTestDatabase database)
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { database.Path, database.Path + "-wal", database.Path + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void AssertFailure(PreImportSnapshotResult result, string code, string root)
    {
        Assert.False(result.CanProceed);
        Assert.Equal(code, result.Code);
        Assert.NotEmpty(result.SafeSummary);
        Assert.Null(result.Metadata);
        Assert.Empty(Directory.GetFiles(root, "pre-import-*.db", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }
}
