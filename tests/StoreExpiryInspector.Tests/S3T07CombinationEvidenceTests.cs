using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S3T07CombinationEvidenceTests
{
    private static readonly string[] Headers =
    [
        "商品大类", "商品编码", "商品条码", "商品名称", "生产日期", "有效日期", "保质期",
        "保质期单位", "是否该做临期折扣", "该批次累计到货数量", "该商品门店库存总数"
    ];

    private static readonly DateOnly BusinessDate = new(2026, 8, 27);

    private static readonly DateTime ParsedAtUtc =
        new(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime OccurredAtUtc =
        new(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);

    [Fact]
    public void RealXlsxNewNoneBatchRemainsActiveWithNoTask()
    {
        using var database = SqliteTestDatabase.Create();
        var prepared = Prepare(
            database,
            "new-none.xlsx",
            [Row("P-NONE", "B-NONE", "none商品", "2026-01-01", "2026-12-31", "12", "5")]);

        Assert.Equal([("P-NONE", 5)], prepared.Plan.ExplicitProductStocks
            .Select(stock => (stock.ProductCode, stock.Quantity)));

        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Equal(new DateOnly(2026, 10, 2), batch.NextTriggerDate);
        Assert.Empty(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void ExplicitZeroWinsOverExistingArrivalAndRecoveryWithoutVersionOrTask()
    {
        using var database = SqliteTestDatabase.Create();
        long batchId;
        using (var seed = database.Open())
        {
            var seedProduct = AddProduct(seed, "P-ZERO-ARRIVAL", stock: 5);
            var seedBatch = AddBatch(
                seed,
                seedProduct,
                currentArrivalQty: 1,
                maxArrivalQty: 1,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: OccurredAtUtc.AddMinutes(-1),
                attentionVersion: 4);
            batchId = seedBatch.Id;
            seed.SaveChanges();
        }

        var prepared = Prepare(
            database,
            "zero-existing-arrival.xlsx",
            [Row("P-ZERO-ARRIVAL", "B-ZERO-ARRIVAL", "归零优先", "2026-01-01", "2026-09-20", "12", "0", "2")]);

        Assert.Equal([("P-ZERO-ARRIVAL", 0)], prepared.Plan.ExplicitProductStocks
            .Select(stock => (stock.ProductCode, stock.Quantity)));
        Assert.Contains(
            prepared.Plan.UpdatedBatches,
            batch => batch.BatchKey.ProductCode == "P-ZERO-ARRIVAL");

        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(batchId, batch.Id);
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal(1, product.LifecycleGeneration);
        Assert.Equal(2, batch.CurrentArrivalQty);
        Assert.Equal(2, batch.MaxArrivalQty);
        Assert.Equal(4, batch.AttentionVersion);
        Assert.Equal("product_stock_zero", batch.StopReason);
        Assert.Equal("stopped", batch.TrackingStatus);
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Single(verify.LifecycleEvents.AsNoTracking()
            .Where(item => item.EventType == "product_stock_zero"));
        Assert.Empty(verify.LifecycleEvents.AsNoTracking()
            .Where(item => item.EventType == "batch_tracking_resumed"));
    }

    [Fact]
    public void StoppedCheckedZeroBatchResumesAfterRealImportBreakthrough()
    {
        using var database = SqliteTestDatabase.Create();
        long batchId;
        using (var seed = database.Open())
        {
            var seedProduct = AddProduct(seed, "P-RESUME", stock: 5);
            var seedBatch = AddBatch(
                seed,
                seedProduct,
                currentArrivalQty: 1,
                maxArrivalQty: 1,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: OccurredAtUtc.AddMinutes(-1),
                attentionVersion: 2);
            batchId = seedBatch.Id;
            seed.SaveChanges();
        }

        var prepared = Prepare(
            database,
            "resume.xlsx",
            [Row("P-RESUME", "B-RESUME", "恢复商品", "2026-01-01", "2026-09-20", "12", "5", "2")]);
        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(batchId, batch.Id);
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Null(batch.StopReason);
        Assert.Null(batch.StoppedAtUtc);
        Assert.Equal(2, batch.CurrentArrivalQty);
        Assert.Equal(2, batch.MaxArrivalQty);
        Assert.Equal(3, batch.AttentionVersion);
        var resumed = Assert.Single(verify.LifecycleEvents.AsNoTracking()
            .Where(item => item.EventType == "batch_tracking_resumed"));
        Assert.Equal(result.ImportId, resumed.SourceImportId);
        Assert.Equal(batchId, resumed.BatchId);
        Assert.Single(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void PreviouslyTerminatedProductNeverRestoresOldBatchButNewBatchUsesCurrentGeneration()
    {
        using var database = SqliteTestDatabase.Create();
        long oldBatchId;
        using (var seed = database.Open())
        {
            var seedProduct = AddProduct(
                seed,
                "P-TERMINATED",
                stock: 5,
                lifecycleGeneration: 1,
                stockZeroTerminated: true);
            var seedOldBatch = AddBatch(
                seed,
                seedProduct,
                expiryDate: new DateOnly(2026, 9, 15),
                currentArrivalQty: 1,
                maxArrivalQty: 1,
                lifecycleGeneration: 0,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: OccurredAtUtc.AddMinutes(-1),
                attentionVersion: 2);
            oldBatchId = seedOldBatch.Id;
            seed.SaveChanges();
        }

        var prepared = Prepare(
            database,
            "terminated-and-new.xlsx",
            [
                Row("P-TERMINATED", "B-TERMINATED", "旧批次", "2026-01-01", "2026-09-15", "12", "5", "2"),
                Row("P-TERMINATED", "B-TERMINATED", "新批次", "2026-02-01", "2026-09-20", "12", "5", "1")
            ]);
        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        var batches = verify.Batches.AsNoTracking().OrderBy(batch => batch.ExpiryDate).ToArray();
        Assert.Equal(2, batches.Length);
        var oldBatch = Assert.Single(batches, batch => batch.Id == oldBatchId);
        var newBatch = Assert.Single(batches, batch => batch.ExpiryDate == new DateOnly(2026, 9, 20));
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal(1, product.LifecycleGeneration);
        Assert.Equal("stopped", oldBatch.TrackingStatus);
        Assert.Equal("batch_checked_zero", oldBatch.StopReason);
        Assert.Equal(2, oldBatch.AttentionVersion);
        Assert.Equal(1, newBatch.LifecycleGeneration);
        Assert.Equal("active", newBatch.TrackingStatus);
        Assert.Equal(ExpiryStageCalculator.Discount20, newBatch.CurrentStage);
        Assert.Empty(verify.LifecycleEvents.AsNoTracking()
            .Where(item => item.EventType == "batch_tracking_resumed"));
        Assert.Single(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void ImportTouchesOnlyProductAAndLeavesBCProductBatchTaskDraftEventFieldsUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var productA = AddProduct(seed, "P-A", stock: 5);
            AddBatch(seed, productA, expiryDate: new DateOnly(2026, 12, 31));

            foreach (var code in new[] { "P-B", "P-C" })
            {
                var product = AddProduct(seed, code, stock: 5);
                var batch = AddBatch(
                    seed,
                    product,
                    expiryDate: new DateOnly(2026, 9, 20),
                    currentStage: ExpiryStageCalculator.Discount50,
                    nextTriggerDate: new DateOnly(2026, 8, 27),
                    attentionVersion: 3);
                AddTaskWithDraft(seed, product, batch);
                seed.LifecycleEvents.Add(new LifecycleEvent
                {
                    ProductId = product.Id,
                    BatchId = batch.Id,
                    EventType = "batch_checked_zero",
                    Reason = "seed",
                    OccurredAtUtc = OccurredAtUtc.AddDays(-1),
                    SourceImportId = null
                });
                seed.SaveChanges();
            }
        }

        S3T07ScopeSnapshot before;
        using (var snapshotContext = database.Open())
        {
            before = CaptureScope(snapshotContext, "P-B", "P-C");
        }

        var prepared = Prepare(
            database,
            "only-a.xlsx",
            [Row("P-A", "B-A", "A商品", "2026-01-01", "2026-12-31", "12", "5", "2")]);
        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var after = CaptureScope(verify, "P-B", "P-C");
        AssertScopeEqual(before, after);
        Assert.Equal(3, verify.Products.AsNoTracking().Count());
        Assert.Equal(3, verify.Batches.AsNoTracking().Count());
        Assert.Equal(2, verify.Tasks.AsNoTracking().Count());
    }

    [Fact]
    public void ExplicitZeroEqualToPreviousStockStillTerminatesWhenBatchChanges()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var seedProduct = AddProduct(seed, "P-SAME-ZERO", stock: 0);
            AddBatch(seed, seedProduct, currentArrivalQty: 1, maxArrivalQty: 1);
        }

        var prepared = Prepare(
            database,
            "same-zero.xlsx",
            [Row("P-SAME-ZERO", "B-SAME-ZERO", "相同归零", "2026-01-01", "2026-09-20", "12", "0", "2")]);
        Assert.Equal([("P-SAME-ZERO", 0)], prepared.Plan.ExplicitProductStocks
            .Select(stock => (stock.ProductCode, stock.Quantity)));

        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal(1, product.LifecycleGeneration);
        Assert.Single(verify.LifecycleEvents.AsNoTracking()
            .Where(item => item.EventType == "product_stock_zero"));
    }

    [Fact]
    public void BlankInvalidAndConflictingStocksHaveNoExplicitFactsAndDoNotTerminate()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            foreach (var code in new[] { "P-BLANK", "P-INVALID", "P-CONFLICT" })
            {
                AddProduct(seed, code, stock: 0);
            }
        }

        var prepared = Prepare(
            database,
            "invalid-stocks.xlsx",
            [
                Row("P-BLANK", "B-BLANK", "空白库存", "2026-01-01", "2026-09-20", "12", null),
                Row("P-INVALID", "B-INVALID", "非法库存", "2026-01-01", "2026-09-21", "12", "-1"),
                Row("P-CONFLICT", "B-CONFLICT", "冲突库存一", "2026-01-01", "2026-09-22", "12", "1"),
                Row("P-CONFLICT", "B-CONFLICT", "冲突库存二", "2026-02-01", "2026-09-23", "12", "2")
            ]);

        Assert.Empty(prepared.Plan.ExplicitProductStocks);
        Assert.Single(prepared.Classification.StockConflicts);
        Assert.Contains(prepared.Plan.Preview.PlanningIssues, issue => issue.Code == "invalid_stock_quantity");
        var result = Execute(database, prepared.Contract);

        Assert.True(result.Succeeded);
        using var verify = database.Open();
        Assert.All(verify.Products.AsNoTracking(), product =>
        {
            Assert.Equal(0, product.EffectiveStockQty);
            Assert.False(product.IsStockZeroTerminated);
        });
        Assert.Equal(4, verify.Batches.AsNoTracking().Count());
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Empty(verify.LifecycleEvents.AsNoTracking());
    }

    [Fact]
    public void PostImportFailureRollsBackImportWorkbookBackupIssueAndRetentionAlongsideLifecycle()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddHistory(seed, 1);
            AddHistory(seed, 2);
        }

        using (var schema = database.Open())
        {
            schema.Database.ExecuteSqlRaw(
                "CREATE TRIGGER fail_task_item_insert BEFORE INSERT ON task_items BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        }

        var prepared = Prepare(
            database,
            "rollback-all-stage2.xlsx",
            [Row("P-ROLLBACK-ALL", "B-ROLLBACK-ALL", "全量回滚", "2026-01-01", "2026-09-20", "12", "5", "3")]);
        var result = Execute(database, prepared.Contract);

        Assert.False(result.Succeeded);
        Assert.Equal(ConfirmedImportCodes.TransactionFailed, result.Code);
        Assert.NotNull(result.SnapshotPath);
        Assert.True(File.Exists(result.SnapshotPath));
        using var verify = database.Open();
        Assert.Equal(new[] { "old-1.xlsx", "old-2.xlsx" }, verify.Imports.AsNoTracking()
            .OrderBy(import => import.Id).Select(import => import.SourceFileName));
        Assert.Equal(new[] { "old-1.xlsx", "old-2.xlsx" }, verify.ImportWorkbooks.AsNoTracking()
            .OrderBy(workbook => workbook.Id).Select(workbook => workbook.OriginalFileName));
        Assert.Equal(new[] { "old-1.db", "old-2.db" }, verify.BackupRecords.AsNoTracking()
            .OrderBy(backup => backup.Id).Select(backup => backup.FilePath));
        Assert.Equal(new[] { "old-1 issue", "old-2 issue" }, verify.ImportIssues.AsNoTracking()
            .OrderBy(issue => issue.Id).Select(issue => issue.SafeSummary));
        Assert.Equal(2, verify.ImportWorkbooks.AsNoTracking().Count());
        Assert.Empty(verify.Products.AsNoTracking());
        Assert.Empty(verify.Batches.AsNoTracking());
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Empty(verify.LifecycleEvents.AsNoTracking());
    }

    [Fact]
    public void AppEntryKeepsStartupUriAndContainsOnlyExplicitInitializationClockCoordinatorAndLoggingBoundary()
    {
        var root = FindRepositoryRoot();
        var appSource = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml"));
        var coordinatorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "Application",
            "ApplicationStartupCoordinator.cs"));
        var importCoordinatorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "Application",
            "Imports",
            "ConfirmedImportLifecycleOrchestrator.cs"));
        var executorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "Application",
            "Imports",
            "ConfirmedImportExecutor.cs"));
        var startupUseCaseSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "StoreExpiryInspector",
            "Application",
            "StartupRecalculationUseCase.cs"));

        Assert.Contains("StartupUri=\"UI/MainWindow.xaml\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("DatabaseInitializer.Initialize();", appSource, StringComparison.Ordinal);
        Assert.Contains("DateOnly.FromDateTime(DateTime.Now)", appSource, StringComparison.Ordinal);
        Assert.Contains("DateTime.UtcNow", appSource, StringComparison.Ordinal);
        Assert.Equal(1, appSource.Split("DateTime.Now", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, appSource.Split("DateTime.UtcNow", StringSplitOptions.None).Length - 1);
        Assert.Contains("ApplicationStartupCoordinator", appSource, StringComparison.Ordinal);
        Assert.Contains("startup_clock_rollback", appSource, StringComparison.Ordinal);
        Assert.Contains("startup_failed", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", coordinatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", coordinatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", importCoordinatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", importCoordinatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductStockZeroLifecycleUseCase", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PostImportLifecycleUseCase", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductTaskAggregator", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiryStageCalculator", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Inspection", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("batch_tracking_resumed", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("product_stock_zero", executorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", startupUseCaseSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", startupUseCaseSource, StringComparison.Ordinal);
    }

    private static PreparedImport Prepare(
        SqliteTestDatabase database,
        string fileName,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var path = Path.Combine(database.Directory, fileName);
        WriteWorkbook(path, rows);
        using var context = database.Open();
        var workbook = new ExcelTemplateReader().Read(path);
        var classification = new ExcelFileClassifier().Classify(workbook);
        var plan = new ExcelImportPlanner().Plan(context, classification);
        var confirmation = new ImportConfirmationGuard().Confirm(
            new ImportConfirmationGuard().BindPreview(path, workbook, plan));
        return new PreparedImport(
            classification,
            plan,
            Assert.IsType<ImportConfirmationContract>(confirmation.Contract));
    }

    private static ConfirmedImportResult Execute(
        SqliteTestDatabase database,
        ImportConfirmationContract contract)
    {
        using var context = database.Open();
        return new ConfirmedImportLifecycleOrchestrator().Execute(
            context,
            new ConfirmedImportLifecycleRequest(
                contract,
                Path.Combine(database.Directory, "snapshots"),
                ParsedAtUtc,
                BusinessDate,
                OccurredAtUtc));
    }

    private static Product AddProduct(
        StoreDbContext context,
        string code,
        int stock,
        int lifecycleGeneration = 0,
        bool stockZeroTerminated = false)
    {
        var product = new Product
        {
            ProductCode = code,
            CurrentName = $"{code}名称",
            CurrentBarcode = $"{code}-BARCODE",
            ExcelStockQty = stock,
            EffectiveStockQty = stock,
            EffectiveStockSource = "excel",
            LifecycleGeneration = lifecycleGeneration,
            IsStockZeroTerminated = stockZeroTerminated,
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(
        StoreDbContext context,
        Product product,
        DateOnly? productionDate = null,
        DateOnly? expiryDate = null,
        int currentArrivalQty = 1,
        int maxArrivalQty = 1,
        int lifecycleGeneration = 0,
        string trackingStatus = "active",
        string? stopReason = null,
        DateTime? stoppedAtUtc = null,
        string currentStage = ExpiryStageCalculator.None,
        DateOnly? nextTriggerDate = null,
        int attentionVersion = 0)
    {
        var batch = new Batch
        {
            ProductId = product.Id,
            ProductionDate = productionDate ?? new DateOnly(2026, 1, 1),
            ExpiryDate = expiryDate ?? new DateOnly(2026, 9, 20),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = currentArrivalQty,
            MaxArrivalQty = maxArrivalQty,
            SourceDiscountReference = "是",
            LifecycleGeneration = lifecycleGeneration,
            TrackingStatus = trackingStatus,
            StopReason = stopReason,
            StoppedAtUtc = stoppedAtUtc,
            CurrentStage = currentStage,
            NextTriggerDate = nextTriggerDate,
            AttentionVersion = attentionVersion,
            HandledAttentionVersion = 0,
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static (ProductTask Task, ProductTaskItem Item, InspectionDraft Draft, InspectionDraftItem DraftItem)
        AddTaskWithDraft(StoreDbContext context, Product product, Batch batch)
    {
        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Discount50,
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.Tasks.Add(task);
        context.SaveChanges();

        var item = new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            Stage = ExpiryStageCalculator.Discount50,
            AttentionVersion = batch.AttentionVersion,
            RequiresReconfirmation = false,
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.TaskItems.Add(item);
        context.SaveChanges();

        var draft = new InspectionDraft
        {
            TaskId = task.Id,
            InspectorName = "保留草稿",
            CheckDate = BusinessDate.AddDays(-1),
            IsInvalid = false,
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.Drafts.Add(draft);
        context.SaveChanges();

        var draftItem = new InspectionDraftItem
        {
            DraftId = draft.Id,
            TaskItemId = item.Id,
            TaskId = task.Id,
            CheckedQty = 1,
            ConfirmedAttentionVersion = batch.AttentionVersion
        };
        context.DraftItems.Add(draftItem);
        context.SaveChanges();
        return (task, item, draft, draftItem);
    }

    private static void AddHistory(StoreDbContext context, int number)
    {
        var bytes = Encoding.UTF8.GetBytes($"old workbook {number}");
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var importedAt = new DateTime(2026, 8, number, 8, 0, 0, DateTimeKind.Utc);
        var import = new ImportRecord
        {
            SourceFileName = $"old-{number}.xlsx",
            SourceFileSha256 = sha256,
            ParsedAtUtc = importedAt,
            ConfirmedAtUtc = importedAt,
            Status = ImportStatuses.Succeeded
        };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ImportWorkbooks.Add(new ImportWorkbook
        {
            ImportId = import.Id,
            OriginalFileName = import.SourceFileName,
            Content = bytes,
            Sha256 = sha256,
            SavedAtUtc = importedAt
        });
        context.ImportIssues.Add(new ImportIssue
        {
            ImportId = import.Id,
            RowNumber = 1,
            IssueType = "seed_issue",
            FieldName = "商品编码",
            SafeSummary = $"old-{number} issue"
        });
        context.BackupRecords.Add(new BackupRecord
        {
            BackupType = "pre_import",
            FilePath = $"old-{number}.db",
            Sha256 = sha256,
            CreatedAtUtc = importedAt,
            VerificationStatus = "verified"
        });
        context.SaveChanges();
    }

    private static S3T07ScopeSnapshot CaptureScope(
        StoreDbContext context,
        params string[] productCodes)
    {
        var products = context.Products
            .AsNoTracking()
            .Where(product => productCodes.Contains(product.ProductCode))
            .OrderBy(product => product.Id)
            .ToArray();
        var productIds = products.Select(product => product.Id).ToArray();
        var batches = context.Batches
            .AsNoTracking()
            .Where(batch => productIds.Contains(batch.ProductId))
            .OrderBy(batch => batch.Id)
            .ToArray();
        var tasks = context.Tasks
            .AsNoTracking()
            .Where(task => productIds.Contains(task.ProductId))
            .OrderBy(task => task.Id)
            .ToArray();
        var taskIds = tasks.Select(task => task.Id).ToArray();
        var taskItems = context.TaskItems
            .AsNoTracking()
            .Where(item => taskIds.Contains(item.TaskId))
            .OrderBy(item => item.Id)
            .ToArray();
        var drafts = context.Drafts
            .AsNoTracking()
            .Where(draft => taskIds.Contains(draft.TaskId))
            .OrderBy(draft => draft.Id)
            .ToArray();
        var draftIds = drafts.Select(draft => draft.Id).ToArray();
        var draftItems = context.DraftItems
            .AsNoTracking()
            .Where(item => draftIds.Contains(item.DraftId))
            .OrderBy(item => item.Id)
            .ToArray();
        var events = context.LifecycleEvents
            .AsNoTracking()
            .Where(item => productIds.Contains(item.ProductId))
            .OrderBy(item => item.Id)
            .ToArray();

        return new(
            products.Select(product => new ProductState(
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
                product.UpdatedAtUtc)).ToArray(),
            batches.Select(batch => new BatchState(
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
                batch.UpdatedAtUtc)).ToArray(),
            tasks.Select(task => new TaskState(
                task.Id,
                task.ProductId,
                task.Status,
                task.HighestStage,
                task.CreatedAtUtc,
                task.UpdatedAtUtc,
                task.ClosedAtUtc,
                task.CloseReason)).ToArray(),
            taskItems.Select(item => new TaskItemState(
                item.Id,
                item.TaskId,
                item.BatchId,
                item.ProductId,
                item.Stage,
                item.AttentionVersion,
                item.RequiresReconfirmation,
                item.CreatedAtUtc,
                item.UpdatedAtUtc)).ToArray(),
            drafts.Select(draft => new DraftState(
                draft.Id,
                draft.TaskId,
                draft.InspectorName,
                draft.CheckDate,
                draft.IsInvalid,
                draft.InvalidReason,
                draft.InvalidatedAtUtc,
                draft.CreatedAtUtc,
                draft.UpdatedAtUtc)).ToArray(),
            draftItems.Select(item => new DraftItemState(
                item.Id,
                item.DraftId,
                item.TaskItemId,
                item.TaskId,
                item.CheckedQty,
                item.ConfirmedAttentionVersion)).ToArray(),
            events.Select(item => new EventState(
                item.Id,
                item.ProductId,
                item.BatchId,
                item.EventType,
                item.Reason,
                item.OccurredAtUtc,
                item.SourceImportId,
                item.SourceInspectionId,
                item.SourceAdjustmentId)).ToArray());
    }

    private static void AssertScopeEqual(S3T07ScopeSnapshot expected, S3T07ScopeSnapshot actual)
    {
        Assert.Equal(expected.Products, actual.Products);
        Assert.Equal(expected.Batches, actual.Batches);
        Assert.Equal(expected.Tasks, actual.Tasks);
        Assert.Equal(expected.TaskItems, actual.TaskItems);
        Assert.Equal(expected.Drafts, actual.Drafts);
        Assert.Equal(expected.DraftItems, actual.DraftItems);
        Assert.Equal(expected.Events, actual.Events);
    }

    private static IReadOnlyList<string?> Row(
        string productCode,
        string barcode,
        string name,
        string productionDate,
        string expiryDate,
        string shelfLife,
        string? stock,
        string arrival = "1") =>
    [
        "食品", productCode, barcode, name, productionDate, expiryDate, shelfLife, "M", "是", arrival, stock
    ];

    private static void WriteWorkbook(
        string path,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        var header = string.Join(string.Empty, Headers.Select((value, index) => InlineCell(ColumnName(index), 1, value)));
        var sheetRows = new StringBuilder();
        sheetRows.Append($"<row r=\"1\">{header}</row>");
        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            var cells = string.Join(
                string.Empty,
                rows[index].Select((value, column) => InlineCell(ColumnName(column), rowNumber, value ?? string.Empty)));
            sheetRows.Append($"<row r=\"{rowNumber}\">{cells}</row>");
        }

        AddEntry(archive, "xl/worksheets/sheet1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>{sheetRows}</sheetData></worksheet>");
    }

    private static string InlineCell(string column, int row, string value) =>
        $"<c r=\"{column}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBased)
    {
        var value = zeroBased + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }

        return result;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("StoreExpiryInspector.slnx was not found from the test base directory.");
    }

    private sealed record PreparedImport(
        ExcelClassificationResult Classification,
        ImportPlan Plan,
        ImportConfirmationContract Contract);

    private sealed record S3T07ScopeSnapshot(
        ProductState[] Products,
        BatchState[] Batches,
        TaskState[] Tasks,
        TaskItemState[] TaskItems,
        DraftState[] Drafts,
        DraftItemState[] DraftItems,
        EventState[] Events);

    private sealed record ProductState(
        long Id,
        string ProductCode,
        string? CurrentName,
        string? CurrentBarcode,
        string CategoryCode,
        string? PolicyCode,
        int ExcelStockQty,
        int EffectiveStockQty,
        string? EffectiveStockSource,
        int LifecycleGeneration,
        bool IsStockZeroTerminated,
        long? LastSeenImportId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record BatchState(
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

    private sealed record TaskState(
        long Id,
        long ProductId,
        string Status,
        string HighestStage,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? ClosedAtUtc,
        string? CloseReason);

    private sealed record TaskItemState(
        long Id,
        long TaskId,
        long BatchId,
        long ProductId,
        string Stage,
        int AttentionVersion,
        bool RequiresReconfirmation,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record DraftState(
        long Id,
        long TaskId,
        string? InspectorName,
        DateOnly? CheckDate,
        bool IsInvalid,
        string? InvalidReason,
        DateTime? InvalidatedAtUtc,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record DraftItemState(
        long Id,
        long DraftId,
        long TaskItemId,
        long TaskId,
        int? CheckedQty,
        int ConfirmedAttentionVersion);

    private sealed record EventState(
        long Id,
        long ProductId,
        long? BatchId,
        string EventType,
        string Reason,
        DateTime OccurredAtUtc,
        long? SourceImportId,
        long? SourceInspectionId,
        long? SourceAdjustmentId);
}
