using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S21T01SlimSetupTests
{
    [Fact]
    public void InstallerKeepsOneSourceWithExplicitSlimAndFullModes()
    {
        var installer = File.ReadAllText(Path.Combine(Root(), "installer", "StoreExpiryInspector.iss"));

        Assert.Contains("#ifdef SAME_SCHEMA_SLIM", installer, StringComparison.Ordinal);
        Assert.Contains("#ifdef CROSS_SCHEMA_FULL", installer, StringComparison.Ordinal);
        Assert.Contains("#error Define exactly one Setup mode", installer, StringComparison.Ordinal);
        Assert.Contains("VersionIsOlderThanMinimum(AppVersion)", installer, StringComparison.Ordinal);
        Assert.Contains("当前安装版本过旧，不能直接升级到 v{#AppVersion}，请先升级到 v{#MinimumDirectVersion} 后再安装。", installer, StringComparison.Ordinal);
        Assert.Contains("--installer-preflight", installer, StringComparison.Ordinal);
        Assert.Contains("Source: \"{#UpdatePackage}\"", installer, StringComparison.Ordinal);
        Assert.Contains("Source: \"{#UpdateManifest}\"", installer, StringComparison.Ordinal);
        Assert.Contains("Source: \"{#UpdateSignature}\"", installer, StringComparison.Ordinal);
        Assert.Equal(1, Count(installer, "Source: \"{#PayloadDir}\\*\"; DestDir: \"{app}\\app\""));
        Assert.Equal(1, Count(installer, "Source: \"{#PayloadDir}\\*\"; DestDir: \"{tmp}\\StoreExpiryInspector-preflight\""));
    }

    [Fact]
    public void DatabaseFixtureProbe()
    {
        var database = Environment.GetEnvironmentVariable("S21_T01_DATABASE");
        if (string.IsNullOrWhiteSpace(database)) return;

        var action = Environment.GetEnvironmentVariable("S21_T01_DATABASE_ACTION");
        if (action == "ADD_HIGHER")
        {
            using var connection = new SqliteConnection($"Data Source={database}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory(MigrationId, ProductVersion) VALUES ('99999999999999_S21Future', '99.0.0');";
            command.ExecuteNonQuery();
            SqliteConnection.ClearPool(connection);
            return;
        }

        Assert.Equal("VALIDATE", action);
        var resultPath = Environment.GetEnvironmentVariable("S21_T01_DATABASE_RESULT")!;
        using var validation = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true, Pooling = false }.ToString());
        validation.Open();
        Assert.Equal("ok", Scalar(validation, "PRAGMA integrity_check;"));
        Assert.Equal(0L, Convert.ToInt64(Scalar(validation, "SELECT COUNT(*) FROM pragma_foreign_key_check;")));
        var migrations = Strings(validation, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId COLLATE BINARY;");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database)));
        File.WriteAllText(resultPath, JsonSerializer.Serialize(new { fingerprint, integrity = "ok", foreignKeys = 0, migrations }));
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    private static string[] Strings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static int Count(string value, string fragment) => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string Root()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "StoreExpiryInspector.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException();
    }
}
