using System.IO;
using System.Security;
using System.Security.Cryptography;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Imports;

public static class ImportStatuses
{
    public const string Succeeded = "Succeeded";

    public const string Undone = "Undone";

    public static bool IsValid(string? value) => value is Succeeded or Undone;
}

public static class ImportConfirmationCodes
{
    public const string Ready = "ready";

    public const string NoChanges = "no_changes";

    public const string FileChanged = "file_changed";

    public const string FileMissing = "file_missing";

    public const string FileUnavailable = "file_unavailable";
}

public sealed class ImportPreviewIdentity
{
    public ImportPreviewIdentity(string sourceFilePath, ExcelWorkbookDto workbook, ImportPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(plan);

        if (!Path.IsPathFullyQualified(sourceFilePath))
        {
            throw new ArgumentException("The preview source path must be absolute.", nameof(sourceFilePath));
        }

        var fullPath = Path.GetFullPath(sourceFilePath);
        var fileName = Path.GetFileName(fullPath);
        if (!string.Equals(fileName, workbook.SourceFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The workbook file name does not match the preview source path.", nameof(workbook));
        }

        if (!IsLowerSha256(workbook.SourceFileSha256))
        {
            throw new ArgumentException("The workbook SHA-256 must be 64 lowercase hexadecimal characters.", nameof(workbook));
        }

        SourceFilePath = fullPath;
        SourceFileName = fileName;
        SourceFileSha256 = workbook.SourceFileSha256;
        Plan = plan;
    }

    public string SourceFilePath { get; }

    public string SourceFileName { get; }

    public string SourceFileSha256 { get; }

    public ImportPlan Plan { get; }

    private static bool IsLowerSha256(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class ImportConfirmationGuard
{
    public ImportPreviewIdentity BindPreview(string sourceFilePath, ExcelWorkbookDto workbook, ImportPlan plan) =>
        new(sourceFilePath, workbook, plan);

    public ImportConfirmationResult Confirm(ImportPreviewIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!identity.Plan.HasChanges)
        {
            return ImportConfirmationResult.NoChanges();
        }

        try
        {
            var (bytes, sha256) = ReadFileBytes(identity.SourceFilePath);
            if (!string.Equals(sha256, identity.SourceFileSha256, StringComparison.Ordinal))
            {
                return ImportConfirmationResult.FileChanged();
            }

            return ImportConfirmationResult.Ready(
                new ImportConfirmationContract(
                    identity.SourceFilePath,
                    identity.SourceFileName,
                    sha256,
                    bytes,
                    identity.Plan));
        }
        catch (FileNotFoundException)
        {
            return ImportConfirmationResult.FileMissing();
        }
        catch (DirectoryNotFoundException)
        {
            return ImportConfirmationResult.FileUnavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return ImportConfirmationResult.FileUnavailable();
        }
        catch (SecurityException)
        {
            return ImportConfirmationResult.FileUnavailable();
        }
        catch (IOException)
        {
            return ImportConfirmationResult.FileUnavailable();
        }
    }

    private static (byte[] Bytes, string Sha256) ReadFileBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var content = new MemoryStream();
        stream.CopyTo(content);
        var bytes = content.ToArray();
        var digest = SHA256.HashData(bytes);
        return (bytes, Convert.ToHexString(digest).ToLowerInvariant());
    }
}

public sealed class ImportConfirmationResult
{
    private ImportConfirmationResult(
        string code,
        bool canConfirm,
        string safeUserMessage,
        ImportConfirmationContract? contract)
    {
        Code = code;
        CanConfirm = canConfirm;
        SafeUserMessage = safeUserMessage;
        Contract = contract;
    }

    public string Code { get; }

    public bool CanConfirm { get; }

    public string SafeUserMessage { get; }

    public ImportConfirmationContract? Contract { get; }

    internal static ImportConfirmationResult Ready(ImportConfirmationContract contract) => new(
        ImportConfirmationCodes.Ready,
        true,
        "文件身份已验证，可以确认导入。",
        contract);

    internal static ImportConfirmationResult NoChanges() => new(
        ImportConfirmationCodes.NoChanges,
        false,
        "预览没有商品或批次变化，无需确认导入。",
        null);

    internal static ImportConfirmationResult FileChanged() => new(
        ImportConfirmationCodes.FileChanged,
        false,
        "源文件内容已变化，请重新解析并预览后再确认。",
        null);

    internal static ImportConfirmationResult FileMissing() => new(
        ImportConfirmationCodes.FileMissing,
        false,
        "预览绑定的源文件已不存在，请重新解析并预览。",
        null);

    internal static ImportConfirmationResult FileUnavailable() => new(
        ImportConfirmationCodes.FileUnavailable,
        false,
        "无法读取预览绑定的源文件，请重新解析并预览。",
        null);
}

public sealed class ImportConfirmationContract
{
    internal ImportConfirmationContract(
        string sourceFilePath,
        string sourceFileName,
        string sourceFileSha256,
        byte[] workbookBytes,
        ImportPlan plan)
    {
        SourceFilePath = sourceFilePath;
        SourceFileName = sourceFileName;
        SourceFileSha256 = sourceFileSha256;
        WorkbookBytes = new ReadOnlyMemory<byte>(workbookBytes);
        Plan = plan;
    }

    public string SourceFilePath { get; }

    public string SourceFileName { get; }

    public string SourceFileSha256 { get; }

    public ReadOnlyMemory<byte> WorkbookBytes { get; }

    public ImportPlan Plan { get; }

    public string TargetImportStatus => ImportStatuses.Succeeded;

    public int NewTaskProductCount => 0;
}
