using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class PostImportLifecycleUseCaseTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 27);
    private static readonly DateTime SeedUtc = new(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OccurredAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewNoneBatchStartsCurrentGenerationWithoutTask()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId, generation: 3).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(100),
                0,
                currentArrivalQty: 4,
                maxArrivalQty: 4);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, Request(importId, Group(productId, New(batchId, 0, 4))));

            Assert.Equal(1, result.StartedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal(3, batch.LifecycleGeneration);
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Equal(BusinessDate.AddDays(10), batch.NextTriggerDate);
        Assert.Empty(verify.Tasks);
        Assert.Equal(0, batch.AttentionVersion);
        Assert.Equal(0, batch.HandledAttentionVersion);
    }

    [Fact]
    public void DirectLifecycleRejectsExcludedOrUnbaselinedProductBeforeWrites()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            var product = AddProduct(seed, importId);
            product.ExpiryManagementStatus = ExpiryManagementStatus.Excluded;
            product.PolicyCode = null;
            product.PolicyVersion = null;
            seed.SaveChanges();
            productId = product.Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(10), 0);
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(context, Request(importId, Group(productId, New(batchId, 0, 2)))));
            Assert.Empty(context.Tasks);
        }
    }

    [Theory]
    [InlineData(80, "discount_50")]
    [InlineData(30, "discount_20")]
    [InlineData(10, "withdraw")]
    [InlineData(-1, "expired")]
    public void NewActionableBatchUsesCurrentStageAndTask(int daysToExpiry, string expectedStage)
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(daysToExpiry), 0);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, Request(importId, Group(productId, New(batchId, 0, 2))));

            Assert.Equal(1, result.StartedBatchCount);
            Assert.Equal(1, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        Assert.Equal(expectedStage, verify.Batches.Single().CurrentStage);
        Assert.Equal(expectedStage, verify.TaskItems.Single().Stage);
        Assert.Single(verify.Tasks);
    }

    [Fact]
    public void MultipleNewBatchesShareOneTaskAndKeepStages()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long firstBatchId;
        long secondBatchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            firstBatchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(80), 0);
            secondBatchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                0,
                currentArrivalQty: 3,
                maxArrivalQty: 3);
        }

        using (var context = database.Open())
        {
            var result = Execute(
                context,
                Request(
                    importId,
                    Group(productId, New(firstBatchId, 0, 2), New(secondBatchId, 0, 3))));
            Assert.Equal(1, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        Assert.Single(verify.Tasks);
        Assert.Equal(2, verify.TaskItems.Count());
        Assert.Equal(
            new[] { ExpiryStageCalculator.Discount50, ExpiryStageCalculator.Withdraw },
            verify.TaskItems.OrderBy(item => item.BatchId).Select(item => item.Stage).ToArray());
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.Tasks.Single().HighestStage);
    }

    [Fact]
    public void NewBatchAfterProductZeroUsesProductGenerationAndDoesNotReviveOldBatch()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long oldBatchId;
        long newBatchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            var product = AddProduct(seed, importId, generation: 4, terminated: true);
            productId = product.Id;
            oldBatchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                lifecycleGeneration: 3,
                trackingStatus: "stopped",
                stopReason: "product_stock_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10);
            newBatchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(80), 0);
        }

        using (var context = database.Open())
        {
            var result = Execute(
                context,
                Request(
                    importId,
                    Group(
                        productId,
                        New(newBatchId, 0, 2),
                        Existing(oldBatchId, 7, 10, 0, 1))));
            Assert.Equal(1, result.StartedBatchCount);
            Assert.Equal(0, result.NewArrivalBatchCount);
        }

        using var verify = database.Open();
        var oldBatch = verify.Batches.Single(batch => batch.Id == oldBatchId);
        Assert.Equal(3, oldBatch.LifecycleGeneration);
        Assert.Equal("stopped", oldBatch.TrackingStatus);
        Assert.Equal("product_stock_zero", oldBatch.StopReason);
        Assert.Equal(0, oldBatch.AttentionVersion);
        var newBatch = verify.Batches.Single(batch => batch.Id == newBatchId);
        Assert.Equal(4, newBatch.LifecycleGeneration);
        Assert.Equal(ExpiryStageCalculator.Discount50, newBatch.CurrentStage);
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
        Assert.Equal(newBatchId, verify.TaskItems.Single().BatchId);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void ArrivalAtOrBelowHistoricalMaximumDoesNothing(int currentArrivalQty)
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(80),
                lifecycleGeneration: 0,
                currentArrivalQty: currentArrivalQty,
                maxArrivalQty: 8,
                stage: ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        {
            var result = Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, currentArrivalQty, 0, 0))));
            Assert.Equal(0, result.ChangedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal(0, batch.AttentionVersion);
        Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
        Assert.Empty(verify.Tasks);
    }

    [Fact]
    public void ArrivalAboveHistoricalMaximumUsesCasAndCurrentStage()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(30),
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                stage: ExpiryStageCalculator.None,
                attentionVersion: 2);
        }

        using (var context = database.Open())
        {
            var result = Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, 2, 3))));
            Assert.Equal(1, result.NewArrivalBatchCount);
            Assert.Equal(1, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal(3, batch.AttentionVersion);
        Assert.Equal(2, batch.HandledAttentionVersion);
        Assert.Equal(ExpiryStageCalculator.Discount20, batch.CurrentStage);
        Assert.Equal(10, batch.CurrentArrivalQty);
        Assert.Equal(10, batch.MaxArrivalQty);
        Assert.True(verify.TaskItems.Single().RequiresReconfirmation);
    }

    [Fact]
    public void ArrivalMergesOpenTaskAndPreservesDraftContent()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        long taskId;
        long itemId;
        var draftTime = SeedUtc.AddMinutes(1);
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(80),
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                stage: ExpiryStageCalculator.Discount50);
            var task = new ProductTask
            {
                ProductId = productId,
                HighestStage = ExpiryStageCalculator.Discount50,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            seed.Tasks.Add(task);
            seed.SaveChanges();
            taskId = task.Id;
            var item = new ProductTaskItem
            {
                TaskId = taskId,
                BatchId = batchId,
                ProductId = productId,
                Stage = ExpiryStageCalculator.Discount50,
                AttentionVersion = 0,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            seed.TaskItems.Add(item);
            seed.SaveChanges();
            itemId = item.Id;
            var draft = new InspectionDraft
            {
                TaskId = taskId,
                InspectorName = "张三",
                CheckDate = BusinessDate,
                CreatedAtUtc = draftTime,
                UpdatedAtUtc = draftTime
            };
            seed.Drafts.Add(draft);
            seed.SaveChanges();
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = itemId,
                TaskId = taskId,
                CheckedQty = 7,
                ConfirmedAttentionVersion = 0
            });
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Execute(context, Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1))));
        }

        using var verify = database.Open();
        Assert.Equal(taskId, verify.Tasks.Single().Id);
        Assert.Equal(itemId, verify.TaskItems.Single().Id);
        Assert.Equal(ExpiryStageCalculator.Discount50, verify.TaskItems.Single().Stage);
        Assert.Equal(1, verify.TaskItems.Single().AttentionVersion);
        Assert.True(verify.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal("张三", verify.Drafts.Single().InspectorName);
        Assert.Equal(draftTime, verify.Drafts.Single().UpdatedAtUtc);
        Assert.Equal(7, verify.DraftItems.Single().CheckedQty);
        Assert.Equal(0, verify.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void ArrivalAfterCompletedTaskCreatesOnlyCurrentTask()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                stage: ExpiryStageCalculator.Expired);
            seed.Tasks.Add(new ProductTask
            {
                ProductId = productId,
                Status = "completed",
                HighestStage = ExpiryStageCalculator.Expired,
                ClosedAtUtc = SeedUtc,
                CloseReason = "completed",
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            });
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Execute(context, Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1))));
        }

        using var verify = database.Open();
        Assert.Single(verify.Tasks.Where(task => task.Status == "open"));
        Assert.Single(verify.Tasks.Where(task => task.Status == "completed"));
        Assert.Single(verify.TaskItems);
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.TaskItems.Single().Stage);
    }

    [Fact]
    public void ArrivalCrossingMultipleStagesCreatesOnlyTodayStage()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(-1),
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                stage: ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        {
            Execute(context, Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1))));
        }

        using var verify = database.Open();
        Assert.Equal(ExpiryStageCalculator.Expired, verify.Batches.Single().CurrentStage);
        Assert.Single(verify.TaskItems);
        Assert.Equal(ExpiryStageCalculator.Expired, verify.TaskItems.Single().Stage);
    }

    [Fact]
    public void CheckedZeroBatchWithNewArrivalResumesAndWritesOneEvent()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1))));
            Assert.Equal(1, result.ResumedBatchCount);
            Assert.Equal(1, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Null(batch.StopReason);
        Assert.Null(batch.StoppedAtUtc);
        Assert.Equal(1, batch.AttentionVersion);
        var lifecycleEvent = verify.LifecycleEvents.Single();
        Assert.Equal("batch_tracking_resumed", lifecycleEvent.EventType);
        Assert.Equal("new_arrival_after_batch_checked_zero", lifecycleEvent.Reason);
        Assert.Equal(productId, lifecycleEvent.ProductId);
        Assert.Equal(batchId, lifecycleEvent.BatchId);
        Assert.Equal(importId, lifecycleEvent.SourceImportId);
        Assert.Equal(OccurredAtUtc, lifecycleEvent.OccurredAtUtc);
        Assert.Single(verify.TaskItems);
    }

    [Fact]
    public void CheckedZeroBatchWithoutBreakthroughDoesNotResume()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 8,
                maxArrivalQty: 8);
        }

        using (var context = database.Open())
        {
            Execute(context, Request(importId, Group(productId, Existing(batchId, 8, 8, 0, 0))));
        }

        using var verify = database.Open();
        Assert.Equal("stopped", verify.Batches.Single().TrackingStatus);
        Assert.Equal("batch_checked_zero", verify.Batches.Single().StopReason);
        Assert.Equal(0, verify.Batches.Single().AttentionVersion);
        Assert.Empty(verify.LifecycleEvents);
        Assert.Empty(verify.Tasks);
    }

    [Fact]
    public void ProductZeroRejectsWholeRequestBeforeNewBatchStarts()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId, stock: 0).Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(10), 0);
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() =>
                Execute(context, Request(importId, Group(productId, New(batchId, 0, 2)))));
            Assert.Empty(context.Tasks);
        }

        using var verify = database.Open();
        Assert.Equal(ExpiryStageCalculator.None, verify.Batches.Single().CurrentStage);
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.Tasks);
    }

    [Fact]
    public void ProductZeroHistoryAndNonBatchStopNeverResume()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long productZeroBatchId;
        long otherStoppedBatchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            var product = AddProduct(seed, importId, terminated: true);
            productId = product.Id;
            productZeroBatchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                lifecycleGeneration: 0,
                trackingStatus: "stopped",
                stopReason: "product_stock_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                attentionVersion: 2);
            otherStoppedBatchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                lifecycleGeneration: 0,
                trackingStatus: "stopped",
                stopReason: "manual_stop",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                attentionVersion: 3);
        }

        using (var context = database.Open())
        {
            Execute(
                context,
                Request(
                    importId,
                    Group(
                        productId,
                        Existing(productZeroBatchId, 8, 10, 2, 3),
                        Existing(otherStoppedBatchId, 8, 10, 3, 4))));
        }

        using var verify = database.Open();
        Assert.Equal(2, verify.Batches.Single(batch => batch.Id == productZeroBatchId).AttentionVersion);
        Assert.Equal(3, verify.Batches.Single(batch => batch.Id == otherStoppedBatchId).AttentionVersion);
        Assert.Empty(verify.LifecycleEvents);
        Assert.Empty(verify.Tasks);
    }

    [Fact]
    public void ExactReplayIsIdempotentAndConflictingVersionIsRejected()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                stage: ExpiryStageCalculator.None);
        }

        var request = Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1)));
        DateTime firstBatchUpdatedAt;
        DateTime firstTaskUpdatedAt;
        using (var context = database.Open())
        {
            Execute(context, request);
            firstBatchUpdatedAt = context.Batches.Single().UpdatedAtUtc;
            firstTaskUpdatedAt = context.Tasks.Single().UpdatedAtUtc;
        }

        using (var context = database.Open())
        {
            var result = Execute(context, request);
            Assert.Equal(0, result.ChangedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
        }

        using (var verify = database.Open())
        {
            Assert.Equal(1, verify.Batches.Single().AttentionVersion);
            Assert.Equal(firstBatchUpdatedAt, verify.Batches.Single().UpdatedAtUtc);
            Assert.Equal(firstTaskUpdatedAt, verify.Tasks.Single().UpdatedAtUtc);
            Assert.Empty(verify.LifecycleEvents);
            Assert.Single(verify.TaskItems);
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 0)))));
        }
    }

    [Fact]
    public void NewFactReplayDoesNotRewriteTimestampOrDuplicateTask()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(10), 0);
        }

        var request = Request(importId, Group(productId, New(batchId, 0, 2)));
        DateTime firstUpdatedAt;
        long taskId;
        using (var context = database.Open())
        {
            Execute(context, request);
            firstUpdatedAt = context.Batches.Single().UpdatedAtUtc;
            taskId = context.Tasks.Single().Id;
        }

        using (var context = database.Open())
        {
            var result = Execute(context, request);
            Assert.Equal(0, result.ChangedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        Assert.Equal(firstUpdatedAt, verify.Batches.Single().UpdatedAtUtc);
        Assert.Equal(taskId, verify.Tasks.Single().Id);
        Assert.Single(verify.TaskItems);
    }

    [Fact]
    public void NewFactReplayAfterStartupAdvancesStageDoesNotDowngradeBatchOrTask()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(100), 0);
        }

        var request = Request(importId, Group(productId, New(batchId, 0, 2)));
        using (var context = database.Open())
        {
            Execute(context, request);
        }

        var laterUpdatedAt = OccurredAtUtc.AddHours(1);
        using (var context = database.Open())
        {
            var result = new StartupRecalculationUseCase().Execute(
                context,
                new StartupRecalculationRequest(BusinessDate.AddDays(10), laterUpdatedAt));

            Assert.Equal(1, result.MatchedBatchCount);
            Assert.Equal(1, result.ChangedBatchCount);
            Assert.Equal(1, result.AggregatedProductCount);
        }

        long taskId;
        using (var beforeReplay = database.Open())
        {
            var batch = beforeReplay.Batches.Single();
            Assert.Equal(ExpiryStageCalculator.Discount50, batch.CurrentStage);
            Assert.Equal(BusinessDate.AddDays(40), batch.NextTriggerDate);
            Assert.Equal(laterUpdatedAt, batch.UpdatedAtUtc);
            taskId = beforeReplay.Tasks.Single().Id;
            Assert.Equal(ExpiryStageCalculator.Discount50, beforeReplay.TaskItems.Single().Stage);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, request);

            Assert.Equal(0, result.ChangedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        var replayedBatch = verify.Batches.Single();
        Assert.Equal(ExpiryStageCalculator.Discount50, replayedBatch.CurrentStage);
        Assert.Equal(BusinessDate.AddDays(40), replayedBatch.NextTriggerDate);
        Assert.Equal(laterUpdatedAt, replayedBatch.UpdatedAtUtc);
        Assert.Equal(taskId, verify.Tasks.Single().Id);
        Assert.Equal(ExpiryStageCalculator.Discount50, verify.TaskItems.Single().Stage);
    }

    [Fact]
    public void NewFactReplayAfterFormalBatchStopDoesNotResumeBatch()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(100), 0);
        }

        var request = Request(importId, Group(productId, New(batchId, 0, 2)));
        using (var context = database.Open())
        {
            Execute(context, request);
        }

        var stoppedAt = OccurredAtUtc.AddHours(2);
        using (var context = database.Open())
        {
            var batch = context.Batches.Single();
            batch.TrackingStatus = "stopped";
            batch.StopReason = "batch_checked_zero";
            batch.StoppedAtUtc = stoppedAt;
            batch.NextTriggerDate = null;
            batch.UpdatedAtUtc = stoppedAt;
            context.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = Execute(context, request);

            Assert.Equal(0, result.ChangedBatchCount);
            Assert.Equal(0, result.ResumedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        var replayedBatch = verify.Batches.Single();
        Assert.Equal("stopped", replayedBatch.TrackingStatus);
        Assert.Equal("batch_checked_zero", replayedBatch.StopReason);
        Assert.Equal(stoppedAt, replayedBatch.StoppedAtUtc);
        Assert.Equal(ExpiryStageCalculator.None, replayedBatch.CurrentStage);
        Assert.Null(replayedBatch.NextTriggerDate);
        Assert.Equal(stoppedAt, replayedBatch.UpdatedAtUtc);
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void OnlyRequestedProductIsReadAndChanged()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productAId;
        long productBId;
        long productCId;
        long batchAId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productAId = AddProduct(seed, importId, code: "A").Id;
            productBId = AddProduct(seed, importId, code: "B").Id;
            productCId = AddProduct(seed, importId, code: "C").Id;
            batchAId = AddBatch(seed, productAId, importId, BusinessDate.AddDays(10), 0);
            AddBatch(seed, productBId, importId, BusinessDate.AddDays(10), 0);
            AddBatch(seed, productCId, importId, BusinessDate.AddDays(10), 0);
        }

        using (var context = database.Open())
        {
            Execute(context, Request(importId, Group(productAId, New(batchAId, 0, 2))));
        }

        using var verify = database.Open();
        Assert.Equal("active", verify.Batches.Single(batch => batch.ProductId == productAId).TrackingStatus);
        Assert.Equal(ExpiryStageCalculator.None, verify.Batches.Single(batch => batch.ProductId == productBId).CurrentStage);
        Assert.Equal(ExpiryStageCalculator.None, verify.Batches.Single(batch => batch.ProductId == productCId).CurrentStage);
        Assert.Empty(verify.Tasks.Where(task => task.ProductId != productAId));
    }

    [Fact]
    public void SQLiteFailureRollsBackBatchVersionTaskAndResumeEvent()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10);
        }

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t05_task_item
                BEFORE INSERT ON task_items
                BEGIN
                    SELECT RAISE(ABORT, 'forced S3-T05 failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1)))));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal(0, batch.AttentionVersion);
        Assert.Equal("stopped", batch.TrackingStatus);
        Assert.Equal("batch_checked_zero", batch.StopReason);
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void SQLiteFailureWhileWritingResumeEventRollsBackBatchAndVersion()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10);
        }

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t05_event
                BEFORE INSERT ON lifecycle_events
                BEGIN
                    SELECT RAISE(ABORT, 'forced S3-T05 event failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1)))));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        Assert.Equal(0, verify.Batches.Single().AttentionVersion);
        Assert.Equal("stopped", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void OuterTransactionFailureRestoresTrackedStateAndLeavesTransactionForCaller()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10);
        }

        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t05_outer_item
                BEFORE INSERT ON task_items
                BEGIN
                    SELECT RAISE(ABORT, 'forced S3-T05 outer failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1)))));
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.False(context.ChangeTracker.HasChanges());
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.Equal(0, verify.Batches.Single().AttentionVersion);
        Assert.Equal("stopped", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void InputGatesRejectInvalidImportIdentitySourceValuesAndUtcBeforeWrites()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId).Id;
            batchId = AddBatch(seed, productId, importId, BusinessDate.AddDays(10), 0);
            AddImport(seed, status: ImportStatuses.Undone, undone: true);
        }

        using (var context = database.Open())
        {
            Assert.Throws<KeyNotFoundException>(() => Execute(
                context,
                Request(importId + 100, Group(productId, New(batchId, 0, 2)))));
            Assert.Throws<ArgumentOutOfRangeException>(() => Execute(
                context,
                Request(importId, Group(productId, New(batchId, -1, 2)))));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                new PostImportLifecycleRequest(
                    importId,
                    BusinessDate,
                    DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Local),
                    [Group(productId, New(batchId, 0, 2))])));
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                new PostImportLifecycleRequest(
                    importId,
                    BusinessDate,
                    OccurredAtUtc,
                    [Group(productId, New(batchId, 0, 3))])));
            Assert.Empty(context.Tasks);
            Assert.False(context.ChangeTracker.HasChanges());
        }
    }

    [Fact]
    public void ImportUndoAndFactShapeAreRejected()
    {
        using var database = SqliteTestDatabase.Create();
        long undoneImportId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            undoneImportId = AddImport(seed, status: ImportStatuses.Undone, undone: true).Id;
            productId = AddProduct(seed, undoneImportId).Id;
            batchId = AddBatch(seed, productId, undoneImportId, BusinessDate.AddDays(10), 0);
        }

        using var context = database.Open();
        Assert.Throws<InvalidOperationException>(() => Execute(
            context,
            Request(undoneImportId, Group(productId, New(batchId, 0, 2)))));
        Assert.Throws<ArgumentException>(() => Execute(
            context,
            Request(undoneImportId, Group(productId, new PostImportBatchFact(batchId, "other", 0, 2)))));
        Assert.Throws<ArgumentException>(() => Execute(
            context,
            Request(undoneImportId, Group(productId, Existing(batchId, 0, 2, 0, 0)))));
        Assert.Empty(context.Tasks);
    }

    [Fact]
    public void LastSeenCrossProductAndDuplicateIdentityGatesRejectWithoutWrites()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productAId;
        long productBId;
        long batchAId;
        long batchBId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productAId = AddProduct(seed, importId, code: "A").Id;
            productBId = AddProduct(seed, importId, code: "B").Id;
            batchAId = AddBatch(seed, productAId, importId, BusinessDate.AddDays(10), 0);
            batchBId = AddBatch(seed, productBId, importId, BusinessDate.AddDays(10), 0);
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                Request(importId, Group(productAId, New(batchBId, 0, 2)))));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                new PostImportLifecycleRequest(
                    importId,
                    BusinessDate,
                    OccurredAtUtc,
                    [
                        Group(productAId, New(batchAId, 0, 2)),
                        Group(productAId, New(batchBId, 0, 2))
                    ])));
        }

        using (var update = database.Open())
        {
            update.Batches.Single(batch => batch.Id == batchAId).LastSeenImportId = null;
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                Request(importId, Group(productAId, New(batchAId, 0, 2)))));
        }

        using var verify = database.Open();
        Assert.Empty(verify.Tasks);
        Assert.Equal(2, verify.Batches.Count());
    }

    [Fact]
    public void RecoveryRequiresCurrentProductGenerationAndVersionOverflowIsRejected()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            productId = AddProduct(seed, importId, generation: 2).Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(10),
                lifecycleGeneration: 1,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                currentArrivalQty: 10,
                maxArrivalQty: 10);
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, 0, 1)))));
        }

        using (var update = database.Open())
        {
            var batch = update.Batches.Single(candidate => candidate.Id == batchId);
            batch.LifecycleGeneration = 2;
            batch.AttentionVersion = int.MaxValue;
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<OverflowException>(() => Execute(
                context,
                Request(importId, Group(productId, Existing(batchId, 8, 10, int.MaxValue, 0)))));
        }

        using var verify = database.Open();
        var batchAfter = verify.Batches.Single(candidate => candidate.Id == batchId);
        Assert.Equal(int.MaxValue, batchAfter.AttentionVersion);
        Assert.Equal("stopped", batchAfter.TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void HandledVersionInventoryGenerationAndIdentityFieldsRemainUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long batchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            var product = AddProduct(seed, importId, generation: 7);
            productId = product.Id;
            batchId = AddBatch(
                seed,
                productId,
                importId,
                BusinessDate.AddDays(30),
                lifecycleGeneration: 7,
                currentArrivalQty: 10,
                maxArrivalQty: 10,
                stage: ExpiryStageCalculator.None,
                attentionVersion: 4);
            seed.Batches.Single().HandledAttentionVersion = 3;
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Execute(context, Request(importId, Group(productId, Existing(batchId, 8, 10, 4, 5))));
        }

        using var verify = database.Open();
        var productAfter = verify.Products.Single();
        var batchAfter = verify.Batches.Single();
        Assert.Equal(7, productAfter.LifecycleGeneration);
        Assert.False(productAfter.IsStockZeroTerminated);
        Assert.Equal(5, batchAfter.AttentionVersion);
        Assert.Equal(3, batchAfter.HandledAttentionVersion);
        Assert.Equal(7, batchAfter.LifecycleGeneration);
        Assert.Equal(10, batchAfter.CurrentArrivalQty);
        Assert.Equal(10, batchAfter.MaxArrivalQty);
        Assert.Null(verify.Inspections.SingleOrDefault());
    }

    [Fact]
    public void ForeignKeyAndCheckConstraintsStillRejectInvalidLifecycleEvent()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Throws<DbUpdateException>(() =>
        {
            context.LifecycleEvents.Add(new LifecycleEvent
            {
                ProductId = 999,
                EventType = "batch_tracking_resumed",
                Reason = "reason",
                OccurredAtUtc = OccurredAtUtc
            });
            context.SaveChanges();
        });
        context.ChangeTracker.Clear();
        Assert.Empty(context.LifecycleEvents);
    }

    private static PostImportLifecycleResult Execute(
        StoreDbContext context,
        PostImportLifecycleRequest request) =>
        new PostImportLifecycleUseCase().Execute(context, request);

    private static PostImportLifecycleRequest Request(
        long importId,
        params PostImportProductGroup[] groups) =>
        new(importId, BusinessDate, OccurredAtUtc, groups);

    private static PostImportProductGroup Group(
        long productId,
        params PostImportBatchFact[] facts) =>
        new(productId, facts);

    private static PostImportBatchFact New(
        long batchId,
        int previousMaxArrivalQty,
        int currentArrivalQty) =>
        new(
            batchId,
            PostImportBatchFactKinds.New,
            previousMaxArrivalQty,
            currentArrivalQty);

    private static PostImportBatchFact Existing(
        long batchId,
        int previousMaxArrivalQty,
        int currentArrivalQty,
        int expectedAttentionVersion,
        int targetAttentionVersion) =>
        new(
            batchId,
            PostImportBatchFactKinds.Existing,
            previousMaxArrivalQty,
            currentArrivalQty,
            expectedAttentionVersion,
            targetAttentionVersion);

    private static ImportRecord AddImport(
        StoreDbContext context,
        string status = ImportStatuses.Succeeded,
        bool undone = false)
    {
        var import = new ImportRecord
        {
            SourceFileName = "source.xlsx",
            SourceFileSha256 = new string('a', 64),
            ParsedAtUtc = SeedUtc,
            ConfirmedAtUtc = SeedUtc,
            Status = status,
            IsUndone = undone,
            UndoneAtUtc = undone ? OccurredAtUtc : null
        };
        context.Imports.Add(import);
        context.SaveChanges();
        return import;
    }

    private static Product AddProduct(
        StoreDbContext context,
        long importId,
        string code = "P",
        int stock = 5,
        int generation = 0,
        bool terminated = false)
    {
        var product = new Product
        {
            ProductCode = code,
            CurrentName = code,
            CurrentBarcode = "barcode-" + code,
            ExcelStockQty = stock,
            EffectiveStockQty = stock,
            EffectiveStockSource = "excel",
            LifecycleGeneration = generation,
            IsStockZeroTerminated = terminated,
            LastSeenImportId = importId,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Products.Add(product);
        context.SaveChanges();
        if (!context.ScopeBaselines.Any())
        {
            context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = importId, BusinessDate = BusinessDate, IsCompleted = true, CompletedAtUtc = SeedUtc });
            context.SaveChanges();
        }
        return product;
    }

    private static long AddBatch(
        StoreDbContext context,
        long productId,
        long importId,
        DateOnly expiryDate,
        int lifecycleGeneration = 0,
        string trackingStatus = "active",
        string? stopReason = null,
        DateTime? stoppedAtUtc = null,
        int currentArrivalQty = 2,
        int maxArrivalQty = 2,
        string stage = ExpiryStageCalculator.None,
        int attentionVersion = 0)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 1, 1).AddDays((int)context.Batches.LongCount()),
            ExpiryDate = expiryDate,
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = currentArrivalQty,
            MaxArrivalQty = maxArrivalQty,
            SourceDiscountReference = "source",
            LifecycleGeneration = lifecycleGeneration,
            TrackingStatus = trackingStatus,
            StopReason = stopReason,
            StoppedAtUtc = stoppedAtUtc,
            CurrentStage = stage,
            NextTriggerDate = null,
            AttentionVersion = attentionVersion,
            HandledAttentionVersion = attentionVersion,
            LastSeenImportId = importId,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch.Id;
    }
}
