using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class StartupRecalculationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 27);
    private static readonly DateTime SeedUtc = new(2026, 8, 27, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RunUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FutureAndStoppedBatchesAreNotCandidatesButDueActiveBatchIsProcessed()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            AddBatch(seed, product.Id, BusinessDate.AddDays(30), BusinessDate, ExpiryStageCalculator.None);
            AddBatch(seed, product.Id, BusinessDate.AddDays(31), BusinessDate.AddDays(1), ExpiryStageCalculator.None);
            AddBatch(
                seed,
                product.Id,
                BusinessDate.AddDays(32),
                BusinessDate.AddDays(-1),
                ExpiryStageCalculator.None,
                trackingStatus: "stopped");
        }

        using (var context = database.Open())
        {
            var result = Execute(context);

            Assert.Equal(1, result.MatchedBatchCount);
            Assert.Equal(1, result.ChangedBatchCount);
            Assert.Equal(1, result.AggregatedBatchCount);
            Assert.Equal(1, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        Assert.Equal(1, verify.Batches.Count(batch => batch.CurrentStage == ExpiryStageCalculator.Discount50));
        Assert.Equal(1, verify.Batches.Count(batch => batch.NextTriggerDate == BusinessDate.AddDays(1)));
        Assert.Equal(1, verify.Batches.Count(batch => batch.TrackingStatus == "stopped"));
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
    }

    [Fact]
    public void TriggerDateEqualToBusinessDateProcessesThatDay()
    {
        using var database = SqliteTestDatabase.Create();
        long batchId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            batchId = AddBatch(
                seed,
                product.Id,
                BusinessDate.AddDays(30),
                BusinessDate,
                ExpiryStageCalculator.None).Id;
        }

        using (var context = database.Open())
        {
            var result = Execute(context);

            Assert.Equal(1, result.MatchedBatchCount);
            Assert.Equal(ExpiryStageCalculator.Discount50, context.Batches.Single().CurrentStage);
            Assert.Equal(BusinessDate.AddDays(16), context.Batches.Single().NextTriggerDate);
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single(batch => batch.Id == batchId);
        Assert.Equal(ExpiryStageCalculator.Discount50, batch.CurrentStage);
        Assert.Equal(BusinessDate.AddDays(16), batch.NextTriggerDate);
    }

    [Fact]
    public void NormalDiscount50Discount20WithdrawAndExpiredAdvanceDirectly()
    {
        using var database = SqliteTestDatabase.Create();
        const int expectedAttentionVersion = 4;
        var expiryDate = new DateOnly(2026, 12, 31);
        long batchId;
        long taskId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            var batch = AddBatch(
                seed,
                product.Id,
                expiryDate,
                expiryDate.AddDays(-30),
                ExpiryStageCalculator.None,
                attentionVersion: expectedAttentionVersion);
            batchId = batch.Id;
            taskId = SeedTask(seed, batch, ExpiryStageCalculator.Discount50).Id;
        }

        RunAt(database, expiryDate.AddDays(-30), RunUtc);
        AssertBatch(database, batchId, ExpiryStageCalculator.Discount50, expiryDate.AddDays(-14), expectedAttentionVersion);

        RunAt(database, expiryDate.AddDays(-14), RunUtc.AddMinutes(1));
        AssertBatch(database, batchId, ExpiryStageCalculator.Discount20, expiryDate.AddDays(-7), expectedAttentionVersion);

        RunAt(database, expiryDate.AddDays(-7), RunUtc.AddMinutes(2));
        AssertBatch(database, batchId, ExpiryStageCalculator.Withdraw, expiryDate, expectedAttentionVersion);

        RunAt(database, expiryDate, RunUtc.AddMinutes(3));
        using var verify = database.Open();
        var batchAfter = verify.Batches.Single(batch => batch.Id == batchId);
        Assert.Equal(ExpiryStageCalculator.Expired, batchAfter.CurrentStage);
        Assert.Null(batchAfter.NextTriggerDate);
        Assert.Equal(expectedAttentionVersion, batchAfter.AttentionVersion);
        Assert.Equal(expectedAttentionVersion, batchAfter.HandledAttentionVersion);
        Assert.Equal(taskId, verify.Tasks.Single().Id);
        Assert.Single(verify.TaskItems);
        Assert.Equal(ExpiryStageCalculator.Expired, verify.TaskItems.Single().Stage);
    }

    [Fact]
    public void OfflineGapProducesOnlyTheCurrentStageAndOneCurrentItem()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            AddBatch(
                seed,
                product.Id,
                BusinessDate.AddDays(3),
                BusinessDate.AddDays(-10),
                ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        {
            var result = Execute(context);

            Assert.Equal(1, result.MatchedBatchCount);
            Assert.Equal(ExpiryStageCalculator.Withdraw, context.Batches.Single().CurrentStage);
            Assert.Equal(BusinessDate.AddDays(3), context.Batches.Single().NextTriggerDate);
        }

        using var verify = database.Open();
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.TaskItems.Single().Stage);
        Assert.DoesNotContain(
            verify.TaskItems,
            item => item.Stage is ExpiryStageCalculator.Discount50 or ExpiryStageCalculator.Discount20);
    }

    [Fact]
    public void NoneUpdatesStaleBatchWithoutCreatingTask()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            AddBatch(
                seed,
                product.Id,
                BusinessDate.AddDays(31),
                BusinessDate,
                ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        {
            var result = Execute(context);

            Assert.Equal(1, result.MatchedBatchCount);
            Assert.Equal(1, result.ChangedBatchCount);
            Assert.Equal(0, result.AggregatedBatchCount);
            Assert.Equal(0, result.AggregatedProductCount);
            Assert.Equal(ExpiryStageCalculator.None, context.Batches.Single().CurrentStage);
            Assert.Equal(BusinessDate.AddDays(1), context.Batches.Single().NextTriggerDate);
        }

        using var verify = database.Open();
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.TaskItems);
    }

    [Fact]
    public void SameProductDueBatchesShareOneTaskAndKeepTheirStages()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        using (var seed = database.Open())
        {
            productId = AddProduct(seed).Id;
            AddBatch(seed, productId, BusinessDate.AddDays(30), BusinessDate, ExpiryStageCalculator.None);
            AddBatch(seed, productId, BusinessDate.AddDays(14), BusinessDate, ExpiryStageCalculator.None);
            AddBatch(seed, productId, BusinessDate.AddDays(7), BusinessDate, ExpiryStageCalculator.None);
            AddBatch(seed, productId, BusinessDate.AddDays(40), BusinessDate.AddDays(1), ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        {
            var result = Execute(context);

            Assert.Equal(3, result.MatchedBatchCount);
            Assert.Equal(3, result.AggregatedBatchCount);
            Assert.Equal(1, result.AggregatedProductCount);
        }

        using var verify = database.Open();
        Assert.Single(verify.Tasks);
        Assert.Equal(3, verify.TaskItems.Count());
        Assert.Equal(
            new[]
            {
                ExpiryStageCalculator.Discount50,
                ExpiryStageCalculator.Discount20,
                ExpiryStageCalculator.Withdraw
            },
            verify.TaskItems.OrderBy(item => item.BatchId).Select(item => item.Stage).ToArray());
        Assert.Equal(ExpiryStageCalculator.Withdraw, verify.Tasks.Single().HighestStage);
    }

    [Fact]
    public void ExistingTaskAndDraftAreUpdatedInPlaceAndDraftContentIsPreserved()
    {
        using var database = SqliteTestDatabase.Create();
        var draftTime = RunUtc.AddHours(-1);
        long batchId;
        long taskId;
        long taskItemId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            var expiryDate = BusinessDate.AddDays(14);
            var batch = AddBatch(
                seed,
                product.Id,
                expiryDate,
                BusinessDate,
                ExpiryStageCalculator.Discount50);
            batchId = batch.Id;
            var task = SeedTask(seed, batch, ExpiryStageCalculator.Discount50);
            taskId = task.Id;
            taskItemId = seed.TaskItems.Single().Id;
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
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
                TaskItemId = taskItemId,
                TaskId = task.Id,
                CheckedQty = 7,
                ConfirmedAttentionVersion = 0
            });
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = Execute(context);
            Assert.Equal(1, result.AggregatedBatchCount);
            Assert.Equal(1, result.AggregatedProductCount);
            Assert.Equal(taskId, context.Tasks.Single().Id);
        }

        using var verify = database.Open();
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
        var item = verify.TaskItems.Single();
        Assert.Equal(taskItemId, item.Id);
        Assert.Equal(batchId, item.BatchId);
        Assert.Equal(ExpiryStageCalculator.Discount20, item.Stage);
        Assert.True(item.RequiresReconfirmation);
        Assert.Equal(0, item.AttentionVersion);
        var draftAfter = verify.Drafts.Single();
        var draftItemAfter = verify.DraftItems.Single();
        Assert.Equal("张三", draftAfter.InspectorName);
        Assert.Equal(BusinessDate, draftAfter.CheckDate);
        Assert.Equal(draftTime, draftAfter.CreatedAtUtc);
        Assert.Equal(draftTime, draftAfter.UpdatedAtUtc);
        Assert.Equal(7, draftItemAfter.CheckedQty);
        Assert.Equal(0, draftItemAfter.ConfirmedAttentionVersion);
        Assert.Equal(0, verify.Batches.Single().AttentionVersion);
        Assert.Equal(0, verify.Batches.Single().HandledAttentionVersion);
    }

    [Fact]
    public void RepeatingTheSameBusinessDateIsIdempotent()
    {
        using var database = SqliteTestDatabase.Create();
        long batchId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            batchId = AddBatch(
                seed,
                product.Id,
                BusinessDate.AddDays(30),
                BusinessDate,
                ExpiryStageCalculator.None).Id;
        }

        long taskId;
        long taskItemId;
        DateTime batchUpdatedAt;
        DateTime taskUpdatedAt;
        DateTime itemUpdatedAt;
        using (var context = database.Open())
        {
            var first = Execute(context, RunUtc);
            Assert.Equal(1, first.MatchedBatchCount);
            taskId = context.Tasks.Single().Id;
            taskItemId = context.TaskItems.Single().Id;
            batchUpdatedAt = context.Batches.Single().UpdatedAtUtc;
            taskUpdatedAt = context.Tasks.Single().UpdatedAtUtc;
            itemUpdatedAt = context.TaskItems.Single().UpdatedAtUtc;
        }

        using (var context = database.Open())
        {
            var second = Execute(context, RunUtc.AddHours(1));
            Assert.Equal(0, second.MatchedBatchCount);
            Assert.Equal(0, second.ChangedBatchCount);
            Assert.Equal(0, second.AggregatedBatchCount);
            Assert.Equal(0, second.AggregatedProductCount);
        }

        using var verify = database.Open();
        Assert.Equal(taskId, verify.Tasks.Single().Id);
        Assert.Equal(taskUpdatedAt, verify.Tasks.Single().UpdatedAtUtc);
        Assert.Equal(taskItemId, verify.TaskItems.Single().Id);
        Assert.Equal(itemUpdatedAt, verify.TaskItems.Single().UpdatedAtUtc);
        Assert.Equal(batchUpdatedAt, verify.Batches.Single(batch => batch.Id == batchId).UpdatedAtUtc);
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
    }

    [Fact]
    public void FailureInLaterProductRollsBackBatchesTasksItemsAndDrafts()
    {
        using var database = SqliteTestDatabase.Create();
        long firstBatchId;
        long secondBatchId;
        long firstTaskItemId;
        var draftTime = RunUtc.AddHours(-1);
        using (var seed = database.Open())
        {
            var firstProduct = AddProduct(seed);
            var firstBatch = AddBatch(
                seed,
                firstProduct.Id,
                BusinessDate.AddDays(14),
                BusinessDate,
                ExpiryStageCalculator.Discount50);
            firstBatchId = firstBatch.Id;
            var firstTask = SeedTask(seed, firstBatch, ExpiryStageCalculator.Discount50);
            firstTaskItemId = seed.TaskItems.Single().Id;
            var draft = new InspectionDraft
            {
                TaskId = firstTask.Id,
                InspectorName = "李四",
                CreatedAtUtc = draftTime,
                UpdatedAtUtc = draftTime
            };
            seed.Drafts.Add(draft);
            seed.SaveChanges();
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = firstTaskItemId,
                TaskId = firstTask.Id,
                CheckedQty = 4,
                ConfirmedAttentionVersion = 0
            });
            seed.SaveChanges();

            var secondProduct = AddProduct(seed);
            secondBatchId = AddBatch(
                seed,
                secondProduct.Id,
                BusinessDate.AddDays(7),
                BusinessDate,
                ExpiryStageCalculator.None).Id;
            var triggerSql =
                "CREATE TRIGGER fail_s3_t03_item\n" +
                "BEFORE INSERT ON task_items\n" +
                "WHEN NEW.batch_id = " + secondBatchId.ToString(CultureInfo.InvariantCulture) + "\n" +
                "BEGIN\n" +
                "SELECT RAISE(ABORT, 'forced task item failure');\n" +
                "END;";
            seed.Database.ExecuteSqlRaw(triggerSql);
        }

        using (var context = database.Open())
        {
            Assert.Throws<DbUpdateException>(() => Execute(context));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        var firstBatchAfter = verify.Batches.Single(batch => batch.Id == firstBatchId);
        var secondBatchAfter = verify.Batches.Single(batch => batch.Id == secondBatchId);
        Assert.Equal(ExpiryStageCalculator.Discount50, firstBatchAfter.CurrentStage);
        Assert.Equal(BusinessDate, firstBatchAfter.NextTriggerDate);
        Assert.Equal(SeedUtc, firstBatchAfter.UpdatedAtUtc);
        Assert.Equal(ExpiryStageCalculator.None, secondBatchAfter.CurrentStage);
        Assert.Equal(BusinessDate, secondBatchAfter.NextTriggerDate);
        Assert.Equal(SeedUtc, secondBatchAfter.UpdatedAtUtc);
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
        Assert.Equal(firstTaskItemId, verify.TaskItems.Single().Id);
        Assert.Equal(ExpiryStageCalculator.Discount50, verify.TaskItems.Single().Stage);
        Assert.False(verify.TaskItems.Single().RequiresReconfirmation);
        Assert.Single(verify.Drafts);
        Assert.Single(verify.DraftItems);
        Assert.Equal("李四", verify.Drafts.Single().InspectorName);
        Assert.Equal(4, verify.DraftItems.Single().CheckedQty);
    }

    [Fact]
    public void ExistingOuterTransactionCanRollbackTheWholeRecalculation()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            AddBatch(seed, product.Id, BusinessDate.AddDays(30), BusinessDate, ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            var result = Execute(context);
            Assert.Equal(1, result.ChangedBatchCount);
            Assert.Single(context.Tasks);
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.Equal(ExpiryStageCalculator.None, verify.Batches.Single().CurrentStage);
        Assert.Equal(BusinessDate, verify.Batches.Single().NextTriggerDate);
        Assert.Empty(verify.Tasks);
        Assert.Empty(verify.TaskItems);
    }

    [Fact]
    public void CandidateQueryUsesTheCompositeIndexAndDatabaseFiltering()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context);
        for (var index = 0; index < 20; index++)
        {
            AddBatch(
                context,
                product.Id,
                BusinessDate.AddDays(30 + index),
                BusinessDate.AddDays(index + 1),
                ExpiryStageCalculator.None);
        }
        AddBatch(
            context,
            product.Id,
            BusinessDate.AddDays(20),
            BusinessDate.AddDays(-1),
            ExpiryStageCalculator.None);

        var result = Execute(context);
        Assert.Equal(1, result.MatchedBatchCount);

        var query = context.Batches
            .AsTracking()
            .Where(batch =>
                batch.TrackingStatus == "active" &&
                batch.NextTriggerDate.HasValue &&
                batch.NextTriggerDate.Value <= BusinessDate);
        var sql = query.ToQueryString();
        Assert.Contains("tracking_status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next_trigger_date", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<=", sql, StringComparison.Ordinal);

        var plan = ExplainCandidateQuery(context, BusinessDate);
        Assert.Contains("IX_batches_tracking_status_next_trigger_date", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("SCAN batches", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonUtcTimestampIsRejectedBeforeAnyWrite()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed);
            AddBatch(seed, product.Id, BusinessDate.AddDays(30), BusinessDate, ExpiryStageCalculator.None);
        }

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() =>
                Execute(context, DateTime.SpecifyKind(RunUtc, DateTimeKind.Unspecified)));
            Assert.Empty(context.Tasks);
            Assert.Empty(context.TaskItems);
        }

        using var verify = database.Open();
        Assert.Equal(ExpiryStageCalculator.None, verify.Batches.Single().CurrentStage);
        Assert.Equal(BusinessDate, verify.Batches.Single().NextTriggerDate);
    }

    [Theory]
    [InlineData(ExpiryPolicies.Food, 270, 25, ExpiryStageCalculator.Discount50)]
    [InlineData(ExpiryPolicies.Pet, 270, 80, ExpiryStageCalculator.Discount50)]
    [InlineData(ExpiryPolicies.GeneralLong, 360, 170, ExpiryStageCalculator.Discount50)]
    public void CompletedManagedScopeUsesItsOwnPolicy(string policyCode, int shelfLifeDays, int remainingDays, string expectedStage)
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, policyCode);
            AddBatch(seed, product.Id, BusinessDate.AddDays(remainingDays), BusinessDate, ExpiryStageCalculator.None, shelfLifeDays: shelfLifeDays);
        }

        using (var context = database.Open())
        {
            Execute(context);
        }

        using var verify = database.Open();
        Assert.Equal(expectedStage, verify.Batches.Single().CurrentStage);
        Assert.Equal(expectedStage, verify.Tasks.Single().HighestStage);
    }

    [Fact]
    public void UncoveredManagedGeneralLongFailsAndRollsBack()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, ExpiryPolicies.GeneralLong);
            AddBatch(seed, product.Id, BusinessDate.AddDays(30), BusinessDate, ExpiryStageCalculator.None, shelfLifeDays: 180);
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(context));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        Assert.Equal(ExpiryStageCalculator.None, verify.Batches.Single().CurrentStage);
        Assert.Empty(verify.Tasks);
    }

    private static StartupRecalculationResult Execute(
        StoreDbContext context,
        DateTime? updatedAtUtc = null) =>
        new StartupRecalculationUseCase().Execute(
            context,
            new StartupRecalculationRequest(BusinessDate, updatedAtUtc ?? RunUtc));

    private static void RunAt(SqliteTestDatabase database, DateOnly businessDate, DateTime updatedAtUtc)
    {
        using var context = database.Open();
        var result = new StartupRecalculationUseCase().Execute(
            context,
            new StartupRecalculationRequest(businessDate, updatedAtUtc));
        Assert.Equal(1, result.MatchedBatchCount);
        Assert.Equal(1, result.ChangedBatchCount);
        Assert.Equal(1, result.AggregatedBatchCount);
        Assert.Equal(1, result.AggregatedProductCount);
    }

    private static void AssertBatch(
        SqliteTestDatabase database,
        long batchId,
        string expectedStage,
        DateOnly? expectedNextTriggerDate,
        int expectedAttentionVersion)
    {
        using var context = database.Open();
        var batch = context.Batches.Single(item => item.Id == batchId);
        Assert.Equal(expectedStage, batch.CurrentStage);
        Assert.Equal(expectedNextTriggerDate, batch.NextTriggerDate);
        Assert.Equal(expectedAttentionVersion, batch.AttentionVersion);
        Assert.Equal(expectedAttentionVersion, batch.HandledAttentionVersion);
        Assert.Single(context.Tasks);
        Assert.Single(context.TaskItems);
    }

    private static Product AddProduct(StoreDbContext context, string policyCode = ExpiryPolicies.Food)
    {
        var scopeKey = policyCode switch
        {
            ExpiryPolicies.Food => "food",
            ExpiryPolicies.Pet => "pet",
            ExpiryPolicies.GeneralLong => "daily_use",
            _ => throw new ArgumentOutOfRangeException(nameof(policyCode))
        };
        var product = new Product { ProductCode = $"SKU-{Guid.NewGuid():N}", CategoryCode = scopeKey, PolicyCode = policyCode, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed };
        context.Products.Add(product);
        context.SaveChanges();
        if (context.ScopeBaselines.Any(baseline => baseline.ScopeKey == product.CategoryCode && baseline.PolicyCode == product.PolicyCode && baseline.PolicyVersion == product.PolicyVersion))
        {
            return product;
        }
        var import = new ImportRecord { SourceFileName = "baseline.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = SeedUtc, ConfirmedAtUtc = SeedUtc, Status = "succeeded" };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = product.CategoryCode, PolicyCode = product.PolicyCode!, PolicyVersion = product.PolicyVersion!.Value, CreatedImportId = import.Id, BusinessDate = DateOnly.FromDateTime(SeedUtc), IsCompleted = true, CompletedAtUtc = SeedUtc });
        context.SaveChanges();
        return product;
    }

    private static Batch AddBatch(
        StoreDbContext context,
        long productId,
        DateOnly expiryDate,
        DateOnly? nextTriggerDate,
        string currentStage,
        string trackingStatus = "active",
        int attentionVersion = 0,
        int shelfLifeDays = 270)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ExpiryDate = expiryDate,
            ShelfLifeValue = shelfLifeDays,
            ShelfLifeUnit = "D",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            TrackingStatus = trackingStatus,
            CurrentStage = currentStage,
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

    private static ProductTask SeedTask(StoreDbContext context, Batch batch, string stage)
    {
        new ProductTaskAggregator().Aggregate(
            context,
            new ProductTaskAggregationRequest(
                batch.ProductId,
                new[]
                {
                    new ProductTaskBatchResult(batch.Id, stage, batch.AttentionVersion, false)
                },
                SeedUtc));
        return context.Tasks.Single();
    }

    private static string ExplainCandidateQuery(StoreDbContext context, DateOnly businessDate)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                EXPLAIN QUERY PLAN
                SELECT id
                FROM batches
                WHERE tracking_status = 'active'
                  AND next_trigger_date IS NOT NULL
                  AND next_trigger_date <= $businessDate;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$businessDate";
            parameter.Value = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            command.Parameters.Add(parameter);
            using var reader = command.ExecuteReader();
            var lines = new List<string>();
            while (reader.Read())
            {
                lines.Add(reader.GetString(3));
            }

            return string.Join(Environment.NewLine, lines);
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }
}
