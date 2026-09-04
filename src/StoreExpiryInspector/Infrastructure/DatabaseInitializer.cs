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
        var path = string.IsNullOrWhiteSpace(databasePath)
            ? GetDefaultDatabasePath()
            : Path.GetFullPath(databasePath);
        ValidateExistingDatabase(path);
        using var context = CreateContext(databasePath);
        context.Database.Migrate();
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
    }

    private static void ValidateExistingDatabase(string path)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length == 0)
        {
            throw new InvalidDataException("Existing SQLite database is empty.");
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
            connection.Open();
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            using var reader = integrity.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
            {
                throw new InvalidDataException("SQLite integrity check failed.");
            }

            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var foreignKeyReader = foreignKeys.ExecuteReader();
            if (foreignKeyReader.Read())
            {
                throw new InvalidDataException("SQLite foreign key check failed.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException or IOException)
        {
            throw new InvalidDataException("Existing SQLite database cannot be validated.", exception);
        }
    }
}
