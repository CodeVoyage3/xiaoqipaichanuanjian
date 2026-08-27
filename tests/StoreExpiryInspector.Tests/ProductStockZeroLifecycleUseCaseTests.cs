using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ProductStockZeroLifecycleUseCaseTests
{
    private static readonly DateTime SeedUtc = new(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OccurredAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ZeroProductWithoutTaskStopsEveryHistoricalBatchAndWritesProductEvent()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-BATCHES", generation: 4);
            productId = product.Id;
            AddBatch(seed, productId, "none", DateOnly.MaxValue, lifecycleGeneration: 4);
            AddBatch(seed, productId, ExpiryStageCalculator.Discount20, DateOnly.MaxValue, lifecycleGeneration: 4);
            AddBatch(
                seed,
                productId,
                ExpiryStageCalculator.Withdraw,
                null,
                lifecycleGeneration: 2,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId);

            Assert.True(result.ProductTerminated);
            Assert.Equal(3, result.StoppedBatchCount);
            Assert.False(result.TaskClosed);
            Assert.False(result.DraftInvalidated);
            Assert.Equal(1, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var productAfter = verify.Products.AsNoTracking().Single();
        Assert.True(productAfter.IsStockZeroTerminated);
        Assert.Equal(5, productAfter.LifecycleGeneration);
        Assert.Equal(OccurredAtUtc, productAfter.UpdatedAtUtc);
        var batches = verify.Batches.AsNoTracking().OrderBy(batch => batch.Id).ToArray();
        Assert.Equal(3, batches.Length);
        Assert.All(batches, batch =>
        {
            Assert.Equal("stopped", batch.TrackingStatus);
            Assert.Equal("product_stock_zero", batch.StopReason);
            Assert.Equal(OccurredAtUtc, batch.StoppedAtUtc);
            Assert.Null(batch.NextTriggerDate);
            Assert.Equal(OccurredAtUtc, batch.UpdatedAtUtc);
        });
        Assert.Equal(
            new[] { "none", ExpiryStageCalculator.Discount20, ExpiryStageCalculator.Withdraw },
            batches.Select(batch => batch.CurrentStage));
        Assert.Equal(new[] { 4, 4, 2 }, batches.Select(batch => batch.LifecycleGeneration));
        Assert.Single(verify.LifecycleEvents);
        var lifecycleEvent = verify.LifecycleEvents.Single();
        Assert.Equal(productId, lifecycleEvent.ProductId);
        Assert.Null(lifecycleEvent.BatchId);
        Assert.Equal("product_stock_zero", lifecycleEvent.EventType);
        Assert.Equal("product_stock_zero", lifecycleEvent.Reason);
        Assert.Equal(OccurredAtUtc, lifecycleEvent.OccurredAtUtc);
        Assert.Null(lifecycleEvent.SourceImportId);
        Assert.Null(lifecycleEvent.SourceAdjustmentId);
    }

    [Fact]
    public void OpenTaskIsSystemClosedAndItsDraftIsInvalidatedInPlace()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long taskItemId;
        long draftItemId;
        long adjustmentId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-TASK");
            productId = product.Id;
            var batch = AddBatch(seed, productId, ExpiryStageCalculator.Discount20, DateOnly.MaxValue);
            var task = AddTask(seed, productId, ExpiryStageCalculator.Discount20);
            var taskItem = AddTaskItem(seed, task.Id, batch.Id, productId);
            taskItemId = taskItem.Id;
            var draft = AddDraft(seed, task.Id, "张三");
            var draftItem = new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = taskItem.Id,
                TaskId = task.Id,
                CheckedQty = 7,
                ConfirmedAttentionVersion = 0
            };
            seed.DraftItems.Add(draftItem);
            seed.SaveChanges();
            draftItemId = draftItem.Id;
            var adjustment = new InventoryAdjustment
            {
                ProductId = productId,
                ExcelStockQtySnapshot = 3,
                AdjustedStockQty = 0,
                AdjustedAtUtc = OccurredAtUtc
            };
            seed.InventoryAdjustments.Add(adjustment);
            seed.SaveChanges();
            adjustmentId = adjustment.Id;
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId, sourceAdjustmentId: adjustmentId);
            Assert.True(result.TaskClosed);
            Assert.True(result.DraftInvalidated);
            Assert.Equal(3, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var taskAfter = verify.Tasks.Single();
        Assert.Equal("system_closed", taskAfter.Status);
        Assert.Equal("product_stock_zero", taskAfter.CloseReason);
        Assert.Equal(OccurredAtUtc, taskAfter.ClosedAtUtc);
        Assert.Equal(OccurredAtUtc, taskAfter.UpdatedAtUtc);
        Assert.Single(verify.TaskItems);
        Assert.Equal(taskItemId, verify.TaskItems.Single().Id);
        var draftAfter = verify.Drafts.Single();
        Assert.True(draftAfter.IsInvalid);
        Assert.Equal("product_stock_zero", draftAfter.InvalidReason);
        Assert.Equal(OccurredAtUtc, draftAfter.InvalidatedAtUtc);
        Assert.Equal("张三", draftAfter.InspectorName);
        Assert.Single(verify.DraftItems);
        Assert.Equal(draftItemId, verify.DraftItems.Single().Id);
        Assert.Equal(7, verify.DraftItems.Single().CheckedQty);
        Assert.Equal(
            new[] { "product_stock_zero", "task_auto_closed", "draft_invalidated" },
            verify.LifecycleEvents.OrderBy(item => item.Id).Select(item => item.EventType));
        Assert.All(verify.LifecycleEvents, item =>
        {
            Assert.Equal(productId, item.ProductId);
            Assert.Null(item.BatchId);
            Assert.Equal("product_stock_zero", item.Reason);
            Assert.Equal(adjustmentId, item.SourceAdjustmentId);
            Assert.Null(item.SourceImportId);
            Assert.Null(item.SourceInspectionId);
        });
    }

    [Fact]
    public void OpenTaskWithoutDraftDoesNotCreateDraftInvalidationEvent()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-NO-DRAFT");
            productId = product.Id;
            var batch = AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue);
            var task = AddTask(seed, productId, ExpiryStageCalculator.Withdraw);
            AddTaskItem(seed, task.Id, batch.Id, productId);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId);
            Assert.True(result.TaskClosed);
            Assert.False(result.DraftInvalidated);
            Assert.Equal(2, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        Assert.Empty(verify.Drafts);
        Assert.Equal(
            new[] { "product_stock_zero", "task_auto_closed" },
            verify.LifecycleEvents.OrderBy(item => item.Id).Select(item => item.EventType));
    }

    [Fact]
    public void ClosedTaskAndInvalidDraftAreNotRewritten()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        var originalClosedAtUtc = SeedUtc.AddHours(-2);
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-HISTORY");
            productId = product.Id;
            var task = AddTask(seed, productId, ExpiryStageCalculator.Expired, "completed");
            task.ClosedAtUtc = originalClosedAtUtc;
            task.UpdatedAtUtc = originalClosedAtUtc;
            seed.SaveChanges();
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "历史人员",
                IsInvalid = true,
                InvalidReason = "earlier_reason",
                InvalidatedAtUtc = originalClosedAtUtc,
                CreatedAtUtc = originalClosedAtUtc,
                UpdatedAtUtc = originalClosedAtUtc
            };
            seed.Drafts.Add(draft);
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId);
            Assert.False(result.TaskClosed);
            Assert.False(result.DraftInvalidated);
            Assert.Equal(1, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var taskAfter = verify.Tasks.Single();
        Assert.Equal("completed", taskAfter.Status);
        Assert.Equal("completed", taskAfter.CloseReason);
        Assert.Equal(originalClosedAtUtc, taskAfter.ClosedAtUtc);
        var draftAfter = verify.Drafts.Single();
        Assert.True(draftAfter.IsInvalid);
        Assert.Equal("earlier_reason", draftAfter.InvalidReason);
        Assert.Equal(originalClosedAtUtc, draftAfter.InvalidatedAtUtc);
        Assert.Equal(new[] { "product_stock_zero" }, verify.LifecycleEvents.Select(item => item.EventType));
    }

    [Fact]
    public void ProductIsolationLeavesOtherProductFieldsAndHistoryUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        long firstProductId;
        long secondProductId;
        long thirdProductId;
        ProductSnapshot secondProductBefore;
        ProductSnapshot thirdProductBefore;
        BatchSnapshot secondBatchBefore;
        BatchSnapshot thirdBatchBefore;
        using (var seed = database.Open())
        {
            var first = AddProduct(seed, "SKU-ZERO-A");
            firstProductId = first.Id;
            var firstBatch = AddBatch(seed, first.Id, ExpiryStageCalculator.Discount50, DateOnly.MaxValue);
            var firstTask = AddTask(seed, first.Id, ExpiryStageCalculator.Discount50);
            AddTaskItem(seed, firstTask.Id, firstBatch.Id, first.Id);

            var second = AddProduct(seed, "SKU-ZERO-B", stock: 4, generation: 7);
            secondProductId = second.Id;
            secondProductBefore = Snapshot(second);
            var secondBatch = AddBatch(
                seed,
                second.Id,
                ExpiryStageCalculator.None,
                DateOnly.MaxValue,
                lifecycleGeneration: 7,
                attentionVersion: 3);
            secondBatchBefore = Snapshot(secondBatch);

            var third = AddProduct(seed, "SKU-ZERO-C", stock: 9, generation: 2);
            thirdProductId = third.Id;
            thirdProductBefore = Snapshot(third);
            var thirdBatch = AddBatch(
                seed,
                third.Id,
                ExpiryStageCalculator.Expired,
                null,
                lifecycleGeneration: 1,
                trackingStatus: "stopped",
                stopReason: "manual_stop",
                stoppedAtUtc: SeedUtc,
                attentionVersion: 4);
            thirdBatchBefore = Snapshot(thirdBatch);
        }

        using (var context = database.Open())
        {
            Execute(context, firstProductId);
        }

        using var verify = database.Open();
        Assert.Equal(secondProductBefore, Snapshot(verify.Products.AsNoTracking().Single(product => product.Id == secondProductId)));
        Assert.Equal(thirdProductBefore, Snapshot(verify.Products.AsNoTracking().Single(product => product.Id == thirdProductId)));
        Assert.Empty(verify.LifecycleEvents.Where(item => item.ProductId == secondProductId));
        Assert.Empty(verify.LifecycleEvents.Where(item => item.ProductId == thirdProductId));
        Assert.Equal(secondBatchBefore, Snapshot(verify.Batches.AsNoTracking().Single(batch => batch.ProductId == secondProductId)));
        Assert.Equal(thirdBatchBefore, Snapshot(verify.Batches.AsNoTracking().Single(batch => batch.ProductId == thirdProductId)));
        Assert.Single(verify.Tasks);
        Assert.Equal(firstProductId, verify.Tasks.Single().ProductId);
    }

    [Fact]
    public void StartupRecalculationNoLongerMatchesStoppedProductBatches()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-STARTUP").Id;
            AddBatch(seed, productId, ExpiryStageCalculator.None, new DateOnly(2026, 8, 27));
        }

        using (var context = database.Open())
        {
            Execute(context, productId);
        }

        using (var context = database.Open())
        {
            var result = new StartupRecalculationUseCase().Execute(
                context,
                new StartupRecalculationRequest(new DateOnly(2026, 8, 27), OccurredAtUtc.AddHours(1)));
            Assert.Equal(0, result.MatchedBatchCount);
            Assert.Equal(0, result.AggregatedBatchCount);
        }

        using var verify = database.Open();
        Assert.Empty(verify.Tasks);
        Assert.Equal("stopped", verify.Batches.Single().TrackingStatus);
        Assert.Null(verify.Batches.Single().NextTriggerDate);
    }

    [Fact]
    public void RepeatingTheSameRequestIsIdempotent()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-IDEMPOTENT").Id;
            AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue);
        }

        using (var context = database.Open())
        {
            var first = Execute(context, productId);
            Assert.True(first.ProductTerminated);
        }

        ProductSnapshot productBefore;
        BatchSnapshot batchBefore;
        using (var context = database.Open())
        {
            productBefore = Snapshot(context.Products.AsNoTracking().Single());
            batchBefore = Snapshot(context.Batches.AsNoTracking().Single());
        }

        using (var context = database.Open())
        {
            var second = Execute(context, productId);
            Assert.False(second.ProductTerminated);
            Assert.Equal(0, second.StoppedBatchCount);
            Assert.False(second.TaskClosed);
            Assert.False(second.DraftInvalidated);
            Assert.Equal(0, second.LifecycleEventCount);
        }

        using var verify = database.Open();
        Assert.Equal(productBefore, Snapshot(verify.Products.AsNoTracking().Single()));
        Assert.Equal(batchBefore, Snapshot(verify.Batches.AsNoTracking().Single()));
        Assert.Single(verify.LifecycleEvents);
    }

    [Fact]
    public void RepeatingWithLaterTimestampLeavesCompleteBatchTerminalStateUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-LATER-TIMESTAMP", generation: 6).Id;
            AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue, lifecycleGeneration: 6);
        }

        using (var context = database.Open())
        {
            Execute(context, productId);
        }

        ProductSnapshot productBefore;
        BatchSnapshot batchBefore;
        using (var context = database.Open())
        {
            productBefore = Snapshot(context.Products.AsNoTracking().Single());
            batchBefore = Snapshot(context.Batches.AsNoTracking().Single());
        }

        var laterOccurredAtUtc = OccurredAtUtc.AddDays(1);
        using (var context = database.Open())
        {
            var result = ExecuteAt(context, productId, laterOccurredAtUtc);
            Assert.False(result.ProductTerminated);
            Assert.Equal(0, result.StoppedBatchCount);
            Assert.Equal(0, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        Assert.Equal(productBefore, Snapshot(verify.Products.AsNoTracking().Single()));
        Assert.Equal(batchBefore, Snapshot(verify.Batches.AsNoTracking().Single()));
        Assert.Single(verify.LifecycleEvents);
    }

    [Fact]
    public void AlreadyTerminatedProductRepairsErroneousNewBatchTaskAndDraftWithoutNewProductEvent()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-REPAIR", generation: 8, terminated: true);
            productId = product.Id;
            var batch = AddBatch(seed, productId, ExpiryStageCalculator.Discount50, DateOnly.MaxValue, lifecycleGeneration: 8);
            var task = AddTask(seed, productId, ExpiryStageCalculator.Discount50);
            var item = AddTaskItem(seed, task.Id, batch.Id, productId);
            var draft = AddDraft(seed, task.Id, "修复人员");
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = item.Id,
                TaskId = task.Id,
                CheckedQty = 3
            });
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId);
            Assert.False(result.ProductTerminated);
            Assert.Equal(1, result.StoppedBatchCount);
            Assert.True(result.TaskClosed);
            Assert.True(result.DraftInvalidated);
            Assert.Equal(2, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        Assert.Equal(8, verify.Products.Single().LifecycleGeneration);
        Assert.Equal(new[] { "task_auto_closed", "draft_invalidated" },
            verify.LifecycleEvents.OrderBy(item => item.Id).Select(item => item.EventType));
        Assert.DoesNotContain(verify.LifecycleEvents, item => item.EventType == "product_stock_zero");
        Assert.Equal("product_stock_zero", verify.Batches.Single().StopReason);
        Assert.Equal("system_closed", verify.Tasks.Single().Status);
        Assert.True(verify.Drafts.Single().IsInvalid);
    }

    [Fact]
    public void RestoringStockDoesNotReviveHistoricalBatchOrCreateTask()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-RESTORE").Id;
            AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue);
        }

        using (var context = database.Open())
        {
            Execute(context, productId);
            var product = context.Products.Single();
            product.EffectiveStockQty = 5;
            product.ExcelStockQty = 5;
            context.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = new StartupRecalculationUseCase().Execute(
                context,
                new StartupRecalculationRequest(new DateOnly(2026, 8, 27), OccurredAtUtc.AddHours(1)));
            Assert.Equal(0, result.MatchedBatchCount);
        }

        using var verify = database.Open();
        Assert.Equal(5, verify.Products.Single().EffectiveStockQty);
        Assert.True(verify.Products.Single().IsStockZeroTerminated);
        Assert.Equal(1, verify.Products.Single().LifecycleGeneration);
        Assert.Equal("stopped", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.Tasks);
    }

    [Fact]
    public void ProductWithoutBatchesStillEndsItsLifecycle()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-NO-BATCH", generation: 11).Id;
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId);
            Assert.True(result.ProductTerminated);
            Assert.Equal(0, result.StoppedBatchCount);
            Assert.Equal(1, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var product = verify.Products.Single();
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal(12, product.LifecycleGeneration);
        Assert.Single(verify.LifecycleEvents);
    }

    [Fact]
    public void NonZeroProductIsRejectedBeforeAnyLifecycleWrite()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-NONZERO", stock: 2).Id;
            AddBatch(seed, productId, ExpiryStageCalculator.None, DateOnly.MaxValue);
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(context, productId));
            Assert.Empty(context.LifecycleEvents);
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single().IsStockZeroTerminated);
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void InvalidIdentityTimestampAndSourceShapeAreRejectedBeforeWrites()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-VALIDATION").Id;
        }

        using (var context = database.Open())
        {
            var useCase = new ProductStockZeroLifecycleUseCase();
            Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(0, OccurredAtUtc)));
            Assert.Throws<ArgumentException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(productId, DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Unspecified))));
            Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(productId, OccurredAtUtc, SourceImportId: 0)));
            Assert.Throws<ArgumentException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(productId, OccurredAtUtc, SourceImportId: 1, SourceAdjustmentId: 1)));
            Assert.Throws<KeyNotFoundException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(999, OccurredAtUtc)));
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single().IsStockZeroTerminated);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void ImportSourceMustExistAndAdjustmentSourceMustBelongToProduct()
    {
        using var database = SqliteTestDatabase.Create();
        long firstProductId;
        long secondProductId;
        long otherAdjustmentId;
        using (var seed = database.Open())
        {
            firstProductId = AddProduct(seed, "SKU-ZERO-SOURCE-A").Id;
            secondProductId = AddProduct(seed, "SKU-ZERO-SOURCE-B").Id;
            var otherAdjustment = new InventoryAdjustment
            {
                ProductId = secondProductId,
                ExcelStockQtySnapshot = 4,
                AdjustedStockQty = 0,
                AdjustedAtUtc = OccurredAtUtc
            };
            seed.InventoryAdjustments.Add(otherAdjustment);
            seed.SaveChanges();
            otherAdjustmentId = otherAdjustment.Id;
        }

        using (var context = database.Open())
        {
            Assert.Throws<KeyNotFoundException>(() => Execute(context, firstProductId, sourceImportId: 999));
            Assert.Throws<ArgumentException>(() => Execute(context, firstProductId, sourceAdjustmentId: otherAdjustmentId));
            Assert.False(context.Products.Single(product => product.Id == firstProductId).IsStockZeroTerminated);
            Assert.Empty(context.LifecycleEvents);
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single(product => product.Id == firstProductId).IsStockZeroTerminated);
        Assert.False(verify.Products.Single(product => product.Id == secondProductId).IsStockZeroTerminated);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void NonZeroAdjustmentSourceIsRejectedBeforeAnyLifecycleWrite()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long adjustmentId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-SOURCE-NONZERO").Id;
            var adjustment = new InventoryAdjustment
            {
                ProductId = productId,
                ExcelStockQtySnapshot = 4,
                AdjustedStockQty = 2,
                AdjustedAtUtc = OccurredAtUtc
            };
            seed.InventoryAdjustments.Add(adjustment);
            seed.SaveChanges();
            adjustmentId = adjustment.Id;
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                productId,
                sourceAdjustmentId: adjustmentId));
            Assert.False(context.Products.Single().IsStockZeroTerminated);
            Assert.Empty(context.LifecycleEvents);
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single().IsStockZeroTerminated);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void ExistingTaskDraftAndLifecycleEventConstraintsRejectIllegalRows()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long batchId;
        long otherBatchId;
        long taskId;
        long draftId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-CONSTRAINTS");
            productId = product.Id;
            batchId = AddBatch(seed, productId, ExpiryStageCalculator.Discount50, DateOnly.MaxValue).Id;
            var otherProduct = AddProduct(seed, "SKU-ZERO-CONSTRAINTS-OTHER", stock: 1);
            otherBatchId = AddBatch(seed, otherProduct.Id, ExpiryStageCalculator.Discount50, DateOnly.MaxValue).Id;
            taskId = AddTask(seed, productId, ExpiryStageCalculator.Discount50).Id;
            draftId = AddDraft(seed, taskId, "约束测试").Id;
        }

        using var context = database.Open();
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated($"""
            UPDATE tasks
            SET status = 'system_closed', closed_at_utc = NULL, close_reason = NULL
            WHERE id = {taskId};
            """));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated($"""
            UPDATE drafts
            SET is_invalid = 1, invalid_reason = NULL, invalidated_at_utc = NULL
            WHERE id = {draftId};
            """));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO lifecycle_events
                (product_id, event_type, reason, occurred_at_utc)
            VALUES ({productId}, 'unknown_type', 'product_stock_zero', {OccurredAtUtc});
            """));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO lifecycle_events
                (product_id, event_type, reason, occurred_at_utc)
            VALUES ({productId}, 'product_stock_zero', ' ', {OccurredAtUtc});
            """));
        Assert.Throws<SqliteException>(() => context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO lifecycle_events
                (product_id, batch_id, event_type, reason, occurred_at_utc)
            VALUES ({productId}, {otherBatchId}, 'batch_checked_zero', 'wrong product batch', {OccurredAtUtc});
            """));

        Assert.Equal("open", context.Tasks.AsNoTracking().Single().Status);
        Assert.False(context.Drafts.AsNoTracking().Single().IsInvalid);
        Assert.Empty(context.LifecycleEvents);
        Assert.Equal(productId, context.Batches.AsNoTracking().Single(batch => batch.Id == batchId).ProductId);
        Assert.Equal(2, context.Batches.AsNoTracking().Count());
    }

    [Fact]
    public void LifecycleGenerationOverflowRejectsWholeOperation()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-OVERFLOW", generation: int.MaxValue).Id;
            AddBatch(seed, productId, ExpiryStageCalculator.Discount50, DateOnly.MaxValue);
        }

        using (var context = database.Open())
        {
            Assert.Throws<OverflowException>(() => Execute(context, productId));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        var product = verify.Products.Single();
        Assert.False(product.IsStockZeroTerminated);
        Assert.Equal(int.MaxValue, product.LifecycleGeneration);
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void SQLiteFailureRollsBackProductBatchesTaskDraftAndEvents()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-ROLLBACK");
            productId = product.Id;
            var batch = AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue);
            var task = AddTask(seed, productId, ExpiryStageCalculator.Withdraw);
            var item = AddTaskItem(seed, task.Id, batch.Id, productId);
            var draft = AddDraft(seed, task.Id, "回滚人员");
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = item.Id,
                TaskId = task.Id,
                CheckedQty = 4
            });
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t04_event
                BEFORE INSERT ON lifecycle_events
                BEGIN
                    SELECT RAISE(ABORT, 'forced lifecycle event failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(context, productId));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single().IsStockZeroTerminated);
        Assert.Equal(0, verify.Products.Single().LifecycleGeneration);
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.Batches.Single().CurrentStage);
        Assert.Equal("open", verify.Tasks.Single().Status);
        Assert.False(verify.Drafts.Single().IsInvalid);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void ExistingOuterTransactionCanRollbackTheWholeLifecycle()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-OUTER").Id;
            AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue);
        }

        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            var result = Execute(context, productId);
            Assert.True(result.ProductTerminated);
            Assert.Equal("stopped", context.Batches.Single().TrackingStatus);
            Assert.Single(context.LifecycleEvents);
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single().IsStockZeroTerminated);
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void ExistingOuterTransactionKeepsTransactionForCallerAfterFailure()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-OUTER-FAILURE").Id;
            AddBatch(seed, productId, ExpiryStageCalculator.Withdraw, DateOnly.MaxValue);
        }

        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t04_outer_event
                BEFORE INSERT ON lifecycle_events
                BEGIN
                    SELECT RAISE(ABORT, 'forced outer lifecycle event failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(context, productId));
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.False(context.ChangeTracker.HasChanges());
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.False(verify.Products.Single().IsStockZeroTerminated);
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void ExistingHistoryAndBatchIdentityFieldsRemainUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long batchId;
        long inspectionId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "SKU-ZERO-HISTORY-FIELDS", generation: 3);
            productId = product.Id;
            var batch = AddBatch(
                seed,
                productId,
                ExpiryStageCalculator.Discount20,
                new DateOnly(2026, 8, 28),
                lifecycleGeneration: 2,
                attentionVersion: 5);
            batchId = batch.Id;
            var task = AddTask(seed, productId, ExpiryStageCalculator.Discount20, "done");
            task.Status = "completed";
            task.ClosedAtUtc = SeedUtc;
            seed.SaveChanges();
            var inspection = new Inspection
            {
                TaskId = task.Id,
                ProductId = productId,
                ProductCodeSnapshot = product.ProductCode,
                StageSnapshot = ExpiryStageCalculator.Discount20,
                StockQtySnapshot = 4,
                InspectorName = "正式记录",
                CheckDate = new DateOnly(2026, 8, 26),
                SubmittedAtUtc = SeedUtc
            };
            seed.Inspections.Add(inspection);
            seed.SaveChanges();
            inspectionId = inspection.Id;
        }

        using (var context = database.Open())
        {
            Execute(context, productId);
        }

        using var verify = database.Open();
        Assert.Equal(inspectionId, verify.Inspections.Single().Id);
        var batchAfter = verify.Batches.Single(candidate => candidate.Id == batchId);
        Assert.Equal(2, batchAfter.LifecycleGeneration);
        Assert.Equal(5, batchAfter.AttentionVersion);
        Assert.Equal(ExpiryStageCalculator.Discount20, batchAfter.CurrentStage);
        Assert.Equal(7, batchAfter.CurrentArrivalQty);
        Assert.Equal(9, batchAfter.MaxArrivalQty);
        Assert.Equal("source", batchAfter.SourceDiscountReference);
        Assert.Null(batchAfter.NextTriggerDate);
        Assert.Equal("product_stock_zero", batchAfter.StopReason);
        Assert.Single(verify.Inspections);
    }

    [Fact]
    public void ValidImportSourceIsCopiedToEachActualLifecycleEvent()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long importId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "SKU-ZERO-IMPORT-SOURCE").Id;
            var import = new ImportRecord
            {
                SourceFileName = "zero.xlsx",
                SourceFileSha256 = new string('a', 64),
                ParsedAtUtc = SeedUtc,
                Status = "confirmed"
            };
            seed.Imports.Add(import);
            seed.SaveChanges();
            importId = import.Id;
            var batch = AddBatch(seed, productId, ExpiryStageCalculator.Discount50, DateOnly.MaxValue);
            var task = AddTask(seed, productId, ExpiryStageCalculator.Discount50);
            AddTaskItem(seed, task.Id, batch.Id, productId);
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId, sourceImportId: importId);
            Assert.Equal(2, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        Assert.All(verify.LifecycleEvents, item =>
        {
            Assert.Equal(importId, item.SourceImportId);
            Assert.Null(item.SourceAdjustmentId);
            Assert.Null(item.SourceInspectionId);
        });
    }

    private static ProductStockZeroResult Execute(
        StoreDbContext context,
        long productId,
        long? sourceImportId = null,
        long? sourceAdjustmentId = null) =>
        ExecuteAt(context, productId, OccurredAtUtc, sourceImportId, sourceAdjustmentId);

    private static ProductStockZeroResult ExecuteAt(
        StoreDbContext context,
        long productId,
        DateTime occurredAtUtc,
        long? sourceImportId = null,
        long? sourceAdjustmentId = null) =>
        new ProductStockZeroLifecycleUseCase().Execute(
            context,
            new ProductStockZeroRequest(
                productId,
                occurredAtUtc,
                sourceImportId,
                sourceAdjustmentId));

    private static Product AddProduct(
        StoreDbContext context,
        string code,
        int stock = 0,
        int generation = 0,
        bool terminated = false)
    {
        var product = new Product
        {
            ProductCode = code,
            ExcelStockQty = stock,
            EffectiveStockQty = stock,
            EffectiveStockSource = "manual",
            LifecycleGeneration = generation,
            IsStockZeroTerminated = terminated,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(
        StoreDbContext context,
        long productId,
        string stage,
        DateOnly? nextTriggerDate,
        int lifecycleGeneration = 0,
        string trackingStatus = "active",
        string? stopReason = null,
        DateTime? stoppedAtUtc = null,
        int attentionVersion = 0)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 1, 1).AddDays((int)context.Batches.LongCount()),
            ExpiryDate = new DateOnly(2026, 12, 31).AddDays((int)context.Batches.LongCount()),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 7,
            MaxArrivalQty = 9,
            SourceDiscountReference = "source",
            LifecycleGeneration = lifecycleGeneration,
            TrackingStatus = trackingStatus,
            StopReason = stopReason,
            StoppedAtUtc = stoppedAtUtc,
            CurrentStage = stage,
            NextTriggerDate = nextTriggerDate,
            AttentionVersion = attentionVersion,
            HandledAttentionVersion = attentionVersion,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static ProductTask AddTask(
        StoreDbContext context,
        long productId,
        string highestStage,
        string? closeReason = null)
    {
        var task = new ProductTask
        {
            ProductId = productId,
            Status = closeReason is null ? "open" : "completed",
            HighestStage = highestStage,
            ClosedAtUtc = closeReason is null ? null : SeedUtc,
            CloseReason = closeReason,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();
        return task;
    }

    private static ProductTaskItem AddTaskItem(
        StoreDbContext context,
        long taskId,
        long batchId,
        long productId)
    {
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
        context.TaskItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static InspectionDraft AddDraft(StoreDbContext context, long taskId, string inspectorName)
    {
        var draft = new InspectionDraft
        {
            TaskId = taskId,
            InspectorName = inspectorName,
            CheckDate = new DateOnly(2026, 8, 27),
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Drafts.Add(draft);
        context.SaveChanges();
        return draft;
    }

    private static ProductSnapshot Snapshot(Product product) => new(
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
        product.UpdatedAtUtc);

    private static BatchSnapshot Snapshot(Batch batch) => new(
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
        batch.UpdatedAtUtc);

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
}
