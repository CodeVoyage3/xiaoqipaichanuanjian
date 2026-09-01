using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ManualInventoryAdjustmentUseCaseTests
{
    private static readonly DateTime SeedUtc = new(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AdjustedAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly BusinessDate = new(2026, 8, 27);
    private static readonly string[] Headers =
    [
        "商品大类", "商品编码", "商品条码", "商品名称", "生产日期", "有效日期", "保质期",
        "保质期单位", "是否该做临期折扣", "该批次累计到货数量", "该商品门店库存总数"
    ];

    [Fact]
    public void PositiveAdjustmentCreatesHistoryAndUpdatesEffectiveStock()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-POSITIVE", excelStock: 10, effectiveStock: 10).Id;
        }

        ManualInventoryAdjustmentResult result;
        using (var context = database.Open())
        {
            result = Execute(context, productId, 8);
        }

        using var verify = database.Open();
        var adjustment = Assert.Single(verify.InventoryAdjustments.AsNoTracking());
        var product = Assert.Single(verify.Products.AsNoTracking());
        Assert.True(result.Changed);
        Assert.False(result.NoChange);
        Assert.Equal(10, result.PreviousEffectiveStockQty);
        Assert.Equal(8, result.CorrectedStockQty);
        Assert.Equal(adjustment.Id, result.AdjustmentId!.Value);
        Assert.Equal(productId, adjustment.ProductId);
        Assert.Equal(10, adjustment.ExcelStockQtySnapshot);
        Assert.Equal(8, adjustment.AdjustedStockQty);
        Assert.Equal(AdjustedAtUtc, adjustment.AdjustedAtUtc);
        Assert.Equal(8, product.EffectiveStockQty);
        Assert.Equal("manual", product.EffectiveStockSource);
        Assert.Equal(AdjustedAtUtc, product.UpdatedAtUtc);
        Assert.Equal(10, product.ExcelStockQty);
        Assert.False(result.ProductTerminated);
    }

    [Fact]
    public void PositiveAdjustmentLeavesBatchFieldsUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        BatchSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-BATCH-UNCHANGED", 10, 10).Id;
            var batch = AddBatch(seed, productId);
            before = Snapshot(batch);
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 8);
        }

        using var verify = database.Open();
        Assert.Equal(before, Snapshot(Assert.Single(verify.Batches.AsNoTracking())));
    }

    [Fact]
    public void PositiveAdjustmentLeavesTaskAndTaskItemUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        TaskSnapshot beforeTask;
        TaskItemSnapshot beforeItem;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-TASK-UNCHANGED", 10, 10).Id;
            var batch = AddBatch(seed, productId);
            var task = AddTask(seed, productId);
            var item = AddTaskItem(seed, task.Id, batch.Id, productId);
            beforeTask = Snapshot(task);
            beforeItem = Snapshot(item);
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 8);
        }

        using var verify = database.Open();
        Assert.Equal(beforeTask, Snapshot(Assert.Single(verify.Tasks.AsNoTracking())));
        Assert.Equal(beforeItem, Snapshot(Assert.Single(verify.TaskItems.AsNoTracking())));
    }

    [Fact]
    public void PositiveAdjustmentLeavesDraftAndDraftItemUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        DraftSnapshot beforeDraft;
        DraftItemSnapshot beforeItem;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-DRAFT-UNCHANGED", 10, 10).Id;
            var batch = AddBatch(seed, productId);
            var task = AddTask(seed, productId);
            var taskItem = AddTaskItem(seed, task.Id, batch.Id, productId);
            var draft = AddDraft(seed, task.Id);
            var draftItem = AddDraftItem(seed, draft.Id, taskItem.Id, task.Id);
            beforeDraft = Snapshot(draft);
            beforeItem = Snapshot(draftItem);
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 8);
        }

        using var verify = database.Open();
        Assert.Equal(beforeDraft, Snapshot(Assert.Single(verify.Drafts.AsNoTracking())));
        Assert.Equal(beforeItem, Snapshot(Assert.Single(verify.DraftItems.AsNoTracking())));
    }

    [Fact]
    public void PositiveAdjustmentLeavesInspectionHistoryUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        InspectionSnapshot beforeInspection;
        InspectionItemSnapshot beforeItem;
        InspectionRevisionSnapshot beforeRevision;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-INSPECTION-UNCHANGED", 10, 10).Id;
            var batch = AddBatch(seed, productId);
            var task = AddTask(seed, productId);
            AddTaskItem(seed, task.Id, batch.Id, productId);
            var inspection = AddInspection(seed, task.Id, productId);
            var item = AddInspectionItem(seed, inspection.Id, productId, batch.Id);
            var revision = AddInspectionRevision(seed, item.Id);
            beforeInspection = Snapshot(inspection);
            beforeItem = Snapshot(item);
            beforeRevision = Snapshot(revision);
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 8);
        }

        using var verify = database.Open();
        Assert.Equal(beforeInspection, Snapshot(Assert.Single(verify.Inspections.AsNoTracking())));
        Assert.Equal(beforeItem, Snapshot(Assert.Single(verify.InspectionItems.AsNoTracking())));
        Assert.Equal(beforeRevision, Snapshot(Assert.Single(verify.InspectionItemRevisions.AsNoTracking())));
    }

    [Fact]
    public void NegativeQuantityIsRejectedBeforeAnyWrite()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-NEGATIVE", 10, 10).Id;
            AddBatch(seed, productId);
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Execute(context, productId, -1));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
    }

    [Fact]
    public void SameValueReturnsNoChangeWithoutAdjustmentOrTimestampUpdate()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        DateTime originalUpdatedAt;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-SAME", 10, 10);
            product.UpdatedAtUtc = SeedUtc;
            seed.SaveChanges();
            productId = product.Id;
            originalUpdatedAt = product.UpdatedAtUtc;
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId, 10, confirmation: false);
            Assert.False(result.Changed);
            Assert.True(result.NoChange);
            Assert.Null(result.AdjustmentId);
            Assert.Equal(10, result.PreviousEffectiveStockQty);
            Assert.Equal(10, result.CorrectedStockQty);
            Assert.False(result.ProductTerminated);
        }

        using var verify = database.Open();
        var productAfter = Assert.Single(verify.Products.AsNoTracking());
        Assert.Equal(originalUpdatedAt, productAfter.UpdatedAtUtc);
        Assert.Empty(verify.InventoryAdjustments.AsNoTracking());
    }

    [Fact]
    public void ConsecutiveAdjustmentsKeepTheOriginalExcelSnapshot()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-CONSECUTIVE", 10, 10).Id;
        }

        using (var context = database.Open())
        {
            var first = Execute(context, productId, 8);
            var second = Execute(context, productId, 6, AdjustedAtUtc.AddMinutes(1));
            Assert.True(first.Changed);
            Assert.True(second.Changed);
        }

        using var verify = database.Open();
        var adjustments = verify.InventoryAdjustments.AsNoTracking().OrderBy(item => item.Id).ToArray();
        Assert.Equal(2, adjustments.Length);
        Assert.Equal(new[] { 10, 10 }, adjustments.Select(item => item.ExcelStockQtySnapshot));
        Assert.Equal(new[] { 8, 6 }, adjustments.Select(item => item.AdjustedStockQty));
        Assert.Equal(6, verify.Products.Single().EffectiveStockQty);
        Assert.Equal("manual", verify.Products.Single().EffectiveStockSource);
    }

    [Fact]
    public void InspectionTaskDetailReadsTheAdjustedEffectiveStock()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long taskId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-QUERY", 10, 10).Id;
            var batch = AddBatch(seed, productId);
            var task = AddTask(seed, productId);
            AddTaskItem(seed, task.Id, batch.Id, productId);
            taskId = task.Id;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 8);
            var detail = new InspectionTaskQuery().GetDetail(context, taskId).Detail;
            Assert.NotNull(detail);
            Assert.Equal(8, detail!.EffectiveStockQty);
        }
    }

    [Fact]
    public void ZeroWithoutConfirmationLeavesTheWholeBusinessGraphUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            var scenario = AddRichScenario(seed, "P-ZERO-NO-CONFIRM", 10, 10);
            productId = scenario.ProductId;
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() => Execute(context, productId, 0, confirmation: false));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
    }

    [Fact]
    public void ConfirmedZeroCreatesAdjustmentAndUsesTheStockZeroUseCase()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-ZERO", 10, 10).Id;
            AddBatch(seed, productId);
        }

        ManualInventoryAdjustmentResult result;
        using (var context = database.Open())
        {
            result = Execute(context, productId, 0);
        }

        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        var adjustment = Assert.Single(verify.InventoryAdjustments.AsNoTracking());
        var lifecycleEvent = Assert.Single(verify.LifecycleEvents.AsNoTracking());
        Assert.True(result.Changed);
        Assert.True(result.ProductTerminated);
        Assert.Equal(10, adjustment.ExcelStockQtySnapshot);
        Assert.Equal(0, adjustment.AdjustedStockQty);
        Assert.Equal(0, product.EffectiveStockQty);
        Assert.Equal("manual", product.EffectiveStockSource);
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal("product_stock_zero", lifecycleEvent.EventType);
        Assert.Equal(adjustment.Id, lifecycleEvent.SourceAdjustmentId);
    }

    [Fact]
    public void ConfirmedZeroStopsEveryHistoricalBatchInTheApprovedTerminalState()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-ZERO-BATCHES", 10, 10).Id;
            AddBatch(seed, productId, currentStage: "none");
            AddBatch(seed, productId, currentStage: ExpiryStageCalculator.Discount20);
            AddBatch(
                seed,
                productId,
                trackingStatus: "stopped",
                stopReason: "batch_checked_zero",
                stoppedAtUtc: SeedUtc,
                nextTriggerDate: BusinessDate);
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
        }

        using var verify = database.Open();
        Assert.All(verify.Batches.AsNoTracking(), batch =>
        {
            Assert.Equal("stopped", batch.TrackingStatus);
            Assert.Equal("product_stock_zero", batch.StopReason);
            Assert.Equal(AdjustedAtUtc, batch.StoppedAtUtc);
            Assert.Null(batch.NextTriggerDate);
            Assert.Equal(AdjustedAtUtc, batch.UpdatedAtUtc);
        });
    }

    [Fact]
    public void ConfirmedZeroClosesOpenTaskAsSystemClosed()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            var scenario = AddRichScenario(seed, "P-ZERO-TASK", 10, 10);
            productId = scenario.ProductId;
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId, 0);
            Assert.True(result.ProductTerminated);
        }

        using var verify = database.Open();
        var task = Assert.Single(verify.Tasks.AsNoTracking());
        Assert.Equal("system_closed", task.Status);
        Assert.Equal("product_stock_zero", task.CloseReason);
        Assert.Equal(AdjustedAtUtc, task.ClosedAtUtc);
        Assert.NotEqual("completed", task.Status);
    }

    [Fact]
    public void ConfirmedZeroInvalidatesDraftInPlaceAndRetainsDraftItems()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long draftId;
        long draftItemId;
        using (var seed = database.Open())
        {
            var scenario = AddRichScenario(seed, "P-ZERO-DRAFT", 10, 10);
            productId = scenario.ProductId;
            draftId = scenario.DraftId;
            draftItemId = scenario.DraftItemId;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
        }

        using var verify = database.Open();
        var draft = Assert.Single(verify.Drafts.AsNoTracking());
        var item = Assert.Single(verify.DraftItems.AsNoTracking());
        Assert.Equal(draftId, draft.Id);
        Assert.True(draft.IsInvalid);
        Assert.Equal("product_stock_zero", draft.InvalidReason);
        Assert.Equal(AdjustedAtUtc, draft.InvalidatedAtUtc);
        Assert.Equal(draftItemId, item.Id);
        Assert.Equal(7, item.CheckedQty);
    }

    [Fact]
    public void ConfirmedZeroLifecycleEventsAllPointToThisAdjustmentOnly()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-ZERO-SOURCES", 10, 10).ProductId;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
        }

        using var verify = database.Open();
        var adjustment = Assert.Single(verify.InventoryAdjustments.AsNoTracking());
        var events = verify.LifecycleEvents.AsNoTracking().ToArray();
        Assert.NotEmpty(events);
        Assert.All(events, lifecycleEvent =>
        {
            Assert.Equal(productId, lifecycleEvent.ProductId);
            Assert.Equal(adjustment.Id, lifecycleEvent.SourceAdjustmentId);
            Assert.Null(lifecycleEvent.SourceImportId);
            Assert.Null(lifecycleEvent.SourceInspectionId);
        });
    }

    [Fact]
    public void StockZeroUseCaseRejectsCrossProductNonZeroAndMissingAdjustmentSources()
    {
        using var database = SqliteTestDatabase.Create();
        long productA;
        long productB;
        long crossProductAdjustment;
        long nonZeroAdjustment;
        using (var seed = database.Open())
        {
            productA = AddProduct(seed, "P-SOURCE-A", 0, 0).Id;
            productB = AddProduct(seed, "P-SOURCE-B", 0, 0).Id;
            var cross = new InventoryAdjustment
            {
                ProductId = productB,
                ExcelStockQtySnapshot = 3,
                AdjustedStockQty = 0,
                AdjustedAtUtc = AdjustedAtUtc
            };
            var nonZero = new InventoryAdjustment
            {
                ProductId = productA,
                ExcelStockQtySnapshot = 3,
                AdjustedStockQty = 2,
                AdjustedAtUtc = AdjustedAtUtc
            };
            seed.InventoryAdjustments.AddRange(cross, nonZero);
            seed.SaveChanges();
            crossProductAdjustment = cross.Id;
            nonZeroAdjustment = nonZero.Id;
        }

        using (var context = database.Open())
        {
            var useCase = new ProductStockZeroLifecycleUseCase();
            Assert.Throws<ArgumentException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(productA, AdjustedAtUtc, SourceAdjustmentId: crossProductAdjustment)));
            Assert.Throws<ArgumentException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(productA, AdjustedAtUtc, SourceAdjustmentId: nonZeroAdjustment)));
            Assert.Throws<KeyNotFoundException>(() => useCase.Execute(
                context,
                new ProductStockZeroRequest(productA, AdjustedAtUtc, SourceAdjustmentId: 99999)));
            Assert.Empty(context.LifecycleEvents.AsNoTracking());
        }
    }

    [Fact]
    public void ZeroingOneProductLeavesOtherProductsAndTheirBusinessGraphsUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        long productA;
        GraphSnapshot beforeOthers;
        using (var seed = database.Open())
        {
            productA = AddRichScenario(seed, "P-ISOLATED-A", 10, 10).ProductId;
            AddRichScenario(seed, "P-ISOLATED-B", 10, 10);
            AddRichScenario(seed, "P-ISOLATED-C", 10, 10);
            beforeOthers = CaptureForProducts(seed, ["P-ISOLATED-B", "P-ISOLATED-C"]);
        }

        using (var context = database.Open())
        {
            Execute(context, productA, 0);
        }

        using var verify = database.Open();
        AssertGraphEqual(
            beforeOthers,
            CaptureForProducts(verify, ["P-ISOLATED-B", "P-ISOLATED-C"]));
    }

    [Fact]
    public void RepeatingConfirmedZeroReturnsNoChangeWithoutNewHistoryOrEvents()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-REPEAT-ZERO", 10, 10).ProductId;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
        }

        ProductSnapshot productBefore;
        BatchSnapshot batchBefore;
        LifecycleEventSnapshot[] eventsBefore;
        using (var before = database.Open())
        {
            productBefore = Snapshot(Assert.Single(before.Products.AsNoTracking()));
            batchBefore = Snapshot(Assert.Single(before.Batches.AsNoTracking()));
            eventsBefore = before.LifecycleEvents.AsNoTracking().Select(Snapshot).ToArray();
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId, 0, AdjustedAtUtc.AddHours(1));
            Assert.False(result.Changed);
            Assert.Null(result.AdjustmentId);
            Assert.False(result.ProductTerminated);
        }

        using var verify = database.Open();
        Assert.Equal(productBefore, Snapshot(Assert.Single(verify.Products.AsNoTracking())));
        Assert.Equal(batchBefore, Snapshot(Assert.Single(verify.Batches.AsNoTracking())));
        Assert.Equal(eventsBefore, verify.LifecycleEvents.AsNoTracking().Select(Snapshot).ToArray());
        Assert.Single(verify.InventoryAdjustments.AsNoTracking());
    }

    [Fact]
    public void RestoringStockAfterZeroKeepsHistoryAndDoesNotReviveBatches()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-RESTORE", 10, 10).ProductId;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
            var result = Execute(context, productId, 5, AdjustedAtUtc.AddHours(1));
            Assert.True(result.Changed);
            Assert.False(result.ProductTerminated);
        }

        using var verify = database.Open();
        var product = Assert.Single(verify.Products.AsNoTracking());
        var batch = Assert.Single(verify.Batches.AsNoTracking());
        Assert.Equal(5, product.EffectiveStockQty);
        Assert.Equal("manual", product.EffectiveStockSource);
        Assert.True(product.IsStockZeroTerminated);
        Assert.Equal(1, product.LifecycleGeneration);
        Assert.Equal("stopped", batch.TrackingStatus);
        Assert.Equal("product_stock_zero", batch.StopReason);
        Assert.Equal(2, verify.InventoryAdjustments.AsNoTracking().Count());
    }

    [Fact]
    public void RestoringStockAfterZeroDoesNotReopenTaskOrDraft()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-RESTORE-TASK", 10, 10).ProductId;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
            Execute(context, productId, 5, AdjustedAtUtc.AddHours(1));
        }

        using var verify = database.Open();
        Assert.Equal("system_closed", Assert.Single(verify.Tasks.AsNoTracking()).Status);
        Assert.True(Assert.Single(verify.Drafts.AsNoTracking()).IsInvalid);
        Assert.Single(verify.DraftItems.AsNoTracking());
    }

    [Fact]
    public void RestoringStockDoesNotCreateOrStartAnewBatch()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-RESTORE-NO-BATCH", 10, 10).Id;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
            Execute(context, productId, 5, AdjustedAtUtc.AddHours(1));
        }

        using var verify = database.Open();
        Assert.Empty(verify.Batches.AsNoTracking());
        Assert.Empty(verify.Tasks.AsNoTracking());
        Assert.Equal(2, verify.InventoryAdjustments.AsNoTracking().Count());
    }

    [Fact]
    public void PostImportS3T05StartsOnlyARealNewBatchInTheCurrentGenerationAfterManualRestore()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        long productId;
        long oldBatchId;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            var product = AddProduct(seed, "P-RESTORE-S3T05", 10, 10);
            product.LastSeenImportId = importId;
            seed.SaveChanges();
            var oldBatch = AddBatch(seed, product.Id);
            oldBatch.LastSeenImportId = importId;
            seed.SaveChanges();
            productId = product.Id;
            oldBatchId = oldBatch.Id;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 0);
            Execute(context, productId, 5, AdjustedAtUtc.AddHours(1));
        }

        long newBatchId;
        using (var stage2Facts = database.Open())
        {
            // This is the already-persisted Stage 2 new-batch fact; S3-T05 starts it below.
            var newBatch = AddUnprocessedNewBatch(stage2Facts, productId, importId);
            newBatchId = newBatch.Id;
        }

        using (var context = database.Open())
        {
            var result = new PostImportLifecycleUseCase().Execute(
                context,
                new PostImportLifecycleRequest(
                    importId,
                    BusinessDate,
                    AdjustedAtUtc.AddHours(2),
                    [new PostImportProductGroup(
                        productId,
                        [new PostImportBatchFact(
                            newBatchId,
                            PostImportBatchFactKinds.New,
                            PreviousMaxArrivalQty: 0,
                            CurrentArrivalQty: 2)])]));
            Assert.Equal(1, result.StartedBatchCount);
        }

        using var verify = database.Open();
        var productAfter = verify.Products.AsNoTracking().Single(item => item.Id == productId);
        var oldBatchAfter = verify.Batches.AsNoTracking().Single(item => item.Id == oldBatchId);
        var newBatchAfter = verify.Batches.AsNoTracking().Single(item => item.Id == newBatchId);
        Assert.Equal(1, productAfter.LifecycleGeneration);
        Assert.True(productAfter.IsStockZeroTerminated);
        Assert.Equal(5, productAfter.EffectiveStockQty);
        Assert.Equal("stopped", oldBatchAfter.TrackingStatus);
        Assert.Equal("product_stock_zero", oldBatchAfter.StopReason);
        Assert.Equal(0, oldBatchAfter.LifecycleGeneration);
        Assert.Equal(1, newBatchAfter.LifecycleGeneration);
        Assert.Equal("active", newBatchAfter.TrackingStatus);
        Assert.Null(newBatchAfter.StopReason);
        Assert.NotEqual(ExpiryStageCalculator.None, newBatchAfter.CurrentStage);
        Assert.Single(verify.TaskItems.AsNoTracking());
        Assert.Equal(newBatchId, verify.TaskItems.AsNoTracking().Single().BatchId);
    }

    [Fact]
    public void PositiveAdjustmentChangesOnlyApprovedProductFieldsAndAddsOneAdjustment()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-FULL-GRAPH", 10, 10).ProductId;
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            Execute(context, productId, 8);
        }

        using var verify = database.Open();
        var after = Capture(verify);
        Assert.Equal(before.Batches, after.Batches);
        Assert.Equal(before.Tasks, after.Tasks);
        Assert.Equal(before.TaskItems, after.TaskItems);
        Assert.Equal(before.Drafts, after.Drafts);
        Assert.Equal(before.DraftItems, after.DraftItems);
        Assert.Equal(before.Inspections, after.Inspections);
        Assert.Equal(before.InspectionItems, after.InspectionItems);
        Assert.Equal(before.InspectionItemRevisions, after.InspectionItemRevisions);
        Assert.Equal(before.LifecycleEvents, after.LifecycleEvents);
        Assert.Equal(before.InventoryAdjustments.Length + 1, after.InventoryAdjustments.Length);
        var beforeProduct = Assert.Single(before.Products);
        var afterProduct = Assert.Single(after.Products);
        Assert.Equal(beforeProduct with
        {
            EffectiveStockQty = 8,
            EffectiveStockSource = "manual",
            UpdatedAtUtc = AdjustedAtUtc
        }, afterProduct);
        Assert.Equal(beforeProduct.ExcelStockQty, afterProduct.ExcelStockQty);
        Assert.Equal(beforeProduct.LastSeenImportId, afterProduct.LastSeenImportId);
        Assert.False(after.LifecycleEvents.Any());
    }

    [Fact]
    public void AdjustmentInsertFailureRollsBackProductAndClearsTracker()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-ADJUSTMENT-FAIL", 10, 10).ProductId;
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_manual_adjustment_insert
                BEFORE INSERT ON inventory_adjustments
                BEGIN
                    SELECT RAISE(ABORT, 'forced adjustment insert failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(context, productId, 8));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
    }

    [Fact]
    public void StockZeroFailureAfterFirstSaveRollsBackAdjustmentProductAndLifecycle()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-S3T04-FAIL", 10, 10).ProductId;
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_manual_stock_zero_event
                BEFORE INSERT ON lifecycle_events
                BEGIN
                    SELECT RAISE(ABORT, 'forced stock zero lifecycle failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(context, productId, 0));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
    }

    [Fact]
    public void StockZeroFailureInsideOuterTransactionLeavesRollbackToTheCaller()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddRichScenario(seed, "P-S3T04-OUTER-FAIL", 10, 10).ProductId;
            before = Capture(seed);
        }

        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_manual_stock_zero_outer_event
                BEFORE INSERT ON lifecycle_events
                BEGIN
                    SELECT RAISE(ABORT, 'forced outer stock zero lifecycle failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(context, productId, 0));
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Empty(context.ChangeTracker.Entries());
            transaction.Rollback();
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
    }

    [Fact]
    public void CallerOuterRollbackRemovesPositiveAndZeroAdjustmentTransactions()
    {
        using (var positiveDatabase = SqliteTestDatabase.Create())
        {
            long productId;
            using (var seed = positiveDatabase.Open())
            {
                productId = AddProduct(seed, "P-OUTER-POSITIVE", 10, 10).Id;
            }

            using (var context = positiveDatabase.Open())
            using (var transaction = context.Database.BeginTransaction())
            {
                Execute(context, productId, 8);
                transaction.Rollback();
            }

            using var verify = positiveDatabase.Open();
            Assert.Equal(10, Assert.Single(verify.Products.AsNoTracking()).EffectiveStockQty);
            Assert.Empty(verify.InventoryAdjustments.AsNoTracking());
        }

        using (var zeroDatabase = SqliteTestDatabase.Create())
        {
            long productId;
            using (var seed = zeroDatabase.Open())
            {
                productId = AddRichScenario(seed, "P-OUTER-ZERO", 10, 10).ProductId;
            }

            using (var context = zeroDatabase.Open())
            using (var transaction = context.Database.BeginTransaction())
            {
                Execute(context, productId, 0);
                transaction.Rollback();
            }

            using var verify = zeroDatabase.Open();
            var product = Assert.Single(verify.Products.AsNoTracking());
            Assert.Equal(10, product.EffectiveStockQty);
            Assert.False(product.IsStockZeroTerminated);
            Assert.Empty(verify.InventoryAdjustments.AsNoTracking());
            Assert.Empty(verify.LifecycleEvents.AsNoTracking());
            Assert.Equal("active", Assert.Single(verify.Batches.AsNoTracking()).TrackingStatus);
            Assert.Equal("open", Assert.Single(verify.Tasks.AsNoTracking()).Status);
        }
    }

    [Fact]
    public void NonUtcTimestampIsRejectedBeforeAnyWrite()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed, "P-NON-UTC", 10, 10).Id;
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            var nonUtc = DateTime.SpecifyKind(AdjustedAtUtc, DateTimeKind.Unspecified);
            Assert.Throws<ArgumentException>(() => Execute(context, productId, 8, nonUtc));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
    }

    [Fact]
    public void InvalidProductIdAndMissingProductAreRejectedWithoutWrites()
    {
        using var database = SqliteTestDatabase.Create();
        long existingProductId;
        GraphSnapshot before;
        using (var seed = database.Open())
        {
            existingProductId = AddProduct(seed, "P-IDENTITY", 10, 10).Id;
            before = Capture(seed);
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Execute(context, 0, 8));
            Assert.Throws<KeyNotFoundException>(() => Execute(context, 99999, 8));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        AssertGraphEqual(before, Capture(verify));
        Assert.Equal(existingProductId, Assert.Single(verify.Products.AsNoTracking()).Id);
    }

    [Fact]
    public void IncrementalExcelImportOverridesCurrentStockAndPreservesAdjustmentHistory()
    {
        using var database = SqliteTestDatabase.Create();
        var sourcePath = Path.Combine(database.Directory, "incremental.xlsx");
        WriteWorkbook(sourcePath,
        [
            "食品", "P-IMPORT", "B-IMPORT", "导入商品", "2026-01-01", "2026-09-20", "12", "M", "是", "3", "12"
        ]);
        long productId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P-IMPORT", 10, 8, source: "manual");
            product.CurrentName = "导入商品";
            product.CurrentBarcode = "B-IMPORT";
            seed.SaveChanges();
            var seedAdjustment = new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = 10,
                AdjustedStockQty = 8,
                AdjustedAtUtc = AdjustedAtUtc
            };
            seed.InventoryAdjustments.Add(seedAdjustment);
            seed.SaveChanges();
            productId = product.Id;
            AddProduct(seed, "P-ABSENT", 7, 6, source: "manual");
        }

        ImportConfirmationContract contract;
        var parsedAtUtc = AdjustedAtUtc.AddHours(1);
        var importOccurredAtUtc = AdjustedAtUtc.AddHours(2);
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(sourcePath);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(sourcePath, workbook, plan)).Contract);
        }

        using (var context = database.Open())
        {
            var result = new ConfirmedImportLifecycleOrchestrator().Execute(
                context,
                new ConfirmedImportLifecycleRequest(
                    contract,
                    Path.Combine(database.Directory, "snapshots"),
                    parsedAtUtc,
                    BusinessDate,
                    importOccurredAtUtc));
            Assert.True(result.Succeeded);
        }

        using var verify = database.Open();
        var imported = verify.Products.AsNoTracking().Single(item => item.Id == productId);
        var absent = verify.Products.AsNoTracking().Single(item => item.ProductCode == "P-ABSENT");
        Assert.Equal(12, imported.ExcelStockQty);
        Assert.Equal(12, imported.EffectiveStockQty);
        Assert.Equal("excel", imported.EffectiveStockSource);
        var adjustment = Assert.Single(verify.InventoryAdjustments.AsNoTracking());
        Assert.Equal(10, adjustment.ExcelStockQtySnapshot);
        Assert.Equal(8, adjustment.AdjustedStockQty);
        Assert.Equal(7, absent.ExcelStockQty);
        Assert.Equal(6, absent.EffectiveStockQty);
        Assert.Equal("manual", absent.EffectiveStockSource);
    }

    private static ManualInventoryAdjustmentResult Execute(
        StoreDbContext context,
        long productId,
        int correctedStockQty,
        DateTime? adjustedAtUtc = null,
        bool confirmation = true) => new ManualInventoryAdjustmentUseCase().Execute(
            context,
            new ManualInventoryAdjustmentRequest(
                productId,
                correctedStockQty,
                confirmation,
                adjustedAtUtc ?? AdjustedAtUtc));

    private static Product AddProduct(
        StoreDbContext context,
        string code,
        int excelStock,
        int effectiveStock,
        string source = "excel")
    {
        var product = new Product
        {
            ProductCode = code,
            CurrentName = code + " name",
            CurrentBarcode = code + " barcode",
            ExcelStockQty = excelStock,
            EffectiveStockQty = effectiveStock,
            EffectiveStockSource = source,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static ImportRecord AddImport(StoreDbContext context)
    {
        var import = new ImportRecord
        {
            SourceFileName = "stage2.xlsx",
            SourceFileSha256 = new string('a', 64),
            ParsedAtUtc = SeedUtc,
            ConfirmedAtUtc = SeedUtc,
            Status = ImportStatuses.Succeeded,
            IsUndone = false,
            ProductCount = 1,
            BatchCount = 1
        };
        context.Imports.Add(import);
        context.SaveChanges();
        return import;
    }

    private static Batch AddUnprocessedNewBatch(
        StoreDbContext context,
        long productId,
        long importId)
    {
        var batchIndex = context.Batches.Count(batch => batch.ProductId == productId);
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 2, 1).AddDays(batchIndex),
            ExpiryDate = new DateOnly(2026, 9, 25),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 2,
            MaxArrivalQty = 2,
            SourceDiscountReference = "stage2",
            LifecycleGeneration = 0,
            TrackingStatus = "active",
            CurrentStage = ExpiryStageCalculator.None,
            NextTriggerDate = null,
            AttentionVersion = 0,
            HandledAttentionVersion = 0,
            LastSeenImportId = importId,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static Batch AddBatch(
        StoreDbContext context,
        long productId,
        string currentStage = "discount_20",
        string trackingStatus = "active",
        string? stopReason = null,
        DateTime? stoppedAtUtc = null,
        DateOnly? nextTriggerDate = null)
    {
        var batchIndex = context.Batches.Count(batch => batch.ProductId == productId);
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 1, 1).AddDays(batchIndex),
            ExpiryDate = new DateOnly(2026, 9, 20).AddDays(batchIndex),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            SourceDiscountReference = "test",
            LifecycleGeneration = 0,
            TrackingStatus = trackingStatus,
            StopReason = stopReason,
            StoppedAtUtc = stoppedAtUtc,
            CurrentStage = currentStage,
            NextTriggerDate = nextTriggerDate ?? new DateOnly(2026, 8, 28),
            AttentionVersion = 2,
            HandledAttentionVersion = 1,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static ProductTask AddTask(StoreDbContext context, long productId)
    {
        var task = new ProductTask
        {
            ProductId = productId,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Discount20,
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
            Stage = ExpiryStageCalculator.Discount20,
            AttentionVersion = 2,
            RequiresReconfirmation = true,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.TaskItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static InspectionDraft AddDraft(StoreDbContext context, long taskId)
    {
        var draft = new InspectionDraft
        {
            TaskId = taskId,
            InspectorName = "测试人员",
            CheckDate = BusinessDate,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Drafts.Add(draft);
        context.SaveChanges();
        return draft;
    }

    private static InspectionDraftItem AddDraftItem(
        StoreDbContext context,
        long draftId,
        long taskItemId,
        long taskId)
    {
        var item = new InspectionDraftItem
        {
            DraftId = draftId,
            TaskItemId = taskItemId,
            TaskId = taskId,
            CheckedQty = 7,
            ConfirmedAttentionVersion = 2
        };
        context.DraftItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static Inspection AddInspection(StoreDbContext context, long taskId, long productId)
    {
        var inspection = new Inspection
        {
            TaskId = taskId,
            ProductId = productId,
            ProductCodeSnapshot = "snapshot-code",
            ProductNameSnapshot = "snapshot-name",
            BarcodeSnapshot = "snapshot-barcode",
            StageSnapshot = ExpiryStageCalculator.Discount20,
            StockQtySnapshot = 10,
            InspectorName = "历史检查人",
            CheckDate = BusinessDate,
            SubmittedAtUtc = SeedUtc
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();
        return inspection;
    }

    private static InspectionItem AddInspectionItem(
        StoreDbContext context,
        long inspectionId,
        long productId,
        long batchId)
    {
        var item = new InspectionItem
        {
            InspectionId = inspectionId,
            ProductId = productId,
            BatchId = batchId,
            ProductionDateSnapshot = new DateOnly(2026, 1, 1),
            ExpiryDateSnapshot = new DateOnly(2026, 9, 20),
            StageSnapshot = ExpiryStageCalculator.Discount20,
            ArrivalQtySnapshot = 10,
            CheckedQty = 7,
            UpdatedAtUtc = SeedUtc
        };
        context.InspectionItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static InspectionItemRevision AddInspectionRevision(
        StoreDbContext context,
        long inspectionItemId)
    {
        var revision = new InspectionItemRevision
        {
            InspectionItemId = inspectionItemId,
            PreviousCheckedQty = 8,
            NewCheckedQty = 7,
            ChangedAtUtc = SeedUtc
        };
        context.InspectionItemRevisions.Add(revision);
        context.SaveChanges();
        return revision;
    }

    private static Scenario AddRichScenario(
        StoreDbContext context,
        string code,
        int excelStock,
        int effectiveStock)
    {
        var product = AddProduct(context, code, excelStock, effectiveStock);
        var batch = AddBatch(context, product.Id);
        var task = AddTask(context, product.Id);
        var taskItem = AddTaskItem(context, task.Id, batch.Id, product.Id);
        var draft = AddDraft(context, task.Id);
        var draftItem = AddDraftItem(context, draft.Id, taskItem.Id, task.Id);
        var inspection = AddInspection(context, task.Id, product.Id);
        var inspectionItem = AddInspectionItem(context, inspection.Id, product.Id, batch.Id);
        AddInspectionRevision(context, inspectionItem.Id);
        return new(product.Id, task.Id, draft.Id, draftItem.Id, batch.Id);
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

    private static TaskSnapshot Snapshot(ProductTask task) => new(
        task.Id,
        task.ProductId,
        task.Status,
        task.HighestStage,
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.ClosedAtUtc,
        task.CloseReason);

    private static TaskItemSnapshot Snapshot(ProductTaskItem item) => new(
        item.Id,
        item.TaskId,
        item.BatchId,
        item.ProductId,
        item.Stage,
        item.AttentionVersion,
        item.RequiresReconfirmation,
        item.CreatedAtUtc,
        item.UpdatedAtUtc);

    private static DraftSnapshot Snapshot(InspectionDraft draft) => new(
        draft.Id,
        draft.TaskId,
        draft.InspectorName,
        draft.CheckDate,
        draft.IsInvalid,
        draft.InvalidReason,
        draft.InvalidatedAtUtc,
        draft.CreatedAtUtc,
        draft.UpdatedAtUtc);

    private static DraftItemSnapshot Snapshot(InspectionDraftItem item) => new(
        item.Id,
        item.DraftId,
        item.TaskItemId,
        item.TaskId,
        item.CheckedQty,
        item.ConfirmedAttentionVersion);

    private static InspectionSnapshot Snapshot(Inspection inspection) => new(
        inspection.Id,
        inspection.TaskId,
        inspection.ProductId,
        inspection.ProductCodeSnapshot,
        inspection.ProductNameSnapshot,
        inspection.BarcodeSnapshot,
        inspection.StageSnapshot,
        inspection.StockQtySnapshot,
        inspection.InspectorName,
        inspection.CheckDate,
        inspection.SubmittedAtUtc);

    private static InspectionItemSnapshot Snapshot(InspectionItem item) => new(
        item.Id,
        item.InspectionId,
        item.ProductId,
        item.BatchId,
        item.ProductionDateSnapshot,
        item.ExpiryDateSnapshot,
        item.StageSnapshot,
        item.ArrivalQtySnapshot,
        item.CheckedQty,
        item.UpdatedAtUtc);

    private static InspectionRevisionSnapshot Snapshot(InspectionItemRevision revision) => new(
        revision.Id,
        revision.InspectionItemId,
        revision.PreviousCheckedQty,
        revision.NewCheckedQty,
        revision.ChangedAtUtc);

    private static LifecycleEventSnapshot Snapshot(LifecycleEvent lifecycleEvent) => new(
        lifecycleEvent.Id,
        lifecycleEvent.ProductId,
        lifecycleEvent.BatchId,
        lifecycleEvent.EventType,
        lifecycleEvent.Reason,
        lifecycleEvent.OccurredAtUtc,
        lifecycleEvent.SourceImportId,
        lifecycleEvent.SourceInspectionId,
        lifecycleEvent.SourceAdjustmentId);

    private static GraphSnapshot Capture(StoreDbContext context) => new(
        context.Products.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.Batches.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.Tasks.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.TaskItems.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.Drafts.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.DraftItems.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.Inspections.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.InspectionItems.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.InspectionItemRevisions.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray(),
        context.InventoryAdjustments.AsNoTracking().OrderBy(item => item.Id).Select(item => new AdjustmentSnapshot(
            item.Id,
            item.ProductId,
            item.ExcelStockQtySnapshot,
            item.AdjustedStockQty,
            item.AdjustedAtUtc)).ToArray(),
        context.LifecycleEvents.AsNoTracking().OrderBy(item => item.Id).Select(Snapshot).ToArray());

    private static void AssertGraphEqual(GraphSnapshot expected, GraphSnapshot actual)
    {
        Assert.Equal(expected.Products, actual.Products);
        Assert.Equal(expected.Batches, actual.Batches);
        Assert.Equal(expected.Tasks, actual.Tasks);
        Assert.Equal(expected.TaskItems, actual.TaskItems);
        Assert.Equal(expected.Drafts, actual.Drafts);
        Assert.Equal(expected.DraftItems, actual.DraftItems);
        Assert.Equal(expected.Inspections, actual.Inspections);
        Assert.Equal(expected.InspectionItems, actual.InspectionItems);
        Assert.Equal(expected.InspectionItemRevisions, actual.InspectionItemRevisions);
        Assert.Equal(expected.InventoryAdjustments, actual.InventoryAdjustments);
        Assert.Equal(expected.LifecycleEvents, actual.LifecycleEvents);
    }

    private static GraphSnapshot CaptureForProducts(
        StoreDbContext context,
        IReadOnlyCollection<string> productCodes)
    {
        var productIds = context.Products.AsNoTracking()
            .Where(product => productCodes.Contains(product.ProductCode))
            .Select(product => product.Id)
            .ToArray();
        return new(
            context.Products.AsNoTracking().Where(item => productIds.Contains(item.Id)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.Batches.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.Tasks.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.TaskItems.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.Drafts.AsNoTracking().Where(item => context.Tasks.AsNoTracking().Where(task => productIds.Contains(task.ProductId)).Select(task => task.Id).Contains(item.TaskId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.DraftItems.AsNoTracking().Where(item => context.Tasks.AsNoTracking().Where(task => productIds.Contains(task.ProductId)).Select(task => task.Id).Contains(item.TaskId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.Inspections.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.InspectionItems.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.InspectionItemRevisions.AsNoTracking().Where(item => context.InspectionItems.AsNoTracking().Where(inspectionItem => productIds.Contains(inspectionItem.ProductId)).Select(inspectionItem => inspectionItem.Id).Contains(item.InspectionItemId)).OrderBy(item => item.Id).Select(Snapshot).ToArray(),
            context.InventoryAdjustments.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(item => new AdjustmentSnapshot(
                item.Id,
                item.ProductId,
                item.ExcelStockQtySnapshot,
                item.AdjustedStockQty,
                item.AdjustedAtUtc)).ToArray(),
            context.LifecycleEvents.AsNoTracking().Where(item => productIds.Contains(item.ProductId)).OrderBy(item => item.Id).Select(Snapshot).ToArray());
    }

    private static void WriteWorkbook(string path, IReadOnlyList<string> values)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        var header = string.Join(string.Empty, Headers.Select((value, index) => InlineCell(ColumnName(index), 1, value)));
        var row = string.Join(string.Empty, values.Select((value, index) => InlineCell(ColumnName(index), 2, value)));
        AddEntry(archive, "xl/worksheets/sheet1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{header}</row><row r=\"2\">{row}</row></sheetData></worksheet>");
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

    private sealed record Scenario(
        long ProductId,
        long TaskId,
        long DraftId,
        long DraftItemId,
        long BatchId);

    private sealed record GraphSnapshot(
        ProductSnapshot[] Products,
        BatchSnapshot[] Batches,
        TaskSnapshot[] Tasks,
        TaskItemSnapshot[] TaskItems,
        DraftSnapshot[] Drafts,
        DraftItemSnapshot[] DraftItems,
        InspectionSnapshot[] Inspections,
        InspectionItemSnapshot[] InspectionItems,
        InspectionRevisionSnapshot[] InspectionItemRevisions,
        AdjustmentSnapshot[] InventoryAdjustments,
        LifecycleEventSnapshot[] LifecycleEvents);

    private sealed record ProductSnapshot(
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

    private sealed record TaskSnapshot(
        long Id,
        long ProductId,
        string Status,
        string HighestStage,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? ClosedAtUtc,
        string? CloseReason);

    private sealed record TaskItemSnapshot(
        long Id,
        long TaskId,
        long BatchId,
        long ProductId,
        string Stage,
        int AttentionVersion,
        bool RequiresReconfirmation,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record DraftSnapshot(
        long Id,
        long TaskId,
        string? InspectorName,
        DateOnly? CheckDate,
        bool IsInvalid,
        string? InvalidReason,
        DateTime? InvalidatedAtUtc,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record DraftItemSnapshot(
        long Id,
        long DraftId,
        long TaskItemId,
        long TaskId,
        int? CheckedQty,
        int ConfirmedAttentionVersion);

    private sealed record InspectionSnapshot(
        long Id,
        long TaskId,
        long ProductId,
        string ProductCodeSnapshot,
        string? ProductNameSnapshot,
        string? BarcodeSnapshot,
        string StageSnapshot,
        int StockQtySnapshot,
        string InspectorName,
        DateOnly CheckDate,
        DateTime SubmittedAtUtc);

    private sealed record InspectionItemSnapshot(
        long Id,
        long InspectionId,
        long ProductId,
        long BatchId,
        DateOnly? ProductionDateSnapshot,
        DateOnly ExpiryDateSnapshot,
        string StageSnapshot,
        int ArrivalQtySnapshot,
        int CheckedQty,
        DateTime UpdatedAtUtc);

    private sealed record InspectionRevisionSnapshot(
        long Id,
        long InspectionItemId,
        int PreviousCheckedQty,
        int NewCheckedQty,
        DateTime ChangedAtUtc);

    private sealed record AdjustmentSnapshot(
        long Id,
        long ProductId,
        int ExcelStockQtySnapshot,
        int AdjustedStockQty,
        DateTime AdjustedAtUtc);

    private sealed record LifecycleEventSnapshot(
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
