using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InstallerPreflightTests
{
    [Fact]
    public void No_database_is_allowed_without_creating_a_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var result = InstallerPreflight.Check(root);
        Assert.Equal(InstallerPreflightCode.NoDatabase, result.Code);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Current_database_is_allowed_read_only_without_changing_database_or_sidecars()
    {
        using var database = SyntheticDatabase.Create();
        var before = Fingerprints(database.Path);

        var result = InstallerPreflight.Check(database.Root);

        Assert.Equal(InstallerPreflightCode.CurrentMigration9Healthy, result.Code);
        Assert.Equal(before, Fingerprints(database.Path));
    }

    [Fact]
    public void Older_unknown_and_corrupt_databases_are_blocked()
    {
        using var database = SyntheticDatabase.Create();
        Execute(database.Path, "DELETE FROM __EFMigrationsHistory WHERE MigrationId=(SELECT MAX(MigrationId) FROM __EFMigrationsHistory);");
        Assert.Equal(InstallerPreflightCode.OlderSchema, InstallerPreflight.Check(database.Root).Code);
        Execute(database.Path, "INSERT INTO __EFMigrationsHistory(MigrationId, ProductVersion) VALUES ('99999999999999_Future', '10.0.0');");
        Assert.Equal(InstallerPreflightCode.NewerOrUnknownSchema, InstallerPreflight.Check(database.Root).Code);
        ClearPool(database.Path);
        File.WriteAllBytes(database.Path, [1, 2, 3]);
        Assert.Equal(InstallerPreflightCode.CorruptOrUnreadable, InstallerPreflight.Check(database.Root).Code);
    }

    [Fact]
    public void Existing_isolated_root_requires_explicit_reuse_flag()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var root = Path.Combine(temp, Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() => RuntimeDataRoot.EnsureOrdinaryTree(root, temp));
            RuntimeDataRoot.EnsureOrdinaryTree(root, temp, allowExisting: true);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static Dictionary<string, string> Fingerprints(string database)
    {
        ClearPool(database);
        return new[] { database, database + "-wal", database + "-shm" }
        .Where(File.Exists).ToDictionary(path => path, path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
        SqliteConnection.ClearPool(connection);
    }

    private static void ClearPool(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString());
        SqliteConnection.ClearPool(connection);
    }

    private sealed class SyntheticDatabase : IDisposable
    {
        private SyntheticDatabase(string root) { Root = root; Path = System.IO.Path.Combine(root, "data", "app.db"); }
        public string Root { get; }
        public string Path { get; }
        public static SyntheticDatabase Create()
        {
            var database = new SyntheticDatabase(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()));
            DatabaseInitializer.Initialize(database.Path);
            ClearPool(database.Path);
            return database;
        }
        public void Dispose()
        {
            ClearPool(Path);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
