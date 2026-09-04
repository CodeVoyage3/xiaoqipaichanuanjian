using System.Diagnostics;
using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
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

    [Fact]
    [Trait("Category", "S8T03")]
    public void Product_write_then_next_real_command_failure_rolls_back_the_real_import()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var sourcePath = Path.Combine(root, "source", "rollback-product-part.xlsx");
        var snapshotDirectory = Path.Combine(root, "snapshots");
        var evidencePath = Path.Combine(root, "evidence", "rollback-product-part.json");
        AssertIsUnderRoot(root, databasePath, sourcePath, snapshotDirectory, evidencePath);
        DatabaseInitializer.Initialize(databasePath);
        WriteWorkbook(sourcePath, products: 1, batchesPerProduct: 1, seed: false);

        ImportConfirmationContract contract;
        using (var preview = DatabaseInitializer.CreateContext(databasePath))
        {
            var workbook = new ExcelTemplateReader().Read(sourcePath);
            var plan = new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        }

        var before = BusinessFingerprint(databasePath);
        var interceptor = new FailAfterProductWriteInterceptor();
        ConfirmedImportResult result;
        using (var execute = OpenWithInterceptor(databasePath, interceptor))
        {
            result = new ConfirmedImportLifecycleOrchestrator().Execute(execute, new(
                contract, snapshotDirectory, new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc),
                new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc)));
            Assert.False(result.Succeeded);
            Assert.Equal(ConfirmedImportCodes.TransactionFailed, result.Code);
            Assert.True(interceptor.ProductWriteCount > 0);
            Assert.True(interceptor.FailedAfterProductWrite);
        }

        Assert.Equal(before, BusinessFingerprint(databasePath));
        var verification = Verify(databasePath);
        Assert.True(verification.IntegrityOk);
        Assert.Equal(0, verification.ForeignKeyViolations);
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            card = "S8-T03", case_name = "product_part", root, database_path = databasePath, source_path = sourcePath,
            snapshot_directory = snapshotDirectory, before_fingerprint = before, after_fingerprint = BusinessFingerprint(databasePath),
            result_succeeded = result.Succeeded, result_code = result.Code, product_write_success_count = interceptor.ProductWriteCount,
            failure_trigger_count = interceptor.FailureTriggerCount, integrity_check = verification.IntegrityOk ? "ok" : "failed",
            foreign_key_check_count = verification.ForeignKeyViolations
        }, new JsonSerializerOptions { WriteIndented = true }));
        using var verify = DatabaseInitializer.CreateContext(databasePath);
        Assert.Equal(0, verify.Imports.Count(import => import.Status == ImportStatuses.Succeeded));
    }

    [Theory]
    [InlineData("product_part", 12)]
    [InlineData("batch_part", 12)]
    [InlineData("import_record", 12)]
    [InlineData("post_middle", 400)]
    [Trait("Category", "S8T03")]
    public void Controlled_real_write_failures_roll_back_every_business_table(string injectionStage, int products)
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var seedPath = Path.Combine(root, "source", "seed.xlsx");
        var sourcePath = Path.Combine(root, "source", injectionStage + ".xlsx");
        var snapshots = Path.Combine(root, "snapshots");
        var evidencePath = Path.Combine(root, "evidence", injectionStage + ".json");
        AssertIsUnderRoot(root, databasePath, seedPath, sourcePath, snapshots, evidencePath);
        DatabaseInitializer.Initialize(databasePath);
        WriteWorkbook(seedPath, 10, 1, seed: true); // Seed every supported category before the main import; no main scope cold-starts.
        SeedExisting(databasePath, seedPath, snapshots); // A real succeeded import is deliberately part of the before-state.
        WriteWorkbook(sourcePath, products, 1, seed: false);
        var contract = ReadConfirmedContract(databasePath, sourcePath);
        var before = BusinessFingerprint(databasePath);
        var interceptor = new FailureMatrixInterceptor(injectionStage);
        var resolved = false;
        var zeroSaved = false;
        ConfirmedImportResult result;
        using (var execute = OpenWithInterceptor(databasePath, interceptor))
        {
            void Measure(string stage, TimeSpan _) { if (stage == "resolve_facts") { resolved = true; interceptor.MarkResolved(); } if (stage == "stock_zero") { zeroSaved = true; interceptor.MarkStockZero(); } }
            result = new ConfirmedImportLifecycleOrchestrator(measure: Measure).Execute(execute, new(
                contract, snapshots, new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc),
                new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc)));
        }

        Assert.False(result.Succeeded);
        Assert.Equal(ConfirmedImportCodes.TransactionFailed, result.Code);
        Assert.True(interceptor.FailureTriggerCount > 0);
        if (injectionStage == "product_part") Assert.True(interceptor.ProductWrites > 0);
        if (injectionStage == "batch_part") Assert.True(interceptor.BatchWrites > 0);
        if (injectionStage == "post_middle") { Assert.True(resolved); Assert.True(zeroSaved); Assert.True(interceptor.SuccessfulPostCommands >= 250); }
        Assert.Equal(before, BusinessFingerprint(databasePath));
        AssertFailureEvidence(evidencePath, injectionStage, root, databasePath, sourcePath, snapshots, before, result, interceptor, resolveFactsCompleted: resolved);
    }

    [Fact]
    [Trait("Category", "S8T03")]
    public void Resolve_facts_failure_after_real_stage2_rolls_back_seeded_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var seedPath = Path.Combine(root, "source", "seed.xlsx");
        var sourcePath = Path.Combine(root, "source", "resolve-facts.xlsx");
        var snapshots = Path.Combine(root, "snapshots");
        var evidencePath = Path.Combine(root, "evidence", "post-before.json");
        AssertIsUnderRoot(root, databasePath, seedPath, sourcePath, snapshots, evidencePath);
        DatabaseInitializer.Initialize(databasePath);
        WriteWorkbook(seedPath, 10, 1, seed: true);
        SeedExisting(databasePath, seedPath, snapshots);
        WriteWorkbook(sourcePath, 12, 1, seed: false);
        var contract = ReadConfirmedContract(databasePath, sourcePath);
        var before = BusinessFingerprint(databasePath);
        var resolved = false;
        ConfirmedImportResult result;
        using (var execute = DatabaseInitializer.CreateContext(databasePath))
        {
            void Measure(string stage, TimeSpan _) { if (stage == "resolve_facts") { resolved = true; throw new InvalidOperationException("S8-T03 forced failure after resolve facts."); } }
            result = new ConfirmedImportLifecycleOrchestrator(measure: Measure).Execute(execute, new(
                contract, snapshots, new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc)));
        }

        Assert.True(resolved);
        Assert.False(result.Succeeded);
        Assert.Equal(ConfirmedImportCodes.TransactionFailed, result.Code);
        Assert.Equal(before, BusinessFingerprint(databasePath));
        AssertFailureEvidence(evidencePath, "post_before", root, databasePath, sourcePath, snapshots, before, result, null, resolveFactsCompleted: true);
    }

    [Fact]
    [Trait("Category", "S8T03")]
    public void Zero_stock_failure_after_real_zero_save_rolls_back_seeded_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var seedPath = Path.Combine(root, "source", "seed.xlsx");
        var sourcePath = Path.Combine(root, "source", "zero-after-save.xlsx");
        var snapshots = Path.Combine(root, "snapshots");
        var evidencePath = Path.Combine(root, "evidence", "zero-after-save.json");
        AssertIsUnderRoot(root, databasePath, seedPath, sourcePath, snapshots, evidencePath);
        DatabaseInitializer.Initialize(databasePath);
        WriteWorkbook(seedPath, 10, 1, seed: true);
        SeedExisting(databasePath, seedPath, snapshots);
        WriteWorkbook(sourcePath, 12, 1, seed: false); // Product 0 and 10 have real Excel stock 0.
        var contract = ReadConfirmedContract(databasePath, sourcePath);
        var before = BusinessFingerprint(databasePath);
        var zeroSaved = false;
        ConfirmedImportResult result;
        using (var execute = DatabaseInitializer.CreateContext(databasePath))
        {
            void Measure(string stage, TimeSpan _) { if (stage == "stock_zero") { zeroSaved = true; throw new InvalidOperationException("S8-T03 forced failure after zero-stock SaveChanges."); } }
            result = new ConfirmedImportLifecycleOrchestrator(measure: Measure).Execute(execute, new(
                contract, snapshots, new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc)));
        }
        Assert.True(zeroSaved);
        Assert.False(result.Succeeded);
        Assert.Equal(before, BusinessFingerprint(databasePath));
        AssertFailureEvidence(evidencePath, "zero_after_save", root, databasePath, sourcePath, snapshots, before, result, null, resolveFactsCompleted: true);
    }

    [Fact]
    [Trait("Category", "S8T03")]
    public void Parser_validation_plan_and_snapshot_failures_do_not_mutate_a_real_seed()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var seedPath = Path.Combine(root, "source", "seed.xlsx");
        var sourcePath = Path.Combine(root, "source", "valid.xlsx");
        var badPath = Path.Combine(root, "source", "parser-failure.xlsx");
        var validationPath = Path.Combine(root, "source", "validation-failure.xlsx");
        var snapshots = Path.Combine(root, "snapshots-file");
        AssertIsUnderRoot(root, databasePath, seedPath, sourcePath, badPath, validationPath, snapshots);
        DatabaseInitializer.Initialize(databasePath);
        WriteWorkbook(seedPath, 10, 1, seed: true);
        SeedExisting(databasePath, seedPath, Path.Combine(root, "seed-snapshots"));
        WriteWorkbook(sourcePath, 2, 1, seed: false);
        WriteWorkbook(validationPath, 1, 1, seed: false, invalidExpiry: true, productOffset: 100);
        File.WriteAllText(badPath, "not an xlsx");
        var before = BusinessFingerprint(databasePath);
        Assert.ThrowsAny<Exception>(() => new ExcelTemplateReader().Read(badPath));
        AssertNoMutationEvidence(Path.Combine(root, "evidence", "parser.json"), "parser", root, databasePath, badPath, before, "reader_threw");
        var invalidWorkbook = new ExcelTemplateReader().Read(validationPath);
        var blocked = new ExcelFileClassifier().Classify(invalidWorkbook);
        Assert.NotEmpty(blocked.RowIssues);
        using (var preview = DatabaseInitializer.CreateContext(databasePath))
        {
            var plan = new ExcelImportPlanner().Plan(preview, blocked);
            var confirmation = new ImportConfirmationGuard().Confirm(new ImportConfirmationGuard().BindPreview(validationPath, invalidWorkbook, plan));
            Assert.False(plan.HasChanges);
            Assert.False(confirmation.CanConfirm);
            Assert.Equal(ImportConfirmationCodes.NoChanges, confirmation.Code);
        }
        AssertNoMutationEvidence(Path.Combine(root, "evidence", "validation.json"), "validation", root, databasePath, validationPath, before, "no_changes");
        var plannerInterceptor = new FailOnPlannerReadInterceptor();
        using (var preview = OpenWithInterceptor(databasePath, plannerInterceptor))
        {
            var validWorkbook = new ExcelTemplateReader().Read(sourcePath);
            Assert.ThrowsAny<Exception>(() => new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(validWorkbook)));
        }
        Assert.Equal(1, plannerInterceptor.FailureTriggerCount);
        AssertNoMutationEvidence(Path.Combine(root, "evidence", "plan.json"), "plan", root, databasePath, sourcePath, before, "planner_read_threw");
        File.WriteAllText(snapshots, "not a directory");
        var contract = ReadConfirmedContract(databasePath, sourcePath);
        ConfirmedImportResult snapshotResult;
        using (var execute = DatabaseInitializer.CreateContext(databasePath))
        {
            snapshotResult = new ConfirmedImportLifecycleOrchestrator().Execute(execute, new(
                contract, snapshots, new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc)));
        }
        Assert.False(snapshotResult.Succeeded);
        Assert.Equal(ConfirmedImportCodes.SnapshotFailed, snapshotResult.Code);
        Assert.Equal(before, BusinessFingerprint(databasePath));
        using (var verify = DatabaseInitializer.CreateContext(databasePath)) Assert.Empty(verify.Imports.Where(import => import.Status == ImportStatuses.Succeeded).Skip(1));
        AssertNoMutationEvidence(Path.Combine(root, "evidence", "snapshot.json"), "snapshot", root, databasePath, sourcePath, before, snapshotResult.Code);
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

    [Fact]
    [Trait("Category", "S8T03HighScale")]
    public void High_scale_resolve_facts_failure_requires_explicit_gate()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("S8_T03_RUN_HIGH_SCALE_FAILURE"), "1", StringComparison.Ordinal)) return;
        const int rows = 100_000;
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T03", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "app.db");
        var seedPath = Path.Combine(root, "source", "seed.xlsx");
        var sourcePath = Path.Combine(root, "source", "synthetic-100000-failure.xlsx");
        var snapshots = Path.Combine(root, "snapshots");
        var evidencePath = Path.Combine(root, "evidence", "100000-post-before.json");
        AssertIsUnderRoot(root, databasePath, seedPath, sourcePath, snapshots, evidencePath);
        DatabaseInitializer.Initialize(databasePath);
        WriteWorkbook(seedPath, 1, 1, seed: true);
        SeedExisting(databasePath, seedPath, snapshots);
        WriteWorkbook(sourcePath, rows / 2, 2, seed: false);
        var contract = ReadConfirmedContract(databasePath, sourcePath);
        var before = BusinessFingerprint(databasePath);
        var resolved = false;
        var stage2Products = 0;
        var stage2Batches = 0;
        var stage2Workbooks = 0;
        var stage2SucceededImports = 0;
        ConfirmedImportResult result;
        using (var execute = DatabaseInitializer.CreateContext(databasePath))
        {
            void Measure(string stage, TimeSpan _)
            {
                if (stage != "resolve_facts") return;
                resolved = true;
                stage2Products = execute.Products.Count();
                stage2Batches = execute.Batches.Count();
                stage2Workbooks = execute.ImportWorkbooks.Count();
                stage2SucceededImports = execute.Imports.Count(import => import.Status == ImportStatuses.Succeeded);
                throw new InvalidOperationException("S8-T03 forced 100k failure after Stage2.");
            }
            result = new ConfirmedImportLifecycleOrchestrator(measure: Measure).Execute(execute, new(
                contract, snapshots, new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 8, 1, 0, DateTimeKind.Utc)));
        }
        Assert.True(resolved);
        Assert.Equal(50_000, stage2Products);
        Assert.Equal(100_000, stage2Batches);
        Assert.Equal(2, stage2Workbooks);
        Assert.Equal(2, stage2SucceededImports);
        Assert.False(result.Succeeded);
        Assert.Equal(before, BusinessFingerprint(databasePath));
        AssertFailureEvidence(evidencePath, "post_before_100000", root, databasePath, sourcePath, snapshots, before, result, null, resolveFactsCompleted: true,
            stage2State: new Dictionary<string, int> { ["products"] = stage2Products, ["batches"] = stage2Batches, ["workbooks"] = stage2Workbooks, ["succeeded_imports"] = stage2SucceededImports });
    }

    private static void Run(int rows, bool requireExplicitHighScaleGate)
    {
        var evidenceKind = Environment.GetEnvironmentVariable("S8_T03_EVIDENCE_KIND") ?? "after";
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
        Dictionary<string, long> seededProductIds;
        Dictionary<string, long> seededBatchIds;
        using (var seeded = DatabaseInitializer.CreateContext(databasePath))
        {
            seededProductIds = seeded.Products.ToDictionary(product => product.ProductCode, product => product.Id, StringComparer.Ordinal);
            seededBatchIds = seeded.Batches.Include(batch => batch.Product).ToDictionary(batch => $"{batch.Product.ProductCode}|{batch.ProductionDate:yyyy-MM-dd}|{batch.ExpiryDate:yyyy-MM-dd}", batch => batch.Id, StringComparer.Ordinal);
        }
        var database_physical_bytes_before = DatabasePhysicalBytes(databasePath);
        var database_logical_bytes_before = DatabaseLogicalBytes(databasePath);
        Measure(measures, "workbook_generate", () => WriteWorkbook(sourcePath, rows / 2, batchesPerProduct: 2, seed: false));
        workbookBytes = new FileInfo(sourcePath).Length;
        allocationStart = GC.GetTotalAllocatedBytes();
        var gcStart = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
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
        var sqlMetrics = new SqlMetricsInterceptor();
        var saveMetrics = new SaveChangesMetricsInterceptor();
        Measure(measures, "snapshot_write_post", () =>
        {
            using var execute = OpenWithInterceptors(databasePath, sqlMetrics, saveMetrics);
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
        var importElapsedMs = total.Elapsed.TotalMilliseconds;
        var importAllocatedBytes = GC.GetTotalAllocatedBytes() - allocationStart;
        var importWorkingSetBytes = Environment.WorkingSet;
        var importGc = new[] { GC.CollectionCount(0) - gcStart[0], GC.CollectionCount(1) - gcStart[1], GC.CollectionCount(2) - gcStart[2] };
        var verificationWatch = Stopwatch.StartNew();

        Assert.True(result.Succeeded, result.Code);
        Assert.True(File.Exists(result.SnapshotPath));
        AssertIsUnderRoot(root, result.SnapshotPath!);
        var importId = Assert.IsType<long>(result.ImportId);
        using (var verify = DatabaseInitializer.CreateContext(databasePath))
        {
            var productsByCode = verify.Products.AsNoTracking().ToDictionary(product => product.ProductCode, StringComparer.Ordinal);
            var batchesByKey = verify.Batches.AsNoTracking().ToDictionary(batch => (batch.ProductId, batch.ProductionDate, batch.ExpiryDate));
            var batchesById = verify.Batches.AsNoTracking().ToDictionary(batch => batch.Id);
            Assert.Equal(rows / 2, verify.Products.Count());
            Assert.Equal(rows, verify.Batches.Count());
            Assert.Equal(2, verify.Imports.Count(import => import.Status == ImportStatuses.Succeeded));
            Assert.Equal(2, verify.ImportWorkbooks.Count());
            Assert.Equal(rows / 20, verify.Products.Count(product => product.EffectiveStockQty == 0));
            Assert.Equal(rows / 2, verify.Products.Count(product => product.LastSeenImportId == result.ImportId));
            Assert.Equal(rows, verify.Batches.Count(batch => batch.LastSeenImportId == result.ImportId));
            Assert.Equal(rows / 20, verify.Products.Count(product => product.IsStockZeroTerminated));
            Assert.Equal(rows / 20 * 2, verify.Batches.Count(batch => batch.TrackingStatus == "stopped" && batch.StopReason == "product_stock_zero"));
            var import = Assert.Single(verify.Imports.Where(item => item.Id == importId));
            Assert.Equal(ImportStatuses.Succeeded, import.Status);
            Assert.Equal(rows / 2, import.ProductCount);
            Assert.Equal(rows, import.BatchCount);
            Assert.Equal(plan.NewProductCount, import.NewProductCount);
            Assert.Equal(plan.NewBatchCount, import.NewBatchCount);
            Assert.Equal(plan.UpdatedBatchCount, import.UpdatedBatchCount);
            Assert.Equal(0, import.IssueCount);
            Assert.Equal(result.SnapshotPath, import.PreImportSnapshotPath);
            Assert.All(verify.ImportWorkbooks.Where(workbook => workbook.ImportId == importId), workbook =>
            {
                Assert.Equal(Path.GetFileName(sourcePath), workbook.OriginalFileName);
                Assert.Equal(contract.SourceFileSha256, workbook.Sha256);
                Assert.Equal(contract.WorkbookBytes.ToArray(), workbook.Content);
            });
            Assert.All(verify.Batches.Include(batch => batch.Product).Where(batch => batch.LastSeenImportId == result.ImportId).ToArray(), batch =>
            {
                if (batch.Product.IsStockZeroTerminated)
                {
                    Assert.Equal("stopped", batch.TrackingStatus);
                    Assert.Equal("product_stock_zero", batch.StopReason);
                    Assert.Null(batch.NextTriggerDate);
                }
                else if (batch.Product.ExpiryManagementStatus != ExpiryManagementStatus.Managed)
                {
                    Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
                }
                else
                {
                    var expected = ExpiryPolicyCalculator.Calculate(batch.Product.PolicyCode!, batch.Product.PolicyVersion!.Value, new DateOnly(2026, 9, 4), batch.ExpiryDate,
                        batch.ShelfLifeUnit switch { "D" => batch.ShelfLifeValue, "M" => batch.ShelfLifeValue * 30, "Y" => batch.ShelfLifeValue * 365, _ => throw new InvalidOperationException() });
                    Assert.Equal(expected?.CurrentStage ?? ExpiryStageCalculator.None, batch.CurrentStage);
                }
            });
            for (var productNumber = 0; productNumber < rows / 2; productNumber++)
            {
                var code = $"S8T03-{productNumber:D6}";
                var product = productsByCode[code];
                Assert.Equal("合成更新商品" + productNumber.ToString("D6"), product.CurrentName);
                Assert.Equal("690" + productNumber.ToString("D10"), product.CurrentBarcode);
                Assert.Equal(productNumber % 10 == 0 ? 0 : 30, product.EffectiveStockQty);
                if (seededProductIds.TryGetValue(code, out var seededProductId)) Assert.Equal(seededProductId, product.Id);
                for (var batchNumber = 0; batchNumber < 2; batchNumber++)
                {
                    var production = batchNumber == 0 ? new DateOnly(2026, 1, 1) : new DateOnly(2026, 2, 1);
                    var expiry = batchNumber == 0 && productNumber % 10 is 0 or 1 ? new DateOnly(2026, 9, 9) : new DateOnly(2027, 9, 4);
                    var batch = batchesByKey[(product.Id, production, expiry)];
                    Assert.Equal(batchNumber == 0 ? 2 : 1, batch.CurrentArrivalQty);
                    Assert.Equal(batchNumber == 0 ? 2 : 1, batch.MaxArrivalQty);
                    if (seededBatchIds.TryGetValue($"{code}|{production:yyyy-MM-dd}|{expiry:yyyy-MM-dd}", out var seededBatchId)) Assert.Equal(seededBatchId, batch.Id);
                }
            }
            var excluded = verify.Products.Where(product => product.LastSeenImportId == importId && product.ExpiryManagementStatus != ExpiryManagementStatus.Managed);
            Assert.NotEmpty(excluded);
            Assert.All(verify.Batches.Join(excluded, batch => batch.ProductId, product => product.Id, (batch, _) => batch), batch => Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage));
            Assert.Empty(verify.Tasks.Join(excluded, task => task.ProductId, product => product.Id, (task, _) => task));
            var openTasks = verify.Tasks.Include(task => task.Items).Include(task => task.Product).Where(task => task.Status == "open").ToArray();
            Assert.Equal(rows / 20, openTasks.Length);
            Assert.Equal(openTasks.Length, openTasks.Select(task => task.ProductId).Distinct().Count());
            Assert.All(openTasks, task =>
            {
                Assert.Equal("pet", task.Product.CategoryCode);
                var item = Assert.Single(task.Items);
                var batch = batchesById[item.BatchId];
                Assert.Equal(task.ProductId, item.ProductId);
                Assert.Equal(new DateOnly(2026, 1, 1), batch.ProductionDate);
                var expected = Assert.IsType<ExpiryStageResult>(ExpiryPolicyCalculator.Calculate(ExpiryPolicies.Pet, ExpiryPolicies.Version1, new DateOnly(2026, 9, 4), batch.ExpiryDate, 10));
                Assert.Equal(ExpiryStageCalculator.Withdraw, task.HighestStage);
                Assert.Equal(expected.CurrentStage, item.Stage);
                Assert.Equal(expected.CurrentStage, batch.CurrentStage);
                Assert.Equal(expected.NextTriggerDate, batch.NextTriggerDate);
            });
            Assert.Equal(existingProductCount / 10, verify.Tasks.Count(task => task.Status == "system_closed" && task.CloseReason == "product_stock_zero"));
        }

        var verification = Verify(databasePath);
        verificationWatch.Stop();
        Assert.True(verification.IntegrityOk);
        Assert.Equal(0, verification.ForeignKeyViolations);
        var evidence = new
        {
            card = "S8-T03",
            kind = evidenceKind,
            implementation_commit = Environment.GetEnvironmentVariable("S8_T03_COMMIT") ?? "not_supplied",
            rows,
            product_count = rows / 2,
            high_scale_gate = requireExplicitHighScaleGate,
            root,
            source_path = sourcePath,
            database_path = databasePath,
            snapshot_path = result.SnapshotPath,
            workbook_bytes = workbookBytes,
            database_physical_bytes_before,
            database_logical_bytes_before,
            database_physical_bytes = DatabasePhysicalBytes(databasePath),
            database_logical_bytes = DatabaseLogicalBytes(databasePath),
            snapshot_bytes = new FileInfo(result.SnapshotPath!).Length,
            measures_ms = measures,
            sample_count = 1,
            median_ms = measures,
            max_ms = measures,
            import_stage_ms = stageMeasures,
            total_ms = importElapsedMs,
            total_median_ms = importElapsedMs,
            total_max_ms = importElapsedMs,
            managed_allocated_bytes = importAllocatedBytes,
            working_set_bytes = importWorkingSetBytes,
            gc_collection_counts = importGc,
            runtime_version = Environment.Version.ToString(), os_version = Environment.OSVersion.VersionString,
            verification_ms = verificationWatch.Elapsed.TotalMilliseconds,
            actual_sql_command_count = sqlMetrics.CommandCount,
            actual_sql_max_parameter_count = sqlMetrics.MaxParameterCount,
            save_changes_attempt_count = saveMetrics.AttemptCount,
            save_changes_success_count = saveMetrics.SuccessCount,
            transaction_count = (int?)null,
            transaction_count_note = "not collected: no transaction interceptor was added to this performance path",
            known_main_import_context_creations = 2,
            measurement_note = "total/allocation/working-set/GC end immediately after main import; they exclude database initialization, seed import, workbook generation, and all verification. SQL/SaveChanges metrics cover execute context only, excluding planner, seed, and verification.",
            integrity_check = verification.IntegrityOk ? "ok" : "failed",
            foreign_key_check_count = verification.ForeignKeyViolations,
            data_distribution = "2 batches/product; first up-to-1000 products pre-exist with product-name/stock and batch-0 arrival update, remaining products/batches are new; products rotate all 10 supported categories; every tenth product has stock 0; D/M/Y all occur"
        };
            File.WriteAllText(Path.Combine(evidenceDirectory, evidenceKind + ".json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, evidenceKind + "-failure.json"), JsonSerializer.Serialize(new
            {
                card = "S8-T03", kind = evidenceKind + "_failure", rows, root, source_path = sourcePath,
                database_path = databasePath, snapshot_directory = snapshotDirectory, evidence_directory = evidenceDirectory,
                workbook_bytes = workbookBytes, measures_ms = measures, total_ms = total.Elapsed.TotalMilliseconds,
                managed_allocated_bytes = GC.GetTotalAllocatedBytes() - allocationStart,
                implementation_commit = Environment.GetEnvironmentVariable("S8_T03_COMMIT") ?? "not_supplied",
                exception_type = exception.GetType().FullName, exception_message = exception.Message
            }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }

    internal static void SeedExisting(string databasePath, string sourcePath, string snapshotDirectory)
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

    internal static ImportConfirmationContract ReadConfirmedContract(string databasePath, string sourcePath)
    {
        var workbook = new ExcelTemplateReader().Read(sourcePath);
        var classification = new ExcelFileClassifier().Classify(workbook);
        Assert.Empty(classification.RowIssues);
        using var preview = DatabaseInitializer.CreateContext(databasePath);
        var plan = new ExcelImportPlanner().Plan(preview, classification);
        return Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
            new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
    }

    private static void AssertFailureEvidence(
        string evidencePath, string injectionStage, string root, string databasePath, string sourcePath, string snapshots,
        string before, ConfirmedImportResult result, FailureMatrixInterceptor? interceptor, bool resolveFactsCompleted,
        IReadOnlyDictionary<string, int>? stage2State = null)
    {
        var after = BusinessFingerprint(databasePath);
        var verification = Verify(databasePath);
        Assert.True(verification.IntegrityOk);
        Assert.Equal(0, verification.ForeignKeyViolations);
        using (var verify = DatabaseInitializer.CreateContext(databasePath))
        {
            Assert.Equal(1, verify.Imports.Count(import => import.Status == ImportStatuses.Succeeded));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            card = "S8-T03", implementation_commit = Environment.GetEnvironmentVariable("S8_T03_COMMIT") ?? "not_supplied",
            injection_stage = injectionStage, root, database_path = databasePath, source_path = sourcePath, snapshot_directory = snapshots,
            before_fingerprint = before, after_fingerprint = after, result_succeeded = result.Succeeded, result_code = result.Code,
            resolve_facts_completed = resolveFactsCompleted, actual_successful_commands = interceptor?.SuccessfulCommands,
            product_write_success_count = interceptor?.ProductWrites, batch_write_success_count = interceptor?.BatchWrites,
            actual_successful_post_commands = interceptor?.SuccessfulPostCommands, failure_trigger_count = interceptor?.FailureTriggerCount,
            stage2_state_before_injected_failure = stage2State,
            integrity_check = verification.IntegrityOk ? "ok" : "failed", foreign_key_check_count = verification.ForeignKeyViolations
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AssertNoMutationEvidence(string evidencePath, string injectionStage, string root, string databasePath, string sourcePath, string before, string result)
    {
        var after = BusinessFingerprint(databasePath);
        var verification = Verify(databasePath);
        Assert.Equal(before, after);
        Assert.True(verification.IntegrityOk);
        Assert.Equal(0, verification.ForeignKeyViolations);
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            card = "S8-T03", implementation_commit = Environment.GetEnvironmentVariable("S8_T03_COMMIT") ?? "not_supplied",
            injection_stage = injectionStage, root, database_path = databasePath, source_path = sourcePath,
            before_fingerprint = before, after_fingerprint = after, result, actual_successful_commands = (int?)null, failure_trigger_count = (int?)null,
            integrity_check = verification.IntegrityOk ? "ok" : "failed", foreign_key_check_count = verification.ForeignKeyViolations
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void WriteWorkbook(string path, int products, int batchesPerProduct, bool seed, bool invalidExpiry = false, int productOffset = 0)
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
            var productNumber = product + productOffset;
            var category = (productNumber % 10) switch { 0 => "食品", 1 => "宠物", 2 => "日用", 3 => "美妆", 4 => "家居", 5 => "香氛香水", 6 => "文具", 7 => "潮流玩具", 8 => "应季搭配", _ => "赠品小样" };
            var stock = seed ? "20" : product % 10 == 0 ? "0" : "30";
            var shortShelfLife = category is "食品" or "宠物";
            var yearlyShelfLife = product % 10 == 2;
            for (var batch = 0; batch < batchesPerProduct; batch++)
            {
                var row = product * batchesPerProduct + batch + 2;
                WriteRow(writer, row,
                [category, $"S8T03-{productNumber:D6}", $"690{productNumber:D10}", $"{(seed ? "合成商品" : "合成更新商品")}{productNumber:D6}", batch == 0 ? "2026-01-01" : "2026-02-01", invalidExpiry ? "not-a-date" : batch == 0 && shortShelfLife ? "2026-09-09" : "2027-09-04", shortShelfLife ? "10" : yearlyShelfLife ? "1" : "12", shortShelfLife ? "D" : yearlyShelfLife ? "Y" : "M", "是", (seed ? 1 : batch == 0 ? 2 : 1).ToString(), stock]);
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

    private static StoreDbContext OpenWithInterceptor(string databasePath, DbCommandInterceptor interceptor) =>
        OpenWithInterceptors(databasePath, interceptor);

    private static StoreDbContext OpenWithInterceptors(string databasePath, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString())
            .AddInterceptors(interceptors)
            .Options);

    // The rollback matrix uses this complete business-table fingerprint rather than counts alone.
    private static string BusinessFingerprint(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name";
        using var reader = tables.ExecuteReader();
        var text = new StringBuilder();
        while (reader.Read())
        {
            var table = reader.GetString(0);
            using var rows = connection.CreateCommand();
            rows.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY rowid";
            using var values = rows.ExecuteReader();
            text.Append(table).Append('|');
            while (values.Read())
            {
                for (var column = 0; column < values.FieldCount; column++) AppendFingerprintValue(text, values, column);
                text.Append('\u001e');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static void AppendFingerprintValue(StringBuilder text, SqliteDataReader values, int column)
    {
        text.Append(values.GetName(column)).Append(':').Append(values.GetDataTypeName(column)).Append(':');
        if (values.IsDBNull(column))
        {
            text.Append("null:0");
        }
        else if (values.GetValue(column) is byte[] bytes)
        {
            text.Append("blob:").Append(bytes.Length).Append(':').Append(Convert.ToHexString(bytes));
        }
        else
        {
            var value = Convert.ToString(values.GetValue(column), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            text.Append(values.GetFieldType(column).FullName).Append(':').Append(value.Length).Append(':').Append(value);
        }

        text.Append('\u001f');
    }

    private sealed class FailureMatrixInterceptor(string stage) : DbCommandInterceptor
    {
        public int SuccessfulCommands { get; private set; }
        public int SuccessfulPostCommands { get; private set; }
        public int FailureTriggerCount { get; private set; }
        public int ProductWrites { get; private set; }
        public int BatchWrites { get; private set; }
        private bool Resolved { get; set; }
        private bool ZeroSaved { get; set; }

        public void MarkResolved() => Resolved = true;

        public void MarkStockZero() => ZeroSaved = true;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            FailIfRequired(command.CommandText);
            return result;
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            FailIfRequired(command.CommandText);
            return result;
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            RecordSuccess(command.CommandText);
            return result;
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            RecordSuccess(command.CommandText);
            return result;
        }

        private void FailIfRequired(string sql)
        {
            var products = sql.Contains("INSERT INTO \"products\"", StringComparison.OrdinalIgnoreCase);
            var batches = sql.Contains("INSERT INTO \"batches\"", StringComparison.OrdinalIgnoreCase);
            var imports = sql.Contains("INSERT INTO \"imports\"", StringComparison.OrdinalIgnoreCase);
            var postBatchUpdate = sql.Contains("UPDATE \"batches\"", StringComparison.OrdinalIgnoreCase);
            var fail = stage switch
            {
                "product_part" => ProductWrites > 0 && products,
                "batch_part" => BatchWrites > 0 && batches,
                "import_record" => imports,
                "post_middle" => Resolved && ZeroSaved && SuccessfulPostCommands >= 250 && postBatchUpdate,
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
            };
            if (!fail) return;
            FailureTriggerCount++;
            throw new InvalidOperationException("S8-T03 controlled real SQL failure: " + stage);
        }

        private void RecordSuccess(string sql)
        {
            SuccessfulCommands++;
            if (sql.Contains("INSERT INTO \"products\"", StringComparison.OrdinalIgnoreCase)) ProductWrites++;
            if (sql.Contains("INSERT INTO \"batches\"", StringComparison.OrdinalIgnoreCase)) BatchWrites++;
            if (Resolved && ZeroSaved && sql.Contains("UPDATE \"batches\"", StringComparison.OrdinalIgnoreCase)) SuccessfulPostCommands++;
        }
    }

    private sealed class SqlMetricsInterceptor : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }
        public int MaxParameterCount { get; private set; }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) { Record(command); return result; }
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) { Record(command); return result; }
        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) { Record(command); return result; }

        private void Record(DbCommand command)
        {
            CommandCount++;
            MaxParameterCount = Math.Max(MaxParameterCount, command.Parameters.Count);
        }
    }

    private sealed class SaveChangesMetricsInterceptor : SaveChangesInterceptor
    {
        public int AttemptCount { get; private set; }
        public int SuccessCount { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { AttemptCount++; return result; }
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { SuccessCount++; return result; }
    }

    private sealed class FailOnPlannerReadInterceptor : DbCommandInterceptor
    {
        public int FailureTriggerCount { get; private set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) { FailureTriggerCount++; throw new InvalidOperationException("S8-T03 controlled planner read failure."); }
            return result;
        }
    }

    private sealed class FailAfterProductWriteInterceptor : DbCommandInterceptor
    {
        public int ProductWriteCount { get; private set; }

        public bool FailedAfterProductWrite { get; private set; }

        public int FailureTriggerCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            FailIfRequired(command.CommandText);
            return result;
        }

        private void FailIfRequired(string commandText)
        {
            if (ProductWriteCount > 0 && commandText.Contains("INSERT INTO \"batches\"", StringComparison.OrdinalIgnoreCase))
            {
                FailedAfterProductWrite = true;
                FailureTriggerCount++;
                throw new InvalidOperationException("S8-T03 forced failure after persisted product write.");
            }
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            if (command.CommandText.Contains("INSERT INTO \"products\"", StringComparison.OrdinalIgnoreCase)) ProductWriteCount++;
            return result;
        }
    }
}
