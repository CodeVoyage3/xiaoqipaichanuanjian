using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Application.Updates;
using System.Windows;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

namespace StoreExpiryInspector.S9T07Fixture;

public partial class FixtureApp : System.Windows.Application
{
    [STAThread]
    public static void Main()
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (arguments is ["--s9-t07-kill", var pidText, var startedText, var executable] && int.TryParse(pidText, out var pid) && DateTimeOffset.TryParse(startedText, out var started))
        {
            using var process = Process.GetProcessById(pid);
            if (Math.Abs((process.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) > 1 || !string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Exact actor identity mismatch.");
            process.Kill(entireProcessTree: true); if (!process.WaitForExit(5000)) throw new TimeoutException("Exact actor did not exit."); return;
        }
        if (arguments is ["--s9-t07-lock-probe", var database, var marker])
        {
            WriteProbeMarker(marker, "attempting");
            try
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 0 }.ToString()); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM __EFMigrationsHistory;"; command.ExecuteScalar(); WriteProbeMarker(marker, "opened");
            }
            catch (SqliteException) { WriteProbeMarker(marker, "blocked"); }
            return;
        }
        new FixtureApp().Run();
    }

    private static void WriteProbeMarker(string path, string state)
    {
        var bytes = Encoding.UTF8.GetBytes(state);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length == 6 && e.Args[0] == "--data-root" && e.Args[2] == "--allow-existing-isolated-data-root" && e.Args[3] == "--s9-t07-normal-launch" && Guid.TryParse(e.Args[4], out _) && Guid.TryParse(e.Args[5], out _))
            {
                var normalRoot = Path.GetFullPath(e.Args[1]); var normalOperation = e.Args[4]; var normalToken = e.Args[5];
                var normalMutex = new Mutex(false, "Local\\StoreExpiryInspector.S9T07Fixture.Normal." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalRoot.ToUpperInvariant())))); bool ownsNormalMutex;
                try { ownsNormalMutex = normalMutex.WaitOne(0); } catch (AbandonedMutexException) { ownsNormalMutex = true; }
                if (!ownsNormalMutex) { normalMutex.Dispose(); Shutdown(1); return; }
                var beforeIdentify = Environment.GetEnvironmentVariable("S9_T07_NORMAL_BEFORE_IDENTIFY_MARKER"); var releaseBeforeIdentify = Environment.GetEnvironmentVariable("S9_T07_NORMAL_BEFORE_IDENTIFY_RELEASE");
                if (!string.IsNullOrWhiteSpace(beforeIdentify) && !string.IsNullOrWhiteSpace(releaseBeforeIdentify))
                {
                    File.WriteAllText(beforeIdentify, $"{Environment.ProcessId}|{Process.GetCurrentProcess().StartTime.ToUniversalTime():O}");
                    for (var until = DateTime.UtcNow.AddSeconds(30); !File.Exists(releaseBeforeIdentify) && DateTime.UtcNow < until; Thread.Sleep(50)) { }
                    if (!File.Exists(releaseBeforeIdentify)) { normalMutex.ReleaseMutex(); normalMutex.Dispose(); Shutdown(1); return; }
                }
                _ = NormalLaunchHandshake.Identify(normalRoot, normalOperation, normalToken, AppContext.BaseDirectory, schema => SchemaUpgradeSnapshots.ValidateMetadata(normalRoot, schema.Snapshot!));
                var identified = Environment.GetEnvironmentVariable("S9_T07_NORMAL_IDENTIFIED_MARKER"); if (!string.IsNullOrWhiteSpace(identified)) File.WriteAllText(identified, $"{Environment.ProcessId}|{Process.GetCurrentProcess().StartTime.ToUniversalTime():O}");
                if (int.TryParse(Environment.GetEnvironmentVariable("S9_T07_NORMAL_DELAY_LOADED_MS"), out var delay) && delay is >= 100 and <= 30_000) Thread.Sleep(delay);
                var normalWindow = new Window { Width = 1, Height = 1, ShowInTaskbar = false, Visibility = Visibility.Hidden };
                normalWindow.Loaded += (_, _) => { NormalLaunchHandshake.Loaded(normalRoot, normalOperation, normalToken); File.WriteAllText(Path.Combine(normalRoot, "updates", normalOperation, "normal-loaded.marker"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, startedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime() })); };
                normalWindow.Closed += (_, _) => { normalMutex.ReleaseMutex(); normalMutex.Dispose(); };
                normalWindow.Show(); return;
            }
            var (root, operation, token, target) = Arguments(e.Args);
            var migrations = FixtureMigrations.Target(target);
            var authorization = UpgradeHealthAck.WaitForSchemaAuthorization(root, operation, token, migrations, TimeSpan.FromSeconds(30));
            if (authorization is null) { Shutdown(1); return; }
            SchemaUpgradeSnapshots.TakeOverFrozenSource(root, authorization.SourceSha256, authorization.SourceMigrations, connection => FixtureMigrations.Apply(connection, migrations));
            if (!FixtureMigrations.VerifyCoreRead(Path.Combine(root, "data", "app.db"), migrations)) { Shutdown(1); return; }
            WriteMigrationMarker(root, operation, UpgradeHealthAck.VerifyDatabase(Path.Combine(root, "data", "app.db"), includeWal: true));
            UpgradeHealthAck.WriteSchemaMigrationApplied(root, operation, token, migrations);
            if (Environment.GetEnvironmentVariable("S9_T07_FIXTURE_PAUSE_AFTER_MIGRATION_APPLIED") == "1") Thread.Sleep(Timeout.Infinite);
            if (Environment.GetEnvironmentVariable("S9_T07_FIXTURE_FAIL_AFTER_MIGRATION") == "1") { Shutdown(1); return; }
            var window = new Window { Width = 1, Height = 1, ShowInTaskbar = false, Visibility = Visibility.Hidden };
            window.Loaded += (_, _) => { UpgradeHealthAck.WriteSchema(root, operation, token, "1.0.4", migrations); Shutdown(); };
            window.Show();
        }
        catch (Exception exception)
        {
            if (e.Args.Length == 8 && Guid.TryParse(e.Args[4], out _) && Guid.TryParse(e.Args[5], out _))
            {
                try
                {
                    var root = Path.GetFullPath(e.Args[1]); var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), root);
                    if (Guid.TryParse(relative, out _) && Directory.Exists(Path.Combine(root, "updates", e.Args[4]))) File.WriteAllText(Path.Combine(root, "updates", e.Args[4], "fixture-failure.log"), exception.ToString());
                }
                catch { }
            }
            Shutdown(1);
        }
    }

    private static (string Root, string Operation, string Token, int Target) Arguments(string[] args)
    {
        if (args.Length != 8 || args[0] != "--data-root" || args[2] != "--allow-existing-isolated-data-root" || args[3] != "--s9-t07-verify" || !Guid.TryParse(args[4], out _) || !Guid.TryParse(args[5], out _) || args[6] != "--s9-t07-fixture-target" || !int.TryParse(args[7], out _)) throw new ArgumentException();
        var target = int.Parse(args[7]); if (target is not 10 and not 11) throw new ArgumentException();
        var root = Path.GetFullPath(args[1]); var temp = Path.GetFullPath(Path.GetTempPath()); var relative = Path.GetRelativePath(temp, root);
        if (root.StartsWith("\\\\", StringComparison.Ordinal) || root.IndexOf(':', 2) >= 0 || Path.IsPathRooted(relative) || !Guid.TryParse(relative, out _) || !Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException();
        return (root, args[4], args[5], target);
    }

    private static void WriteMigrationMarker(string root, string operation, IReadOnlyList<string> migrations)
    {
        var marker = Environment.GetEnvironmentVariable("S9_T07_FIXTURE_MIGRATION_MARKER");
        if (string.IsNullOrWhiteSpace(marker)) return;
        var path = Path.GetFullPath(marker); if (!string.Equals(Path.GetDirectoryName(path), Path.Combine(root, "updates", operation), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { operationId = operation, migrations }); using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes); stream.Flush(true);
    }
}

internal static class FixtureMigrations
{
    internal static readonly string[] Source = ["20260826123739_InitialCreate", "20260826130822_AddTasksAndDrafts", "20260826135612_AddInspectionHistory", "20260826142429_AddInventoryAdjustments", "20260826152131_AddImportPersistence", "20260826155455_AddBackupMetadata", "20260826162033_AddSettingsAndAppState", "20260826170403_AddLifecycleEvents", "20260901155124_AddPolicyAndBaselineFoundation"];
    internal static IReadOnlyList<string> Target(int target) => target == 10 ? [.. Source, "20260905120000_S9T07Fixture10"] : [.. Source, "20260905120000_S9T07Fixture10", "20260905121000_S9T07Fixture11"];
    internal static void Apply(SqliteConnection connection, IReadOnlyList<string> target)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS s9t07_fixture (id INTEGER PRIMARY KEY, payload BLOB NOT NULL, stage INTEGER NOT NULL DEFAULT 10);");
        Execute(connection, transaction, "INSERT OR IGNORE INTO s9t07_fixture(id,payload) VALUES (1,$blob);", "$blob", Enumerable.Range(0, 131073).Select(i => (byte)(i % 251)).ToArray());
        if (target.Count == 11) Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_s9t07_fixture_stage ON s9t07_fixture(stage); UPDATE s9t07_fixture SET stage=11;");
        foreach (var migration in target.Skip(Source.Length)) Execute(connection, transaction, "INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ($migration,'1.0.4');", "$migration", migration);
        transaction.Commit();
    }
    internal static bool VerifyCoreRead(string database, IReadOnlyList<string> target)
    {
        if (!UpgradeHealthAck.VerifyDatabase(database, includeWal: true).SequenceEqual(target, StringComparer.Ordinal)) return false;
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT length(payload),stage FROM s9t07_fixture WHERE id=1;";
        using var reader = command.ExecuteReader(); return reader.Read() && reader.GetInt32(0) == 131073 && reader.GetInt32(1) == (target.Count == 11 ? 11 : 10);
    }
    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, string? parameter = null, object? value = null) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; if (parameter is not null) command.Parameters.AddWithValue(parameter, value!); command.ExecuteNonQuery(); }
}

[Migration("20260826123739_InitialCreate")] internal sealed class M01 { }
[Migration("20260826130822_AddTasksAndDrafts")] internal sealed class M02 { }
[Migration("20260826135612_AddInspectionHistory")] internal sealed class M03 { }
[Migration("20260826142429_AddInventoryAdjustments")] internal sealed class M04 { }
[Migration("20260826152131_AddImportPersistence")] internal sealed class M05 { }
[Migration("20260826155455_AddBackupMetadata")] internal sealed class M06 { }
[Migration("20260826162033_AddSettingsAndAppState")] internal sealed class M07 { }
[Migration("20260826170403_AddLifecycleEvents")] internal sealed class M08 { }
[Migration("20260901155124_AddPolicyAndBaselineFoundation")] internal sealed class M09 { }
[Migration("20260905120000_S9T07Fixture10")] internal sealed class M10 { }
