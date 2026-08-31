using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace StoreExpiryInspector.Infrastructure;

public static class DatabaseInitializer
{
    public static string GetDefaultDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoreExpiryInspector",
            "data",
            "app.db");
    }

    public static string GetDefaultBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoreExpiryInspector",
            "backups");
    }

    public static StoreDbContext CreateContext(string? databasePath = null)
    {
        var path = string.IsNullOrWhiteSpace(databasePath)
            ? GetDefaultDatabasePath()
            : Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                ForeignKeys = true
            }.ToString())
            .Options;
        return new StoreDbContext(options);
    }

    public static void Initialize(string? databasePath = null)
    {
        using var context = CreateContext(databasePath);
        context.Database.Migrate();
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
    }
}
