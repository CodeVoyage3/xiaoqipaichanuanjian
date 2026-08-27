using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class BatchCheckedZeroLifecycleUseCaseTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 27);
    private static readonly DateTime SeedUtc = new(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SubmittedAtUtc = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OccurredAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StopsOnlyTargetBatchAndPreservesFormalHistoryProductTaskAndDraft()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long targetBatchId;
        long otherBatchId;
        long otherProductBatchId;
        long inspectionId;
        long inspectionItemId;
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "TARGET", stock: 9, generation: 4);
            productId = product.Id;
            var targetBatch = AddBatch(
                seed,
                productId,
                currentStage: ExpiryStageCalculator.Withdraw,
                nextTriggerDate: BusinessDate,
                currentArrivalQty: 8,
                maxArrivalQty: 11,
                lifecycleGeneration: 4,
                attentionVersion: 7,
                handledAttentionVersion: 6);
            targetBatchId = targetBatch.Id;
            otherBatchId = AddBatch(
                seed,
                productId,
                currentStage: ExpiryStageCalculator.Discount20,
                nextTriggerDate: BusinessDate.AddDays(1),
                currentArrivalQty: 4,
                maxArrivalQty: 4,
                lifecycleGeneration: 4,
                attentionVersion: 2,
                handledAttentionVersion: 1).Id;

            var otherProduct = AddProduct(seed, "OTHER", stock: 3, generation: 2);
            otherProductBatchId = AddBatch(
                seed,
                otherProduct.Id,
                currentStage: ExpiryStageCalculator.Discount50,
                nextTriggerDate: BusinessDate,
                lifecycleGeneration: 2).Id;

            var task = AddTask(seed, productId, ExpiryStageCalculator.Withdraw);
            var taskItem = AddTaskItem(
                seed,
                task.Id,
                targetBatchId,
                productId,
                ExpiryStageCalculator.Withdraw,
                attentionVersion: 7);
            var draft = AddDraft(seed, task.Id);
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = taskItem.Id,
                TaskId = task.Id,
                CheckedQty = 6,
                ConfirmedAttentionVersion = 7
            });
            seed.SaveChanges();

            var inspection = AddInspection(seed, task.Id, productId, product.ProductCode);
            inspectionId = inspection.Id;
            var inspectionItem = AddInspectionItem(seed, inspection.Id, productId, targetBatchId, 0);
            inspectionItemId = inspectionItem.Id;
            seed.InspectionItemRevisions.Add(new InspectionItemRevision
            {
                InspectionItemId = inspectionItemId,
                PreviousCheckedQty = 1,
                NewCheckedQty = 0,
                ChangedAtUtc = OccurredAtUtc.AddMinutes(-1)
            });
            seed.SaveChanges();
        }

        ProductSnapshot productBefore;
        BatchSnapshot targetBefore;
        BatchSnapshot otherBefore;
        BatchSnapshot otherProductBatchBefore;
        TaskSnapshot taskBefore;
        TaskItemSnapshot taskItemBefore;
        DraftSnapshot draftBefore;
        DraftItemSnapshot draftItemBefore;
        InspectionSnapshot inspectionBefore;
        InspectionItemSnapshot itemBefore;
        RevisionSnapshot revisionBefore;
        using (var before = database.Open())
        {
            productBefore = Snapshot(before.Products.Single(product => product.Id == productId));
            targetBefore = Snapshot(before.Batches.Single(batch => batch.Id == targetBatchId));
            otherBefore = Snapshot(before.Batches.Single(batch => batch.Id == otherBatchId));
            otherProductBatchBefore = Snapshot(before.Batches.Single(batch => batch.Id == otherProductBatchId));
            taskBefore = Snapshot(before.Tasks.Single());
            taskItemBefore = Snapshot(before.TaskItems.Single());
            draftBefore = Snapshot(before.Drafts.Single());
            draftItemBefore = Snapshot(before.DraftItems.Single());
            inspectionBefore = Snapshot(before.Inspections.Single());
            itemBefore = Snapshot(before.InspectionItems.Single());
            revisionBefore = Snapshot(before.InspectionItemRevisions.Single());
        }

        using (var context = database.Open())
        {
            var result = Execute(context, productId, targetBatchId, inspectionId, inspectionItemId);

            Assert.True(result.BatchStopped);
            Assert.False(result.IdempotentReplay);
            Assert.Equal(1, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var productAfter = Snapshot(verify.Products.Single(product => product.Id == productId));
        var targetAfter = Snapshot(verify.Batches.Single(batch => batch.Id == targetBatchId));
        var otherAfter = Snapshot(verify.Batches.Single(batch => batch.Id == otherBatchId));
        var otherProductBatchAfter = Snapshot(verify.Batches.Single(batch => batch.Id == otherProductBatchId));
        var taskAfter = Snapshot(verify.Tasks.Single());
        var taskItemAfter = Snapshot(verify.TaskItems.Single());
        var draftAfter = Snapshot(verify.Drafts.Single());
        var draftItemAfter = Snapshot(verify.DraftItems.Single());
        var inspectionAfter = Snapshot(verify.Inspections.Single());
        var itemAfter = Snapshot(verify.InspectionItems.Single());
        var revisionAfter = Snapshot(verify.InspectionItemRevisions.Single());

        Assert.Equal(productBefore, productAfter);
        Assert.Equal(otherBefore, otherAfter);
        Assert.Equal(otherProductBatchBefore, otherProductBatchAfter);
        Assert.Equal(taskBefore, taskAfter);
        Assert.Equal(taskItemBefore, taskItemAfter);
        Assert.Equal(draftBefore, draftAfter);
        Assert.Equal(draftItemBefore, draftItemAfter);
        Assert.Equal(inspectionBefore, inspectionAfter);
        Assert.Equal(itemBefore, itemAfter);
        Assert.Equal(revisionBefore, revisionAfter);
        Assert.Equal(
            targetBefore with
            {
                TrackingStatus = "stopped",
                StopReason = "batch_checked_zero",
                StoppedAtUtc = OccurredAtUtc,
                NextTriggerDate = null,
                UpdatedAtUtc = OccurredAtUtc
            },
            targetAfter);

        var lifecycleEvent = Assert.Single(verify.LifecycleEvents);
        Assert.Equal("batch_checked_zero", lifecycleEvent.EventType);
        Assert.Equal("batch_checked_zero", lifecycleEvent.Reason);
        Assert.Equal(productId, lifecycleEvent.ProductId);
        Assert.Equal(targetBatchId, lifecycleEvent.BatchId);
        Assert.Equal(inspectionId, lifecycleEvent.SourceInspectionId);
        Assert.Null(lifecycleEvent.SourceImportId);
        Assert.Null(lifecycleEvent.SourceAdjustmentId);
        Assert.Equal(OccurredAtUtc, lifecycleEvent.OccurredAtUtc);
    }

    [Fact]
    public void StoppedBatchIsNotMatchedByStartupRecalculation()
    {
        using var database = SqliteTestDatabase.Create();
        long productId;
        long batchId;
        long inspectionId;
        long itemId;
        using (var seed = database.Open())
        {
            var scenario = SeedScenario(seed, nextTriggerDate: BusinessDate.AddDays(-1));
            productId = scenario.ProductId;
            batchId = scenario.BatchId;
            inspectionId = scenario.InspectionId;
            itemId = scenario.InspectionItemId;
        }

        using (var context = database.Open())
        {
            Execute(context, productId, batchId, inspectionId, itemId);
            var recalculation = new StartupRecalculationUseCase().Execute(
                context,
                new StartupRecalculationRequest(BusinessDate, OccurredAtUtc.AddHours(1)));

            Assert.Equal(0, recalculation.MatchedBatchCount);
            Assert.Equal(0, recalculation.ChangedBatchCount);
            Assert.Single(context.Tasks);
            Assert.Single(context.TaskItems);
        }

        using var verify = database.Open();
        Assert.Equal("stopped", verify.Batches.Single().TrackingStatus);
        Assert.Null(verify.Batches.Single().NextTriggerDate);
        Assert.Single(verify.Tasks);
        Assert.Single(verify.TaskItems);
    }

    [Fact]
    public void RequestAndPersistedCheckedQuantityMustBothBeZero()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId,
                checkedQty: 1));
            Assert.False(context.ChangeTracker.HasChanges());
        }

        using (var update = database.Open())
        {
            var item = update.InspectionItems.Single();
            item.CheckedQty = 2;
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
            Assert.False(context.ChangeTracker.HasChanges());
        }

        using var verify = database.Open();
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void MissingAndCrossProductInspectionFactsAreRejected()
    {
        using var database = SqliteTestDatabase.Create();
        Scenario first;
        Scenario second;
        long secondBatchForFirstProductId;
        using (var seed = database.Open())
        {
            first = SeedScenario(seed, productCode: "FIRST");
            second = SeedScenario(seed, productCode: "SECOND");
            secondBatchForFirstProductId = AddBatch(seed, first.ProductId).Id;

            var completedTask = AddTask(seed, first.ProductId, ExpiryStageCalculator.Discount50, "completed");
            var secondInspection = AddInspection(seed, completedTask.Id, first.ProductId, "FIRST");
            first = first with
            {
                SecondInspectionId = secondInspection.Id,
                SecondInspectionItemId = AddInspectionItem(
                    seed,
                    secondInspection.Id,
                    first.ProductId,
                    secondBatchForFirstProductId,
                    0).Id
            };
        }

        using (var context = database.Open())
        {
            Assert.Throws<KeyNotFoundException>(() => Execute(
                context,
                first.ProductId,
                first.BatchId,
                first.InspectionId + 100,
                first.InspectionItemId));
            Assert.Throws<KeyNotFoundException>(() => Execute(
                context,
                first.ProductId,
                first.BatchId,
                first.InspectionId,
                first.InspectionItemId + 100));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                first.ProductId,
                first.BatchId,
                first.InspectionId,
                second.InspectionItemId));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                first.ProductId,
                first.BatchId,
                first.SecondInspectionId,
                first.InspectionItemId));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                first.ProductId,
                secondBatchForFirstProductId,
                first.InspectionId,
                first.InspectionItemId));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                first.ProductId,
                first.BatchId,
                second.InspectionId,
                second.InspectionItemId));
            Assert.False(context.ChangeTracker.HasChanges());
        }

        using var verify = database.Open();
        Assert.All(verify.Batches, batch => Assert.Equal("active", batch.TrackingStatus));
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void InvalidTimesAndBatchStatesAreRejectedWithoutWrites()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);

        using (var context = database.Open())
        {
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId,
                occurredAtUtc: DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Local)));
            Assert.Throws<ArgumentException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId,
                occurredAtUtc: SubmittedAtUtc.AddMinutes(-1)));
        }

        using (var update = database.Open())
        {
            var batch = update.Batches.Single();
            batch.StopReason = "manual_stop";
            batch.StoppedAtUtc = SeedUtc;
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
        }

        using (var update = database.Open())
        {
            var batch = update.Batches.Single();
            batch.TrackingStatus = "stopped";
            batch.StopReason = "batch_checked_zero";
            batch.StoppedAtUtc = SeedUtc;
            batch.NextTriggerDate = null;
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
        }

        using (var update = database.Open())
        {
            var batch = update.Batches.Single();
            batch.StopReason = "manual_stop";
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
        }

        using var verify = database.Open();
        Assert.Empty(verify.LifecycleEvents);
        Assert.Equal("manual_stop", verify.Batches.Single().StopReason);
    }

    [Fact]
    public void MatchingEventReplayIsIdempotentAndDuplicateIdentityIsRejected()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);
        BatchSnapshot firstBatch;
        using (var context = database.Open())
        {
            Execute(context, scenario.ProductId, scenario.BatchId, scenario.InspectionId, scenario.InspectionItemId);
            firstBatch = Snapshot(context.Batches.Single());
        }

        using (var context = database.Open())
        {
            var result = Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId,
                occurredAtUtc: OccurredAtUtc.AddHours(2));
            Assert.False(result.BatchStopped);
            Assert.True(result.IdempotentReplay);
            Assert.Equal(0, result.LifecycleEventCount);
            Assert.Equal(firstBatch, Snapshot(context.Batches.Single()));
        }

        using (var duplicate = database.Open())
        {
            duplicate.LifecycleEvents.Add(new LifecycleEvent
            {
                ProductId = scenario.ProductId,
                BatchId = scenario.BatchId,
                EventType = "batch_checked_zero",
                Reason = "batch_checked_zero",
                OccurredAtUtc = OccurredAtUtc.AddMinutes(1),
                SourceInspectionId = scenario.InspectionId
            });
            duplicate.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
            Assert.Equal(firstBatch, Snapshot(context.Batches.Single()));
        }

        using var verify = database.Open();
        Assert.Equal(2, verify.LifecycleEvents.Count());
    }

    [Fact]
    public void MalformedMatchingEventIsRejectedInsteadOfUsedAsAnAnchor()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);
        using (var seed = database.Open())
        {
            seed.LifecycleEvents.Add(new LifecycleEvent
            {
                ProductId = scenario.ProductId,
                BatchId = scenario.BatchId,
                EventType = "batch_checked_zero",
                Reason = "wrong_reason",
                OccurredAtUtc = OccurredAtUtc,
                SourceInspectionId = scenario.InspectionId
            });
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Assert.Throws<InvalidOperationException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
            Assert.False(context.ChangeTracker.HasChanges());
        }

        using var verify = database.Open();
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Single(verify.LifecycleEvents);
    }

    [Fact]
    public void T05RecoveryThenOldZeroReplayDoesNotStopAgain()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        Scenario scenario;
        using (var seed = database.Open())
        {
            var import = AddImport(seed);
            importId = import.Id;
            scenario = SeedScenario(
                seed,
                lifecycleGeneration: 3,
                currentArrivalQty: 8,
                maxArrivalQty: 8,
                nextTriggerDate: BusinessDate.AddDays(10));
            seed.Products.Single(product => product.Id == scenario.ProductId).LastSeenImportId = importId;
            var targetBatch = seed.Batches.Single(batch => batch.Id == scenario.BatchId);
            targetBatch.LastSeenImportId = importId;
            targetBatch.CurrentArrivalQty = 10;
            targetBatch.MaxArrivalQty = 10;
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Execute(context, scenario.ProductId, scenario.BatchId, scenario.InspectionId, scenario.InspectionItemId);
        }

        var importRequest = new PostImportLifecycleRequest(
            importId,
            BusinessDate,
            OccurredAtUtc.AddHours(1),
            new[]
            {
                new PostImportProductGroup(
                    scenario.ProductId,
                    new[]
                    {
                        new PostImportBatchFact(
                            scenario.BatchId,
                            PostImportBatchFactKinds.Existing,
                            8,
                            10,
                            0,
                            1)
                    })
            });
        using (var context = database.Open())
        {
            var result = new PostImportLifecycleUseCase().Execute(context, importRequest);
            Assert.Equal(1, result.ResumedBatchCount);
            Assert.Equal(1, result.LifecycleEventCount);
        }

        BatchSnapshot recovered;
        DateTime resumeEventTime;
        using (var context = database.Open())
        {
            recovered = Snapshot(context.Batches.Single());
            resumeEventTime = context.LifecycleEvents
                .Single(item => item.EventType == "batch_tracking_resumed")
                .OccurredAtUtc;
            var replay = Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId,
                occurredAtUtc: OccurredAtUtc.AddHours(3));
            Assert.False(replay.BatchStopped);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(0, replay.LifecycleEventCount);
            Assert.Equal(recovered, Snapshot(context.Batches.Single()));
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Null(batch.StopReason);
        Assert.Null(batch.StoppedAtUtc);
        Assert.Equal(1, batch.AttentionVersion);
        Assert.Equal(resumeEventTime, verify.LifecycleEvents
            .Single(item => item.EventType == "batch_tracking_resumed")
            .OccurredAtUtc);
        Assert.Single(verify.LifecycleEvents.Where(item => item.EventType == "batch_checked_zero"));
    }

    [Fact]
    public void T05BelowHistoricalMaximumDoesNotRecoverStoppedBatch()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        Scenario scenario;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            scenario = SeedScenario(
                seed,
                currentArrivalQty: 8,
                maxArrivalQty: 8,
                nextTriggerDate: BusinessDate.AddDays(10));
            seed.Products.Single(product => product.Id == scenario.ProductId).LastSeenImportId = importId;
            seed.Batches.Single(batch => batch.Id == scenario.BatchId).LastSeenImportId = importId;
            seed.SaveChanges();
        }

        using (var context = database.Open())
        {
            Execute(context, scenario.ProductId, scenario.BatchId, scenario.InspectionId, scenario.InspectionItemId);
            var request = new PostImportLifecycleRequest(
                importId,
                BusinessDate,
                OccurredAtUtc.AddHours(1),
                new[]
                {
                    new PostImportProductGroup(
                        scenario.ProductId,
                        new[]
                        {
                            new PostImportBatchFact(
                                scenario.BatchId,
                                PostImportBatchFactKinds.Existing,
                                8,
                                8,
                                0,
                                0)
                        })
                });
            var result = new PostImportLifecycleUseCase().Execute(context, request);
            Assert.Equal(0, result.ResumedBatchCount);
            Assert.Equal(0, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal("stopped", batch.TrackingStatus);
        Assert.Equal("batch_checked_zero", batch.StopReason);
        Assert.Equal(0, batch.AttentionVersion);
        Assert.Single(verify.LifecycleEvents);
        Assert.Equal("batch_checked_zero", verify.LifecycleEvents.Single().EventType);
    }

    [Fact]
    public void T04ProductZeroHasPriorityOverLaterT05Recovery()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        Scenario scenario;
        using (var seed = database.Open())
        {
            importId = AddImport(seed).Id;
            scenario = SeedScenario(
                seed,
                currentArrivalQty: 8,
                maxArrivalQty: 8,
                nextTriggerDate: BusinessDate.AddDays(10));
        }

        using (var context = database.Open())
        {
            Execute(context, scenario.ProductId, scenario.BatchId, scenario.InspectionId, scenario.InspectionItemId);
            var product = context.Products.Single();
            product.EffectiveStockQty = 0;
            product.ExcelStockQty = 0;
            context.SaveChanges();
            new ProductStockZeroLifecycleUseCase().Execute(
                context,
                new ProductStockZeroRequest(scenario.ProductId, OccurredAtUtc.AddHours(1)));
        }

        using (var update = database.Open())
        {
            var product = update.Products.Single();
            product.EffectiveStockQty = 5;
            product.LastSeenImportId = importId;
            var batch = update.Batches.Single();
            batch.LastSeenImportId = importId;
            batch.CurrentArrivalQty = 10;
            batch.MaxArrivalQty = 10;
            update.SaveChanges();
        }

        using (var context = database.Open())
        {
            var request = new PostImportLifecycleRequest(
                importId,
                BusinessDate,
                OccurredAtUtc.AddHours(2),
                new[]
                {
                    new PostImportProductGroup(
                        scenario.ProductId,
                        new[]
                        {
                            new PostImportBatchFact(
                                scenario.BatchId,
                                PostImportBatchFactKinds.Existing,
                                8,
                                10,
                                0,
                                1)
                        })
                });
            var result = new PostImportLifecycleUseCase().Execute(context, request);
            Assert.Equal(0, result.ResumedBatchCount);
            Assert.Equal(0, result.LifecycleEventCount);
        }

        using var verify = database.Open();
        Assert.True(verify.Products.Single().IsStockZeroTerminated);
        Assert.Equal("product_stock_zero", verify.Batches.Single().StopReason);
        Assert.Equal(0, verify.Batches.Single().AttentionVersion);
        Assert.DoesNotContain(verify.LifecycleEvents, item => item.EventType == "batch_tracking_resumed");
    }

    [Fact]
    public void LifecycleEventFailureRollsBackBatchAndClearsOwnedTracker()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);
        using (var context = database.Open())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t06_event
                BEFORE INSERT ON lifecycle_events
                WHEN NEW.event_type = 'batch_checked_zero'
                BEGIN
                    SELECT RAISE(ABORT, 'forced S3-T06 event failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        using var verify = database.Open();
        var batch = verify.Batches.Single();
        Assert.Equal("active", batch.TrackingStatus);
        Assert.Null(batch.StopReason);
        Assert.Null(batch.StoppedAtUtc);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void OuterTransactionRollbackOwnsNeitherCommitNorRollback()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);
        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            var result = Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId);
            Assert.True(result.BatchStopped);
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Equal("stopped", context.Batches.Single().TrackingStatus);
            Assert.Single(context.LifecycleEvents);
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void OuterTransactionFailureRestoresTrackedBatchAndLeavesTransactionOpen()
    {
        using var database = SqliteTestDatabase.Create();
        var scenario = SeedScenario(database);
        using (var context = database.Open())
        using (var transaction = context.Database.BeginTransaction())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_s3_t06_outer_event
                BEFORE INSERT ON lifecycle_events
                WHEN NEW.event_type = 'batch_checked_zero'
                BEGIN
                    SELECT RAISE(ABORT, 'forced S3-T06 outer failure');
                END;
                """);

            Assert.Throws<DbUpdateException>(() => Execute(
                context,
                scenario.ProductId,
                scenario.BatchId,
                scenario.InspectionId,
                scenario.InspectionItemId));
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.Equal("active", context.Batches.Single().TrackingStatus);
            transaction.Rollback();
        }

        using var verify = database.Open();
        Assert.Equal("active", verify.Batches.Single().TrackingStatus);
        Assert.Empty(verify.LifecycleEvents);
    }

    private static BatchCheckedZeroLifecycleResult Execute(
        StoreDbContext context,
        long productId,
        long batchId,
        long inspectionId,
        long inspectionItemId,
        int checkedQty = 0,
        DateTime? occurredAtUtc = null) =>
        new BatchCheckedZeroLifecycleUseCase().Execute(
            context,
            new BatchCheckedZeroLifecycleRequest(
                productId,
                batchId,
                inspectionId,
                inspectionItemId,
                checkedQty,
                occurredAtUtc ?? OccurredAtUtc));

    private static Scenario SeedScenario(
        SqliteTestDatabase database,
        string productCode = "TARGET",
        int stock = 5,
        int lifecycleGeneration = 2,
        int currentArrivalQty = 8,
        int maxArrivalQty = 8,
        DateOnly? nextTriggerDate = null)
    {
        using var context = database.Open();
        return SeedScenario(
            context,
            productCode,
            stock,
            lifecycleGeneration,
            currentArrivalQty,
            maxArrivalQty,
            nextTriggerDate);
    }

    private static Scenario SeedScenario(
        StoreDbContext context,
        string productCode = "TARGET",
        int stock = 5,
        int lifecycleGeneration = 2,
        int currentArrivalQty = 8,
        int maxArrivalQty = 8,
        DateOnly? nextTriggerDate = null)
    {
        var product = AddProduct(context, productCode, stock, lifecycleGeneration);
        var batch = AddBatch(
            context,
            product.Id,
            currentStage: ExpiryStageCalculator.Discount50,
            nextTriggerDate: nextTriggerDate ?? BusinessDate.AddDays(1),
            currentArrivalQty,
            maxArrivalQty,
            lifecycleGeneration);
        var task = AddTask(context, product.Id, ExpiryStageCalculator.Discount50);
        var taskItem = AddTaskItem(
            context,
            task.Id,
            batch.Id,
            product.Id,
            ExpiryStageCalculator.Discount50,
            attentionVersion: batch.AttentionVersion);
        _ = taskItem;
        var inspection = AddInspection(context, task.Id, product.Id, product.ProductCode);
        var inspectionItem = AddInspectionItem(context, inspection.Id, product.Id, batch.Id, 0);
        return new(product.Id, batch.Id, inspection.Id, inspectionItem.Id);
    }

    private static ImportRecord AddImport(StoreDbContext context)
    {
        var import = new ImportRecord
        {
            SourceFileName = "source.xlsx",
            SourceFileSha256 = new string('a', 64),
            ParsedAtUtc = SeedUtc,
            ConfirmedAtUtc = SeedUtc,
            Status = ImportStatuses.Succeeded,
            IsUndone = false
        };
        context.Imports.Add(import);
        context.SaveChanges();
        return import;
    }

    private static Product AddProduct(
        StoreDbContext context,
        string productCode,
        int stock = 5,
        int generation = 2)
    {
        var product = new Product
        {
            ProductCode = productCode,
            CurrentName = productCode + " name",
            CurrentBarcode = productCode + " barcode",
            CategoryCode = "food",
            PolicyCode = "food_v1",
            ExcelStockQty = stock,
            EffectiveStockQty = stock,
            EffectiveStockSource = "excel",
            LifecycleGeneration = generation,
            IsStockZeroTerminated = false,
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
        string currentStage = ExpiryStageCalculator.None,
        DateOnly? nextTriggerDate = null,
        int currentArrivalQty = 8,
        int maxArrivalQty = 8,
        int lifecycleGeneration = 2,
        string trackingStatus = "active",
        string? stopReason = null,
        DateTime? stoppedAtUtc = null,
        int attentionVersion = 0,
        int handledAttentionVersion = 0)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = new DateOnly(2026, 1, 1).AddDays((int)context.Batches.LongCount()),
            ExpiryDate = BusinessDate.AddDays(100),
            ShelfLifeValue = 365,
            ShelfLifeUnit = "D",
            CurrentArrivalQty = currentArrivalQty,
            MaxArrivalQty = maxArrivalQty,
            SourceDiscountReference = "source-reference",
            LifecycleGeneration = lifecycleGeneration,
            TrackingStatus = trackingStatus,
            StopReason = stopReason,
            StoppedAtUtc = stoppedAtUtc,
            CurrentStage = currentStage,
            NextTriggerDate = nextTriggerDate,
            AttentionVersion = attentionVersion,
            HandledAttentionVersion = handledAttentionVersion,
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
        string? status = null)
    {
        var task = new ProductTask
        {
            ProductId = productId,
            Status = status ?? "open",
            HighestStage = highestStage,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc,
            ClosedAtUtc = status is null or "open" ? null : SeedUtc,
            CloseReason = status is null or "open" ? null : status
        };
        context.Tasks.Add(task);
        context.SaveChanges();
        return task;
    }

    private static ProductTaskItem AddTaskItem(
        StoreDbContext context,
        long taskId,
        long batchId,
        long productId,
        string stage,
        int attentionVersion)
    {
        var item = new ProductTaskItem
        {
            TaskId = taskId,
            BatchId = batchId,
            ProductId = productId,
            Stage = stage,
            AttentionVersion = attentionVersion,
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
            InspectorName = "Inspector",
            CheckDate = BusinessDate,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Drafts.Add(draft);
        context.SaveChanges();
        return draft;
    }

    private static Inspection AddInspection(
        StoreDbContext context,
        long taskId,
        long productId,
        string productCode)
    {
        var inspection = new Inspection
        {
            TaskId = taskId,
            ProductId = productId,
            ProductCodeSnapshot = productCode,
            ProductNameSnapshot = productCode + " name",
            BarcodeSnapshot = productCode + " barcode",
            StageSnapshot = ExpiryStageCalculator.Discount50,
            StockQtySnapshot = 5,
            InspectorName = "Inspector",
            CheckDate = BusinessDate,
            SubmittedAtUtc = SubmittedAtUtc
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();
        return inspection;
    }

    private static InspectionItem AddInspectionItem(
        StoreDbContext context,
        long inspectionId,
        long productId,
        long batchId,
        int checkedQty)
    {
        var item = new InspectionItem
        {
            InspectionId = inspectionId,
            ProductId = productId,
            BatchId = batchId,
            ProductionDateSnapshot = new DateOnly(2026, 1, 1),
            ExpiryDateSnapshot = BusinessDate.AddDays(100),
            StageSnapshot = ExpiryStageCalculator.Discount50,
            ArrivalQtySnapshot = 8,
            CheckedQty = checkedQty,
            UpdatedAtUtc = SubmittedAtUtc
        };
        context.InspectionItems.Add(item);
        context.SaveChanges();
        return item;
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

    private static RevisionSnapshot Snapshot(InspectionItemRevision revision) => new(
        revision.Id,
        revision.InspectionItemId,
        revision.PreviousCheckedQty,
        revision.NewCheckedQty,
        revision.ChangedAtUtc);

    private sealed record Scenario(
        long ProductId,
        long BatchId,
        long InspectionId,
        long InspectionItemId,
        long SecondInspectionId = 0,
        long SecondInspectionItemId = 0);

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

    private sealed record RevisionSnapshot(
        long Id,
        long InspectionItemId,
        int PreviousCheckedQty,
        int NewCheckedQty,
        DateTime ChangedAtUtc);
}
