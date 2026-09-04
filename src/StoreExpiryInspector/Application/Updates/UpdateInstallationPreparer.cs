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

namespace StoreExpiryInspector.Application.Updates;

public sealed record PreparedUpdateInstallation(string OperationId, string JournalPath, string UpdaterPath);

public sealed class UpdateInstallationPreparer
{
    private const string ProductId = "StoreExpiryInspector";

    private readonly SignedUpdatePackageDownloader _downloader;

    public UpdateInstallationPreparer(SignedUpdatePackageDownloader downloader) => _downloader = downloader;

    public PreparedUpdateInstallation Prepare(VerifiedUpdatePackage package, Process parent, CancellationToken cancellationToken)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return PrepareCore(package, parent, Path.Combine(local, "Programs", ProductId), Path.Combine(local, ProductId), Path.Combine(AppContext.BaseDirectory, "Updater"), false, cancellationToken);
    }

    // Internal so tests can prove the transaction without ever naming a production root.
    internal PreparedUpdateInstallation PrepareForTest(VerifiedUpdatePackage package, Process parent, string installRoot, string dataRoot, string updaterSourceRoot, CancellationToken cancellationToken) =>
        PrepareCore(package, parent, installRoot, dataRoot, updaterSourceRoot, true, cancellationToken);

    private PreparedUpdateInstallation PrepareCore(VerifiedUpdatePackage package, Process parent, string installRoot, string dataRoot, string updaterSourceRoot, bool testOnly, CancellationToken cancellationToken)
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

        ValidateRoots(installRoot, dataRoot, updaterSourceRoot, testOnly);
        if (!Directory.Exists(appPath) || Directory.Exists(stagingPath) || Directory.Exists(oldPath)) throw new InvalidOperationException("更新程序目录状态无效。");
        var oldTree = InstallationTreeFingerprint.Create(appPath);
        try
        {
            Directory.CreateDirectory(operationRoot);
            EnsureOrdinaryTree(operationRoot);
            CopyTree(updaterSourceRoot, updaterPath);
            var operationPackage = Path.Combine(operationRoot, "candidate.zip");
            CopyPackage(package.PackagePath, operationPackage, cancellationToken);
            using var lockedPackage = new FileStream(operationPackage, FileMode.Open, FileAccess.Read, FileShare.Read);
            var revalidated = package with { CacheDirectory = operationRoot, PackagePath = operationPackage };
            if (_downloader.RevalidateForInstall(revalidated, cancellationToken).Outcome != UpdatePackageOutcome.Verified)
                throw new InvalidDataException("更新包安装前重验失败。");
            lockedPackage.Position = 0;
            TestCheckpoint(testOnly, "StagingStarted", stagingPath, operationRoot, dataRoot);
            ExtractAuditedArchive(lockedPackage, stagingPath, cancellationToken);
            TestCheckpoint(testOnly, "StagingCompleted", stagingPath, operationRoot, dataRoot);
            var candidateTree = InstallationTreeFingerprint.Create(stagingPath);
            var now = DateTimeOffset.UtcNow;
            var journal = new InstallationJournal(operationId, ProductId, Path.GetFullPath(installRoot), Path.GetFullPath(dataRoot), appPath, stagingPath, oldPath, package.Sha256, SourceVersion(parent), package.Version.ToString(3), parent.Id, parent.StartTime.ToUniversalTime(), InstallationUpdatePhase.Prepared, oldTree, candidateTree, now, now, 0, null, null);
            WriteJournalAtomically(journalPath, journal);
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

    private static void ValidateRoots(string installRoot, string dataRoot, string updaterSourceRoot, bool testOnly)
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
            if (!string.Equals(installRoot, Path.Combine(local, "Programs", ProductId), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(dataRoot, Path.Combine(local, ProductId), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(updaterSourceRoot, Path.Combine(AppContext.BaseDirectory, "Updater"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("升级根目录身份无效。");
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
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal));
        File.Move(temporary, path, true);
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

internal sealed record InstallationJournal(string OperationId, string ProductId, string InstallRoot, string DataRoot, string AppPath, string StagingPath, string OldPath, string PackageSha256, string SourceVersion, string TargetVersion, int ParentPid, DateTimeOffset ParentStartedUtc, InstallationUpdatePhase Phase, InstallationTreeFingerprint OldTree, InstallationTreeFingerprint CandidateTree, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, int CandidatePid, DateTimeOffset? CandidateStartedUtc, string? LastError);
