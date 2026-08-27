using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Tests;

public sealed class SqliteTestDatabase : IDisposable
{
    private SqliteTestDatabase(string directory)
    {
        Directory = directory;
        Path = System.IO.Path.Combine(directory, "app.db");
    }

    public string Directory { get; }

    public string Path { get; }

    public static SqliteTestDatabase Create()
    {
        var database = CreateEmpty();
        DatabaseInitializer.Initialize(database.Path);
        return database;
    }

    public static SqliteTestDatabase CreateEmpty()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "StoreExpiryInspectorTests",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        return new SqliteTestDatabase(directory);
    }

    public StoreDbContext Open() => DatabaseInitializer.CreateContext(Path);

    public static HashSet<string> ReadSchemaNames(StoreDbContext context, string type)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$type";
            parameter.Value = type;
            command.Parameters.Add(parameter);
            using var reader = command.ExecuteReader();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static string ReadIndexSql(StoreDbContext context, string indexName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = indexName;
            command.Parameters.Add(parameter);
            return (string?)command.ExecuteScalar() ?? string.Empty;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static string ReadTableSql(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            return (string?)command.ExecuteScalar() ?? string.Empty;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static string[] ReadTableColumns(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }

            return columns.ToArray();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static string[] ReadForeignKeyDeleteActions(StoreDbContext context, string tableName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list({tableName})";
            using var reader = command.ExecuteReader();
            var actions = new List<string>();
            while (reader.Read())
            {
                actions.Add(reader.GetString(6));
            }

            return actions.ToArray();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static string ReadPragma(StoreDbContext context, string pragma)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA {pragma}";
            return (string?)command.ExecuteScalar() ?? string.Empty;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public void Dispose()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            ForeignKeys = true
        }.ToString());
        SqliteConnection.ClearPool(connection);
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
