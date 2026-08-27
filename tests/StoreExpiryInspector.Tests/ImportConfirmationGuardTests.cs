using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ImportConfirmationGuardTests
{
    private static readonly string[] Headers =
    [
        "商品大类",
        "商品编码",
        "商品条码",
        "商品名称",
        "生产日期",
        "有效日期",
        "保质期",
        "保质期单位",
        "是否该做临期折扣",
        "该批次累计到货数量",
        "该商品门店库存总数"
    ];

    [Fact]
    public void BindsActualReaderIdentityAndFreezesReadyBytes()
    {
        using var fixture = CreatePreview(withChanges: true);
        var guard = new ImportConfirmationGuard();
        var identity = guard.BindPreview(fixture.Path, fixture.Workbook, fixture.Plan);

        Assert.Equal(Path.GetFullPath(fixture.Path), identity.SourceFilePath);
        Assert.Equal(fixture.Workbook.SourceFileName, identity.SourceFileName);
        Assert.Equal(fixture.Workbook.SourceFileSha256, identity.SourceFileSha256);
        Assert.Same(fixture.Plan, identity.Plan);

        var result = guard.Confirm(identity);

        Assert.Equal(ImportConfirmationCodes.Ready, result.Code);
        Assert.True(result.CanConfirm);
        Assert.NotNull(result.Contract);
        var contract = result.Contract!;
        var bytesBeforePathChange = contract.WorkbookBytes.ToArray();
        Assert.Equal(
            contract.SourceFileSha256,
            Convert.ToHexString(SHA256.HashData(bytesBeforePathChange)).ToLowerInvariant());
        Assert.Equal(ImportStatuses.Succeeded, contract.TargetImportStatus);
        Assert.Equal(0, contract.NewTaskProductCountSchemaPlaceholder);
        Assert.DoesNotContain(
            typeof(ImportConfirmationContract).GetProperties(),
            property => property.Name == "NewTaskProductCount");
        Assert.DoesNotContain(
            typeof(ImportConfirmationContract).GetProperties(),
            property => property.PropertyType == typeof(byte[]));

        File.WriteAllBytes(fixture.Path, [0x01, 0x02, 0x03]);
        Assert.Equal(bytesBeforePathChange, contract.WorkbookBytes.ToArray());
        Assert.Equal(
            "ready",
            result.Code);
        Assert.Equal(
            contract.SourceFileSha256,
            Convert.ToHexString(SHA256.HashData(contract.WorkbookBytes.ToArray())).ToLowerInvariant());

        var exposedBytes = contract.WorkbookBytes;
        Assert.True(MemoryMarshal.TryGetArray(exposedBytes, out var exposedArray));
        exposedArray.Array![exposedArray.Offset] ^= 0xff;
        Assert.Equal(bytesBeforePathChange, contract.WorkbookBytes.ToArray());
        Assert.Equal(
            contract.SourceFileSha256,
            Convert.ToHexString(SHA256.HashData(contract.WorkbookBytes.ToArray())).ToLowerInvariant());
    }

    [Fact]
    public void RejectsModifiedReplacedAndMismatchedContent()
    {
        using var fixture = CreatePreview(withChanges: true);
        var guard = new ImportConfirmationGuard();
        var identity = guard.BindPreview(fixture.Path, fixture.Workbook, fixture.Plan);

        File.WriteAllBytes(fixture.Path, [0x10, 0x20, 0x30]);
        AssertFileChanged(guard.Confirm(identity));

        File.WriteAllBytes(fixture.Path, fixture.OriginalBytes);
        var replacementPath = Path.Combine(fixture.Directory, "replacement.xlsx");
        File.WriteAllBytes(replacementPath, [0x40, 0x50, 0x60]);
        File.Move(replacementPath, fixture.Path, overwrite: true);
        AssertFileChanged(guard.Confirm(identity));

        File.WriteAllBytes(fixture.Path, fixture.OriginalBytes);
        var mismatchedWorkbook = new ExcelWorkbookDto(
            fixture.Workbook.SourceFileName,
            new string('0', 64),
            fixture.Workbook.WorksheetName,
            fixture.Workbook.NormalizedHeaders,
            fixture.Workbook.Rows);
        var mismatchedIdentity = guard.BindPreview(fixture.Path, mismatchedWorkbook, fixture.Plan);
        AssertFileChanged(guard.Confirm(mismatchedIdentity));
    }

    [Fact]
    public void SameBytesReplacementPassesByContentIdentity()
    {
        using var fixture = CreatePreview(withChanges: true);
        var guard = new ImportConfirmationGuard();
        var identity = guard.BindPreview(fixture.Path, fixture.Workbook, fixture.Plan);
        var replacementPath = Path.Combine(fixture.Directory, "same-content.xlsx");
        File.WriteAllBytes(replacementPath, fixture.OriginalBytes);
        File.Move(replacementPath, fixture.Path, overwrite: true);

        var result = guard.Confirm(identity);

        Assert.Equal(ImportConfirmationCodes.Ready, result.Code);
        Assert.NotNull(result.Contract);
        Assert.Equal(fixture.Workbook.SourceFileSha256, result.Contract!.SourceFileSha256);
    }

    [Fact]
    public void MissingDirectoryAndExclusiveFileAreUnavailableOrMissing()
    {
        using (var fixture = CreatePreview(withChanges: true))
        {
            var guard = new ImportConfirmationGuard();
            var identity = guard.BindPreview(fixture.Path, fixture.Workbook, fixture.Plan);
            File.Delete(fixture.Path);

            var result = guard.Confirm(identity);

            Assert.Equal(ImportConfirmationCodes.FileMissing, result.Code);
            Assert.False(result.CanConfirm);
            Assert.Null(result.Contract);
            Assert.Contains("重新解析", result.SafeUserMessage);
        }

        using (var fixture = CreatePreview(withChanges: true))
        {
            var guard = new ImportConfirmationGuard();
            var identity = guard.BindPreview(fixture.Path, fixture.Workbook, fixture.Plan);
            Directory.Delete(fixture.Directory, recursive: true);

            var result = guard.Confirm(identity);

            Assert.Equal(ImportConfirmationCodes.FileUnavailable, result.Code);
            Assert.False(result.CanConfirm);
            Assert.Null(result.Contract);
        }

        using var lockedFixture = CreatePreview(withChanges: true);
        var lockedGuard = new ImportConfirmationGuard();
        var lockedIdentity = lockedGuard.BindPreview(lockedFixture.Path, lockedFixture.Workbook, lockedFixture.Plan);
        using var lockStream = new FileStream(lockedFixture.Path, FileMode.Open, FileAccess.Read, FileShare.None);

        var lockedResult = lockedGuard.Confirm(lockedIdentity);

        Assert.Equal(ImportConfirmationCodes.FileUnavailable, lockedResult.Code);
        Assert.Null(lockedResult.Contract);
    }

    [Fact]
    public void NoChangesReturnsWithoutReadingOrCreatingAContract()
    {
        using var fixture = CreatePreview(withChanges: false);
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var guard = new ImportConfirmationGuard();
        var identity = guard.BindPreview(fixture.Path, fixture.Workbook, fixture.Plan);
        File.Delete(fixture.Path);

        var result = guard.Confirm(identity);

        Assert.Equal(ImportConfirmationCodes.NoChanges, result.Code);
        Assert.False(result.CanConfirm);
        Assert.Null(result.Contract);
        Assert.Equal(0, context.Imports.AsNoTracking().Count());
    }

    [Fact]
    public void RejectsInvalidPathFileNameAndShaBindings()
    {
        using var fixture = CreatePreview(withChanges: true);
        var guard = new ImportConfirmationGuard();

        Assert.Throws<ArgumentException>(() => guard.BindPreview("relative.xlsx", fixture.Workbook, fixture.Plan));
        Assert.Throws<ArgumentException>(() => guard.BindPreview(
            Path.Combine(fixture.Directory, "other.xlsx"),
            fixture.Workbook,
            fixture.Plan));

        foreach (var sha in new[] { new string('A', 64), $" {fixture.Workbook.SourceFileSha256}", new string('z', 64) })
        {
            var workbook = new ExcelWorkbookDto(
                fixture.Workbook.SourceFileName,
                sha,
                fixture.Workbook.WorksheetName,
                fixture.Workbook.NormalizedHeaders,
                fixture.Workbook.Rows);
            Assert.Throws<ArgumentException>(() => guard.BindPreview(fixture.Path, workbook, fixture.Plan));
        }
    }

    [Fact]
    public void FormalStatusesAreExactlySucceededAndUndoneAndPlansExposeNoTaskCount()
    {
        Assert.True(ImportStatuses.IsValid(ImportStatuses.Succeeded));
        Assert.True(ImportStatuses.IsValid(ImportStatuses.Undone));
        foreach (var value in new string?[]
        {
            null,
            string.Empty,
            "Preview",
            "Parsed",
            "Failed",
            "Cancelled",
            "NoChanges",
            "succeeded",
            "undone"
        })
        {
            Assert.False(ImportStatuses.IsValid(value));
        }

        Assert.DoesNotContain(
            typeof(ImportPlan).GetProperties(),
            property => property.Name.Contains("Task", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ImportPreview).GetProperties(),
            property => property.Name.Contains("Task", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertFileChanged(ImportConfirmationResult result)
    {
        Assert.Equal(ImportConfirmationCodes.FileChanged, result.Code);
        Assert.False(result.CanConfirm);
        Assert.Null(result.Contract);
        Assert.Contains("重新解析", result.SafeUserMessage);
    }

    private static PreviewFixture CreatePreview(bool withChanges)
    {
        var directory = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorConfirmationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "preview.xlsx");
        WriteWorkbook(path, withChanges);
        var workbook = new ExcelTemplateReader().Read(path);
        var classification = new ExcelFileClassifier().Classify(workbook);
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(context, classification);
        return new PreviewFixture(directory, path, workbook, plan, File.ReadAllBytes(path));
    }

    private static void WriteWorkbook(string path, bool withChanges)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");

        var header = string.Join(string.Empty, Headers.Select((value, index) => InlineCell(ColumnName(index), 1, value)));
        var row = withChanges
            ? string.Join(string.Empty, new[]
            {
                "食品", "P-READY", "B-READY", "示例商品", "2026-01-01", "2026-12-31", "12", "M", "否", "3", "5"
            }.Select((value, index) => InlineCell(ColumnName(index), 2, value)))
            : string.Empty;
        AddEntry(
            archive,
            "xl/worksheets/sheet1.xml",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{header}</row>{(withChanges ? $"<row r=\"2\">{row}</row>" : string.Empty)}</sheetData></worksheet>");
    }

    private static string InlineCell(string column, int row, string value) =>
        $"<c r=\"{column}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBasedColumn)
    {
        var value = zeroBasedColumn + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }

        return result;
    }

    private sealed class PreviewFixture : IDisposable
    {
        public PreviewFixture(string directory, string path, ExcelWorkbookDto workbook, ImportPlan plan, byte[] originalBytes)
        {
            Directory = directory;
            Path = path;
            Workbook = workbook;
            Plan = plan;
            OriginalBytes = originalBytes;
        }

        public string Directory { get; }

        public string Path { get; }

        public ExcelWorkbookDto Workbook { get; }

        public ImportPlan Plan { get; }

        public byte[] OriginalBytes { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
