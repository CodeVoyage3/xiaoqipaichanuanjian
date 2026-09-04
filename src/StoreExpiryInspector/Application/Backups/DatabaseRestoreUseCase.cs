using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;

namespace StoreExpiryInspector.Application.Backups;

public static class DatabaseRestoreCodes
{
    public const string Restored = "restored";
    public const string BackupNotFound = "backup_not_found";
    public const string BackupInvalid = "backup_invalid";
    public const string HashMismatch = "hash_mismatch";
    public const string IntegrityFailed = "integrity_failed";
    public const string MigrationIncompatible = "migration_incompatible";
    public const string DatabaseInUse = "database_in_use";
    public const string PreRestoreBackupFailed = "pre_restore_backup_failed";
    public const string StagingFailed = "staging_failed";
    public const string ReplaceFailed = "replace_failed";
    public const string FinalValidationFailed = "final_validation_failed";
    public const string CriticalRestoreFailure = "critical_restore_failure";
}

public sealed class DatabaseRestoreUseCase
{
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", "-journal"];
    private readonly Action<string, string?>? checkpoint;
    private readonly LocalDatabaseBackupUseCase backupUseCase = new();
    private readonly PreImportSnapshotService snapshotService = new();

    public DatabaseRestoreUseCase()
    {
    }

    internal DatabaseRestoreUseCase(Action<string, string?> checkpoint) => this.checkpoint = checkpoint;

    internal LocalDatabaseBackupValidation ValidateForListing(
        string backupPath,
        string? formalDatabasePath = null)
    {
        string sourcePath;
        string targetPath;
        try
        {
            sourcePath = Path.GetFullPath(backupPath);
            targetPath = Path.GetFullPath(
                formalDatabasePath ?? DatabaseInitializer.GetDefaultDatabasePath());
        }
        catch
        {
            return LocalDatabaseBackupValidation.Failure(
                DatabaseRestoreCodes.BackupInvalid,
                "备份或正式数据库路径不可用。");
        }

        if (!File.Exists(sourcePath))
        {
            return LocalDatabaseBackupValidation.Failure(
                DatabaseRestoreCodes.BackupNotFound,
                "备份文件不存在。");
        }

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) ||
            sourcePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(sourcePath).Contains(".restore-", StringComparison.OrdinalIgnoreCase))
        {
            return LocalDatabaseBackupValidation.Failure(
                DatabaseRestoreCodes.BackupInvalid,
                "备份路径不是可发布的独立备份文件。");
        }

        // Listing is a read-only operation. Read the model migration baseline
        // from the existing in-memory design-time context instead of creating
        // the formal database directory as CreateContext(path) does.
        var expectedMigrations = ReadExpectedMigrationsForListing();
        if (expectedMigrations is null)
        {
            return LocalDatabaseBackupValidation.Failure(
                DatabaseRestoreCodes.BackupInvalid,
                "无法读取当前应用 migration 基线。");
        }

        var validation = Validate(sourcePath, expectedMigrations, requireMetadata: true);
        return new(
            validation.Succeeded,
            validation.Code,
            validation.Summary,
            validation.Metadata);
    }

    public DatabaseRestoreResult Restore(
        string backupPath,
        bool databaseRuntimeStopped,
        string? formalDatabasePath = null,
        string? backupDirectory = null)
    {
        if (!LocalDatabaseBackupUseCase.TryBeginDatabaseFileOperation())
        {
            return Failure(DatabaseRestoreCodes.DatabaseInUse, "已有数据库备份或恢复正在执行。");
        }

        try
        {
            return RestoreCore(backupPath, databaseRuntimeStopped, formalDatabasePath, backupDirectory);
        }
        finally
        {
            LocalDatabaseBackupUseCase.EndDatabaseFileOperation();
        }
    }

    private DatabaseRestoreResult RestoreCore(
        string backupPath,
        bool databaseRuntimeStopped,
        string? formalDatabasePath,
        string? backupDirectory)
    {
        string sourcePath;
        string targetPath;
        string protectionDirectory;
        try
        {
            sourcePath = Path.GetFullPath(backupPath);
            targetPath = Path.GetFullPath(formalDatabasePath ?? DatabaseInitializer.GetDefaultDatabasePath());
            protectionDirectory = Path.GetFullPath(backupDirectory ?? DatabaseInitializer.GetDefaultBackupDirectory());
        }
        catch
        {
            return Failure(DatabaseRestoreCodes.BackupInvalid, "备份或正式数据库路径不可用。");
        }

        if (!databaseRuntimeStopped)
        {
            return Failure(DatabaseRestoreCodes.DatabaseInUse, "恢复要求上层先停止数据库 runtime 并释放业务连接。");
        }

        if (!File.Exists(sourcePath))
        {
            return Failure(DatabaseRestoreCodes.BackupNotFound, "备份文件不存在。");
        }

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) ||
            sourcePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(sourcePath).Contains(".restore-", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(DatabaseRestoreCodes.BackupInvalid, "备份路径不是可发布的独立备份文件。");
        }

        var expectedMigrations = ReadExpectedMigrations(targetPath);
        if (expectedMigrations is null)
        {
            return Failure(DatabaseRestoreCodes.BackupInvalid, "无法读取当前应用 migration 基线。");
        }

        var sourceValidation = Validate(sourcePath, expectedMigrations, requireMetadata: true);
        if (!sourceValidation.Succeeded)
        {
            return Failure(sourceValidation.Code, sourceValidation.Summary);
        }

        var protection = backupUseCase.CreatePreRestore(targetPath, protectionDirectory);
        if (!protection.Succeeded)
        {
            return Failure(DatabaseRestoreCodes.PreRestoreBackupFailed, "恢复前保护备份创建或验证失败。");
        }

        SqliteConnection.ClearAllPools();
        if (!CanOpenExclusively(targetPath))
        {
            return Failure(DatabaseRestoreCodes.DatabaseInUse, "正式数据库仍被占用，恢复未开始。", protection);
        }

        string originalSha256;
        try
        {
            originalSha256 = ComputeSha256(targetPath);
        }
        catch
        {
            return Failure(DatabaseRestoreCodes.DatabaseInUse, "无法锁定正式数据库原始文件身份，恢复未开始。", protection);
        }

        var directory = Path.GetDirectoryName(targetPath)!;
        var operationId = Guid.NewGuid().ToString("N");
        var stagingPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.restore-{operationId}.tmp");
        var rollbackPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.restore-{operationId}.rollback");
        var failedPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.restore-{operationId}.failed");
        var quarantinedSidecars = new List<(string Original, string Quarantine)>();
        var replaced = false;
        try
        {
            try
            {
                checkpoint?.Invoke("before_staging_copy", stagingPath);
                using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var staging = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(staging);
                staging.Flush(flushToDisk: true);
            }
            catch
            {
                return Failure(DatabaseRestoreCodes.StagingFailed, "恢复 staging 文件创建失败，正式数据库未替换。", protection);
            }

            checkpoint?.Invoke("before_staging_validation", stagingPath);
            var stagingValidation = Validate(stagingPath, expectedMigrations, sourceValidation.Metadata!, requireMetadata: false);
            if (!stagingValidation.Succeeded)
            {
                return Failure(DatabaseRestoreCodes.StagingFailed, "恢复 staging 文件验证失败，正式数据库未替换。", protection);
            }

            checkpoint?.Invoke("staging_validated", stagingPath);
            try
            {
                foreach (var suffix in SidecarSuffixes)
                {
                    var sidecar = targetPath + suffix;
                    if (!File.Exists(sidecar))
                    {
                        continue;
                    }

                    var quarantine = sidecar + $".restore-{operationId}";
                    File.Move(sidecar, quarantine);
                    quarantinedSidecars.Add((sidecar, quarantine));
                }

                checkpoint?.Invoke("before_replace", targetPath);
                File.Replace(stagingPath, targetPath, rollbackPath, ignoreMetadataErrors: true);
                replaced = true;
                checkpoint?.Invoke("after_replace", targetPath);
            }
            catch
            {
                if (!replaced)
                {
                    RestoreSidecars(quarantinedSidecars);
                    return Failure(DatabaseRestoreCodes.ReplaceFailed, "原子替换失败，原正式数据库保持不变。", protection);
                }
            }

            var finalValidation = Validate(targetPath, expectedMigrations, sourceValidation.Metadata!, requireMetadata: false);
            if (finalValidation.Succeeded)
            {
                Delete(rollbackPath);
                DeleteQuarantinedSidecars(quarantinedSidecars);
                DeleteCurrentSidecars(targetPath);
                DeleteCurrentSidecars(stagingPath);
                DeleteCurrentSidecars(rollbackPath);
                DeleteCurrentSidecars(failedPath);
                if (!CleanupCompleted(
                        targetPath,
                        stagingPath,
                        rollbackPath,
                        failedPath,
                        quarantinedSidecars))
                {
                    return Failure(
                        DatabaseRestoreCodes.CriticalRestoreFailure,
                        "数据库已恢复并验证，但恢复副文件清理失败；必须保持业务停止并人工处理。",
                        protection);
                }

                return DatabaseRestoreResult.Success(sourceValidation.Metadata!, protection);
            }

            try
            {
                checkpoint?.Invoke("before_rollback", rollbackPath);
                File.Replace(rollbackPath, targetPath, failedPath, ignoreMetadataErrors: true);
                Delete(failedPath);
                RestoreSidecars(quarantinedSidecars);
                if (!string.Equals(ComputeSha256(targetPath), originalSha256, StringComparison.OrdinalIgnoreCase) ||
                    !ValidateDatabaseStructure(targetPath, expectedMigrations).Succeeded)
                {
                    throw new IOException("Rollback validation failed.");
                }

                return Failure(DatabaseRestoreCodes.FinalValidationFailed, "恢复后验证失败，已回退原正式数据库。", protection);
            }
            catch
            {
                return Failure(
                    DatabaseRestoreCodes.CriticalRestoreFailure,
                    "恢复后验证及自动回退失败；必须保持业务停止并人工处理保护备份。",
                    protection);
            }
        }
        finally
        {
            Delete(stagingPath);
            DeleteCurrentSidecars(stagingPath);
            DeleteCurrentSidecars(rollbackPath);
            DeleteCurrentSidecars(failedPath);
        }
    }

    private RestoreValidation Validate(
        string path,
        IReadOnlyList<string> expectedMigrations,
        LocalDatabaseBackupMetadata? knownMetadata = null,
        bool requireMetadata = false)
    {
        LocalDatabaseBackupMetadata metadata;
        try
        {
            metadata = knownMetadata ?? JsonSerializer.Deserialize<LocalDatabaseBackupMetadata>(
                File.ReadAllText(path + ".metadata.json"))
                ?? throw new JsonException("Backup metadata is empty.");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 ||
                string.IsNullOrWhiteSpace(metadata.BackupId) ||
                string.IsNullOrWhiteSpace(metadata.FileName) ||
                metadata.FileSize <= 0 ||
                string.IsNullOrWhiteSpace(metadata.Sha256) ||
                metadata.MigrationIds is null ||
                metadata.ValidationResult != LocalDatabaseBackupCodes.Success ||
                !string.Equals(metadata.FileName, Path.GetFileName(requireMetadata ? path : metadata.FileName), StringComparison.Ordinal))
            {
                return Invalid(DatabaseRestoreCodes.BackupInvalid, "备份文件或元数据无效。");
            }

            if (info.Length != metadata.FileSize ||
                !string.Equals(ComputeSha256(path), metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(DatabaseRestoreCodes.HashMismatch, "备份 SHA-256 与元数据不一致。");
            }
        }
        catch
        {
            return Invalid(DatabaseRestoreCodes.BackupInvalid, "备份文件或元数据不可读取。");
        }

        var structure = ValidateDatabaseStructure(path, expectedMigrations);
        if (!structure.Succeeded)
        {
            return structure with { Metadata = null };
        }

        if (!metadata.MigrationIds.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
        {
            return Invalid(DatabaseRestoreCodes.MigrationIncompatible, "备份 migration 与当前应用不兼容。");
        }

        var sharedMetadata = new PreImportSnapshotMetadata(
            path,
            path,
            metadata.Sha256,
            metadata.CreatedAtUtc,
            metadata.FileSize,
            metadata.MigrationIds);
        if (!snapshotService.ValidateSnapshot(sharedMetadata))
        {
            return Invalid(DatabaseRestoreCodes.BackupInvalid, "备份关键 schema 验证失败。");
        }

        return new RestoreValidation(true, DatabaseRestoreCodes.Restored, string.Empty, metadata);
    }

    private static RestoreValidation ValidateDatabaseStructure(string path, IReadOnlyList<string> expectedMigrations)
    {
        try
        {
            using var connection = OpenReadOnly(path);
            using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                using var reader = integrity.ExecuteReader();
                if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                {
                    return Invalid(DatabaseRestoreCodes.IntegrityFailed, "备份 SQLite 完整性验证失败。");
                }
            }

            using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId COLLATE BINARY;";
            using var migrationReader = migrationCommand.ExecuteReader();
            var migrations = new List<string>();
            while (migrationReader.Read())
            {
                migrations.Add(migrationReader.GetString(0));
            }

            if (!migrations.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
            {
                return Invalid(DatabaseRestoreCodes.MigrationIncompatible, "备份 migration 与当前应用不兼容。");
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            return Invalid(DatabaseRestoreCodes.IntegrityFailed, "备份 SQLite 完整性验证失败。");
        }
        catch
        {
            return Invalid(DatabaseRestoreCodes.BackupInvalid, "备份 SQLite 或 migration 状态不可读取。");
        }

        return new RestoreValidation(true, DatabaseRestoreCodes.Restored, string.Empty, null);
    }

    private static string[]? ReadExpectedMigrations(string targetPath)
    {
        try
        {
            using var context = DatabaseInitializer.CreateContext(targetPath);
            return context.Database.GetMigrations().ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string[]? ReadExpectedMigrationsForListing()
    {
        try
        {
            using var context = new StoreDbContextFactory().CreateDbContext([]);
            return context.Database.GetMigrations().ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RestoreSidecars(IEnumerable<(string Original, string Quarantine)> sidecars)
    {
        foreach (var (original, quarantine) in sidecars)
        {
            if (File.Exists(quarantine) && !File.Exists(original))
            {
                File.Move(quarantine, original);
            }
        }
    }

    private static void DeleteQuarantinedSidecars(IEnumerable<(string Original, string Quarantine)> sidecars)
    {
        foreach (var (_, quarantine) in sidecars)
        {
            Delete(quarantine);
        }
    }

    private static void DeleteCurrentSidecars(string databasePath)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            Delete(databasePath + suffix);
        }
    }

    private static bool CleanupCompleted(
        string targetPath,
        string stagingPath,
        string rollbackPath,
        string failedPath,
        IEnumerable<(string Original, string Quarantine)> quarantinedSidecars) =>
        !Path.Exists(stagingPath) &&
        !Path.Exists(rollbackPath) &&
        !Path.Exists(failedPath) &&
        new[] { targetPath, stagingPath, rollbackPath, failedPath }
            .All(path => SidecarSuffixes.All(suffix => !Path.Exists(path + suffix))) &&
        quarantinedSidecars.All(sidecar => !Path.Exists(sidecar.Quarantine));

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static RestoreValidation Invalid(string code, string summary) => new(false, code, summary, null);

    private static DatabaseRestoreResult Failure(
        string code,
        string summary,
        LocalDatabaseBackupResult? protection = null) =>
        DatabaseRestoreResult.Failure(code, summary, protection);

    private sealed record RestoreValidation(
        bool Succeeded,
        string Code,
        string Summary,
        LocalDatabaseBackupMetadata? Metadata);
}

public sealed record DatabaseRestoreResult(
    bool Succeeded,
    string Code,
    string SafeSummary,
    string? RestoredBackupId,
    string? PreRestoreBackupId,
    string? PreRestoreBackupPath,
    long? RestoredFileSize,
    string? RestoredSha256,
    IReadOnlyList<string> MigrationIds,
    string? IntegrityResult)
{
    internal static DatabaseRestoreResult Success(
        LocalDatabaseBackupMetadata restored,
        LocalDatabaseBackupResult protection) => new(
            true,
            DatabaseRestoreCodes.Restored,
            "本地数据库已从验证备份安全恢复。",
            restored.BackupId,
            protection.BackupId,
            protection.BackupPath,
            restored.FileSize,
            restored.Sha256,
            restored.MigrationIds,
            "ok");

    internal static DatabaseRestoreResult Failure(
        string code,
        string summary,
        LocalDatabaseBackupResult? protection) => new(
            false,
            code,
            summary,
            null,
            protection?.BackupId,
            protection?.BackupPath,
            null,
            null,
            Array.Empty<string>(),
            null);
}

internal sealed record LocalDatabaseBackupValidation(
    bool Succeeded,
    string Code,
    string Summary,
    LocalDatabaseBackupMetadata? Metadata)
{
    public static LocalDatabaseBackupValidation Failure(string code, string summary) =>
        new(false, code, summary, null);
}
