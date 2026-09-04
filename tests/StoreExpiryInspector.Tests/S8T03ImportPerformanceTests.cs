using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S8T03ImportPerformanceTests
{
    private static readonly string[] Headers =
    [
        "商品大类", "商品编码", "商品条码", "商品名称", "生产日期", "有效日期", "保质期", "保质期单位",
        "是否该做临期折扣", "该批次累计到货数量", "该商品门店库存总数"
    ];

    [Fact]
    [Trait("Category", "S8T03")]
    public void Small_real_import_writes_isolated_before_evidence()
    {
        Run(1_000, requireExplicitHighScaleGate: false);
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [Trait("Category", "S8T03HighScale")]
    public void High_scale_real_import_requires_explicit_gate(int rows)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("S8_T03_RUN_HIGH_SCALE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("S8_T03_ROWS"), out var requestedRows) && requestedRows != rows)
        {
            return;
        }

        Run(rows, requireExplicitHighScaleGate: true);
    }

    private static void Run(int rows, bool requireExplicitHighScaleGate)
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var sourcePath = Path.Combine(root, "source", $"synthetic-{rows}.xlsx");
        var seedPath = Path.Combine(root, "source", $"seed-{rows}.xlsx");
        var snapshotDirectory = Path.Combine(root, "snapshots");
        var evidenceDirectory = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidenceDirectory);
        AssertIsUnderRoot(root, databasePath, sourcePath, seedPath, snapshotDirectory, evidenceDirectory);
        Assert.False(File.Exists(databasePath));
        var measures = new Dictionary<string, double>(StringComparer.Ordinal);
        var stageMeasures = new Dictionary<string, double>(StringComparer.Ordinal);
        var total = new Stopwatch();
        long workbookBytes = 0;
        long allocationStart = 0;
        try
        {
            var existingProductCount = Math.Min(rows / 2, 1_000);

        Measure(measures, "database_initialize", () => DatabaseInitializer.Initialize(databasePath));
        WriteWorkbook(seedPath, existingProductCount, batchesPerProduct: 1, seed: true);
        SeedExisting(databasePath, seedPath, snapshotDirectory);
        Measure(measures, "workbook_generate", () => WriteWorkbook(sourcePath, rows / 2, batchesPerProduct: 2, seed: false));
        workbookBytes = new FileInfo(sourcePath).Length;
        allocationStart = GC.GetTotalAllocatedBytes();
        total.Start();
        ExcelWorkbookDto workbook = null!;
        Measure(measures, "workbook_open_parse", () => workbook = new ExcelTemplateReader().Read(sourcePath));
        ExcelClassificationResult classification = null!;
        Measure(measures, "classify_validate", () => classification = new ExcelFileClassifier().Classify(workbook));
        ImportPlan plan = null!;
        ImportConfirmationContract contract = null!;
        Measure(measures, "plan_existing_load", () =>
        {
            using var preview = DatabaseInitializer.CreateContext(databasePath);
            plan = new ExcelImportPlanner().Plan(preview, classification);
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        });

        Assert.Equal(rows, workbook.Rows.Count);
        Assert.Equal(rows / 2 - existingProductCount, plan.NewProductCount);
        Assert.Equal(rows - existingProductCount, plan.NewBatchCount);
        Assert.Equal(existingProductCount, plan.UpdatedBatchCount);
        Assert.Equal(rows / 2, plan.ExplicitProductStocks.Count);
        Assert.Empty(classification.SkippedRows);

        ConfirmedImportResult result = null!;
        Measure(measures, "snapshot_write_post", () =>
        {
            using var execute = DatabaseInitializer.CreateContext(databasePath);
            void Capture(string stage, TimeSpan elapsed) => stageMeasures[stage] = stageMeasures.GetValueOrDefault(stage) + elapsed.TotalMilliseconds;
            var occurredAtUtc = new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc);
            result = new ConfirmedImportLifecycleOrchestrator(
                executor: new ConfirmedImportExecutor(utcNow: () => occurredAtUtc, measure: Capture),
                measure: Capture).Execute(execute, new(
                contract,
                snapshotDirectory,
                new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc),
                new DateOnly(2026, 9, 4),
                occurredAtUtc));
        });
        total.Stop();

        Assert.True(result.Succeeded, result.Code);
        Assert.True(File.Exists(result.SnapshotPath));
        AssertIsUnderRoot(root, result.SnapshotPath!);
        using (var verify = DatabaseInitializer.CreateContext(databasePath))
        {
            Assert.Equal(rows / 2, verify.Products.Count());
            Assert.Equal(rows, verify.Batches.Count());
            Assert.Equal(2, verify.Imports.Count(import => import.Status == ImportStatuses.Succeeded));
            Assert.Equal(2, verify.ImportWorkbooks.Count());
            Assert.Equal(rows / 20, verify.Products.Count(product => product.EffectiveStockQty == 0));
        }

        var verification = Verify(databasePath);
        Assert.True(verification.IntegrityOk);
        Assert.Equal(0, verification.ForeignKeyViolations);
        var evidence = new
        {
            card = "S8-T03",
            kind = "before",
            implementation_commit = Environment.GetEnvironmentVariable("S8_T03_COMMIT") ?? "not_supplied",
            rows,
            product_count = rows / 2,
            high_scale_gate = requireExplicitHighScaleGate,
            root,
            source_path = sourcePath,
            database_path = databasePath,
            snapshot_path = result.SnapshotPath,
            workbook_bytes = workbookBytes,
            database_physical_bytes = DatabasePhysicalBytes(databasePath),
            database_logical_bytes = DatabaseLogicalBytes(databasePath),
            snapshot_bytes = new FileInfo(result.SnapshotPath!).Length,
            measures_ms = measures,
            import_stage_ms = stageMeasures,
            total_ms = total.Elapsed.TotalMilliseconds,
            managed_allocated_bytes = GC.GetTotalAllocatedBytes() - allocationStart,
            working_set_bytes = Environment.WorkingSet,
            integrity_check = verification.IntegrityOk ? "ok" : "failed",
            foreign_key_check_count = verification.ForeignKeyViolations,
            data_distribution = "2 batches/product; first up-to-1000 products pre-exist with product-name/stock and batch-0 arrival update, remaining products/batches are new; products rotate all 10 supported categories; every tenth product has stock 0; D/M/Y all occur"
        };
            File.WriteAllText(Path.Combine(evidenceDirectory, "before.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, "before-failure.json"), JsonSerializer.Serialize(new
            {
                card = "S8-T03", kind = "before_failure", rows, root, source_path = sourcePath,
                database_path = databasePath, snapshot_directory = snapshotDirectory, evidence_directory = evidenceDirectory,
                workbook_bytes = workbookBytes, measures_ms = measures, total_ms = total.Elapsed.TotalMilliseconds,
                managed_allocated_bytes = GC.GetTotalAllocatedBytes() - allocationStart,
                implementation_commit = Environment.GetEnvironmentVariable("S8_T03_COMMIT") ?? "not_supplied",
                exception_type = exception.GetType().FullName, exception_message = exception.Message
            }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }

    private static void SeedExisting(string databasePath, string sourcePath, string snapshotDirectory)
    {
        var workbook = new ExcelTemplateReader().Read(sourcePath);
        var classification = new ExcelFileClassifier().Classify(workbook);
        ImportPlan plan;
        using (var preview = DatabaseInitializer.CreateContext(databasePath))
        {
            plan = new ExcelImportPlanner().Plan(preview, classification);
        }

        var contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
            new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        using var execute = DatabaseInitializer.CreateContext(databasePath);
        var result = new ConfirmedImportLifecycleOrchestrator().Execute(execute, new(
            contract, snapshotDirectory, new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 9, 3), new DateTime(2026, 9, 3, 8, 1, 0, DateTimeKind.Utc)));
        Assert.True(result.Succeeded, result.Code);
    }

    private static void WriteWorkbook(string path, int products, int batchesPerProduct, bool seed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        Write(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Write(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Write(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        WriteRow(writer, 1, Headers);
        for (var product = 0; product < products; product++)
        {
            var category = (product % 10) switch { 0 => "食品", 1 => "宠物", 2 => "日用", 3 => "美妆", 4 => "家居", 5 => "香氛香水", 6 => "文具", 7 => "潮流玩具", 8 => "应季搭配", _ => "赠品小样" };
            var stock = seed ? "20" : product % 10 == 0 ? "0" : "30";
            var shortShelfLife = category is "食品" or "宠物";
            var yearlyShelfLife = product % 10 == 2;
            for (var batch = 0; batch < batchesPerProduct; batch++)
            {
                var row = product * batchesPerProduct + batch + 2;
                WriteRow(writer, row,
                [category, $"S8T03-{product:D6}", $"690{product:D10}", $"{(seed ? "合成商品" : "合成更新商品")}{product:D6}", batch == 0 ? "2026-01-01" : "2026-02-01", batch == 0 && shortShelfLife ? "2026-09-09" : "2027-09-04", shortShelfLife ? "10" : yearlyShelfLife ? "1" : "12", shortShelfLife ? "D" : yearlyShelfLife ? "Y" : "M", "是", (seed ? 1 : batch == 0 ? 2 : 1).ToString(), stock]);
            }
        }

        writer.Write("</sheetData></worksheet>");
    }

    private static void WriteRow(TextWriter writer, int row, IReadOnlyList<string> values)
    {
        writer.Write($"<row r=\"{row}\">");
        for (var column = 0; column < values.Count; column++)
        {
            writer.Write($"<c r=\"{Column(column)}{row}\" t=\"inlineStr\"><is><t>");
            writer.Write(System.Security.SecurityElement.Escape(values[column]));
            writer.Write("</t></is></c>");
        }

        writer.Write("</row>");
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Column(int index)
    {
        var value = index + 1;
        var result = string.Empty;
        while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; }
        return result;
    }

    private static void Measure(IDictionary<string, double> measures, string name, Action action)
    {
        var watch = Stopwatch.StartNew();
        try { action(); }
        finally { watch.Stop(); measures[name] = watch.Elapsed.TotalMilliseconds; }
    }

    private static void AssertIsUnderRoot(string root, params string[] paths)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var path in paths)
        {
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.StartsWith(fullRoot, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static (bool IntegrityOk, int ForeignKeyViolations) Verify(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var integrityOk = string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        var foreignKeyViolations = 0;
        while (reader.Read()) foreignKeyViolations++;
        return (integrityOk, foreignKeyViolations);
    }

    private static long DatabasePhysicalBytes(string databasePath) =>
        new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);

    private static long DatabaseLogicalBytes(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_count;";
        var pages = Convert.ToInt64(command.ExecuteScalar());
        command.CommandText = "PRAGMA page_size;";
        return checked(pages * Convert.ToInt64(command.ExecuteScalar()));
    }
}
