using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S3T07StartupEvidenceTests
{
    private static readonly DateOnly Today = new(2026, 8, 27);

    private static readonly DateTime OccurredAtUtc =
        new(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);

    [Fact]
    public void FutureNextTriggerIsNotProcessedButSuccessfulRunDateAdvances()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-START-FUTURE");
            AddBatch(
                seed,
                product,
                currentStage: ExpiryStageCalculator.None,
                nextTriggerDate: Today.AddDays(1));
            seed.AppStates.Single().LastNormalRunDate = Today.AddDays(-1);
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(context, Today, OccurredAtUtc);
            Assert.True(result.Succeeded);
            Assert.False(result.ClockRollback);
            Assert.Equal(0, result.Recalculation.MatchedBatchCount);
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Equal(Today.AddDays(1), batch.NextTriggerDate);
        Assert.Equal(Today, verify.AppStates.AsNoTracking().Single().LastNormalRunDate);
        Assert.Empty(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void StoppedBatchIsNotProcessedDuringStartup()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-START-STOPPED");
            AddBatch(
                seed,
                product,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: OccurredAtUtc.AddDays(-1),
                currentStage: ExpiryStageCalculator.None,
                nextTriggerDate: Today);
            seed.AppStates.Single().LastNormalRunDate = Today.AddDays(-1);
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(context, Today, OccurredAtUtc);
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Recalculation.MatchedBatchCount);
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal("stopped", batch.TrackingStatus);
        Assert.Equal("batch_checked_zero", batch.StopReason);
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Equal(Today, batch.NextTriggerDate);
        Assert.Empty(verify.Tasks.AsNoTracking());
    }

    [Fact]
    public void OfflineCrossingMultipleStagesWritesOnlyCurrentStage()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-START-OFFLINE");
            AddBatch(
                seed,
                product,
                expiryDate: Today,
                currentStage: ExpiryStageCalculator.None,
                nextTriggerDate: Today.AddDays(-90));
            seed.AppStates.Single().LastNormalRunDate = Today.AddDays(-90);
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(context, Today, OccurredAtUtc);
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Recalculation.MatchedBatchCount);
            Assert.Equal(1, result.Recalculation.ChangedBatchCount);
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Expired, batch.CurrentStage);
        Assert.Null(batch.NextTriggerDate);
        var task = Assert.Single(verify.Tasks.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Expired, task.HighestStage);
        var item = Assert.Single(verify.TaskItems.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Expired, item.Stage);
    }

    [Fact]
    public void ExistingOpenTaskIsUpgradedInPlaceAndValidDraftRemainsValidButItemNeedsReconfirmation()
    {
        using var database = SqliteTestDatabase.Create();
        long taskId;
        long draftId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-START-TASK");
            var batch = AddBatch(
                seed,
                product,
                currentStage: ExpiryStageCalculator.Discount50,
                nextTriggerDate: Today,
                attentionVersion: 2);
            var seedTask = new ProductTask
            {
                ProductId = product.Id,
                Status = "open",
                HighestStage = ExpiryStageCalculator.Discount50,
                CreatedAtUtc = OccurredAtUtc.AddDays(-2),
                UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
            };
            seed.Tasks.Add(seedTask);
            seed.SaveChanges();
            var seedItem = new ProductTaskItem
            {
                TaskId = seedTask.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = ExpiryStageCalculator.Discount50,
                AttentionVersion = 2,
                RequiresReconfirmation = false,
                CreatedAtUtc = OccurredAtUtc.AddDays(-2),
                UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
            };
            seed.TaskItems.Add(seedItem);
            seed.SaveChanges();
            var seedDraft = new InspectionDraft
            {
                TaskId = seedTask.Id,
                InspectorName = "已填草稿",
                CheckDate = Today.AddDays(-1),
                IsInvalid = false,
                CreatedAtUtc = OccurredAtUtc.AddDays(-2),
                UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
            };
            seed.Drafts.Add(seedDraft);
            seed.SaveChanges();
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = seedDraft.Id,
                TaskItemId = seedItem.Id,
                TaskId = seedTask.Id,
                CheckedQty = 1,
                ConfirmedAttentionVersion = 2
            });
            seed.AppStates.Single().LastNormalRunDate = Today.AddDays(-1);
            seed.SaveChanges();
            taskId = seedTask.Id;
            draftId = seedDraft.Id;
        }

        using (var context = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(context, Today, OccurredAtUtc);
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Recalculation.MatchedBatchCount);
        }

        using var verify = database.Open();
        var task = Assert.Single(verify.Tasks.AsNoTracking());
        Assert.Equal(taskId, task.Id);
        Assert.Equal("open", task.Status);
        Assert.Equal(ExpiryStageCalculator.Discount20, task.HighestStage);
        var item = Assert.Single(verify.TaskItems.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Discount20, item.Stage);
        Assert.Equal(2, item.AttentionVersion);
        Assert.True(item.RequiresReconfirmation);
        var draft = Assert.Single(verify.Drafts.AsNoTracking());
        Assert.Equal(draftId, draft.Id);
        Assert.False(draft.IsInvalid);
        Assert.Equal("已填草稿", draft.InspectorName);
        Assert.Equal(Today.AddDays(-1), draft.CheckDate);
        Assert.Null(draft.InvalidReason);
        Assert.Null(draft.InvalidatedAtUtc);
        Assert.Single(verify.DraftItems.AsNoTracking());
    }

    [Fact]
    public void RunningStartupTwiceOnSameDateIsIdempotent()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-START-IDEMPOTENT");
            AddBatch(
                seed,
                product,
                currentStage: ExpiryStageCalculator.None,
                nextTriggerDate: Today);
            seed.AppStates.Single().LastNormalRunDate = Today.AddDays(-1);
            seed.SaveChanges();
        }

        using (var first = database.Open())
        {
            Assert.True(new ApplicationStartupCoordinator().Execute(first, Today, OccurredAtUtc).Succeeded);
        }

        StartupStateSnapshot firstSnapshot;
        using (var snapshot = database.Open())
        {
            firstSnapshot = Capture(snapshot);
        }

        using (var second = database.Open())
        {
            var result = new ApplicationStartupCoordinator().Execute(second, Today, OccurredAtUtc.AddMinutes(1));
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Recalculation.MatchedBatchCount);
        }

        using var verify = database.Open();
        var secondSnapshot = Capture(verify);
        Assert.Equal(firstSnapshot.Batches, secondSnapshot.Batches);
        Assert.Equal(firstSnapshot.Tasks, secondSnapshot.Tasks);
        Assert.Equal(firstSnapshot.TaskItems, secondSnapshot.TaskItems);
        Assert.Equal(firstSnapshot.Drafts, secondSnapshot.Drafts);
        Assert.Equal(firstSnapshot.LastNormalRunDate, secondSnapshot.LastNormalRunDate);
    }

    [Fact]
    public void AppStateWriteFailureRollsBackStartupRecalculationAndTasks()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-START-FAIL");
            AddBatch(
                seed,
                product,
                currentStage: ExpiryStageCalculator.None,
                nextTriggerDate: Today);
            seed.AppStates.Single().LastNormalRunDate = Today.AddDays(-1);
            seed.SaveChanges();
        }

        using (var schema = database.Open())
        {
            schema.Database.ExecuteSqlRaw(
                "CREATE TRIGGER fail_app_state_update BEFORE UPDATE OF last_normal_run_date ON app_state BEGIN SELECT RAISE(ABORT, 'startup state failure'); END;");
        }

        using (var context = database.Open())
        {
            Assert.Throws<DbUpdateException>(() => new ApplicationStartupCoordinator().Execute(
                context,
                Today,
                OccurredAtUtc));
        }

        using var verify = database.Open();
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Equal(Today, batch.NextTriggerDate);
        Assert.Equal(Today.AddDays(-1), verify.AppStates.AsNoTracking().Single().LastNormalRunDate);
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Empty(verify.TaskItems.AsNoTracking());
    }

    private static Product AddProduct(StoreDbContext context, string code)
    {
        var product = new Product
        {
            ProductCode = code,
            CurrentName = $"{code}名称",
            CurrentBarcode = $"{code}-BARCODE",
            ExcelStockQty = 5,
            EffectiveStockQty = 5,
            EffectiveStockSource = "excel",
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.Products.Add(product);
        context.SaveChanges();
        EnsureFoodBaseline(context);
        return product;
    }

    private static void EnsureFoodBaseline(StoreDbContext context)
    {
        if (context.ScopeBaselines.Any()) return;
        var import = new ImportRecord { SourceFileName = "baseline.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = OccurredAtUtc, ConfirmedAtUtc = OccurredAtUtc, Status = "succeeded" };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = Today, IsCompleted = true, CompletedAtUtc = OccurredAtUtc });
        context.SaveChanges();
    }

    private static Batch AddBatch(
        StoreDbContext context,
        Product product,
        DateOnly? expiryDate = null,
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
            ProductionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = expiryDate ?? new DateOnly(2026, 9, 20),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 1,
            MaxArrivalQty = 1,
            SourceDiscountReference = "是",
            TrackingStatus = trackingStatus,
            StopReason = stopReason,
            StoppedAtUtc = stoppedAtUtc,
            CurrentStage = currentStage,
            NextTriggerDate = nextTriggerDate,
            AttentionVersion = attentionVersion,
            CreatedAtUtc = OccurredAtUtc.AddDays(-2),
            UpdatedAtUtc = OccurredAtUtc.AddDays(-1)
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static StartupStateSnapshot Capture(StoreDbContext context) => new(
        context.Batches.AsNoTracking().Select(batch => new BatchSnapshot(
            batch.Id,
            batch.CurrentStage,
            batch.NextTriggerDate,
            batch.UpdatedAtUtc)).ToArray(),
        context.Tasks.AsNoTracking().Select(task => new TaskSnapshot(
            task.Id,
            task.Status,
            task.HighestStage,
            task.UpdatedAtUtc)).ToArray(),
        context.TaskItems.AsNoTracking().Select(item => new TaskItemSnapshot(
            item.Id,
            item.Stage,
            item.AttentionVersion,
            item.RequiresReconfirmation,
            item.UpdatedAtUtc)).ToArray(),
        context.Drafts.AsNoTracking().Select(draft => new DraftSnapshot(
            draft.Id,
            draft.IsInvalid,
            draft.InvalidReason,
            draft.InvalidatedAtUtc,
            draft.UpdatedAtUtc)).ToArray(),
        context.AppStates.AsNoTracking().Single().LastNormalRunDate);

    private sealed record StartupStateSnapshot(
        BatchSnapshot[] Batches,
        TaskSnapshot[] Tasks,
        TaskItemSnapshot[] TaskItems,
        DraftSnapshot[] Drafts,
        DateOnly? LastNormalRunDate);

    private sealed record BatchSnapshot(
        long Id,
        string CurrentStage,
        DateOnly? NextTriggerDate,
        DateTime UpdatedAtUtc);

    private sealed record TaskSnapshot(
        long Id,
        string Status,
        string HighestStage,
        DateTime UpdatedAtUtc);

    private sealed record TaskItemSnapshot(
        long Id,
        string Stage,
        int AttentionVersion,
        bool RequiresReconfirmation,
        DateTime UpdatedAtUtc);

    private sealed record DraftSnapshot(
        long Id,
        bool IsInvalid,
        string? InvalidReason,
        DateTime? InvalidatedAtUtc,
        DateTime UpdatedAtUtc);
}
