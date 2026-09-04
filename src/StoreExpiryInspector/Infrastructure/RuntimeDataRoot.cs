using System.IO;

namespace StoreExpiryInspector.Infrastructure;

public static class RuntimeDataRoot
{
    private const string DataRootArgument = "--data-root";
    private const string SmokeExitArgument = "--s9-t01-smoke-exit";
    private const string ExistingIsolatedDataRootArgument = "--allow-existing-isolated-data-root";

    private static RuntimeDataRootOptions? _options;

    public static bool IsIsolated => Options.IsIsolated;

    public static bool IsSmokeRun => Options.IsSmokeRun;

    public static string RootDirectory => Options.RootDirectory;

    public static string DatabasePath => Path.Combine(RootDirectory, "data", "app.db");

    public static string BackupDirectory => Path.Combine(RootDirectory, "backups");

    public static string PreImportSnapshotDirectory => Path.Combine(BackupDirectory, "pre-import");

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");

    public static string MutexName => IsIsolated
        ? $@"Local\StoreExpiryInspector.SingleInstance.{Path.GetFileName(RootDirectory)}"
        : @"Local\StoreExpiryInspector.SingleInstance";

    public static void Configure(string[] arguments)
    {
        if (_options is not null)
        {
            throw new InvalidOperationException("运行数据根目录已经初始化。");
        }

        _options = Parse(arguments, Path.GetTempPath());
        if (_options.IsIsolated)
        {
            EnsureOrdinaryTree(_options.RootDirectory, Path.GetTempPath(), _options.AllowExisting);
        }
    }

    internal static RuntimeDataRootOptions Parse(string[] arguments, string tempDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempDirectory);

        string? dataRoot = null;
        var smokeExit = false;
        var allowExisting = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, SmokeExitArgument, StringComparison.Ordinal))
            {
                smokeExit = true;
                continue;
            }

            if (string.Equals(argument, ExistingIsolatedDataRootArgument, StringComparison.Ordinal))
            {
                allowExisting = true;
                continue;
            }

            if (string.Equals(argument, DataRootArgument, StringComparison.Ordinal))
            {
                if (++index >= arguments.Length || dataRoot is not null)
                {
                    throw new ArgumentException("隔离数据目录参数无效。", nameof(arguments));
                }

                dataRoot = arguments[index];
                continue;
            }

            throw new ArgumentException("不支持的启动参数。", nameof(arguments));
        }

        if (dataRoot is null)
        {
            if (smokeExit)
            {
                throw new ArgumentException("发布 smoke 必须指定隔离数据目录。", nameof(arguments));
            }

            if (allowExisting) throw new ArgumentException("复用隔离数据目录必须指定数据目录。", nameof(arguments));
            return new(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoreExpiryInspector"),
                false,
                smokeExit);
        }

        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException("隔离数据目录必须为绝对路径。", nameof(arguments));
        }

        var root = Path.GetFullPath(dataRoot);
        var tempRoot = Path.GetFullPath(tempDirectory);
        var relative = Path.GetRelativePath(tempRoot, root);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ||
            !Guid.TryParse(relative, out _))
        {
            throw new ArgumentException("隔离数据目录必须是 TEMP 下的 GUID 普通目录。", nameof(arguments));
        }

        return new(root, true, smokeExit, allowExisting);
    }

    private static RuntimeDataRootOptions Options => _options ?? new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoreExpiryInspector"),
        false,
        false);

    internal static void EnsureOrdinaryTree(string root, string tempRoot, bool allowExisting = false)
    {
        if (File.Exists(root) || Directory.Exists(root))
        {
            if (!allowExisting) throw new InvalidOperationException("隔离数据目录必须是本次新建的普通目录，已停止启动。");
            EnsureOrdinaryAncestors(Path.GetDirectoryName(root)!, tempRoot);
            EnsureExistingOrdinaryTree(root);
            return;
        }

        EnsureOrdinaryAncestors(Path.GetDirectoryName(root)!, tempRoot);
        Directory.CreateDirectory(root);
        EnsureExistingOrdinaryDirectory(root);
        foreach (var relativePath in new[] { "data", "backups", "backups\\pre-import", "logs" })
        {
            EnsureOrdinaryDirectory(Path.Combine(root, relativePath));
        }
    }

    private static void EnsureOrdinaryAncestors(string parent, string tempRoot)
    {
        var expected = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var foundTempRoot = false;
        for (var current = new DirectoryInfo(parent); current is not null; current = current.Parent)
        {
            EnsureExistingOrdinaryDirectory(current.FullName);
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), expected, StringComparison.OrdinalIgnoreCase))
            {
                foundTempRoot = true;
            }
        }

        if (!foundTempRoot)
        {
            throw new InvalidOperationException("隔离数据目录不在 TEMP 下，已停止启动。");
        }
    }

    private static void EnsureOrdinaryDirectory(string path)
    {
        if (File.Exists(path))
        {
            throw new InvalidOperationException("隔离数据目录结构无效，已停止启动。");
        }

        Directory.CreateDirectory(path);
        EnsureExistingOrdinaryDirectory(path);
    }

    private static void EnsureExistingOrdinaryDirectory(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("隔离数据目录不是普通本地目录，已停止启动。");
        }
    }

    private static void EnsureExistingOrdinaryTree(string root)
    {
        EnsureExistingOrdinaryDirectory(root);
        ValidateTree(root);
    }

    private static void ValidateTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("隔离数据目录不是普通本地目录，已停止启动。");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                ValidateTree(entry);
            }
        }
    }
}

internal sealed record RuntimeDataRootOptions(string RootDirectory, bool IsIsolated, bool IsSmokeRun, bool AllowExisting = false);
