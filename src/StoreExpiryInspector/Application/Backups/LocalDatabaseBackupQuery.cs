using System.IO;
using System.Globalization;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application.Backups;

public sealed class LocalDatabaseBackupQuery
{
    private readonly DatabaseRestoreUseCase _validator;

    public LocalDatabaseBackupQuery(DatabaseRestoreUseCase? validator = null)
    {
        _validator = validator ?? new DatabaseRestoreUseCase();
    }

    public IReadOnlyList<LocalDatabaseBackupListItem> List(
        string? formalDatabasePath = null,
        string? backupDirectory = null)
    {
        string directory;
        try
        {
            directory = Path.GetFullPath(
                backupDirectory ?? DatabaseInitializer.GetDefaultBackupDirectory());
        }
        catch (Exception exception)
        {
            throw new IOException("备份目录路径不可用。", exception);
        }

        IEnumerable<string> paths;
        try
        {
            var directoryAttributes = File.GetAttributes(directory);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("备份目录不可使用重解析点。");
            }

            paths = Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly)
                .Where(IsPublishedBackupFile)
                .Where(path => !IsReparsePoint(path) && !IsReparsePoint(path + ".metadata.json"))
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<LocalDatabaseBackupListItem>();
        }
        catch (FileNotFoundException)
        {
            return Array.Empty<LocalDatabaseBackupListItem>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("备份目录不可读取。", exception);
        }

        var items = new List<LocalDatabaseBackupListItem>();
        try
        {
            foreach (var path in paths)
            {
                var validation = _validator.ValidateForListing(path, formalDatabasePath);
                if (!validation.Succeeded || validation.Metadata is null)
                {
                    continue;
                }

                var metadata = validation.Metadata;
                if (!string.Equals(
                        metadata.BackupId,
                        Path.GetFileNameWithoutExtension(path),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                items.Add(new LocalDatabaseBackupListItem(
                    metadata.BackupId,
                    metadata.FileName,
                    path,
                    metadata.CreatedAtUtc,
                    metadata.FileSize,
                    metadata.Sha256,
                    metadata.MigrationIds,
                    metadata.ValidationResult,
                    true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("备份文件列表不可读取。", exception);
        }

        return items
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.BackupId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPublishedBackupFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return (fileName.StartsWith("backup-", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("pre-restore-", StringComparison.OrdinalIgnoreCase)) &&
               !fileName.Contains(".restore-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}

public sealed record LocalDatabaseBackupListItem(
    string BackupId,
    string FileName,
    string BackupPath,
    DateTime CreatedAtUtc,
    long FileSize,
    string Sha256,
    IReadOnlyList<string> MigrationIds,
    string VerificationStatus,
    bool CanRestore)
{
    public string CreatedAtText => DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string FileSizeText => $"{FileSize.ToString("N0", CultureInfo.InvariantCulture)} bytes";

    public string VerificationStatusText => CanRestore ? "已验证，可恢复" : "不可恢复";
}
