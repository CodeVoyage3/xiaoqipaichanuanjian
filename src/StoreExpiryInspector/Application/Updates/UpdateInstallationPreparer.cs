using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UpdateSafety;

namespace StoreExpiryInspector.Application.Updates;

public sealed record PreparedUpdateInstallation(string OperationId, string JournalPath, string UpdaterPath);

public sealed class UpdateInstallationPreparer
{
    private const string ProductId = "StoreExpiryInspector";
    private const string AppIdKey = "8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14";

    private readonly SignedUpdatePackageDownloader _downloader;

    public UpdateInstallationPreparer(SignedUpdatePackageDownloader downloader) => _downloader = downloader;

    public PreparedUpdateInstallation Prepare(VerifiedUpdatePackage package, Process parent, CancellationToken cancellationToken)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var installRoot = Directory.GetParent(appPath)?.FullName
            ?? throw new InvalidDataException("升级程序根目录身份无效。");
        return PrepareCore(package, parent, installRoot, Path.Combine(local, ProductId), Path.Combine(appPath, "Updater"), false, cancellationToken);
    }

    // Internal so tests can prove the transaction without ever naming a production root.
    internal PreparedUpdateInstallation PrepareForTest(VerifiedUpdatePackage package, Process parent, string installRoot, string dataRoot, string updaterSourceRoot, CancellationToken cancellationToken) =>
        PrepareCore(package, parent, installRoot, dataRoot, updaterSourceRoot, true, cancellationToken);

    internal PreparedUpdateInstallation PrepareForInstaller(VerifiedUpdatePackage package, Process bootstrap, string installRoot, string dataRoot, string sourceVersion, IReadOnlyList<string> sourceMigrations, CancellationToken cancellationToken, bool testOnly = false) =>
        PrepareCore(package, bootstrap, installRoot, dataRoot, Path.Combine(AppContext.BaseDirectory, "Updater"), testOnly, cancellationToken, sourceVersion, sourceMigrations, true, true);

    private PreparedUpdateInstallation PrepareCore(VerifiedUpdatePackage package, Process parent, string installRoot, string dataRoot, string updaterSourceRoot, bool testOnly, CancellationToken cancellationToken, string? sourceVersionOverride = null, IReadOnlyList<string>? sourceMigrationsOverride = null, bool updaterFromCandidate = false, bool installerBootstrap = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(parent);
        var operationId = Guid.NewGuid().ToString();
        var operationRoot = Path.Combine(dataRoot, "updates", operationId);
        var appPath = Path.Combine(installRoot, "app");
        var stagingPath = Path.Combine(installRoot, "app.staging-" + operationId);
        var oldPath = Path.Combine(installRoot, "app.old-" + operationId);
        var updaterPath = Path.Combine(operationRoot, "updater");
        var journalPath = Path.Combine(operationRoot, "journal.json");
        var preJournalPath = Path.Combine(operationRoot, "schema-preparation.json");

        ValidateRoots(installRoot, dataRoot, updaterSourceRoot, testOnly, installerBootstrap);
        if (!Directory.Exists(appPath) || Directory.Exists(stagingPath) || Directory.Exists(oldPath)) throw new InvalidOperationException("更新程序目录状态无效。");
        var oldTree = InstallationTreeFingerprint.Create(appPath);
        try
        {
            Directory.CreateDirectory(operationRoot);
            EnsureOrdinaryTree(operationRoot);
            var operationPackage = Path.Combine(operationRoot, "candidate.zip");
            CopyPackage(package.PackagePath, operationPackage, cancellationToken);
            using var lockedPackage = new FileStream(operationPackage, FileMode.Open, FileAccess.Read, FileShare.Read);
            var revalidated = package with { CacheDirectory = operationRoot, PackagePath = operationPackage };
            var revalidation = _downloader.RevalidateForInstall(revalidated, cancellationToken);
            if (revalidation.Outcome != UpdatePackageOutcome.Verified)
                throw new InvalidDataException($"更新包安装前重验失败：{revalidation.Outcome}。{revalidation.Message}");
            lockedPackage.Position = 0;
            TestCheckpoint(testOnly, "StagingStarted", stagingPath, operationRoot, dataRoot);
            ExtractAuditedArchive(lockedPackage, stagingPath, cancellationToken);
            CopyTree(updaterFromCandidate ? Path.Combine(stagingPath, "Updater") : updaterSourceRoot, updaterPath);
            TestCheckpoint(testOnly, "StagingCompleted", stagingPath, operationRoot, dataRoot);
            var candidateTree = InstallationTreeFingerprint.Create(stagingPath);
            var now = DateTimeOffset.UtcNow;
            var sourceVersion = sourceVersionOverride ?? TestFixtureSourceVersion(testOnly) ?? SourceVersion(parent);
            var declaredSourceMigrations = sourceMigrationsOverride ?? DeclaredSourceMigrations(testOnly, package.TargetMigrations);
            var actualSourceMigrations = ReadAppliedMigrations(dataRoot);
            ValidateSourcePermission(package, sourceVersion, actualSourceMigrations);
            SchemaUpdateJournal? schema = null;
            if (!declaredSourceMigrations.SequenceEqual(package.TargetMigrations, StringComparer.Ordinal))
            {
                if (package.MinimumProtocolVersion < 2 || !SchemaUpdateJournal.IsValidMigrationList(package.TargetMigrations))
                    throw new InvalidDataException("候选更新未声明跨 Schema 协议。");
                var sourceMigrations = actualSourceMigrations;
                if (!sourceMigrations.SequenceEqual(declaredSourceMigrations, StringComparer.Ordinal) || !SchemaUpgradeSnapshots.IsStrictPrefix(sourceMigrations, package.TargetMigrations))
                    throw new InvalidDataException("候选更新不允许当前 Schema 来源。");
                WritePreparationAtomically(preJournalPath, operationId, sourceVersion, sourceMigrations, package.TargetMigrations, SchemaPhase.SourceVerified);
                WritePreparationAtomically(preJournalPath, operationId, sourceVersion, sourceMigrations, package.TargetMigrations, SchemaPhase.SnapshotPreparing);
                var snapshot = SchemaUpgradeSnapshots.Create(dataRoot, operationId, sourceVersion, sourceMigrations);
                schema = new SchemaUpdateJournal(SchemaPhase.SnapshotVerified, snapshot, sourceMigrations, package.TargetMigrations, Guid.NewGuid().ToString());
                SchemaUpdateJournal.Validate(schema, operationId, sourceVersion, package.Version.ToString(3));
            }
            var journal = new InstallationJournal(operationId, ProductId, Path.GetFullPath(installRoot), Path.GetFullPath(dataRoot), appPath, stagingPath, oldPath, package.Sha256, sourceVersion, package.Version.ToString(3), parent.Id, parent.StartTime.ToUniversalTime(), InstallationUpdatePhase.Prepared, oldTree, candidateTree, now, now, 0, null, null, schema);
            WriteJournalAtomically(journalPath, journal);
            if (File.Exists(preJournalPath)) File.Delete(preJournalPath);
            _downloader.DiscardVerifiedCache(package);
            return new(operationId, journalPath, Path.Combine(updaterPath, "StoreExpiryInspector.Updater.exe"));
        }
        catch
        {
            DeleteOwnedDirectory(stagingPath);
            DeleteOwnedDirectory(updaterPath);
            throw;
        }
    }

    private static void ValidateSourcePermission(VerifiedUpdatePackage package, string sourceVersion, IReadOnlyList<string> migrations)
    {
        var version = Version.Parse(sourceVersion);
        if (package.SourceMinVersion is null || package.SourceMaxVersion is null || package.SourceMinMigration is null || package.SourceMaxMigration is null ||
            version < package.SourceMinVersion || version > package.SourceMaxVersion ||
            string.CompareOrdinal(migrations[^1], package.SourceMinMigration) < 0 || string.CompareOrdinal(migrations[^1], package.SourceMaxMigration) > 0)
            throw new InvalidDataException("当前版本或迁移不在已签名更新许可范围内。");
    }

    private static void WritePreparationAtomically(string path, string operationId, string sourceVersion, IReadOnlyList<string> source, IReadOnlyList<string> target, SchemaPhase phase)
    {
        DurableFile.Replace(path, JsonSerializer.Serialize(new { operationId, sourceVersion, sourceMigrations = source, targetMigrations = target, phase, treeSwitchAuthorized = false }));
    }

    private static IReadOnlyList<string> DeclaredSourceMigrations(bool testOnly, IReadOnlyList<string> targetMigrations)
    {
#if S9T07_TEST
        if (testOnly && targetMigrations.SequenceEqual(CurrentSchemaIdentity.Migrations.Take(9), StringComparer.Ordinal))
            return CurrentSchemaIdentity.Migrations.Take(9).ToArray();
        if (testOnly && RuntimeDataRoot.IsS9T07TestInstall)
            return CurrentSchemaIdentity.Migrations.Take(9).ToArray();
#endif
        using var context = new StoreDbContextFactory().CreateDbContext([]);
        var migrations = context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!SchemaUpdateJournal.IsValidMigrationList(migrations)) throw new InvalidDataException("旧程序 Schema 声明无效。");
        return migrations;
    }

    private static IReadOnlyList<string> ReadAppliedMigrations(string dataRoot)
    {
        var database = Path.Combine(dataRoot, "data", "app.db");
        // immutable avoids replaying a preflight WAL before SchemaUpgradeSnapshots can reject it.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new Uri(database).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader(); var migrations = new List<string>(); while (reader.Read()) migrations.Add(reader.GetString(0));
        if (!SchemaUpdateJournal.IsValidMigrationList(migrations)) throw new InvalidDataException("当前 Schema 迁移历史无效。");
        return migrations;
    }

    private static void ValidateRoots(string installRoot, string dataRoot, string updaterSourceRoot, bool testOnly, bool installerBootstrap = false)
    {
        installRoot = Path.GetFullPath(installRoot); dataRoot = Path.GetFullPath(dataRoot); updaterSourceRoot = Path.GetFullPath(updaterSourceRoot);
        if (testOnly)
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            RequireDirectGuidChild(temp, installRoot); RequireDirectGuidChild(temp, dataRoot); RequireUnder(temp, updaterSourceRoot);
        }
        else
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var registeredRoot = Registry.CurrentUser.OpenSubKey($"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{{{AppIdKey}}}_is1")?.GetValue("Inno Setup: App Path") as string;
            if (!string.Equals(dataRoot, Path.Combine(local, ProductId), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(registeredRoot is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(registeredRoot)), installRoot, StringComparison.OrdinalIgnoreCase) ||
                (!installerBootstrap && (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), Path.Combine(installRoot, "app"), StringComparison.OrdinalIgnoreCase) || !string.Equals(updaterSourceRoot, Path.Combine(installRoot, "app", "Updater"), StringComparison.OrdinalIgnoreCase))) ||
                (installerBootstrap && !File.Exists(Path.Combine(installRoot, "app", "StoreExpiryInspector.exe")))) throw new InvalidDataException("升级根目录身份无效。");
        }
        EnsureOrdinaryTree(installRoot); EnsureOrdinaryTree(dataRoot); EnsureOrdinaryTree(updaterSourceRoot);
    }

    private static string SourceVersion(Process parent)
    {
        return NormalizeSourceVersion(parent.MainModule?.FileVersionInfo.ProductVersion?.Split('+')[0]);
    }

    internal static string NormalizeSourceVersion(string? value) =>
        Version.TryParse(value, out var version) && version.Major >= 0 && version.Minor >= 0 && version.Build >= 0 && version.Revision <= 0
            ? version.ToString(3)
            : throw new InvalidDataException("父进程版本身份无效。");

    private static string? TestFixtureSourceVersion(bool testOnly)
    {
#if S9T07_TEST
        if (testOnly && RuntimeDataRoot.IsS9T07TestInstall) return "1.0.5";
#endif
        return null;
    }

    private static void RequireDirectGuidChild(string root, string value)
    {
        var relative = Path.GetRelativePath(root, value);
        if (Path.IsPathRooted(relative) || !Guid.TryParse(relative, out _)) throw new InvalidDataException("测试升级根目录无效。");
    }

    private static void RequireUnder(string root, string value)
    {
        var relative = Path.GetRelativePath(root, value);
        if (relative == "." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) throw new InvalidDataException("路径越界。");
    }

    private static void EnsureOrdinaryTree(string root)
    {
        try { if (new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed) throw new InvalidDataException("升级目录不是固定本地卷。"); }
        catch (ArgumentException) { throw new InvalidDataException("升级目录不是固定本地卷。"); }
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("升级目录不是普通本地目录。");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("升级目录含有重解析点。");
    }

    private static void CopyPackage(string source, string destination, CancellationToken cancellationToken)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination); EnsureOrdinaryTree(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = SafeRelative(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
        if (!File.Exists(Path.Combine(destination, "StoreExpiryInspector.Updater.exe"))) throw new InvalidDataException("独立 Updater 发布树不完整。");
        EnsureOrdinaryTree(destination);
    }

    private static void ExtractAuditedArchive(Stream packageStream, string stagingPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingPath); EnsureOrdinaryTree(stagingPath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, true);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) throw new InvalidDataException("更新包包含目录项。");
            var relative = SafeArchiveRelative(entry.FullName);
            if (!names.Add(relative)) throw new InvalidDataException("更新包包含重复文件。");
            var target = Path.Combine(stagingPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output, 81920);
        }
        EnsureOrdinaryTree(stagingPath);
    }

    private static string SafeArchiveRelative(string value)
    {
        if (value.Contains(':') || value.StartsWith('/') || value.StartsWith('\\') || Path.IsPathFullyQualified(value)) throw new InvalidDataException("更新包路径无效。");
        var relative = value.Replace('/', Path.DirectorySeparatorChar);
        if (relative.Split(Path.DirectorySeparatorChar).Any(part => string.IsNullOrWhiteSpace(part) || part is "." or "..")) throw new InvalidDataException("更新包路径越界。");
        return relative;
    }

    private static string SafeRelative(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        if (Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative.Contains(':')) throw new InvalidDataException("Updater 文件路径无效。");
        return relative;
    }

    private static void WriteJournalAtomically(string path, InstallationJournal journal)
    {
        DurableFile.Replace(path, JsonSerializer.Serialize(journal));
    }

    private static void DeleteOwnedDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { EnsureOrdinaryTree(path); Directory.Delete(path, true); }
        catch (InvalidDataException) { }
    }
    private static void TestCheckpoint(bool testOnly, string checkpoint, string stagingPath, string operationRoot, string dataRoot)
    {
        if (!testOnly || !string.Equals(Environment.GetEnvironmentVariable("S9_T05_PREPARER_CHECKPOINT"), checkpoint, StringComparison.Ordinal)) return;
        var marker = Environment.GetEnvironmentVariable("S9_T05_PREPARER_MARKER");
        if (!string.IsNullOrWhiteSpace(marker)) File.WriteAllText(marker, JsonSerializer.Serialize(new { stagingPath, operationRoot, dataRoot, pid = Process.GetCurrentProcess().Id }));
        Thread.Sleep(Timeout.Infinite);
    }
}

internal sealed record InstallationTreeFingerprint(IReadOnlyList<string> Files, string Hash)
{
    internal static InstallationTreeFingerprint Create(string root)
    {
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(path =>
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative.Contains(':') || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("程序树不安全。");
            using var stream = File.OpenRead(path);
            return $"{relative}|{new FileInfo(path).Length}|{Convert.ToHexString(SHA256.HashData(stream))}";
        }).ToArray();
        return new(entries, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", entries)))));
    }
}

internal enum InstallationUpdatePhase { Prepared, MainExitRequested, MainExited, CandidateStaged, OldAppPreserved, SwitchStarted, CandidateActivated, CandidateStarted, WaitingForHealthAck, Committed, Completed, RollbackRequired, RollbackStarted, OldAppRestored, RollbackVerified, RolledBack, FailedNeedsManualRecovery }

internal sealed record InstallationJournal(string OperationId, string ProductId, string InstallRoot, string DataRoot, string AppPath, string StagingPath, string OldPath, string PackageSha256, string SourceVersion, string TargetVersion, int ParentPid, DateTimeOffset ParentStartedUtc, InstallationUpdatePhase Phase, InstallationTreeFingerprint OldTree, InstallationTreeFingerprint CandidateTree, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, int CandidatePid, DateTimeOffset? CandidateStartedUtc, string? LastError, SchemaUpdateJournal? Schema = null);
