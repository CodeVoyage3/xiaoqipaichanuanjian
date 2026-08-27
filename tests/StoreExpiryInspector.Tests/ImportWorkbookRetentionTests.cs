using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ImportWorkbookRetentionTests
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
    public void RetainsTwoRecentSucceededWorkbooksThroughFiveImports()
    {
        using var database = SqliteTestDatabase.Create();
        SeedDatabase(database);
        var expected = new List<ExpectedWorkbook>();
        var confirmedAtUtc = new[]
        {
            new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 27, 9, 2, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 27, 9, 3, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 27, 9, 3, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 27, 9, 4, 0, DateTimeKind.Utc)
        };

        for (var sequence = 1; sequence <= 5; sequence++)
        {
            using var source = CreateSource(database.Directory, sequence);
            expected.Add(ExecuteSuccessfulImport(database, source.Path, confirmedAtUtc[sequence - 1]));

            using var verify = database.Open();
            AssertRetainedWorkbooks(verify, expected);
            Assert.Equal(Math.Min(sequence, 2), verify.ImportWorkbooks.AsNoTracking().Count());
            Assert.Equal(sequence, verify.Imports.AsNoTracking().Count());
            if (sequence == 4)
            {
                Assert.Equal(
                    expected.Skip(2).Take(2).OrderByDescending(item => item.ImportId).Select(item => item.ImportId),
                    ReadRetainedWorkbooks(verify).Select(item => item.ImportId));
            }
        }

        using var finalVerify = database.Open();
        var imports = finalVerify.Imports.AsNoTracking().OrderBy(import => import.Id).ToArray();
        Assert.Equal(5, imports.Length);
        foreach (var import in imports)
        {
            var matching = Assert.Single(expected, item => item.ImportId == import.Id);
            Assert.Equal(matching.FileName, import.SourceFileName);
            Assert.Equal(matching.Sha256, import.SourceFileSha256);
            Assert.Equal(matching.ParsedAtUtc, import.ParsedAtUtc);
            Assert.Equal(matching.ConfirmedAtUtc, import.ConfirmedAtUtc);
            Assert.Equal(ImportStatuses.Succeeded, import.Status);
            Assert.Equal(import.IssueCount, finalVerify.ImportIssues.AsNoTracking().Count(issue => issue.ImportId == import.Id));
            Assert.NotNull(import.PreImportSnapshotPath);
            Assert.True(File.Exists(import.PreImportSnapshotPath));
        }

        var retained = ReadRetainedWorkbooks(finalVerify);
        Assert.Equal(new[] { expected[4].ImportId, expected[3].ImportId }, retained.Select(item => item.ImportId));
        Assert.Equal(5, finalVerify.BackupRecords.AsNoTracking().Count());
        Assert.Equal(5, finalVerify.ImportIssues.AsNoTracking().Count());
        Assert.Single(finalVerify.Products.AsNoTracking());
        Assert.Single(finalVerify.Batches.AsNoTracking());
        Assert.Empty(finalVerify.Tasks.AsNoTracking());
        Assert.Empty(finalVerify.Drafts.AsNoTracking());
        Assert.Empty(finalVerify.Inspections.AsNoTracking());
        Assert.Empty(finalVerify.InventoryAdjustments.AsNoTracking());
        Assert.Empty(finalVerify.LifecycleEvents.AsNoTracking());
    }

    [Fact]
    public void ChangedSourceDoesNotChangeExistingRetainedWorkbooks()
    {
        using var database = SqliteTestDatabase.Create();
        SeedDatabase(database);
        using (var source = CreateSource(database.Directory, 1))
        {
            ExecuteSuccessfulImport(database, source.Path, new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc));
        }

        using (var source = CreateSource(database.Directory, 2))
        {
            ExecuteSuccessfulImport(database, source.Path, new DateTime(2026, 8, 27, 9, 2, 0, DateTimeKind.Utc));
        }

        ImportSnapshot[] beforeImports;
        WorkbookSnapshot[] beforeWorkbooks;
        using (var before = database.Open())
        {
            beforeImports = ReadImports(before);
            beforeWorkbooks = ReadAllWorkbooks(before);
        }

        using var changedSource = CreateSource(database.Directory, 3);
        var contract = ReadContract(database, changedSource.Path);
        File.WriteAllBytes(changedSource.Path, [1, 2, 3]);
        using (var execute = database.Open())
        {
            var result = new ConfirmedImportExecutor(utcNow: () => new DateTime(2026, 8, 27, 9, 3, 0, DateTimeKind.Utc))
                .Execute(
                    contract,
                    execute,
                    Path.Combine(database.Directory, "snapshots"),
                    new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc));

            Assert.False(result.Succeeded);
            Assert.Equal(ConfirmedImportCodes.FileChanged, result.Code);
            Assert.Null(result.SnapshotPath);
            Assert.Empty(execute.Imports.AsNoTracking().Where(import => import.Id > 2));
            Assert.Empty(execute.ImportWorkbooks.AsNoTracking().Where(workbook => workbook.ImportId > 2));
            Assert.Empty(execute.ChangeTracker.Entries());
        }

        using var after = database.Open();
        Assert.Equal(beforeImports, ReadImports(after));
        AssertSameWorkbooks(beforeWorkbooks, ReadAllWorkbooks(after));
        Assert.Equal(2, after.Imports.AsNoTracking().Count());
        Assert.Equal(2, after.ImportWorkbooks.AsNoTracking().Count());
    }

    [Fact]
    public void RetentionDeleteFailureRollsBackThirdImportAndKeepsSnapshot()
    {
        using var database = SqliteTestDatabase.Create();
        SeedDatabase(database);
        using (var source = CreateSource(database.Directory, 1))
        {
            ExecuteSuccessfulImport(database, source.Path, new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc));
        }

        using (var source = CreateSource(database.Directory, 2))
        {
            ExecuteSuccessfulImport(database, source.Path, new DateTime(2026, 8, 27, 9, 2, 0, DateTimeKind.Utc));
        }

        ImportSnapshot[] beforeImports;
        WorkbookSnapshot[] beforeWorkbooks;
        ProductSnapshot[] beforeProducts;
        BatchSnapshot[] beforeBatches;
        IssueSnapshot[] beforeIssues;
        using (var before = database.Open())
        {
            beforeImports = ReadImports(before);
            beforeWorkbooks = ReadAllWorkbooks(before);
            beforeProducts = ReadProducts(before);
            beforeBatches = ReadBatches(before);
            beforeIssues = ReadIssues(before);
        }

        using var source3 = CreateSource(database.Directory, 3);
        var contract = ReadContract(database, source3.Path);
        using (var trigger = database.Open())
        {
            trigger.Database.ExecuteSqlRaw(
                "CREATE TRIGGER fail_import_workbooks_delete BEFORE DELETE ON import_workbooks " +
                "BEGIN SELECT RAISE(ABORT, 'forced retention failure'); END;");
        }

        ConfirmedImportResult failed;
        using (var execute = database.Open())
        {
            failed = new ConfirmedImportExecutor(utcNow: () => new DateTime(2026, 8, 27, 9, 3, 0, DateTimeKind.Utc))
                .Execute(
                    contract,
                    execute,
                    Path.Combine(database.Directory, "snapshots"),
                    new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc));

            Assert.False(failed.Succeeded);
            Assert.Equal(ConfirmedImportCodes.TransactionFailed, failed.Code);
            Assert.Null(failed.ImportId);
            Assert.Empty(execute.ChangeTracker.Entries());
            execute.SaveChanges();
        }

        var snapshotPath = Assert.IsType<string>(failed.SnapshotPath);
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal(failed.SnapshotMetadata!.Sha256, Sha256(snapshotPath));
        Assert.True(new PreImportSnapshotService().ValidateSnapshot(failed.SnapshotMetadata));

        using (var afterFailure = database.Open())
        {
            Assert.Equal(beforeImports, ReadImports(afterFailure));
            AssertSameWorkbooks(beforeWorkbooks, ReadAllWorkbooks(afterFailure));
            Assert.Equal(beforeProducts, ReadProducts(afterFailure));
            Assert.Equal(beforeBatches, ReadBatches(afterFailure));
            Assert.Equal(beforeIssues, ReadIssues(afterFailure));
            Assert.Equal(2, afterFailure.BackupRecords.AsNoTracking().Count());
            Assert.Equal(2, afterFailure.Imports.AsNoTracking().Count());
            Assert.Equal(2, afterFailure.ImportWorkbooks.AsNoTracking().Count());
        }

        using (var removeTrigger = database.Open())
        {
            removeTrigger.Database.ExecuteSqlRaw("DROP TRIGGER fail_import_workbooks_delete;");
        }

        var retry = ExecuteSuccessfulImport(database, source3.Path, new DateTime(2026, 8, 27, 9, 3, 0, DateTimeKind.Utc));
        Assert.Equal(3, retry.ImportId);
        using var finalVerify = database.Open();
        Assert.Equal(new[] { retry.ImportId, beforeImports[1].Id }, ReadRetainedWorkbooks(finalVerify).Select(item => item.ImportId));
        Assert.Equal(3, finalVerify.Imports.AsNoTracking().Count());
        Assert.Equal(2, finalVerify.ImportWorkbooks.AsNoTracking().Count());
    }

    private static void SeedDatabase(SqliteTestDatabase database)
    {
        using var context = database.Open();
        var product = new Product
        {
            ProductCode = "P",
            CurrentName = "初始商品",
            CurrentBarcode = "B",
            ExcelStockQty = 1,
            EffectiveStockQty = 1,
            EffectiveStockSource = "excel"
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.Batches.Add(new Batch
        {
            ProductId = product.Id,
            ProductionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2026, 12, 31),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 1,
            MaxArrivalQty = 1,
            SourceDiscountReference = "否"
        });
        context.SaveChanges();
    }

    private static ExpectedWorkbook ExecuteSuccessfulImport(
        SqliteTestDatabase database,
        string sourcePath,
        DateTime confirmedAtUtc)
    {
        var contract = ReadContract(database, sourcePath);
        var bytes = File.ReadAllBytes(sourcePath);
        using var context = database.Open();
        var result = new ConfirmedImportExecutor(utcNow: () => confirmedAtUtc)
            .Execute(
                contract,
                context,
                Path.Combine(database.Directory, "snapshots"),
                new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc));
        Assert.True(result.Succeeded);
        var importId = Assert.IsType<long>(result.ImportId);
        return new ExpectedWorkbook(
            importId,
            contract.SourceFileName,
            bytes,
            contract.SourceFileSha256,
            new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc),
            confirmedAtUtc);
    }

    private static ImportConfirmationContract ReadContract(SqliteTestDatabase database, string sourcePath)
    {
        using var context = database.Open();
        var workbook = new ExcelTemplateReader().Read(sourcePath);
        var plan = new ExcelImportPlanner().Plan(context, new ExcelFileClassifier().Classify(workbook));
        var identity = new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan);
        var confirmation = new ImportConfirmationGuard().Confirm(identity);
        Assert.True(confirmation.CanConfirm);
        return Assert.IsType<ImportConfirmationContract>(confirmation.Contract);
    }

    private static void AssertRetainedWorkbooks(
        StoreDbContext context,
        IReadOnlyList<ExpectedWorkbook> expected)
    {
        var desired = expected
            .OrderByDescending(item => item.ConfirmedAtUtc)
            .ThenByDescending(item => item.ImportId)
            .Take(2)
            .ToArray();
        var actual = ReadRetainedWorkbooks(context);
        Assert.Equal(desired.Length, actual.Length);
        for (var index = 0; index < desired.Length; index++)
        {
            Assert.Equal(desired[index].ImportId, actual[index].ImportId);
            Assert.Equal(desired[index].FileName, actual[index].OriginalFileName);
            Assert.Equal(desired[index].Sha256, actual[index].Sha256);
            Assert.Equal(desired[index].ConfirmedAtUtc, actual[index].ConfirmedAtUtc);
            Assert.Equal(desired[index].ConfirmedAtUtc, actual[index].SavedAtUtc);
            Assert.True(desired[index].Bytes.AsSpan().SequenceEqual(actual[index].Content));
        }
    }

    private static StoredWorkbook[] ReadRetainedWorkbooks(StoreDbContext context) =>
        context.ImportWorkbooks.AsNoTracking()
            .Join(
                context.Imports.AsNoTracking(),
                workbook => workbook.ImportId,
                import => import.Id,
                (workbook, import) => new { Workbook = workbook, Import = import })
            .Where(item => item.Import.Status == ImportStatuses.Succeeded)
            .OrderByDescending(item => item.Import.ConfirmedAtUtc)
            .ThenByDescending(item => item.Import.Id)
            .Select(item => new StoredWorkbook(
                item.Workbook.ImportId,
                item.Import.ConfirmedAtUtc,
                item.Workbook.OriginalFileName,
                item.Workbook.Content,
                item.Workbook.Sha256,
                item.Workbook.SavedAtUtc))
            .ToArray();

    private static WorkbookSnapshot[] ReadAllWorkbooks(StoreDbContext context) =>
        context.ImportWorkbooks.AsNoTracking()
            .OrderBy(workbook => workbook.Id)
            .Select(workbook => new WorkbookSnapshot(
                workbook.Id,
                workbook.ImportId,
                workbook.OriginalFileName,
                workbook.Content,
                workbook.Sha256,
                workbook.SavedAtUtc))
            .ToArray();

    private static ImportSnapshot[] ReadImports(StoreDbContext context) =>
        context.Imports.AsNoTracking()
            .OrderBy(import => import.Id)
            .Select(import => new ImportSnapshot(
                import.Id,
                import.SourceFileName,
                import.SourceFileSha256,
                import.ParsedAtUtc,
                import.ConfirmedAtUtc,
                import.Status,
                import.ProductCount,
                import.BatchCount,
                import.NewProductCount,
                import.NewBatchCount,
                import.UpdatedBatchCount,
                import.IssueCount,
                import.UnsupportedCategoryCount,
                import.NewTaskProductCount,
                import.PreImportSnapshotPath,
                import.IsUndone,
                import.UndoneAtUtc))
            .ToArray();

    private static ProductSnapshot[] ReadProducts(StoreDbContext context) =>
        context.Products.AsNoTracking()
            .OrderBy(product => product.Id)
            .Select(product => new ProductSnapshot(
                product.Id,
                product.ProductCode,
                product.CurrentName,
                product.CurrentBarcode,
                product.CategoryCode,
                product.PolicyCode,
                product.ExcelStockQty,
                product.EffectiveStockQty,
                product.EffectiveStockSource,
                product.LifecycleGeneration,
                product.IsStockZeroTerminated,
                product.LastSeenImportId,
                product.CreatedAtUtc,
                product.UpdatedAtUtc))
            .ToArray();

    private static BatchSnapshot[] ReadBatches(StoreDbContext context) =>
        context.Batches.AsNoTracking()
            .OrderBy(batch => batch.Id)
            .Select(batch => new BatchSnapshot(
                batch.Id,
                batch.ProductId,
                batch.ProductionDate,
                batch.ExpiryDate,
                batch.ShelfLifeValue,
                batch.ShelfLifeUnit,
                batch.CurrentArrivalQty,
                batch.MaxArrivalQty,
                batch.SourceDiscountReference,
                batch.LifecycleGeneration,
                batch.TrackingStatus,
                batch.StopReason,
                batch.StoppedAtUtc,
                batch.CurrentStage,
                batch.NextTriggerDate,
                batch.AttentionVersion,
                batch.HandledAttentionVersion,
                batch.LastSeenImportId,
                batch.CreatedAtUtc,
                batch.UpdatedAtUtc))
            .ToArray();

    private static IssueSnapshot[] ReadIssues(StoreDbContext context) =>
        context.ImportIssues.AsNoTracking()
            .OrderBy(issue => issue.Id)
            .Select(issue => new IssueSnapshot(
                issue.Id,
                issue.ImportId,
                issue.RowNumber,
                issue.IssueType,
                issue.FieldName,
                issue.SafeSummary))
            .ToArray();

    private static void AssertSameWorkbooks(
        IReadOnlyList<WorkbookSnapshot> expected,
        IReadOnlyList<WorkbookSnapshot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].ImportId, actual[index].ImportId);
            Assert.Equal(expected[index].OriginalFileName, actual[index].OriginalFileName);
            Assert.Equal(expected[index].Sha256, actual[index].Sha256);
            Assert.Equal(expected[index].SavedAtUtc, actual[index].SavedAtUtc);
            Assert.True(expected[index].Content.AsSpan().SequenceEqual(actual[index].Content));
        }
    }

    private static SourceFixture CreateSource(string parentDirectory, int sequence)
    {
        var directory = Path.Combine(parentDirectory, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"source-{sequence}.xlsx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var header = string.Join(string.Empty, Headers.Select((value, index) => InlineCell(ColumnName(index), 1, value)));
            var rows = new[]
            {
                new[] { "食品", "P", "B", $"商品-{sequence}", "2026-01-01", "2026-12-31", "12", "M", "否", "1", "1" },
                new[] { "食品", "P-BAD", "bad", "坏行", "2026-01-01", "bad-date", "12", "M", "否", "1", "1" }
            };
            var body = rows.Select((row, rowIndex) =>
                $"<row r=\"{rowIndex + 2}\">{string.Join(string.Empty, row.Select((value, index) => InlineCell(ColumnName(index), rowIndex + 2, value)))}</row>");
            AddEntry(archive, "xl/worksheets/sheet1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{header}</row>{string.Join(string.Empty, body)}</sheetData></worksheet>");
        }

        return new SourceFixture(directory, path);
    }

    private static string InlineCell(string column, int row, string value) =>
        $"<c r=\"{column}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
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

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record ExpectedWorkbook(
        long ImportId,
        string FileName,
        byte[] Bytes,
        string Sha256,
        DateTime ParsedAtUtc,
        DateTime ConfirmedAtUtc);

    private sealed record StoredWorkbook(
        long ImportId,
        DateTime? ConfirmedAtUtc,
        string OriginalFileName,
        byte[] Content,
        string Sha256,
        DateTime SavedAtUtc);

    private sealed record WorkbookSnapshot(
        long Id,
        long ImportId,
        string OriginalFileName,
        byte[] Content,
        string Sha256,
        DateTime SavedAtUtc);

    private sealed record ImportSnapshot(
        long Id,
        string SourceFileName,
        string SourceFileSha256,
        DateTime ParsedAtUtc,
        DateTime? ConfirmedAtUtc,
        string Status,
        int ProductCount,
        int BatchCount,
        int NewProductCount,
        int NewBatchCount,
        int UpdatedBatchCount,
        int IssueCount,
        int UnsupportedCategoryCount,
        int NewTaskProductCount,
        string? PreImportSnapshotPath,
        bool IsUndone,
        DateTime? UndoneAtUtc);

    private sealed record ProductSnapshot(
        long Id,
        string ProductCode,
        string? CurrentName,
        string? CurrentBarcode,
        string CategoryCode,
        string PolicyCode,
        int ExcelStockQty,
        int EffectiveStockQty,
        string? EffectiveStockSource,
        int LifecycleGeneration,
        bool IsStockZeroTerminated,
        long? LastSeenImportId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record BatchSnapshot(
        long Id,
        long ProductId,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate,
        int ShelfLifeValue,
        string ShelfLifeUnit,
        int CurrentArrivalQty,
        int MaxArrivalQty,
        string? SourceDiscountReference,
        int LifecycleGeneration,
        string TrackingStatus,
        string? StopReason,
        DateTime? StoppedAtUtc,
        string CurrentStage,
        DateOnly? NextTriggerDate,
        int AttentionVersion,
        int HandledAttentionVersion,
        long? LastSeenImportId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record IssueSnapshot(
        long Id,
        long ImportId,
        int? RowNumber,
        string IssueType,
        string? FieldName,
        string SafeSummary);

    private sealed class SourceFixture : IDisposable
    {
        public SourceFixture(string directory, string path)
        {
            Directory = directory;
            Path = path;
        }

        public string Directory { get; }

        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
