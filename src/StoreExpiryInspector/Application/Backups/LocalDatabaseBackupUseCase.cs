using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;

namespace StoreExpiryInspector.Application.Backups;

public static class LocalDatabaseBackupCodes
{
    public const string Success = "verified";
    public const string SourceNotFound = "source_not_found";
    public const string BackupInProgress = "backup_in_progress";
    public const string CreateFailed = "create_failed";
    public const string ValidationFailed = "validation_failed";
    public const string StorageFailed = "storage_failed";
}

public sealed class LocalDatabaseBackupUseCase
{
    private static int backupInProgress;
    private readonly PreImportSnapshotService snapshotService = new();

    public LocalDatabaseBackupResult Create(
        string? sourceDatabasePath = null,
        string? backupDirectory = null)
    {
        if (!TryBeginDatabaseFileOperation())
        {
            return LocalDatabaseBackupResult.Failure(
                LocalDatabaseBackupCodes.BackupInProgress,
                "已有本地数据库备份正在执行。");
        }

        try
        {
            return CreateCore(sourceDatabasePath, backupDirectory, "backup", "manual", persistRecord: true);
        }
        finally
        {
            EndDatabaseFileOperation();
        }
    }

    internal LocalDatabaseBackupResult CreatePreRestore(string sourceDatabasePath, string backupDirectory) =>
        CreateCore(sourceDatabasePath, backupDirectory, "pre-restore", "pre_restore", persistRecord: false);

    internal static bool TryBeginDatabaseFileOperation() =>
        Interlocked.CompareExchange(ref backupInProgress, 1, 0) == 0;

    internal static void EndDatabaseFileOperation() => Volatile.Write(ref backupInProgress, 0);

    private LocalDatabaseBackupResult CreateCore(
        string? sourceDatabasePath,
        string? backupDirectory,
        string filePrefix,
        string backupType,
        bool persistRecord)
    {
        string sourcePath;
        string destinationPath;
        try
        {
            sourcePath = Path.GetFullPath(sourceDatabasePath ?? DatabaseInitializer.GetDefaultDatabasePath());
            destinationPath = Path.GetFullPath(backupDirectory ?? DatabaseInitializer.GetDefaultBackupDirectory());
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return LocalDatabaseBackupResult.Failure(
                LocalDatabaseBackupCodes.StorageFailed,
                "数据库或备份目录路径不可用。");
        }

        if (!File.Exists(sourcePath))
        {
            return LocalDatabaseBackupResult.Failure(
                LocalDatabaseBackupCodes.SourceNotFound,
                "正式数据库不存在，未创建备份。");
        }

        if (string.Equals(Path.GetDirectoryName(sourcePath), destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return LocalDatabaseBackupResult.Failure(
                LocalDatabaseBackupCodes.StorageFailed,
                "备份目录必须与正式数据库目录分开。");
        }

        string[] expectedMigrationIds;
        try
        {
            using var context = DatabaseInitializer.CreateContext(sourcePath);
            expectedMigrationIds = context.Database.GetMigrations().ToArray();
        }
        catch (Exception)
        {
            return LocalDatabaseBackupResult.Failure(
                LocalDatabaseBackupCodes.CreateFailed,
                "无法读取当前应用 migration 基线。");
        }

        var snapshot = snapshotService.CreateVerifiedSnapshot(
            sourcePath,
            destinationPath,
            filePrefix,
            expectedMigrationIds);
        if (!snapshot.CanProceed || snapshot.Metadata is null)
        {
            return MapSnapshotFailure(snapshot.Code);
        }

        var metadata = snapshot.Metadata;
        var backupId = Path.GetFileNameWithoutExtension(metadata.SnapshotPath);
        var metadataPath = metadata.SnapshotPath + ".metadata.json";
        var temporaryMetadataPath = metadataPath + ".tmp";
        var recordSaved = false;
        try
        {
            var document = new LocalDatabaseBackupMetadata(
                backupId,
                Path.GetFileName(metadata.SnapshotPath),
                metadata.CreatedAtUtc,
                metadata.FileSize,
                metadata.Sha256,
                metadata.MigrationIds,
                LocalDatabaseBackupCodes.Success);
            Directory.CreateDirectory(destinationPath);
            using (var stream = new FileStream(
                       temporaryMetadataPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(stream, document);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryMetadataPath, metadataPath);

            long? recordId = null;
            if (persistRecord)
            {
                using var context = DatabaseInitializer.CreateContext(sourcePath);
                var record = new BackupRecord
                {
                    BackupType = backupType,
                    FilePath = metadata.SnapshotPath,
                    Sha256 = metadata.Sha256,
                    CreatedAtUtc = metadata.CreatedAtUtc,
                    VerificationStatus = LocalDatabaseBackupCodes.Success
                };
                context.BackupRecords.Add(record);
                context.SaveChanges();
                recordId = record.Id;
                recordSaved = true;
            }
            else
            {
                recordSaved = true;
            }

            return LocalDatabaseBackupResult.Success(
                recordId,
                backupId,
                metadata.SnapshotPath,
                metadataPath,
                metadata.CreatedAtUtc,
                metadata.FileSize,
                metadata.Sha256,
                metadata.MigrationIds);
        }
        catch (Exception)
        {
            if (!recordSaved)
            {
                TryDelete(metadata.SnapshotPath);
                TryDelete(metadataPath);
            }

            return LocalDatabaseBackupResult.Failure(
                LocalDatabaseBackupCodes.StorageFailed,
                "备份元数据保存失败，未发布本次备份。");
        }
        finally
        {
            TryDelete(temporaryMetadataPath);
        }
    }

    private static LocalDatabaseBackupResult MapSnapshotFailure(string code) => code switch
    {
        PreImportSnapshotCodes.SourceMissing => LocalDatabaseBackupResult.Failure(
            LocalDatabaseBackupCodes.SourceNotFound,
            "正式数据库不存在，未创建备份。"),
        PreImportSnapshotCodes.VerificationFailed => LocalDatabaseBackupResult.Failure(
            LocalDatabaseBackupCodes.ValidationFailed,
            "备份完整性或 migration 验证失败。"),
        PreImportSnapshotCodes.DestinationUnavailable => LocalDatabaseBackupResult.Failure(
            LocalDatabaseBackupCodes.StorageFailed,
            "备份目录不可用。"),
        _ => LocalDatabaseBackupResult.Failure(
            LocalDatabaseBackupCodes.CreateFailed,
            "SQLite 一致性备份创建失败。")
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static bool IsStorageFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or SecurityException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException;
}

public sealed record LocalDatabaseBackupMetadata(
    string BackupId,
    string FileName,
    DateTime CreatedAtUtc,
    long FileSize,
    string Sha256,
    IReadOnlyList<string> MigrationIds,
    string ValidationResult);

public sealed record LocalDatabaseBackupResult(
    bool Succeeded,
    string Code,
    string SafeSummary,
    long? BackupRecordId,
    string? BackupId,
    string? BackupPath,
    string? MetadataPath,
    DateTime? CreatedAtUtc,
    long? FileSize,
    string? Sha256,
    IReadOnlyList<string> MigrationIds,
    string? ValidationResult)
{
    internal static LocalDatabaseBackupResult Success(
        long? backupRecordId,
        string backupId,
        string backupPath,
        string metadataPath,
        DateTime createdAtUtc,
        long fileSize,
        string sha256,
        IReadOnlyList<string> migrationIds) => new(
            true,
            LocalDatabaseBackupCodes.Success,
            "本地数据库备份已创建并验证。",
            backupRecordId,
            backupId,
            backupPath,
            metadataPath,
            createdAtUtc,
            fileSize,
            sha256,
            migrationIds,
            LocalDatabaseBackupCodes.Success);

    internal static LocalDatabaseBackupResult Failure(string code, string safeSummary) => new(
        false,
        code,
        safeSummary,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        Array.Empty<string>(),
        null);
}
