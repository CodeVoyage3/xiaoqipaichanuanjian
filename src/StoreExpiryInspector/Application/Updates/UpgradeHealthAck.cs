using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace StoreExpiryInspector.Application.Updates;

public static class UpgradeHealthAck
{
    public static IReadOnlyList<string> VerifyDatabase(string databasePath, bool includeWal)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = includeWal ? databasePath : new Uri(databasePath).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, ForeignKeys = true, Pooling = false }.ToString()); connection.Open();
        using (var integrity = connection.CreateCommand()) { integrity.CommandText = "PRAGMA integrity_check;"; if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Upgrade integrity check failed."); }
        using (var foreignKeys = connection.CreateCommand()) { foreignKeys.CommandText = "PRAGMA foreign_key_check;"; using var reader = foreignKeys.ExecuteReader(); if (reader.Read()) throw new InvalidDataException("Upgrade foreign key check failed."); }
        using var migrationsCommand = connection.CreateCommand(); migrationsCommand.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId COLLATE BINARY;"; using var migrationsReader = migrationsCommand.ExecuteReader(); var migrations = new List<string>(); while (migrationsReader.Read()) migrations.Add(migrationsReader.GetString(0));
        if (!SchemaUpdateJournal.IsValidMigrationList(migrations)) throw new InvalidDataException("Upgrade migration health check failed."); return migrations;
    }
    public static void Write(string isolatedRoot, string operationId, string version, int migrationCount, string lastMigration)
    {
        if (!Guid.TryParse(operationId, out _)) throw new InvalidOperationException("Invalid update operation.");
        var directory = Path.Combine(isolatedRoot, "updates", operationId);
        ValidateOrdinaryDirectory(isolatedRoot);
        Directory.CreateDirectory(directory);
        ValidateOrdinaryDirectory(directory);
        var process = Process.GetCurrentProcess();
        var ack = JsonSerializer.Serialize(new { operationId, version, pid = process.Id, startedUtc = process.StartTime.ToUniversalTime().ToString("O"), migrationCount, lastMigration, integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true });
        var target = Path.Combine(directory, "health-ack.json");
        DurableFile.Replace(target, ack);
    }

    public static SchemaCandidateAuthorization? WaitForSchemaAuthorization(string root, string operationId, string launchToken, IReadOnlyList<string> migrations, TimeSpan timeout)
    {
        var process = Process.GetCurrentProcess(); var identity = new SchemaCandidateIdentity(operationId, launchToken, process.Id, process.StartTime.ToUniversalTime(), migrations);
        SchemaCandidateHandshake.Validate(identity, operationId, launchToken);
        var directory = Path.Combine(root, "updates", operationId); ValidateOrdinaryDirectory(root); Directory.CreateDirectory(directory); ValidateOrdinaryDirectory(directory);
        WriteAtomically(Path.Combine(directory, "candidate-identity.json"), JsonSerializer.Serialize(identity, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var authorizationPath = Path.Combine(directory, "candidate-authorization.json"); var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                var text = File.ReadAllText(authorizationPath); using var document = JsonDocument.Parse(text); UpdateProtocolJson.RequireObject(document.RootElement, "operationId", "launchToken", "pid", "startedUtc", "migrations", "sourceSha256", "sourceMigrations");
                var authorization = JsonSerializer.Deserialize<SchemaCandidateAuthorization>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (authorization is not null) { SchemaCandidateHandshake.Validate(authorization, identity); return authorization; }
            }
            catch (IOException) { } catch (JsonException) { }
            Thread.Sleep(100);
        }
        return null;
    }

    public static void WriteSchema(string root, string operationId, string launchToken, string version, IReadOnlyList<string> migrations)
    {
        if (!SchemaUpdateJournal.IsValidMigrationList(migrations)) throw new InvalidDataException("迁移健康确认无效。");
        var process = Process.GetCurrentProcess();
        var ack = JsonSerializer.Serialize(new { operationId, launchToken, version, pid = process.Id, startedUtc = process.StartTime.ToUniversalTime().ToString("O"), migrations, migrationCount = migrations.Count, lastMigration = migrations[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true });
        var directory = Path.Combine(root, "updates", operationId); ValidateOrdinaryDirectory(directory); WriteAtomically(Path.Combine(directory, "health-ack.json"), ack);
    }

    public static void WriteSchemaMigrationApplied(string root, string operationId, string launchToken, IReadOnlyList<string> migrations)
    {
        if (!SchemaUpdateJournal.IsValidMigrationList(migrations)) throw new InvalidDataException("迁移完成确认无效。");
        var process = Process.GetCurrentProcess(); var directory = Path.Combine(root, "updates", operationId); ValidateOrdinaryDirectory(directory);
        WriteAtomically(Path.Combine(directory, "migration-applied.json"), JsonSerializer.Serialize(new { operationId, launchToken, pid = process.Id, startedUtc = process.StartTime.ToUniversalTime().ToString("O"), migrations }));
    }

    private static void WriteAtomically(string path, string content)
    {
        DurableFile.Replace(path, content);
    }

    private static void ValidateOrdinaryDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Unsafe ACK path.");
    }
}
