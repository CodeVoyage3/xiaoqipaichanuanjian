using Microsoft.Data.Sqlite;
using System.IO;

namespace StoreExpiryInspector.Infrastructure;

public enum InstallerPreflightCode
{
    NoDatabase = 1,
    CurrentMigration9Healthy = 2,
    OlderSchema = 10,
    NewerOrUnknownSchema = 11,
    CorruptOrUnreadable = 12,
    InvalidDataRoot = 13
}

public sealed record InstallerPreflightResult(InstallerPreflightCode Code, string Message)
{
    public bool Allowed => Code is InstallerPreflightCode.NoDatabase or InstallerPreflightCode.CurrentMigration9Healthy;
    public string CodeName => Code switch
    {
        InstallerPreflightCode.NoDatabase => "no_database",
        InstallerPreflightCode.CurrentMigration9Healthy => "current_migration_9_healthy",
        InstallerPreflightCode.OlderSchema => "older_schema",
        InstallerPreflightCode.NewerOrUnknownSchema => "newer_or_unknown_schema",
        InstallerPreflightCode.CorruptOrUnreadable => "corrupt_or_unreadable",
        _ => "invalid_data_root"
    };
}

public static class InstallerPreflight
{
    private const string Command = "--installer-preflight";
    private const string DataRootArgument = "--data-root";
    private static readonly string[] CurrentMigrations =
    [
        "20260826123739_InitialCreate",
        "20260826130822_AddTasksAndDrafts",
        "20260826135612_AddInspectionHistory",
        "20260826142429_AddInventoryAdjustments",
        "20260826152131_AddImportPersistence",
        "20260826155455_AddBackupMetadata",
        "20260826162033_AddSettingsAndAppState",
        "20260826170403_AddLifecycleEvents",
        "20260901155124_AddPolicyAndBaselineFoundation"
    ];

    public static bool TryHandle(string[] arguments, out int exitCode)
    {
        exitCode = 0;
        if (!arguments.Contains(Command, StringComparer.Ordinal)) return false;

        var result = ParseAndCheck(arguments);
        Console.Out.WriteLine($"{{\"code\":\"{result.CodeName}\",\"allowed\":{result.Allowed.ToString().ToLowerInvariant()},\"message\":\"{result.Message}\"}}");
        exitCode = result.Allowed ? 0 : (int)result.Code;
        return true;
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
            for (var index = 0; index < streams.Length; index++)
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
        command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var migrations = new List<string>();
        while (reader.Read()) migrations.Add(reader.GetString(0));
        if (migrations.SequenceEqual(CurrentMigrations, StringComparer.Ordinal)) return new(InstallerPreflightCode.CurrentMigration9Healthy, "现有数据库为当前 migration9，已通过只读检查。");
        return migrations.Count < CurrentMigrations.Length && migrations.All(CurrentMigrations.Contains)
            ? new(InstallerPreflightCode.OlderSchema, "检测到旧版数据库。为保护原数据，安装已停止。")
            : new(InstallerPreflightCode.NewerOrUnknownSchema, "检测到未知或更高版本数据库。为保护原数据，安装已停止。");
    }

    private static bool IsOrdinaryRoot(string root)
    {
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
        {
            if (File.Exists(current.FullName)) return false;
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }

        var data = Path.Combine(root, "data");
        return !File.Exists(data) && (!Directory.Exists(data) || (File.GetAttributes(data) & FileAttributes.ReparsePoint) == 0);
    }

    private static bool IsOrdinaryFileOrMissing(string path)
    {
        if (Directory.Exists(path)) return false;
        if (!File.Exists(path)) return true;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
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
