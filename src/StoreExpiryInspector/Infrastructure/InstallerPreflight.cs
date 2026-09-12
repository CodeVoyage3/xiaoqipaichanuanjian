using Microsoft.Data.Sqlite;
using System.IO;
using System.Text;
using StoreExpiryInspector.UpdateSafety;
using StoreExpiryInspector.Application.Updates;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace StoreExpiryInspector.Infrastructure;

public enum InstallerPreflightCode
{
    NoDatabase = 1,
    CurrentSchemaHealthy = 2,
    OlderSchema = 10,
    NewerOrUnknownSchema = 11,
    CorruptOrUnreadable = 12,
    InvalidDataRoot = 13
}

public sealed record InstallerPreflightResult(InstallerPreflightCode Code, string Message)
{
    public bool Allowed => Code is InstallerPreflightCode.NoDatabase or InstallerPreflightCode.CurrentSchemaHealthy;
    public string CodeName => Code switch
    {
        InstallerPreflightCode.NoDatabase => "no_database",
        InstallerPreflightCode.CurrentSchemaHealthy => "current_schema_healthy",
        InstallerPreflightCode.OlderSchema => "older_schema",
        InstallerPreflightCode.NewerOrUnknownSchema => "newer_or_unknown_schema",
        InstallerPreflightCode.CorruptOrUnreadable => "corrupt_or_unreadable",
        _ => "invalid_data_root"
    };
}

public static class InstallerPreflight
{
    private const string Command = "--installer-preflight";
    private const string CrossSchemaCommand = "--installer-cross-schema";
    private const string CrossSchemaResultCommand = "--installer-cross-schema-result";
    private const string DataRootArgument = "--data-root";
    private static readonly IReadOnlyList<string> CurrentMigrations = CurrentSchemaIdentity.Migrations;
    private static readonly IReadOnlyList<string> HistoricalMigration9 = ["20260826123739_InitialCreate", "20260826130822_AddTasksAndDrafts", "20260826135612_AddInspectionHistory", "20260826142429_AddInventoryAdjustments", "20260826152131_AddImportPersistence", "20260826155455_AddBackupMetadata", "20260826162033_AddSettingsAndAppState", "20260826170403_AddLifecycleEvents", "20260901155124_AddPolicyAndBaselineFoundation"];

    public static bool TryHandle(string[] arguments, out int exitCode)
    {
        exitCode = 0;
        if (arguments.Length == 7 && arguments[0] == CrossSchemaResultCommand && arguments[1] == "--data-root" && arguments[3] == "--operation" && arguments[5] == "--install-root" && Guid.TryParse(arguments[4], out var operation) && Path.IsPathFullyQualified(arguments[2]) && Path.IsPathFullyQualified(arguments[6]))
        {
            exitCode = IsCompletedCrossSchemaJournal(Path.Combine(arguments[2], "updates", operation.ToString(), "journal.json"), arguments[6], arguments[2]) ? 0 : 1;
            return true;
        }
        if (arguments.Contains(CrossSchemaCommand, StringComparer.Ordinal))
        {
            exitCode = CrossSchema(arguments);
            return true;
        }
        if (!arguments.Contains(Command, StringComparer.Ordinal)) return false;

        var result = ParseAndCheck(arguments);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.Out.WriteLine($"{{\"code\":\"{result.CodeName}\",\"allowed\":{result.Allowed.ToString().ToLowerInvariant()},\"message\":\"{result.Message}\"}}");
        exitCode = result.Allowed ? 0 : (int)result.Code;
        return true;
    }

    private static bool IsCompletedCrossSchemaJournal(string path, string installRoot, string dataRoot)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("OperationId", out var operation) && Guid.TryParse(operation.GetString(), out var operationId) && operationId.ToString() == Path.GetFileName(Path.GetDirectoryName(path)) &&
                root.TryGetProperty("ProductId", out var product) && product.GetString() == "StoreExpiryInspector" &&
                root.TryGetProperty("InstallRoot", out var installed) && string.Equals(Path.GetFullPath(installed.GetString()!), Path.GetFullPath(installRoot), StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("DataRoot", out var data) && string.Equals(Path.GetFullPath(data.GetString()!), Path.GetFullPath(dataRoot), StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("AppPath", out var app) && string.Equals(Path.GetFullPath(app.GetString()!), Path.Combine(Path.GetFullPath(installRoot), "app"), StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("SourceVersion", out var source) && source.GetString() == "1.0.9" && root.TryGetProperty("TargetVersion", out var target) && target.GetString() == "1.1.0" &&
                root.TryGetProperty("Phase", out var phase) && IsNumericEnum(phase, 10) && root.TryGetProperty("Schema", out var schema) && schema.ValueKind == JsonValueKind.Object &&
                schema.TryGetProperty("Phase", out var schemaPhase) && IsNumericEnum(schemaPhase, (int)SchemaPhase.CandidateCommitted) &&
                schema.TryGetProperty("SourceMigrations", out var sourceMigrations) && IsMigrationSequence(sourceMigrations, HistoricalMigration9) &&
                schema.TryGetProperty("TargetMigrations", out var migrations) && IsMigrationSequence(migrations, CurrentSchemaIdentity.Migrations);
        }
        catch { return false; }
    }

    private static bool IsNumericEnum(JsonElement value, int expected) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var actual) && actual == expected;
    private static bool IsMigrationSequence(JsonElement value, IReadOnlyList<string> expected) => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected, StringComparer.Ordinal);

    private static int CrossSchema(string[] arguments)
    {
        try
        {
            if (arguments.Length != 11 || arguments[0] != CrossSchemaCommand) return 1;
            var values = arguments.Skip(1).Chunk(2).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
            if (values.Count != 5 || !values.TryGetValue("--data-root", out var dataRoot) || !values.TryGetValue("--install-root", out var installRoot) || !values.TryGetValue("--package", out var package) || !values.TryGetValue("--manifest", out var manifest) || !values.TryGetValue("--signature", out var signature)) return 1;
            var source = Check(dataRoot);
            var sourceMigrations = HistoricalMigration9;
            if (source.Code != InstallerPreflightCode.OlderSchema || !ReadExactMigrations(dataRoot).SequenceEqual(sourceMigrations, StringComparer.Ordinal) || FileVersionInfo.GetVersionInfo(Path.Combine(installRoot, "app", "StoreExpiryInspector.exe")).ProductVersion?.Split('+')[0] != "1.0.9") return 10;
            var options = CrossSchemaTrustAnchor();
            var verified = new SignedUpdatePackageDownloader(options: options).PrepareEmbedded(package, manifest, signature, new Version(1, 0, 9), sourceMigrations[^1], CancellationToken.None);
            if (verified.Outcome != UpdatePackageOutcome.Verified || verified.Package is null) return 11;
            using var self = Process.GetCurrentProcess();
            var prepared = new UpdateInstallationPreparer(new SignedUpdatePackageDownloader(options: options)).PrepareForInstaller(verified.Package, self, installRoot, dataRoot, "1.0.9", sourceMigrations, CancellationToken.None, IsS9T07TestRoots(installRoot, dataRoot));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "operation.txt"), prepared.OperationId, new UTF8Encoding(false));
            return 0;
        }
        catch { return 12; }
    }

    private static UpdatePackageOptions CrossSchemaTrustAnchor()
    {
#if S9T07_TEST
        var encoded = Environment.GetEnvironmentVariable("S9_T07_TEST_PUBLIC_KEY");
        // InstallerPreflight is handled before RuntimeDataRoot parses --data-root.  This
        // compile-time-only test hook is therefore deliberately keyed solely by the
        // explicitly injected test public key; production has no such branch.
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(encoded), out _);
            return new UpdatePackageOptions(rsa.ExportParameters(false));
        }
#endif
        return ProductionUpdateTrustAnchor.Options;
    }

    private static bool IsS9T07TestRoots(string installRoot, string dataRoot)
    {
#if S9T07_TEST
        var temp = Path.GetFullPath(Path.GetTempPath());
        return IsDirectGuidChild(temp, installRoot) && IsDirectGuidChild(temp, dataRoot);
#else
        return false;
#endif
    }

#if S9T07_TEST
    private static bool IsDirectGuidChild(string root, string value) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(value)) ?? string.Empty)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParse(Path.GetFileName(Path.GetFullPath(value)), out _);
#endif

    private static IReadOnlyList<string> ReadExactMigrations(string root)
    {
        var database = Path.Combine(root, "data", "app.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new Uri(database).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader(); var values = new List<string>(); while (reader.Read()) values.Add(reader.GetString(0)); return values;
    }

    internal static InstallerPreflightResult ParseAndCheck(string[] arguments)
    {
        if (arguments.Length != 3 || !string.Equals(arguments[0], Command, StringComparison.Ordinal) ||
            !string.Equals(arguments[1], DataRootArgument, StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(arguments[2]))
        {
            return InvalidRoot();
        }

        return Check(arguments[2]);
    }

    internal static InstallerPreflightResult Check(string dataRoot)
    {
        try
        {
            var root = Path.GetFullPath(dataRoot);
            if (Path.IsPathRooted(root) && root.StartsWith("\\\\", StringComparison.Ordinal) || !IsOrdinaryRoot(root)) return InvalidRoot();

            var database = Path.Combine(root, "data", "app.db");
            var wal = database + "-wal";
            var shm = database + "-shm";
            var journal = database + "-journal";
            if (!IsOrdinaryFileOrMissing(database) || !IsOrdinaryFileOrMissing(wal) || !IsOrdinaryFileOrMissing(shm) || !IsOrdinaryFileOrMissing(journal)) return InvalidRoot();
            if (!File.Exists(database))
            {
                return File.Exists(wal) || File.Exists(shm) || File.Exists(journal)
                    ? Corrupt()
                    : new(InstallerPreflightCode.NoDatabase, "未发现现有数据库，可继续安装。");
            }

            // SQLite may create WAL/SHM even for a read-only source connection.
            // Never open the business database: validate a stable, disposable copy instead.
            var scratch = Path.Combine(Path.GetTempPath(), $"StoreExpiryInspector-preflight-{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratch);
            var copy = Path.Combine(scratch, "app.db");
            try
            {
                CopyStableDatabaseFiles([database, wal, shm, journal], scratch);
                return CheckCopy(copy);
            }
            finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Corrupt();
        }
    }

    private static void CopyStableDatabaseFiles(string[] candidates, string scratch)
    {
        var before = candidates.Where(File.Exists).ToArray();
        var streams = new List<FileStream>();
        try
        {
            foreach (var path in before) streams.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            if (!before.SequenceEqual(candidates.Where(File.Exists), StringComparer.OrdinalIgnoreCase)) throw new IOException("数据库文件在检查期间发生变化。");
            for (var index = 0; index < streams.Count; index++)
            {
                using var destination = File.Create(Path.Combine(scratch, Path.GetFileName(before[index])));
                streams[index].CopyTo(destination);
            }
            if (!before.SequenceEqual(candidates.Where(File.Exists), StringComparer.OrdinalIgnoreCase)) throw new IOException("数据库文件在检查期间发生变化。");
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private static InstallerPreflightResult CheckCopy(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, ForeignKeys = true }.ToString());
        connection.Open();
        if (!ScalarIsOk(connection, "PRAGMA integrity_check;") || HasRows(connection, "PRAGMA foreign_key_check;")) return Corrupt();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId, ProductVersion FROM \"__EFMigrationsHistory\" ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var migrations = new List<string>();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(0)) || string.IsNullOrWhiteSpace(reader.GetString(1)))
                return new(InstallerPreflightCode.NewerOrUnknownSchema, "检测到异常 migration 记录。为保护原数据，安装已停止。");
            migrations.Add(reader.GetString(0));
        }
        if (migrations.SequenceEqual(CurrentMigrations, StringComparer.Ordinal)) return new(InstallerPreflightCode.CurrentSchemaHealthy, "现有数据库为当前版本 Schema，已通过只读检查。");
        return migrations.Count < CurrentMigrations.Count && migrations.All(CurrentMigrations.Contains)
            ? new(InstallerPreflightCode.OlderSchema, "检测到旧版数据库。为保护原数据，安装已停止。")
            : new(InstallerPreflightCode.NewerOrUnknownSchema, "检测到未知或更高版本数据库。为保护原数据，安装已停止。");
    }

    private static bool IsOrdinaryRoot(string root)
    {
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
        {
            try
            {
                var attributes = File.GetAttributes(current.FullName);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) return false;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        var data = Path.Combine(root, "data");
        try
        {
            var attributes = File.GetAttributes(data);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool IsOrdinaryFileOrMissing(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool ScalarIsOk(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        return reader.Read() && string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase) && !reader.Read();
    }

    private static bool HasRows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        return reader.Read();
    }

    private static InstallerPreflightResult InvalidRoot() => new(InstallerPreflightCode.InvalidDataRoot, "数据目录不安全或参数无效。为保护原数据，安装已停止。");
    private static InstallerPreflightResult Corrupt() => new(InstallerPreflightCode.CorruptOrUnreadable, "数据库或其 WAL 状态不可安全只读验证。为保护原数据，安装已停止。");
}
