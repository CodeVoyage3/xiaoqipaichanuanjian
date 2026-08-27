using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ProductTaskAggregationTests
{
    private static readonly DateTime AtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CanonicalStagePriorityIsSingleOrderedImplementation()
    {
        Assert.Equal(0, ExpiryStageCalculator.GetStagePriority(ExpiryStageCalculator.None));
        Assert.True(
            ExpiryStageCalculator.CompareStages(ExpiryStageCalculator.Expired, ExpiryStageCalculator.Withdraw) > 0);
        Assert.True(
            ExpiryStageCalculator.CompareStages(ExpiryStageCalculator.Withdraw, ExpiryStageCalculator.Discount20) > 0);
        Assert.True(
            ExpiryStageCalculator.CompareStages(ExpiryStageCalculator.Discount20, ExpiryStageCalculator.Discount50) > 0);
        Assert.Throws<ArgumentException>(() => ExpiryStageCalculator.GetStagePriority("unknown"));
    }

    [Fact]
    public void SingleTrackableBatchCreatesOneTaskAndOneItem()
    {
        using var scenario = Scenario.Create();
        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount50)));

            Assert.True(result.Changed);
            Assert.Equal(1, result.AddedItemCount);
            Assert.Equal(0, result.UpdatedItemCount);
            Assert.Equal(ExpiryStageCalculator.Discount50, result.HighestStage);
        }

        using var verify = scenario.Open();
        Assert.Equal(1, verify.Tasks.Count());
        Assert.Equal(1, verify.TaskItems.Count());
        var item = verify.TaskItems.Single();
        Assert.Equal(scenario.BatchId, item.BatchId);
        Assert.Equal(ExpiryStageCalculator.Discount50, item.Stage);
        Assert.Equal(0, item.AttentionVersion);
    }

    [Fact]
    public void MultipleBatchesCreateOneTaskAndKeepEachStage()
    {
        using var scenario = Scenario.Create(batchCount: 3);
        using (var context = scenario.Open())
        {
            var results = Scenario.BatchResults(
                (scenario.BatchIds[0], ExpiryStageCalculator.Discount50),
                (scenario.BatchIds[1], ExpiryStageCalculator.Discount20),
                (scenario.BatchIds[2], ExpiryStageCalculator.Withdraw));
            var result = Aggregate(context, scenario.ProductId, results);

            Assert.Equal(ExpiryStageCalculator.Withdraw, result.HighestStage);
            Assert.Equal(3, result.AddedItemCount);
        }

        using var verify = scenario.Open();
        Assert.Equal(1, verify.Tasks.Count());
        Assert.Equal(3, verify.TaskItems.Count());
        var stages = verify.TaskItems
            .OrderBy(item => item.BatchId)
            .Select(item => item.Stage)
            .ToArray();
        Assert.Equal(
            new[]
            {
                ExpiryStageCalculator.Discount50,
                ExpiryStageCalculator.Discount20,
                ExpiryStageCalculator.Withdraw
            },
            stages);
    }

    [Fact]
    public void ExistingOpenTaskAbsorbsNewBatchWithoutCreatingSecondTask()
    {
        using var scenario = Scenario.Create(batchCount: 2);
        long taskId;
        using (var seed = scenario.Open())
        {
            Aggregate(seed, scenario.ProductId, Scenario.BatchResults((scenario.BatchIds[0], ExpiryStageCalculator.Discount50)));
            taskId = seed.Tasks.Single().Id;
        }

        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchIds[1], ExpiryStageCalculator.Withdraw)));

            Assert.Equal(taskId, result.TaskId);
            Assert.Equal(1, result.AddedItemCount);
            Assert.Equal(1, context.Tasks.Count(task => task.Status == "open"));
            Assert.Equal(2, context.TaskItems.Count());
        }
    }

    [Fact]
    public void LowerNewBatchDoesNotLowerHighestStageAndHigherBatchUpgradesTask()
    {
        using var scenario = Scenario.Create(batchCount: 3);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchIds[0], ExpiryStageCalculator.Withdraw)));
        }

        using (var context = scenario.Open())
        {
            var lower = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchIds[1], ExpiryStageCalculator.Discount50)));
            Assert.Equal(ExpiryStageCalculator.Withdraw, lower.HighestStage);
        }

        using (var context = scenario.Open())
        {
            var higher = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchIds[2], ExpiryStageCalculator.Expired)));
            Assert.Equal(ExpiryStageCalculator.Expired, higher.HighestStage);
            Assert.Equal(ExpiryStageCalculator.Expired, context.Tasks.Single().HighestStage);
        }
    }

    [Fact]
    public void ExistingItemUpgradesInPlaceWithoutDuplicateAndRecomputesHighestStage()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount50)));
        }

        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount20)));

            Assert.Equal(1, result.UpdatedItemCount);
            Assert.Equal(1, context.TaskItems.Count());
            Assert.Equal(ExpiryStageCalculator.Discount20, context.TaskItems.Single().Stage);
            Assert.Equal(ExpiryStageCalculator.Discount20, context.Tasks.Single().HighestStage);
        }
    }

    [Fact]
    public void ExistingItemCanUpgradeFromWithdrawToExpiredWithoutDuplicate()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount20)));
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw)));
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Expired)));
        }

        using var verify = scenario.Open();
        Assert.Equal(1, verify.Tasks.Count());
        Assert.Equal(1, verify.TaskItems.Count());
        Assert.Equal(ExpiryStageCalculator.Expired, verify.TaskItems.Single().Stage);
        Assert.Equal(ExpiryStageCalculator.Expired, verify.Tasks.Single().HighestStage);
    }

    [Fact]
    public void ExactRepeatDoesNotChangeFieldsOrTimestamps()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        long taskId;
        long itemId;
        ProductTask firstTask;
        ProductTaskItem firstItem;
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount50)));
            firstTask = context.Tasks.AsNoTracking().Single();
            firstItem = context.TaskItems.AsNoTracking().Single();
            taskId = firstTask.Id;
            itemId = firstItem.Id;
        }

        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount50)));
            Assert.False(result.Changed);
            Assert.Equal(taskId, result.TaskId);
            Assert.Equal(0, result.AddedItemCount);
            Assert.Equal(0, result.UpdatedItemCount);
        }

        using var verify = scenario.Open();
        var secondTask = verify.Tasks.AsNoTracking().Single();
        var secondItem = verify.TaskItems.AsNoTracking().Single();
        Assert.Equal(taskId, secondTask.Id);
        Assert.Equal(itemId, secondItem.Id);
        Assert.Equal(firstTask.Id, secondTask.Id);
        Assert.Equal(firstTask.ProductId, secondTask.ProductId);
        Assert.Equal(firstTask.Status, secondTask.Status);
        Assert.Equal(firstTask.HighestStage, secondTask.HighestStage);
        Assert.Equal(firstTask.CreatedAtUtc, secondTask.CreatedAtUtc);
        Assert.Equal(firstTask.UpdatedAtUtc, secondTask.UpdatedAtUtc);
        Assert.Equal(firstItem.Id, secondItem.Id);
        Assert.Equal(firstItem.TaskId, secondItem.TaskId);
        Assert.Equal(firstItem.BatchId, secondItem.BatchId);
        Assert.Equal(firstItem.ProductId, secondItem.ProductId);
        Assert.Equal(firstItem.Stage, secondItem.Stage);
        Assert.Equal(firstItem.AttentionVersion, secondItem.AttentionVersion);
        Assert.Equal(firstItem.RequiresReconfirmation, secondItem.RequiresReconfirmation);
        Assert.Equal(firstItem.CreatedAtUtc, secondItem.CreatedAtUtc);
        Assert.Equal(firstItem.UpdatedAtUtc, secondItem.UpdatedAtUtc);
    }

    [Fact]
    public void DifferentProductsHaveIndependentOpenTasks()
    {
        using var scenario = Scenario.Create(batchCount: 1, secondProduct: true);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw)));
            Aggregate(context, scenario.SecondProductId, Scenario.BatchResults((scenario.SecondBatchId, ExpiryStageCalculator.Expired)));
        }

        using var verify = scenario.Open();
        Assert.Equal(2, verify.Tasks.Count(task => task.Status == "open"));
        Assert.Equal(2, verify.TaskItems.Count());
        Assert.Equal(
            new[] { scenario.ProductId, scenario.SecondProductId },
            verify.Tasks.OrderBy(task => task.ProductId).Select(task => task.ProductId).ToArray());
    }

    [Fact]
    public void NoneDoesNotCreateTaskAndDoesNotRemoveExistingItem()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        using (var context = scenario.Open())
        {
            var noneResult = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.None)));
            Assert.False(noneResult.Changed);
            Assert.Null(noneResult.TaskId);
            Assert.Empty(context.Tasks);
        }

        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw)));
            var noneResult = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.None)));
            Assert.False(noneResult.Changed);
            Assert.Single(context.Tasks);
            Assert.Single(context.TaskItems);
            Assert.Equal(ExpiryStageCalculator.Withdraw, context.TaskItems.Single().Stage);
        }
    }

    [Fact]
    public void ValidDraftIsPreservedAndStageUpgradeRequiresReconfirmation()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        var draftTime = AtUtc.AddHours(-1);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount50)));
            var task = context.Tasks.Single();
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "张三",
                CheckDate = new DateOnly(2026, 8, 27),
                CreatedAtUtc = draftTime,
                UpdatedAtUtc = draftTime
            };
            context.Drafts.Add(draft);
            context.SaveChanges();
            var item = context.TaskItems.Single();
            context.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = item.Id,
                TaskId = task.Id,
                CheckedQty = 7,
                ConfirmedAttentionVersion = 0
            });
            context.SaveChanges();
        }

        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Discount20)));
        }

        using var verify = scenario.Open();
        var draftAfter = verify.Drafts.AsNoTracking().Single();
        var draftItemAfter = verify.DraftItems.AsNoTracking().Single();
        Assert.Equal("张三", draftAfter.InspectorName);
        Assert.Equal(draftTime, draftAfter.CreatedAtUtc);
        Assert.Equal(draftTime, draftAfter.UpdatedAtUtc);
        Assert.Equal(7, draftItemAfter.CheckedQty);
        Assert.Equal(0, draftItemAfter.ConfirmedAttentionVersion);
        Assert.True(verify.TaskItems.Single().RequiresReconfirmation);
    }

    [Fact]
    public void AttentionVersionUpgradePreservesOldDraftConfirmationAndRequiresReconfirmation()
    {
        using var scenario = Scenario.Create(batchCount: 1, attentionVersion: 1);
        var draftTime = AtUtc.AddHours(-1);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw, 1)));
            var task = context.Tasks.Single();
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "李四",
                CreatedAtUtc = draftTime,
                UpdatedAtUtc = draftTime
            };
            context.Drafts.Add(draft);
            context.SaveChanges();
            context.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = context.TaskItems.Single().Id,
                TaskId = task.Id,
                CheckedQty = 4,
                ConfirmedAttentionVersion = 1
            });
            context.SaveChanges();
        }

        using (var mutate = scenario.Open())
        {
            var batch = mutate.Batches.Single();
            batch.AttentionVersion = 2;
            mutate.SaveChanges();
        }

        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw, 2)));
            Assert.True(result.Changed);
        }

        using var verify = scenario.Open();
        Assert.Equal(2, verify.TaskItems.Single().AttentionVersion);
        Assert.True(verify.TaskItems.Single().RequiresReconfirmation);
        Assert.Equal(1, verify.DraftItems.Single().ConfirmedAttentionVersion);
        Assert.Equal(4, verify.DraftItems.Single().CheckedQty);
    }

    [Fact]
    public void ExplicitReconfirmationIsStickyAndFalseDoesNotClearIt()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw, 0, true)));
        }

        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw, 0, false)));
            Assert.False(result.Changed);
        }

        using var verify = scenario.Open();
        Assert.True(verify.TaskItems.Single().RequiresReconfirmation);
    }

    [Fact]
    public void RepeatedCallWithValidDraftDoesNotTouchDraftOrItem()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        var draftTime = AtUtc.AddHours(-1);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw)));
            var task = context.Tasks.Single();
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "王五",
                CreatedAtUtc = draftTime,
                UpdatedAtUtc = draftTime
            };
            context.Drafts.Add(draft);
            context.SaveChanges();
            context.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = context.TaskItems.Single().Id,
                TaskId = task.Id,
                CheckedQty = 2,
                ConfirmedAttentionVersion = 0
            });
            context.SaveChanges();
        }

        var before = ReadTaskDraft(scenario);
        using (var context = scenario.Open())
        {
            var result = Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw)));
            Assert.False(result.Changed);
        }

        Assert.Equal(before, ReadTaskDraft(scenario));
    }

    [Fact]
    public void OuterTransactionCanRollBackEntireAggregation()
    {
        using var scenario = Scenario.Create(batchCount: 2);
        using (var context = scenario.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            Aggregate(
                context,
                scenario.ProductId,
                Scenario.BatchResults(
                    (scenario.BatchIds[0], ExpiryStageCalculator.Discount50),
                    (scenario.BatchIds[1], ExpiryStageCalculator.Withdraw)));
            transaction.Rollback();
        }

        using var verify = scenario.Open();
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.TaskItems);
    }

    [Fact]
    public void SQLiteFailureRollsBackTaskAndAllItems()
    {
        using var scenario = Scenario.Create(batchCount: 2);
        using (var trigger = scenario.Open())
        {
            trigger.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_task_item
                BEFORE INSERT ON task_items
                BEGIN
                    SELECT RAISE(ABORT, 'forced task item failure');
                END;
                """);
        }

        using (var context = scenario.Open())
        {
            Assert.Throws<DbUpdateException>(() => Aggregate(
                context,
                scenario.ProductId,
                Scenario.BatchResults(
                    (scenario.BatchIds[0], ExpiryStageCalculator.Discount50),
                    (scenario.BatchIds[1], ExpiryStageCalculator.Withdraw))));
        }

        using var verify = scenario.Open();
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.TaskItems);
    }

    [Fact]
    public void SQLiteFailureInsideOuterTransactionLeavesOuterTransactionForCaller()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        using var context = scenario.Open();
        using var transaction = context.Database.BeginTransaction();
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_task_item_outer
            BEFORE INSERT ON task_items
            BEGIN
                SELECT RAISE(ABORT, 'forced task item failure');
            END;
            """);

        Assert.Throws<DbUpdateException>(() => Aggregate(
            context,
            scenario.ProductId,
            Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw))));
        Assert.NotNull(context.Database.CurrentTransaction);
        transaction.Rollback();

        using var verify = scenario.Open();
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.TaskItems);
    }

    [Fact]
    public void SQLiteOpenTaskUniqueConstraintRemainsLastProtection()
    {
        using var scenario = Scenario.Create(batchCount: 1);
        using var context = scenario.Open();
        context.Tasks.Add(new ProductTask
        {
            ProductId = scenario.ProductId,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Withdraw,
            CreatedAtUtc = AtUtc,
            UpdatedAtUtc = AtUtc
        });
        context.SaveChanges();
        context.Tasks.Add(new ProductTask
        {
            ProductId = scenario.ProductId,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Expired,
            CreatedAtUtc = AtUtc,
            UpdatedAtUtc = AtUtc
        });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void InvalidInputIsRejectedBeforeAnyWrite()
    {
        using var scenario = Scenario.Create(batchCount: 2);
        using var context = scenario.Open();
        var cases = new[]
        {
            new ProductTaskBatchResult(scenario.BatchIds[0], "invalid", 0, false),
            new ProductTaskBatchResult(scenario.BatchIds[0], ExpiryStageCalculator.Withdraw, -1, false),
            new ProductTaskBatchResult(scenario.BatchIds[0], ExpiryStageCalculator.Withdraw, 0, false),
            new ProductTaskBatchResult(scenario.BatchIds[0], ExpiryStageCalculator.Withdraw, 0, false)
        };

        Assert.Throws<ArgumentException>(() => Aggregate(context, scenario.ProductId, new[] { cases[0] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Aggregate(context, scenario.ProductId, new[] { cases[1] }));
        Assert.Throws<ArgumentException>(() => Aggregate(context, scenario.ProductId, new[]
        {
            cases[2],
            new ProductTaskBatchResult(scenario.BatchIds[0], ExpiryStageCalculator.Withdraw, 0, false)
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Aggregate(
            context,
            0,
            new[] { cases[2] }));

        Assert.Empty(context.Tasks);
        Assert.Empty(context.TaskItems);
    }

    [Fact]
    public void MissingOrCrossProductBatchAndStaleVersionAreRejectedWithoutWrites()
    {
        using var scenario = Scenario.Create(batchCount: 1, secondProduct: true, attentionVersion: 3);
        using var context = scenario.Open();

        Assert.Throws<KeyNotFoundException>(() => Aggregate(
            context,
            scenario.ProductId,
            Scenario.BatchResults((999999, ExpiryStageCalculator.Withdraw, 3))));
        Assert.Throws<ArgumentException>(() => Aggregate(
            context,
            scenario.ProductId,
            Scenario.BatchResults((scenario.SecondBatchId, ExpiryStageCalculator.Withdraw, 0))));
        Assert.Throws<ArgumentException>(() => Aggregate(
            context,
            scenario.ProductId,
            Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw, 2))));
        Assert.Throws<KeyNotFoundException>(() => Aggregate(
            context,
            999999,
            Scenario.BatchResults((scenario.BatchId, ExpiryStageCalculator.Withdraw, 3))));

        Assert.Empty(context.Tasks);
        Assert.Empty(context.TaskItems);
    }

    [Fact]
    public void ExistingItemDowngradeIsRejectedAtomically()
    {
        using var scenario = Scenario.Create(batchCount: 2);
        using (var context = scenario.Open())
        {
            Aggregate(context, scenario.ProductId, Scenario.BatchResults((scenario.BatchIds[0], ExpiryStageCalculator.Withdraw)));
        }

        using (var context = scenario.Open())
        {
            Assert.Throws<ArgumentException>(() => Aggregate(
                context,
                scenario.ProductId,
                Scenario.BatchResults(
                    (scenario.BatchIds[0], ExpiryStageCalculator.Discount50),
                    (scenario.BatchIds[1], ExpiryStageCalculator.Discount20))));
        }

        using var verify = scenario.Open();
        Assert.Single(verify.TaskItems);
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.TaskItems.Single().Stage);
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.Tasks.Single().HighestStage);
    }

    private static ProductTaskAggregationResult Aggregate(
        StoreDbContext context,
        long productId,
        IReadOnlyList<ProductTaskBatchResult> results) =>
        new ProductTaskAggregator().Aggregate(
            context,
            new ProductTaskAggregationRequest(productId, results, AtUtc));

    private static (string InspectorName, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, int? CheckedQty, int ConfirmedVersion) ReadTaskDraft(
        Scenario scenario)
    {
        using var context = scenario.Open();
        var draft = context.Drafts.AsNoTracking().Single();
        var item = context.DraftItems.AsNoTracking().Single();
        return (draft.InspectorName!, draft.CreatedAtUtc, draft.UpdatedAtUtc, item.CheckedQty, item.ConfirmedAttentionVersion);
    }

    private sealed class Scenario : IDisposable
    {
        private Scenario(SqliteTestDatabase database)
        {
            Database = database;
        }

        public long ProductId { get; private set; }

        public long SecondProductId { get; private set; }

        public long BatchId { get; private set; }

        public long SecondBatchId { get; private set; }

        public long[] BatchIds { get; private set; } = Array.Empty<long>();

        public SqliteTestDatabase Database { get; }

        public static Scenario Create(
            int batchCount = 1,
            bool secondProduct = false,
            int attentionVersion = 0)
        {
            var database = SqliteTestDatabase.Create();
            using var context = database.Open();
            var product = new Product { ProductCode = $"SKU-{Guid.NewGuid():N}" };
            context.Products.Add(product);
            context.SaveChanges();
            var scenario = new Scenario(database)
            {
                ProductId = product.Id
            };

            var batches = Enumerable.Range(0, batchCount)
                .Select(index => new Batch
                {
                    ProductId = product.Id,
                    ExpiryDate = new DateOnly(2026, 12, 31).AddDays(index),
                    ShelfLifeValue = 270,
                    ShelfLifeUnit = "D",
                    CurrentArrivalQty = 10,
                    MaxArrivalQty = 10,
                    AttentionVersion = attentionVersion
                })
                .ToArray();
            context.Batches.AddRange(batches);

            if (secondProduct)
            {
                var otherProduct = new Product { ProductCode = $"SKU-{Guid.NewGuid():N}" };
                context.Products.Add(otherProduct);
                context.SaveChanges();
                scenario.SecondProductId = otherProduct.Id;
                var otherBatch = new Batch
                {
                    ProductId = otherProduct.Id,
                    ExpiryDate = new DateOnly(2027, 1, 1),
                    ShelfLifeValue = 270,
                    ShelfLifeUnit = "D",
                    CurrentArrivalQty = 10,
                    MaxArrivalQty = 10,
                    AttentionVersion = 0
                };
                context.Batches.Add(otherBatch);
                context.SaveChanges();
                scenario.SecondBatchId = otherBatch.Id;
            }
            else
            {
                context.SaveChanges();
                scenario.SecondProductId = 0;
                scenario.SecondBatchId = 0;
            }

            scenario.BatchIds = batches.Select(batch => batch.Id).ToArray();
            scenario.BatchId = scenario.BatchIds[0];
            return scenario;
        }

        public static ProductTaskBatchResult[] BatchResults(
            params (long BatchId, string Stage)[] results) =>
            results.Select(result => new ProductTaskBatchResult(result.BatchId, result.Stage, 0, false)).ToArray();

        public static ProductTaskBatchResult[] BatchResults(
            params (long BatchId, string Stage, int AttentionVersion)[] results) =>
            results.Select(result => new ProductTaskBatchResult(result.BatchId, result.Stage, result.AttentionVersion, false)).ToArray();

        public static ProductTaskBatchResult[] BatchResults(
            params (long BatchId, string Stage, int AttentionVersion, bool RequiresReconfirmation)[] results) =>
            results.Select(result => new ProductTaskBatchResult(
                result.BatchId,
                result.Stage,
                result.AttentionVersion,
                result.RequiresReconfirmation)).ToArray();

        public void Dispose() => Database.Dispose();

        public StoreDbContext Open() => Database.Open();
    }
}
