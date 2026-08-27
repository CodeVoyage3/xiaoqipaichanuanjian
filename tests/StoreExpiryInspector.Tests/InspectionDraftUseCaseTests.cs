using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InspectionDraftUseCaseTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 27);
    private static readonly DateTime SavedAtUtc = new(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SaveDraftCreatesDraftAndItemAndReadiness()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            var result = Save(context, ids, inspectorName: " 张三 ", checkDate: BusinessDate, checkedQty: 7);

            Assert.True(result.Changed);
            Assert.Equal(1, result.CurrentItemCount);
            Assert.Equal(1, result.FilledItemCount);
            Assert.Equal(0, result.MissingItemCount);
            Assert.True(result.HasInspectorName);
            Assert.True(result.HasCheckDate);
            Assert.True(result.AllItemsFilled);
            Assert.True(result.IsDraftComplete);
        }

        using var verify = database.Open();
        var draft = verify.Drafts.Single();
        Assert.Equal("张三", draft.InspectorName);
        Assert.Equal(BusinessDate, draft.CheckDate);
        Assert.Single(verify.DraftItems);
        Assert.Equal(7, verify.DraftItems.Single().CheckedQty);
        Assert.Equal(0, verify.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void SaveDraftUpsertsAndPreservesNullZeroAndMissingItems()
    {
        using var database = CreateScenario(itemCount: 2);
        ScenarioIds ids;
        long draftId;
        DateTime createdAt;
        DateTime firstUpdatedAt;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            var first = Save(context, ids, checkedQty: 0, itemIndex: 0);
            draftId = first.DraftId;
            var draft = context.Drafts.Single();
            createdAt = draft.CreatedAtUtc;
            firstUpdatedAt = draft.UpdatedAtUtc;
            Assert.Single(context.DraftItems);
            Assert.Equal(0, context.DraftItems.Single().CheckedQty);
        }

        using (var context = database.Open())
        {
            var second = Save(context, ids, checkedQty: null, itemIndex: 0, savedAtUtc: SavedAtUtc.AddHours(1));
            Assert.True(second.Changed);
            Assert.Equal(draftId, second.DraftId);
            Assert.Single(context.DraftItems);
            Assert.Null(context.DraftItems.Single().CheckedQty);
            Assert.Equal(1, context.DraftItems.Count());
        }

        using (var verify = database.Open())
        {
            var draft = verify.Drafts.Single();
            Assert.Equal(createdAt, draft.CreatedAtUtc);
            Assert.Equal(SavedAtUtc.AddHours(1), draft.UpdatedAtUtc);
            Assert.Equal(2, verify.TaskItems.Count());
            Assert.Equal(1, verify.DraftItems.Count());
            Assert.Equal(firstUpdatedAt, createdAt);
        }
    }

    [Fact]
    public void SaveDraftLeavesUnspecifiedItemUnchangedAndRepeatingContentIsNoOp()
    {
        using var database = CreateScenario(itemCount: 2);
        ScenarioIds ids;
        DateTime draftUpdatedAt;
        DateTime itemUpdatedAt;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, checkedQty: 3, itemIndex: 0);
            Save(context, ids, checkedQty: 4, itemIndex: 1, savedAtUtc: SavedAtUtc.AddMinutes(1));
            draftUpdatedAt = context.Drafts.Single().UpdatedAtUtc;
            itemUpdatedAt = context.TaskItems.OrderBy(item => item.Id).First().UpdatedAtUtc;
        }

        using (var context = database.Open())
        {
            var result = Save(context, ids, checkedQty: 3, itemIndex: 0, savedAtUtc: SavedAtUtc.AddHours(2));
            Assert.False(result.Changed);
            Assert.Equal(draftUpdatedAt, context.Drafts.Single().UpdatedAtUtc);
            Assert.Equal(itemUpdatedAt, context.TaskItems.OrderBy(item => item.Id).First().UpdatedAtUtc);
        }

        using var verify = database.Open();
        var values = verify.DraftItems.AsNoTracking().OrderBy(item => item.TaskItemId).Select(item => item.CheckedQty).ToArray();
        Assert.Equal(new int?[] { 3, 4 }, values);
    }

    [Fact]
    public void ReconfirmItemAtomicallyClearsReconfirmationAndReplayIsNoOp()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, inspectorName: "张三", checkDate: BusinessDate, checkedQty: 5);
            SetReconfirmation(context, ids, true);
            var result = new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(1)));
            Assert.True(result.Changed);
            Assert.Equal(0, result.RequiresReconfirmationCount);
            Assert.True(result.IsDraftComplete);
        }

        using (var context = database.Open())
        {
            var replay = new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(2)));
            Assert.False(replay.Changed);
            Assert.True(replay.IdempotentReplay);
        }

        using var verify = database.Open();
        Assert.False(verify.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal(0, verify.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void ClearDraftDeletesItemsAndDraftAndNoDraftIsIdempotent()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, checkedQty: 1);
            var result = new InspectionDraftUseCase().ClearDraft(context, new(ids.TaskId, ids.ProductId));
            Assert.True(result.Changed);
            Assert.Empty(context.Drafts);
            Assert.Empty(context.DraftItems);
        }

        using (var context = database.Open())
        {
            var result = new InspectionDraftUseCase().ClearDraft(context, new(ids.TaskId, ids.ProductId));
            Assert.False(result.Changed);
        }
    }

    [Fact]
    public void SaveDraftAllowsPartialInputAndReadinessCountsCurrentItems()
    {
        using var database = CreateScenario(itemCount: 3);
        using var context = database.Open();
        var ids = ReadIds(context);
        var result = new InspectionDraftUseCase().SaveDraft(
            context,
            new(
                ids.TaskId,
                ids.ProductId,
                BusinessDate,
                SavedAtUtc,
                "检查人",
                BusinessDate.AddDays(-1),
                new[]
                {
                    new SaveDraftItemRequest(ids.TaskItemIds[0], ids.BatchIds[0], 0, 0),
                    new SaveDraftItemRequest(ids.TaskItemIds[1], ids.BatchIds[1], 0, 2)
                }));

        Assert.Equal(3, result.CurrentItemCount);
        Assert.Equal(2, result.FilledItemCount);
        Assert.Equal(1, result.MissingItemCount);
        Assert.False(result.AllItemsFilled);
        Assert.False(result.IsDraftComplete);
        Assert.Equal(2, context.DraftItems.Count());
        Assert.Contains(context.DraftItems, item => item.CheckedQty == 0);
    }

    [Fact]
    public void SaveDraftRejectsNegativeQtyFutureVersionAndInvalidDatesBeforeWriting()
    {
        using var database = CreateScenario();
        using var context = database.Open();
        var ids = ReadIds(context);
        var useCase = new InspectionDraftUseCase();

        Assert.Throws<ArgumentOutOfRangeException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, Items: new[]
            {
                new SaveDraftItemRequest(ids.TaskItemIds[0], ids.BatchIds[0], 0, -1)
            })));
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, CheckDate: BusinessDate.AddDays(1))));
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, Items: new[]
            {
                new SaveDraftItemRequest(ids.TaskItemIds[0], ids.BatchIds[0], 1, 1)
            })));

        Assert.Empty(context.Drafts);
        Assert.Empty(context.DraftItems);
    }

    [Fact]
    public void SaveDraftRejectsDuplicateInputsAndCrossTaskOrBatchOwnership()
    {
        using var database = CreateScenario(itemCount: 2);
        long otherProductId;
        long otherTaskItemId;
        long otherBatchId;
        long normalBatchId;
        using (var seed = database.Open())
        {
            var originalTask = seed.Tasks.OrderBy(task => task.Id).First();
            normalBatchId = AddBatch(seed, originalTask.ProductId).Id;
            var otherProduct = new Product { ProductCode = "SKU-DRAFT-OTHER" };
            seed.Products.Add(otherProduct);
            seed.SaveChanges();
            var otherBatch = AddBatch(seed, otherProduct.Id);
            var otherTask = AddTask(seed, otherProduct.Id);
            var otherTaskItem = AddTaskItem(seed, otherTask.Id, otherBatch.Id, otherProduct.Id);
            otherProductId = otherProduct.Id;
            otherTaskItemId = otherTaskItem.Id;
            otherBatchId = otherBatch.Id;
        }

        using var context = database.Open();
        var ids = ReadIds(context);
        var useCase = new InspectionDraftUseCase();
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, Items: new[]
            {
                new SaveDraftItemRequest(ids.TaskItemIds[0], ids.BatchIds[0], 0, 1),
                new SaveDraftItemRequest(ids.TaskItemIds[0], ids.BatchIds[1], 0, 2)
            })));
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, Items: new[]
            {
                new SaveDraftItemRequest(ids.TaskItemIds[0], ids.BatchIds[1], 0, 1)
            })));
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, Items: new[]
            {
                new SaveDraftItemRequest(ids.TaskItemIds[0], normalBatchId, 0, 1)
            })));
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, otherProductId, BusinessDate, SavedAtUtc)));
        Assert.Throws<ArgumentException>(() => useCase.SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, Items: new[]
            {
                new SaveDraftItemRequest(otherTaskItemId, otherBatchId, 0, 1)
            })));

        Assert.Empty(context.Drafts);
    }

    [Fact]
    public void SaveDraftRejectsClosedTasksAndSystemInvalidDraftCannotBeRevived()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 1);
            var product = seed.Products.Single();
            product.EffectiveStockQty = 0;
            seed.SaveChanges();
            new StoreExpiryInspector.Application.ProductStockZeroLifecycleUseCase().Execute(
                seed,
                new(product.Id, SavedAtUtc.AddHours(1)));
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Save(context, ids, checkedQty: 2));
            Assert.Throws<InvalidOperationException>(() => new InspectionDraftUseCase().ClearDraft(
                context,
                new(ids.TaskId, ids.ProductId)));
        }

        using var verify = database.Open();
        var draft = verify.Drafts.Single();
        Assert.True(draft.IsInvalid);
        Assert.Equal("product_stock_zero", draft.InvalidReason);
        Assert.Equal(1, verify.DraftItems.Single().CheckedQty);
    }

    [Fact]
    public void SaveDraftDoesNotClearReconfirmationOrAdvanceExistingConfirmation()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, checkedQty: 2);
            var draftItem = context.DraftItems.Single();
            draftItem.ConfirmedAttentionVersion = 0;
            var taskItem = context.TaskItems.Single();
            taskItem.RequiresReconfirmation = true;
            context.SaveChanges();

            var result = Save(context, ids, checkedQty: 3, savedAtUtc: SavedAtUtc.AddHours(1));
            Assert.True(result.Changed);
            Assert.True(context.TaskItems.Single().RequiresReconfirmation);
            Assert.Equal(0, context.DraftItems.Single().ConfirmedAttentionVersion);
        }
    }

    [Fact]
    public void StageAndAttentionVersionUpgradesPreserveDraftContentAndRequireReconfirmation()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, checkedQty: 4);
        }

        using (var context = database.Open())
        {
            var item = context.TaskItems.Single();
            var batch = context.Batches.Single();
            var draft = context.Drafts.Single();
            var draftItem = context.DraftItems.Single();
            var draftUpdatedAt = draft.UpdatedAtUtc;
            var confirmed = draftItem.ConfirmedAttentionVersion;
            var result = new ProductTaskAggregator().Aggregate(
                context,
                new ProductTaskAggregationRequest(
                    ids.ProductId,
                    new[] { new ProductTaskBatchResult(batch.Id, ExpiryStageCalculator.Discount20, 0, false) },
                    SavedAtUtc.AddHours(1)));
            Assert.True(result.Changed);
            Assert.True(item.RequiresReconfirmation);
            Assert.Equal(confirmed, context.DraftItems.Single().ConfirmedAttentionVersion);
            Assert.Equal(4, context.DraftItems.Single().CheckedQty);
            Assert.Equal(draftUpdatedAt, context.Drafts.Single().UpdatedAtUtc);
        }

        using (var context = database.Open())
        {
            var item = context.TaskItems.Single();
            var batch = context.Batches.Single();
            batch.AttentionVersion = 1;
            context.SaveChanges();
            var result = new ProductTaskAggregator().Aggregate(
                context,
                new ProductTaskAggregationRequest(
                    ids.ProductId,
                    new[] { new ProductTaskBatchResult(batch.Id, ExpiryStageCalculator.Discount20, 1, true) },
                    SavedAtUtc.AddHours(2)));
            Assert.True(result.Changed);
            Assert.True(item.RequiresReconfirmation);
            Assert.Equal(1, item.AttentionVersion);
            Assert.Equal(0, context.DraftItems.Single().ConfirmedAttentionVersion);
            Assert.Equal(4, context.DraftItems.Single().CheckedQty);
        }
    }

    [Fact]
    public void StalePageCanSaveButCannotReconfirmAfterAttentionVersionUpgrade()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 1);
        }

        using var staleContext = database.Open();
        _ = staleContext.Tasks.Single();
        _ = staleContext.TaskItems.Single();
        using (var background = database.Open())
        {
            var batch = background.Batches.Single();
            batch.AttentionVersion = 2;
            background.SaveChanges();
            new ProductTaskAggregator().Aggregate(
                background,
                new ProductTaskAggregationRequest(
                    ids.ProductId,
                    new[] { new ProductTaskBatchResult(batch.Id, ExpiryStageCalculator.Discount20, 2, true) },
                    SavedAtUtc.AddHours(1)));
            Assert.True(background.TaskItems.Single().RequiresReconfirmation);
        }

        Assert.True(staleContext.TaskItems.AsNoTracking().Single().RequiresReconfirmation);
        var save = Save(staleContext, ids, checkedQty: 8, savedAtUtc: SavedAtUtc.AddHours(2));
        Assert.True(save.Changed);
        Assert.Equal(1, save.RequiresReconfirmationCount);
        using (var afterSave = database.Open())
        {
            Assert.True(afterSave.TaskItems.Single().RequiresReconfirmation);
        }
        Assert.True(staleContext.ChangeTracker.Entries<ProductTaskItem>().Single().Entity.RequiresReconfirmation);
        Assert.Equal(2, staleContext.TaskItems.Single().AttentionVersion);
        Assert.Equal(0, staleContext.DraftItems.Single().ConfirmedAttentionVersion);
        Assert.Equal(8, staleContext.DraftItems.Single().CheckedQty);

        Assert.Throws<ArgumentException>(() => new InspectionDraftUseCase().ReconfirmItem(
            staleContext,
            new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 1, SavedAtUtc.AddHours(3))));
        var confirmed = new InspectionDraftUseCase().ReconfirmItem(
            staleContext,
            new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 2, SavedAtUtc.AddHours(4)));
        Assert.True(confirmed.Changed);
        Assert.False(staleContext.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal(2, staleContext.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void ReconfirmRequiresCheckedQtyAndOnlyChangesTargetItem()
    {
        using var database = CreateScenario(itemCount: 2);
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, checkedQty: null, itemIndex: 0);
            Save(context, ids, checkedQty: 4, itemIndex: 1, savedAtUtc: SavedAtUtc.AddMinutes(1));
            foreach (var item in context.TaskItems)
            {
                item.RequiresReconfirmation = true;
            }

            context.SaveChanges();
            Assert.Throws<InvalidOperationException>(() => new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(1))));
            var result = new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[1], ids.BatchIds[1], 0, SavedAtUtc.AddHours(2)));
            Assert.True(result.Changed);
            var items = context.TaskItems.OrderBy(item => item.Id).ToArray();
            Assert.True(items[0].RequiresReconfirmation);
            Assert.False(items[1].RequiresReconfirmation);
            Assert.Equal(0, context.DraftItems.OrderBy(item => item.TaskItemId).First().ConfirmedAttentionVersion);
            Assert.Equal(0, context.DraftItems.OrderBy(item => item.TaskItemId).Last().ConfirmedAttentionVersion);
        }
    }

    [Fact]
    public void SaveDraftCanBeRecoveredThroughInspectionTaskQuery()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            Save(context, ids, inspectorName: "张三", checkDate: BusinessDate, checkedQty: 6);
        }

        using var verify = database.Open();
        var detail = new InspectionTaskQuery().GetDetail(verify, ids.TaskId);
        Assert.NotNull(detail.Detail);
        Assert.NotNull(detail.Detail!.Draft);
        Assert.Equal("张三", detail.Detail.Draft!.InspectorName);
        Assert.Equal(6, detail.Detail.TaskItems.Single().CheckedQty);
    }

    [Fact]
    public void SaveDraftFailureRollsBackAllWritesAndClearsTracker()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_draft_item_insert
                BEFORE INSERT ON draft_items
                BEGIN
                    SELECT RAISE(ABORT, 'draft failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Save(context, ids, checkedQty: 7));
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.Empty(context.Drafts);
            Assert.Empty(context.DraftItems);
        }

        using var verify = database.Open();
        Assert.Empty(verify.Drafts);
        Assert.Empty(verify.DraftItems);
    }

    [Fact]
    public void ReconfirmFailureRollsBackDraftItemTaskItemAndDraftTimestamps()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 7);
            var item = seed.TaskItems.Single();
            item.RequiresReconfirmation = true;
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var beforeTaskItem = context.TaskItems.AsNoTracking().Single();
            var beforeDraft = context.Drafts.AsNoTracking().Single();
            var beforeDraftItem = context.DraftItems.AsNoTracking().Single();
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_task_item_update
                BEFORE UPDATE OF requires_reconfirmation ON task_items
                BEGIN
                    SELECT RAISE(ABORT, 'reconfirm failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(1))));
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.Equal(beforeTaskItem.RequiresReconfirmation, context.TaskItems.Single().RequiresReconfirmation);
            Assert.Equal(beforeTaskItem.UpdatedAtUtc, context.TaskItems.Single().UpdatedAtUtc);
            Assert.Equal(beforeDraft.UpdatedAtUtc, context.Drafts.Single().UpdatedAtUtc);
            Assert.Equal(beforeDraftItem.ConfirmedAttentionVersion, context.DraftItems.Single().ConfirmedAttentionVersion);
        }
    }

    [Fact]
    public void OuterTransactionRollbackRemovesSaveAndReconfirmChanges()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var context = database.Open())
        {
            ids = ReadIds(context);
            using var transaction = context.Database.BeginTransaction();
            Save(context, ids, checkedQty: 3);
            Assert.Single(context.Drafts);
            transaction.Rollback();
        }

        using (var verify = database.Open())
        {
            Assert.Empty(verify.Drafts);
            Assert.Empty(verify.DraftItems);
        }

        using (var seed = database.Open())
        {
            Save(seed, ids, checkedQty: 3);
            seed.TaskItems.Single().RequiresReconfirmation = true;
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            using var transaction = context.Database.BeginTransaction();
            new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(1)));
            Assert.False(context.TaskItems.Single().RequiresReconfirmation);
            transaction.Rollback();
        }

        using var final = database.Open();
        Assert.True(final.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal(0, final.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void ReconfirmRejectsMismatchedCurrentTaskAndBatchVersionsWithoutWrite()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 3);
            var item = seed.TaskItems.Single();
            item.RequiresReconfirmation = true;
            item.AttentionVersion = 1;
            seed.SaveChanges();
        }

        using var context = database.Open();
        Assert.Throws<InvalidOperationException>(() => new InspectionDraftUseCase().ReconfirmItem(
            context,
            new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 1, SavedAtUtc.AddHours(1))));
        Assert.True(context.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal(0, context.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void ReconfirmRejectsNotRequiredButMismatchedConfirmationInsteadOfRepairingIt()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 3);
            seed.DraftItems.Single().ConfirmedAttentionVersion = 1;
            seed.SaveChanges();
        }

        using var context = database.Open();
        Assert.Throws<InvalidOperationException>(() => new InspectionDraftUseCase().ReconfirmItem(
            context,
            new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(1))));
        Assert.False(context.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal(1, context.DraftItems.Single().ConfirmedAttentionVersion);
    }

    [Fact]
    public void InvalidDraftCannotBeClearedAndOtherBusinessTablesRemainUnchanged()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            var draft = new InspectionDraft
            {
                TaskId = ids.TaskId,
                IsInvalid = true,
                InvalidReason = "system",
                InvalidatedAtUtc = SavedAtUtc,
                CreatedAtUtc = SavedAtUtc,
                UpdatedAtUtc = SavedAtUtc
            };
            seed.Drafts.Add(draft);
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => new InspectionDraftUseCase().ClearDraft(
                context,
                new(ids.TaskId, ids.ProductId)));
        }

        using var verify = database.Open();
        Assert.True(verify.Drafts.Single().IsInvalid);
        Assert.Equal(1, verify.Products.Count());
        Assert.Equal(1, verify.Batches.Count());
        Assert.Equal(1, verify.Tasks.Count());
        Assert.Equal(1, verify.TaskItems.Count());
    }

    [Fact]
    public void SaveDraftOuterTransactionFailureRestoresTrackerAndDatabase()
    {
        using var database = CreateScenario();
        using (var context = database.Open())
        {
            var ids = ReadIds(context);
            using var transaction = context.Database.BeginTransaction();
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_draft_item_insert_outer
                BEFORE INSERT ON draft_items
                BEGIN
                    SELECT RAISE(ABORT, 'draft failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Save(context, ids, checkedQty: 2));
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.Empty(context.Drafts);
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.Empty(verify.Drafts);
        Assert.Empty(verify.DraftItems);
    }

    [Fact]
    public void ReconfirmOuterTransactionFailureRestoresAllTrackedValues()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 5);
            seed.TaskItems.Single().RequiresReconfirmation = true;
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            using var transaction = context.Database.BeginTransaction();
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_task_item_update_outer
                BEFORE UPDATE OF requires_reconfirmation ON task_items
                BEGIN
                    SELECT RAISE(ABORT, 'reconfirm failure');
                END;
                """);
            Assert.Throws<DbUpdateException>(() => new InspectionDraftUseCase().ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(1))));
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.True(context.TaskItems.Single().RequiresReconfirmation);
            Assert.Equal(0, context.DraftItems.Single().ConfirmedAttentionVersion);
            transaction.Rollback();
        }
    }

    [Fact]
    public void ClearDraftFailureDoesNotDeleteEitherDraftTable()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 9);
        }

        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_draft_delete
                BEFORE DELETE ON drafts
                BEGIN
                    SELECT RAISE(ABORT, 'clear failure');
                END;
                """);
            Assert.Throws<DbUpdateException>(() => new InspectionDraftUseCase().ClearDraft(
                context,
                new(ids.TaskId, ids.ProductId)));
            Assert.False(context.ChangeTracker.HasChanges());
        }

        using var verify = database.Open();
        Assert.Single(verify.Drafts);
        Assert.Single(verify.DraftItems);
    }

    [Fact]
    public void SaveDraftRejectsTaskItemBatchVersionMismatchWithoutCreatingDraft()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            seed.TaskItems.Single().AttentionVersion = 1;
            seed.SaveChanges();
        }

        using var context = database.Open();
        Assert.Throws<InvalidOperationException>(() => Save(context, ids, checkedQty: 1));
        Assert.Empty(context.Drafts);
        Assert.Equal(1, context.TaskItems.Single().AttentionVersion);
    }

    [Fact]
    public void BlankInspectorNameNormalizesToNullAndDateBoundaryIsAccepted()
    {
        using var database = CreateScenario();
        using var context = database.Open();
        var ids = ReadIds(context);
        var result = new InspectionDraftUseCase().SaveDraft(
            context,
            new(ids.TaskId, ids.ProductId, BusinessDate, SavedAtUtc, " \t ", BusinessDate));
        Assert.False(result.HasInspectorName);
        Assert.True(result.HasCheckDate);
        Assert.Null(context.Drafts.Single().InspectorName);
        Assert.Equal(BusinessDate, context.Drafts.Single().CheckDate);
    }

    [Fact]
    public void CompletedTaskRejectsAllDraftActionsWithoutChangingItsHistory()
    {
        using var database = CreateScenario();
        ScenarioIds ids;
        using (var seed = database.Open())
        {
            ids = ReadIds(seed);
            Save(seed, ids, checkedQty: 1);
            var task = seed.Tasks.Single();
            task.Status = "completed";
            task.ClosedAtUtc = SavedAtUtc.AddHours(1);
            task.CloseReason = "submitted";
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var useCase = new InspectionDraftUseCase();
            Assert.Throws<InvalidOperationException>(() => Save(context, ids, checkedQty: 2));
            Assert.Throws<InvalidOperationException>(() => useCase.ReconfirmItem(
                context,
                new(ids.TaskId, ids.ProductId, ids.TaskItemIds[0], ids.BatchIds[0], 0, SavedAtUtc.AddHours(2))));
            Assert.Throws<InvalidOperationException>(() => useCase.ClearDraft(
                context,
                new(ids.TaskId, ids.ProductId)));
        }

        using var verify = database.Open();
        Assert.Equal("completed", verify.Tasks.Single().Status);
        Assert.Single(verify.Drafts);
        Assert.Equal(1, verify.DraftItems.Single().CheckedQty);
    }

    private static SaveDraftResult Save(
        StoreDbContext context,
        ScenarioIds ids,
        string? inspectorName = null,
        DateOnly? checkDate = null,
        int? checkedQty = 1,
        int itemIndex = 0,
        DateTime? savedAtUtc = null)
    {
        return new InspectionDraftUseCase().SaveDraft(
            context,
            new(
                ids.TaskId,
                ids.ProductId,
                BusinessDate,
                savedAtUtc ?? SavedAtUtc,
                inspectorName,
                checkDate,
                new[] { new SaveDraftItemRequest(ids.TaskItemIds[itemIndex], ids.BatchIds[itemIndex], 0, checkedQty) }));
    }

    private static void SetReconfirmation(StoreDbContext context, ScenarioIds ids, bool value)
    {
        var item = context.TaskItems.Single(taskItem => taskItem.Id == ids.TaskItemIds[0]);
        item.RequiresReconfirmation = value;
        context.SaveChanges();
    }

    private static ScenarioIds ReadIds(StoreDbContext context)
    {
        var task = context.Tasks.OrderBy(task => task.Id).First();
        return new(
            task.Id,
            task.ProductId,
            context.TaskItems.Where(item => item.TaskId == task.Id).OrderBy(item => item.Id).Select(item => item.Id).ToArray(),
            context.TaskItems.Where(item => item.TaskId == task.Id).OrderBy(item => item.Id).Select(item => item.BatchId).ToArray());
    }

    private static Batch AddBatch(StoreDbContext context, long productId)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ExpiryDate = BusinessDate.AddDays(30),
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            AttentionVersion = 0
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
            HighestStage = ExpiryStageCalculator.Discount50,
            CreatedAtUtc = SavedAtUtc,
            UpdatedAtUtc = SavedAtUtc
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
            CreatedAtUtc = SavedAtUtc,
            UpdatedAtUtc = SavedAtUtc
        };
        context.TaskItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static SqliteTestDatabase CreateScenario(int itemCount = 1)
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = new Product { ProductCode = "SKU-DRAFT-USECASE" };
        context.Products.Add(product);
        context.SaveChanges();
        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Discount50,
            CreatedAtUtc = SavedAtUtc,
            UpdatedAtUtc = SavedAtUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();
        for (var index = 0; index < itemCount; index++)
        {
            var batch = new Batch
            {
                ProductId = product.Id,
                ExpiryDate = BusinessDate.AddDays(10 + index),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 10,
                MaxArrivalQty = 10,
                AttentionVersion = 0
            };
            context.Batches.Add(batch);
            context.SaveChanges();
            context.TaskItems.Add(new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = ExpiryStageCalculator.Discount50,
                AttentionVersion = 0,
                RequiresReconfirmation = false,
                CreatedAtUtc = SavedAtUtc,
                UpdatedAtUtc = SavedAtUtc
            });
            context.SaveChanges();
        }

        return database;
    }

    private sealed record ScenarioIds(
        long TaskId,
        long ProductId,
        long[] TaskItemIds,
        long[] BatchIds);
}
