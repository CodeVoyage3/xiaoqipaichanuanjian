using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InspectionSubmissionUseCaseTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 28);
    private static readonly DateTime SeedUtc = new(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SubmittedAtUtc = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidSubmissionCreatesCompleteSnapshotsStopsOnlyZeroBatchCompletesTaskAndDeletesDraft()
    {
        using var database = CreateScenario(new int?[] { 0, 3, 2 });
        using (var context = database.Open())
        {
            var result = Submit(context, database);

            Assert.True(result.Submitted);
            Assert.False(result.AlreadySubmitted);
            Assert.False(result.RequiresOverStockConfirmation);
            Assert.NotNull(result.InspectionId);
            Assert.Equal(5, result.TotalCheckedQty);
            Assert.Equal(10, result.EffectiveStockQty);
            Assert.False(context.ChangeTracker.HasChanges());
        }

        using var verify = database.Open();
        var product = verify.Products.Single();
        var batches = verify.Batches.OrderBy(batch => batch.Id).ToArray();
        var task = verify.Tasks.Single();
        var inspection = verify.Inspections.Single();
        var items = verify.InspectionItems.OrderBy(item => item.Id).ToArray();

        Assert.Equal(10, product.EffectiveStockQty);
        Assert.Equal(ExpiryStageCalculator.Expired, inspection.StageSnapshot);
        Assert.Equal(product.ProductCode, inspection.ProductCodeSnapshot);
        Assert.Equal(product.CurrentName, inspection.ProductNameSnapshot);
        Assert.Equal(product.CurrentBarcode, inspection.BarcodeSnapshot);
        Assert.Equal(product.EffectiveStockQty, inspection.StockQtySnapshot);
        Assert.Equal("Inspector", inspection.InspectorName);
        Assert.Equal(BusinessDate, inspection.CheckDate);
        Assert.Equal(SubmittedAtUtc, inspection.SubmittedAtUtc);
        Assert.Equal(3, items.Length);
        Assert.Equal(new[] { 0, 3, 2 }, items.Select(item => item.CheckedQty));
        Assert.Equal(
            batches.Select(batch => batch.Id),
            items.Select(item => item.BatchId));
        Assert.Equal(
            batches.Select(batch => batch.ExpiryDate),
            items.Select(item => item.ExpiryDateSnapshot));
        Assert.Equal(
            batches.Select(batch => batch.CurrentArrivalQty),
            items.Select(item => item.ArrivalQtySnapshot));
        Assert.Equal(
            new[]
            {
                ExpiryStageCalculator.Expired,
                ExpiryStageCalculator.Discount20,
                ExpiryStageCalculator.Discount50
            },
            items.Select(item => item.StageSnapshot));
        Assert.All(items, item => Assert.Equal(SubmittedAtUtc, item.UpdatedAtUtc));
        Assert.Equal("stopped", batches[0].TrackingStatus);
        Assert.Equal("batch_checked_zero", batches[0].StopReason);
        Assert.Null(batches[0].NextTriggerDate);
        Assert.Equal(SubmittedAtUtc, batches[0].StoppedAtUtc);
        Assert.Equal(2, batches[0].HandledAttentionVersion);
        Assert.Equal(2, batches[1].HandledAttentionVersion);
        Assert.Equal(2, batches[2].HandledAttentionVersion);
        Assert.Equal("active", batches[1].TrackingStatus);
        Assert.Equal("active", batches[2].TrackingStatus);
        Assert.Equal(SubmittedAtUtc, batches[0].UpdatedAtUtc);
        Assert.Equal(SeedUtc, batches[1].UpdatedAtUtc);
        Assert.Equal(SeedUtc, batches[2].UpdatedAtUtc);
        Assert.Equal("completed", task.Status);
        Assert.Equal(SubmittedAtUtc, task.ClosedAtUtc);
        Assert.Equal("submitted", task.CloseReason);
        Assert.Equal(SubmittedAtUtc, task.UpdatedAtUtc);
        Assert.Empty(verify.Drafts);
        Assert.Empty(verify.DraftItems);
        Assert.Empty(verify.InspectionItemRevisions);
        var lifecycleEvent = Assert.Single(verify.LifecycleEvents);
        Assert.Equal("batch_checked_zero", lifecycleEvent.EventType);
        Assert.Equal("batch_checked_zero", lifecycleEvent.Reason);
        Assert.Equal(batches[0].ProductId, lifecycleEvent.ProductId);
        Assert.Equal(batches[0].Id, lifecycleEvent.BatchId);
        Assert.Equal(inspection.Id, lifecycleEvent.SourceInspectionId);
        Assert.Null(lifecycleEvent.SourceImportId);
        Assert.Null(lifecycleEvent.SourceAdjustmentId);
        Assert.Equal(SubmittedAtUtc, lifecycleEvent.OccurredAtUtc);
    }

    [Fact]
    public void SubmitUsesCurrentFactsAndS4T01DistinguishesCompletedFromMissing()
    {
        using var database = CreateScenario(new int?[] { 1, 2 });
        long taskId;
        using (var context = database.Open())
        {
            taskId = context.Tasks.Single().Id;
            var result = Submit(context, database);
            Assert.True(result.Submitted);
        }

        using (var queryContext = database.Open())
        {
            var detail = new InspectionTaskQuery().GetDetail(queryContext, taskId);
            Assert.Equal("completed", detail.Status);
            Assert.Null(detail.Detail);
            Assert.Empty(new InspectionTaskQuery().SearchOpenTasks(queryContext, new()).Items);
            Assert.Equal(0, new InspectionTaskQuery().Dashboard(queryContext).OpenTaskCount);
        }
    }

    [Fact]
    public void CompletedWithInspectionReturnsAlreadySubmittedWithoutAnyGraphChange()
    {
        using var database = CreateScenario(new int?[] { 0, 4 });
        using (var context = database.Open())
        {
            Submit(context, database);
        }

        string before;
        using (var context = database.Open())
        {
            before = SnapshotGraph(context);
            var result = Submit(context, database);
            Assert.True(result.AlreadySubmitted);
            Assert.False(result.Submitted);
            Assert.Equal(1, result.InspectionId);
            Assert.Equal(before, SnapshotGraph(context));
            Assert.False(context.ChangeTracker.HasChanges());
        }
    }

    [Fact]
    public void SQLiteInspectionTaskUniqueConstraintRejectsDuplicateFormalHistory()
    {
        using var database = CreateScenario(new int?[] { 1 });
        using var context = database.Open();
        Submit(context, database);

        var existing = context.Inspections.Single();
        context.Inspections.Add(new Inspection
        {
            TaskId = existing.TaskId,
            ProductId = existing.ProductId,
            ProductCodeSnapshot = existing.ProductCodeSnapshot,
            ProductNameSnapshot = existing.ProductNameSnapshot,
            BarcodeSnapshot = existing.BarcodeSnapshot,
            StageSnapshot = existing.StageSnapshot,
            StockQtySnapshot = existing.StockQtySnapshot,
            InspectorName = existing.InspectorName,
            CheckDate = existing.CheckDate,
            SubmittedAtUtc = existing.SubmittedAtUtc.AddMinutes(1)
        });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
        Assert.Single(context.Inspections);
    }

    [Fact]
    public void CompletedWithoutInspectionAndSystemClosedAreExplicitlyRejected()
    {
        using (var database = CreateScenario(new int?[] { 1 }, invalidDraft: true))
        {
            using var context = database.Open();
            var task = context.Tasks.Single();
            task.Status = "completed";
            task.ClosedAtUtc = SubmittedAtUtc;
            context.SaveChanges();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }

        using (var database = CreateScenario(new int?[] { 1 }))
        {
            using var context = database.Open();
            var task = context.Tasks.Single();
            task.Status = "system_closed";
            task.ClosedAtUtc = SubmittedAtUtc;
            task.CloseReason = "product_stock_zero";
            context.SaveChanges();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }
    }

    [Fact]
    public void MissingTaskAndWrongProductAreRejectedWithoutWrites()
    {
        using var database = CreateScenario(new int?[] { 1 });
        using var context = database.Open();
        var before = SnapshotGraph(context);
        Assert.Throws<KeyNotFoundException>(() => new InspectionSubmissionUseCase().Submit(
            context,
            new(9999, database.ProductId, BusinessDate, SubmittedAtUtc)));
        Assert.Equal(before, SnapshotGraph(context));

        Assert.ThrowsAny<Exception>(() => Submit(
            context,
            database,
            productId: database.ProductId + 100));
        Assert.Equal(before, SnapshotGraph(context));
    }

    [Fact]
    public void DraftCompletenessAndVersionGatesRejectWholeSubmission()
    {
        using (var database = CreateScenario(new int?[] { null, 2 }))
        {
            using var verifyContext = database.Open();
            var before = SnapshotGraph(verifyContext);
            Assert.Throws<InvalidOperationException>(() => Submit(verifyContext, database));
            Assert.Equal(before, SnapshotGraph(verifyContext));
        }

        using (var database = CreateScenario(new int?[] { 1, 2 }, requiresReconfirmation: true))
        {
            using var verifyContext = database.Open();
            var before = SnapshotGraph(verifyContext);
            Assert.Throws<InvalidOperationException>(() => Submit(verifyContext, database));
            Assert.Equal(before, SnapshotGraph(verifyContext));
        }

        using (var database = CreateScenario(new int?[] { 1, 2 }, confirmedVersions: new[] { 0, 2 }))
        {
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }

        using (var database = CreateScenario(new int?[] { 1, 2 }, taskAttentionVersions: new[] { 2, 1 }, batchAttentionVersions: new[] { 1, 1 }))
        {
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }
    }

    [Fact]
    public void SaveDraftThenReconfirmUsesBackendVersionAndSubmissionKeepsTheGate()
    {
        using var database = CreateScenario(
            new int?[] { 1, 2 },
            attentionVersions: new[] { 1, 1 },
            handledVersions: new[] { 0, 0 },
            seedDraft: false);
        using var context = database.Open();
        var draftUseCase = new InspectionDraftUseCase();
        var save = draftUseCase.SaveDraft(
            context,
            new SaveDraftRequest(
                database.TaskId,
                database.ProductId,
                BusinessDate,
                SubmittedAtUtc.AddMinutes(-3),
                " Inspector ",
                BusinessDate,
                new[]
                {
                    new SaveDraftItemRequest(database.TaskItemIds[0], database.BatchIds[0], 1, 1),
                    new SaveDraftItemRequest(database.TaskItemIds[1], database.BatchIds[1], 1, 2)
                }));
        Assert.True(save.Changed);
        Assert.True(save.IsDraftComplete);

        context.ChangeTracker.Clear();
        var taskItems = context.TaskItems.OrderBy(item => item.Id).ToArray();
        var batches = context.Batches.OrderBy(item => item.Id).ToArray();
        foreach (var item in taskItems)
        {
            item.AttentionVersion = 2;
            item.RequiresReconfirmation = true;
        }

        foreach (var batch in batches)
        {
            batch.AttentionVersion = 2;
        }

        context.SaveChanges();
        context.ChangeTracker.Clear();

        Assert.Throws<ArgumentException>(() => draftUseCase.ReconfirmItem(
            context,
            new ReconfirmItemRequest(
                database.TaskId,
                database.ProductId,
                database.TaskItemIds[0],
                database.BatchIds[0],
                1,
                SubmittedAtUtc.AddMinutes(-2))));
        Assert.Throws<InvalidOperationException>(() => Submit(context, database));

        var firstReconfirm = draftUseCase.ReconfirmItem(
            context,
            new ReconfirmItemRequest(
                database.TaskId,
                database.ProductId,
                database.TaskItemIds[0],
                database.BatchIds[0],
                2,
                SubmittedAtUtc.AddMinutes(-1)));
        var secondReconfirm = draftUseCase.ReconfirmItem(
            context,
            new ReconfirmItemRequest(
                database.TaskId,
                database.ProductId,
                database.TaskItemIds[1],
                database.BatchIds[1],
                2,
                SubmittedAtUtc.AddMinutes(-1)));
        Assert.True(firstReconfirm.Changed);
        Assert.True(secondReconfirm.Changed);

        var result = Submit(context, database);
        Assert.True(result.Submitted);
        Assert.Equal(2, context.InspectionItems.Count());
    }

    [Fact]
    public void MissingInspectorAndCheckDateOrFutureCheckDateAreRejected()
    {
        foreach (var inspector in new string?[] { null, string.Empty, "   " })
        {
            using var database = CreateScenario(new int?[] { 1 }, inspectorName: inspector);
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }

        using (var database = CreateScenario(new int?[] { 1 }))
        {
            using var context = database.Open();
            context.Drafts.Single().CheckDate = null;
            context.SaveChanges();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }

        using (var database = CreateScenario(new int?[] { 1 }, checkDate: BusinessDate.AddDays(1)))
        {
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }
    }

    [Fact]
    public void MissingOrExtraDraftItemIsRejectedAndZeroRemainsLegal()
    {
        using (var database = CreateScenario(new int?[] { 0, 2 }))
        {
            using (var context = database.Open())
            {
                var draftItem = context.DraftItems.OrderBy(item => item.Id).First();
                context.DraftItems.Remove(draftItem);
                context.SaveChanges();
            }

            using var verifyContext = database.Open();
            var before = SnapshotGraph(verifyContext);
            Assert.Throws<InvalidOperationException>(() => Submit(verifyContext, database));
            Assert.Equal(before, SnapshotGraph(verifyContext));
        }

        using (var database = CreateScenario(new int?[] { 0, 2 }))
        {
            using var context = database.Open();
            var draft = context.Drafts.Single();
            var taskItem = context.TaskItems.OrderBy(item => item.Id).First();
            context.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = taskItem.Id,
                TaskId = taskItem.TaskId,
                CheckedQty = 0,
                ConfirmedAttentionVersion = taskItem.AttentionVersion
            });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = CreateScenario(new int?[] { 0, 2 }))
        {
            using var context = database.Open();
            var result = Submit(context, database);
            Assert.True(result.Submitted);
            Assert.Equal(0, context.InspectionItems.OrderBy(item => item.Id).First().CheckedQty);
        }
    }

    [Fact]
    public void OverStockWarningHasNoBusinessWritesAndExactConfirmationAllowsSubmit()
    {
        using var database = CreateScenario(new int?[] { 6, 7 }, stockQty: 10);
        using (var context = database.Open())
        {
            var before = SnapshotGraph(context);
            var warning = Submit(context, database);
            Assert.True(warning.RequiresOverStockConfirmation);
            Assert.Null(warning.InspectionId);
            Assert.Equal(10, warning.EffectiveStockQty);
            Assert.Equal(13, warning.TotalCheckedQty);
            Assert.Equal(before, SnapshotGraph(context));
            Assert.False(context.ChangeTracker.HasChanges());

            var result = Submit(context, database, confirmedEffectiveStockQty: 10, confirmedTotalCheckedQty: 13);
            Assert.True(result.Submitted);
            Assert.NotNull(result.InspectionId);
        }
    }

    [Fact]
    public void OverStockConfirmationBecomesStaleWhenStockOrDraftQuantityChanges()
    {
        using var database = CreateScenario(new int?[] { 6, 7 }, stockQty: 10);
        using (var context = database.Open())
        {
            var warning = Submit(context, database);
            Assert.True(warning.RequiresOverStockConfirmation);
        }

        using (var context = database.Open())
        {
            context.Products.Single().EffectiveStockQty = 20;
            context.SaveChanges();
        }

        using (var context = database.Open())
        {
            var result = Submit(context, database, confirmedEffectiveStockQty: 10, confirmedTotalCheckedQty: 13);
            Assert.True(result.Submitted);
        }

        using var database2 = CreateScenario(new int?[] { 6, 7 }, stockQty: 10);
        using (var context = database2.Open())
        {
            Assert.True(Submit(context, database2).RequiresOverStockConfirmation);
            context.DraftItems.OrderBy(item => item.Id).First().CheckedQty = 8;
            context.SaveChanges();
            var before = SnapshotGraph(context);
            var warning = Submit(context, database2, confirmedEffectiveStockQty: 10, confirmedTotalCheckedQty: 13);
            Assert.True(warning.RequiresOverStockConfirmation);
            Assert.Equal(15, warning.TotalCheckedQty);
            Assert.Equal(before, SnapshotGraph(context));
        }
    }

    [Fact]
    public void ManualStockCorrectionIsReReadBeforeSubmission()
    {
        using var database = CreateScenario(new int?[] { 6, 6 }, stockQty: 10);
        using (var context = database.Open())
        {
            Assert.True(Submit(context, database).RequiresOverStockConfirmation);
            var adjustment = new ManualInventoryAdjustmentUseCase().Execute(
                context,
                new(database.ProductId, 15, false, SubmittedAtUtc.AddMinutes(1)));
            Assert.True(adjustment.Changed);
            var result = Submit(context, database, confirmedEffectiveStockQty: 10, confirmedTotalCheckedQty: 12);
            Assert.True(result.Submitted);
            Assert.Equal(15, result.EffectiveStockQty);
        }
    }

    [Fact]
    public void SuccessfulSubmissionUpdatesOnlyHandledVersionsAndS3T06StopsAllZeroItems()
    {
        using var database = CreateScenario(new int?[] { 0, 0, 4 }, attentionVersions: new[] { 2, 4, 1 }, handledVersions: new[] { 1, 2, 1 });
        using (var context = database.Open())
        {
            var beforeProduct = context.Products.AsNoTracking().Select(product => new
            {
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
                product.UpdatedAtUtc
            }).Single();
            var before = context.Batches.AsNoTracking().OrderBy(batch => batch.Id).ToArray();
            var result = Submit(context, database);
            Assert.True(result.Submitted);
            Assert.Equal(beforeProduct, context.Products.AsNoTracking().Select(product => new
            {
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
                product.UpdatedAtUtc
            }).Single());
            var after = context.Batches.AsNoTracking().OrderBy(batch => batch.Id).ToArray();
            Assert.Equal("stopped", after[0].TrackingStatus);
            Assert.Equal("stopped", after[1].TrackingStatus);
            Assert.Equal("active", after[2].TrackingStatus);
            Assert.Equal(2, after[0].HandledAttentionVersion);
            Assert.Equal(4, after[1].HandledAttentionVersion);
            Assert.Equal(1, after[2].HandledAttentionVersion);
            Assert.Equal(
                before.Select(batch => batch.AttentionVersion),
                after.Select(batch => batch.AttentionVersion));
            Assert.Equal(before[2].Id, after[2].Id);
            Assert.Equal(before[2].TrackingStatus, after[2].TrackingStatus);
            Assert.Equal(before[2].StopReason, after[2].StopReason);
            Assert.Equal(before[2].NextTriggerDate, after[2].NextTriggerDate);
            Assert.Equal(before[2].UpdatedAtUtc, after[2].UpdatedAtUtc);
            Assert.Equal(2, context.LifecycleEvents.Count());
        }
    }

    [Fact]
    public void FormalDeletionRemovesDraftItemsBeforeDraftAndLeavesInvalidDraftUntouched()
    {
        using (var database = CreateScenario(new int?[] { 1, 0 }))
        {
            using var context = database.Open();
            context.Database.ExecuteSqlRaw("""
                CREATE TABLE delete_order (position INTEGER NOT NULL);
                CREATE TRIGGER record_draft_item_delete
                BEFORE DELETE ON draft_items
                BEGIN
                    INSERT INTO delete_order(position) VALUES (1);
                END;
                CREATE TRIGGER record_draft_delete
                BEFORE DELETE ON drafts
                BEGIN
                    INSERT INTO delete_order(position) VALUES (2);
                END;
                """);
            var result = Submit(context, database);
            Assert.True(result.Submitted);
            Assert.Equal(new[] { 1, 1, 2 }, context.Database.SqlQueryRaw<int>(
                "SELECT position AS Value FROM delete_order ORDER BY rowid").ToArray());
        }

        using (var database = CreateScenario(new int?[] { 1 }, invalidDraft: true))
        {
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
            Assert.True(context.Drafts.Single().IsInvalid);
            Assert.Single(context.DraftItems);
        }
    }

    [Fact]
    public void CompletedInspectionWithInvalidResidualDraftIsAlreadySubmittedWithoutCleanup()
    {
        using var database = CreateScenario(new int?[] { 1 }, invalidDraft: true);
        using (var context = database.Open())
        {
            var task = context.Tasks.Single();
            var product = context.Products.Single();
            var batch = context.Batches.Single();
            var inspection = new Inspection
            {
                TaskId = task.Id,
                ProductId = product.Id,
                ProductCodeSnapshot = product.ProductCode,
                ProductNameSnapshot = product.CurrentName,
                BarcodeSnapshot = product.CurrentBarcode,
                StageSnapshot = task.HighestStage,
                StockQtySnapshot = product.EffectiveStockQty,
                InspectorName = "Inspector",
                CheckDate = BusinessDate,
                SubmittedAtUtc = SubmittedAtUtc
            };
            inspection.Items.Add(new InspectionItem
            {
                Inspection = inspection,
                ProductId = product.Id,
                BatchId = batch.Id,
                ProductionDateSnapshot = batch.ProductionDate,
                ExpiryDateSnapshot = batch.ExpiryDate,
                StageSnapshot = context.TaskItems.Single(item => item.TaskId == task.Id).Stage,
                ArrivalQtySnapshot = batch.CurrentArrivalQty,
                CheckedQty = 1,
                UpdatedAtUtc = SubmittedAtUtc
            });
            context.Inspections.Add(inspection);
            task.Status = "completed";
            task.ClosedAtUtc = SubmittedAtUtc;
            context.SaveChanges();
            var before = SnapshotGraph(context);
            var result = Submit(context, database);
            Assert.True(result.AlreadySubmitted);
            Assert.Equal(inspection.Id, result.InspectionId);
            Assert.Equal(before, SnapshotGraph(context));
            Assert.Single(context.Drafts);
            Assert.Single(context.DraftItems);
        }
    }

    [Fact]
    public void NullProductionDateAndHistoricalRecordsAreSnapshottedWithoutMutation()
    {
        using var database = CreateScenario(new int?[] { 0, 2 });
        using (var context = database.Open())
        {
            context.Batches.OrderBy(batch => batch.Id).First().ProductionDate = null;
            var oldTask = new ProductTask
            {
                ProductId = database.ProductId,
                Status = "completed",
                HighestStage = ExpiryStageCalculator.Discount50,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc,
                ClosedAtUtc = SeedUtc,
                CloseReason = "submitted"
            };
            context.Tasks.Add(oldTask);
            context.SaveChanges();
            var product = context.Products.Single();
            var oldInspection = new Inspection
            {
                TaskId = oldTask.Id,
                ProductId = product.Id,
                ProductCodeSnapshot = product.ProductCode,
                ProductNameSnapshot = (string?)"Historical name",
                BarcodeSnapshot = (string?)"HISTORICAL-BARCODE",
                StageSnapshot = oldTask.HighestStage,
                StockQtySnapshot = product.EffectiveStockQty,
                InspectorName = "Old",
                CheckDate = BusinessDate,
                SubmittedAtUtc = SeedUtc
            };
            context.Inspections.Add(oldInspection);
            context.SaveChanges();
            var oldInspectionFacts = new
            {
                oldInspection.Id,
                oldInspection.TaskId,
                oldInspection.ProductId,
                ProductCodeSnapshot = product.ProductCode,
                oldInspection.ProductNameSnapshot,
                oldInspection.BarcodeSnapshot,
                oldInspection.StageSnapshot,
                oldInspection.StockQtySnapshot,
                oldInspection.InspectorName,
                oldInspection.CheckDate,
                oldInspection.SubmittedAtUtc
            };
            var result = Submit(context, database);
            Assert.True(result.Submitted);
            var item = context.InspectionItems.OrderBy(item => item.Id).First();
            Assert.Null(item.ProductionDateSnapshot);
            var persistedOldInspection = context.Inspections.AsNoTracking()
                .Single(inspection => inspection.Id == oldInspection.Id);
            Assert.Equal(oldInspectionFacts, new
            {
                persistedOldInspection.Id,
                persistedOldInspection.TaskId,
                persistedOldInspection.ProductId,
                persistedOldInspection.ProductCodeSnapshot,
                persistedOldInspection.ProductNameSnapshot,
                persistedOldInspection.BarcodeSnapshot,
                persistedOldInspection.StageSnapshot,
                persistedOldInspection.StockQtySnapshot,
                persistedOldInspection.InspectorName,
                persistedOldInspection.CheckDate,
                persistedOldInspection.SubmittedAtUtc
            });
        }
    }

    [Fact]
    public void InvalidHandledVersionIsRejectedBeforeAnyWrite()
    {
        using (var database = CreateScenario(new int?[] { 1 }, attentionVersions: new[] { 2 }, handledVersions: new[] { 3 }))
        {
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }

        using (var database = CreateScenario(new int?[] { 1 }, attentionVersions: new[] { 2 }, handledVersions: new[] { -1 }))
        {
            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
        }
    }

    [Fact]
    public void AlreadySubmittedNeverCleansAnAnomalousEffectiveDraft()
    {
        using var database = CreateScenario(new int?[] { 1 });
        using (var context = database.Open())
        {
            Submit(context, database);
            var task = context.Tasks.Single();
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "Residual",
                CheckDate = BusinessDate,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            context.Drafts.Add(draft);
            context.SaveChanges();
            var before = SnapshotGraph(context);
            Assert.Throws<InvalidOperationException>(() => Submit(context, database));
            Assert.Equal(before, SnapshotGraph(context));
            Assert.Single(context.Drafts);
        }
    }

    [Fact]
    public void OuterTransactionCanRollbackEntireSubmissionAndUseCaseDoesNotCloseIt()
    {
        using var database = CreateScenario(new int?[] { 0, 3 });
        using var context = database.Open();
        var before = SnapshotGraph(context);
        using var transaction = context.Database.BeginTransaction();
        var result = Submit(context, database);
        Assert.True(result.Submitted);
        Assert.NotNull(context.Database.CurrentTransaction);
        transaction.Rollback();
        Assert.False(context.ChangeTracker.HasChanges());

        using var verify = database.Open();
        Assert.Equal(before, SnapshotGraph(verify));
    }

    [Fact]
    public void InvalidRequestTimeAndIdsAreRejectedBeforeDatabaseWork()
    {
        using var database = CreateScenario(new int?[] { 1 });
        using var context = database.Open();
        var before = SnapshotGraph(context);
        Assert.Throws<ArgumentException>(() => new InspectionSubmissionUseCase().Submit(
            context,
            new(context.Tasks.Single().Id, database.ProductId, BusinessDate, SubmittedAtUtc.ToLocalTime())));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InspectionSubmissionUseCase().Submit(
            context,
            new(0, database.ProductId, BusinessDate, SubmittedAtUtc)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InspectionSubmissionUseCase().Submit(
            context,
            new(context.Tasks.Single().Id, database.ProductId, BusinessDate, SubmittedAtUtc, -1, null)));
        Assert.Equal(before, SnapshotGraph(context));
    }

    [Fact]
    public void ExistingOtherProductCannotBeUsedForTheCurrentTask()
    {
        using var database = CreateScenario(new int?[] { 1 });
        long otherProductId;
        using (var context = database.Open())
        {
            var otherProduct = new Product
            {
                ProductCode = "OTHER-SKU",
                ExcelStockQty = 4,
                EffectiveStockQty = 4,
                EffectiveStockSource = "excel",
                LifecycleGeneration = 7,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            var thirdProduct = new Product
            {
                ProductCode = "THIRD-SKU",
                ExcelStockQty = 6,
                EffectiveStockQty = 5,
                EffectiveStockSource = "manual",
                LifecycleGeneration = 9,
                IsStockZeroTerminated = true,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            context.Products.AddRange(otherProduct, thirdProduct);
            context.SaveChanges();
            otherProductId = otherProduct.Id;
        }

        using var verify = database.Open();
        var before = SnapshotGraph(verify);
        Assert.Throws<ArgumentException>(() => Submit(verify, database, productId: otherProductId));
        Assert.Equal(before, SnapshotGraph(verify));

        var beforeOtherProducts = verify.Products.AsNoTracking()
            .Where(product => product.Id != database.ProductId)
            .OrderBy(product => product.Id)
            .Select(product => new
            {
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
                product.UpdatedAtUtc
            }).ToArray();
        var result = Submit(verify, database);
        Assert.True(result.Submitted);
        var afterOtherProducts = verify.Products.AsNoTracking()
            .Where(product => product.Id != database.ProductId)
            .OrderBy(product => product.Id)
            .Select(product => new
            {
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
                product.UpdatedAtUtc
            }).ToArray();
        Assert.Equal(beforeOtherProducts, afterOtherProducts);
    }

    [Fact]
    public void InspectionAndLifecycleFailuresRollbackAllWritesAndClearTracker()
    {
        using (var database = CreateScenario(new int?[] { 1, 2 }))
        {
            using var context = database.Open();
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_inspection_insert
                BEFORE INSERT ON inspections
                BEGIN
                    SELECT RAISE(ABORT, 'inspection failure');
                END;
                """);
            var before = SnapshotGraph(context);
            Assert.Throws<DbUpdateException>(() => Submit(context, database));
            Assert.False(context.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }

        using (var database = CreateScenario(new int?[] { 1, 2 }))
        {
            using var context = database.Open();
            context.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_second_inspection_item
                BEFORE INSERT ON inspection_items
                WHEN NEW.checked_qty = 2
                BEGIN
                    SELECT RAISE(ABORT, 'inspection item failure');
                END;
                """);
            var before = SnapshotGraph(context);
            Assert.Throws<DbUpdateException>(() => Submit(context, database));
            Assert.False(context.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }

        using (var database = CreateScenario(new int?[] { 0, 0 }))
        {
            long secondBatchId;
            using (var setup = database.Open())
            {
                secondBatchId = setup.Batches.OrderBy(batch => batch.Id).Skip(1).Single().Id;
                var triggerSql = string.Concat(
                    "CREATE TRIGGER fail_second_zero_event " +
                    "BEFORE INSERT ON lifecycle_events " +
                    "WHEN NEW.event_type = 'batch_checked_zero' AND NEW.batch_id = ",
                    secondBatchId.ToString(CultureInfo.InvariantCulture),
                    " BEGIN SELECT RAISE(ABORT, 'second zero failure'); END;");
                setup.Database.ExecuteSqlRaw(triggerSql);
            }

            using var context = database.Open();
            var before = SnapshotGraph(context);
            Assert.Throws<DbUpdateException>(() => Submit(context, database));
            Assert.False(context.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }
    }

    [Fact]
    public void TaskVersionAndDraftDeleteFailuresRollbackWholeBusinessGraph()
    {
        using (var database = CreateScenario(new int?[] { 1, 0 }))
        {
            using var setup = database.Open();
            setup.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_handled_version
                BEFORE UPDATE OF handled_attention_version ON batches
                WHEN NEW.handled_attention_version <> OLD.handled_attention_version
                BEGIN
                    SELECT RAISE(ABORT, 'handled version failure');
                END;
                """);
            var before = SnapshotGraph(setup);
            Assert.Throws<DbUpdateException>(() => Submit(setup, database));
            Assert.False(setup.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }

        using (var database = CreateScenario(new int?[] { 1, 0 }))
        {
            using var setup = database.Open();
            setup.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_task_completion
                BEFORE UPDATE OF status ON tasks
                WHEN NEW.status = 'completed'
                BEGIN
                    SELECT RAISE(ABORT, 'task completion failure');
                END;
                """);
            var before = SnapshotGraph(setup);
            Assert.Throws<DbUpdateException>(() => Submit(setup, database));
            Assert.False(setup.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }

        using (var database = CreateScenario(new int?[] { 1, 0 }))
        {
            using var setup = database.Open();
            setup.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_draft_item_delete
                BEFORE DELETE ON draft_items
                BEGIN
                    SELECT RAISE(ABORT, 'draft item deletion failure');
                END;
                """);
            var before = SnapshotGraph(setup);
            Assert.Throws<DbUpdateException>(() => Submit(setup, database));
            Assert.False(setup.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }

        using (var database = CreateScenario(new int?[] { 1, 0 }))
        {
            using var setup = database.Open();
            setup.Database.ExecuteSqlRaw("""
                CREATE TRIGGER fail_draft_delete
                BEFORE DELETE ON drafts
                BEGIN
                    SELECT RAISE(ABORT, 'draft deletion failure');
                END;
                """);
            var before = SnapshotGraph(setup);
            Assert.Throws<DbUpdateException>(() => Submit(setup, database));
            Assert.False(setup.ChangeTracker.HasChanges());
            using var verify = database.Open();
            Assert.Equal(before, SnapshotGraph(verify));
        }
    }

    [Fact]
    public void OuterTransactionFailureLeavesTransactionForCallerRollback()
    {
        using var database = CreateScenario(new int?[] { 0, 0 });
        using var context = database.Open();
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_outer_zero_event
            BEFORE INSERT ON lifecycle_events
            WHEN NEW.event_type = 'batch_checked_zero'
            BEGIN
                SELECT RAISE(ABORT, 'outer event failure');
            END;
            """);
        var before = SnapshotGraph(context);
        using var transaction = context.Database.BeginTransaction();
        Assert.Throws<DbUpdateException>(() => Submit(context, database));
        Assert.NotNull(context.Database.CurrentTransaction);
        Assert.False(context.ChangeTracker.HasChanges());
        transaction.Rollback();
        using var verify = database.Open();
        Assert.Equal(before, SnapshotGraph(verify));
    }

    private static InspectionSubmissionResult Submit(
        StoreDbContext context,
        Scenario database,
        int? confirmedEffectiveStockQty = null,
        int? confirmedTotalCheckedQty = null,
        long? productId = null) => new InspectionSubmissionUseCase().Submit(
            context,
            new(
                database.TaskId,
                productId ?? database.ProductId,
                BusinessDate,
                SubmittedAtUtc,
                confirmedEffectiveStockQty,
                confirmedTotalCheckedQty));

    private static Scenario CreateScenario(
        int?[] checkedQuantities,
        int stockQty = 10,
        bool requiresReconfirmation = false,
        bool invalidDraft = false,
        bool seedDraft = true,
        string? inspectorName = "  Inspector  ",
        DateOnly? checkDate = null,
        int[]? attentionVersions = null,
        int[]? handledVersions = null,
        int[]? taskAttentionVersions = null,
        int[]? batchAttentionVersions = null,
        int[]? confirmedVersions = null)
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = new Product
        {
            ProductCode = "SUBMIT-SKU",
            CurrentName = "Submit product",
            CurrentBarcode = "690000000001",
            ExcelStockQty = stockQty,
            EffectiveStockQty = stockQty,
            EffectiveStockSource = "excel",
            LifecycleGeneration = 3,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Products.Add(product);
        context.SaveChanges();

        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Expired,
            CreatedAtUtc = SeedUtc,
            UpdatedAtUtc = SeedUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();

        var attention = attentionVersions ?? Enumerable.Repeat(2, checkedQuantities.Length).ToArray();
        var handled = handledVersions ?? attention.Select(value => Math.Max(0, value - 1)).ToArray();
        var taskVersions = taskAttentionVersions ?? attention.ToArray();
        var batchVersions = batchAttentionVersions ?? attention.ToArray();
        var confirmed = confirmedVersions ?? taskVersions.ToArray();
        var batchIds = new List<long>();
        var taskItemIds = new List<long>();
        for (var index = 0; index < checkedQuantities.Length; index++)
        {
            var batch = new Batch
            {
                ProductId = product.Id,
                ProductionDate = BusinessDate.AddDays(-30 - index),
                ExpiryDate = BusinessDate.AddDays(10 + index),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 5 + index,
                MaxArrivalQty = 5 + index,
                LifecycleGeneration = 3,
                TrackingStatus = "active",
                CurrentStage = index == 0 ? ExpiryStageCalculator.Expired : ExpiryStageCalculator.Discount20,
                NextTriggerDate = BusinessDate.AddDays(2),
                AttentionVersion = batchVersions[index],
                HandledAttentionVersion = handled[index],
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            context.Batches.Add(batch);
            context.SaveChanges();
            batchIds.Add(batch.Id);

            var taskItem = new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = index switch
                {
                    0 => ExpiryStageCalculator.Expired,
                    1 => ExpiryStageCalculator.Discount20,
                    _ => ExpiryStageCalculator.Discount50
                },
                AttentionVersion = taskVersions[index],
                RequiresReconfirmation = requiresReconfirmation,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            context.TaskItems.Add(taskItem);
            context.SaveChanges();
            taskItemIds.Add(taskItem.Id);
        }

        if (seedDraft)
        {
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = inspectorName,
                CheckDate = checkDate ?? BusinessDate,
                IsInvalid = invalidDraft,
                InvalidReason = invalidDraft ? "product_stock_zero" : null,
                InvalidatedAtUtc = invalidDraft ? SubmittedAtUtc : null,
                CreatedAtUtc = SeedUtc,
                UpdatedAtUtc = SeedUtc
            };
            context.Drafts.Add(draft);
            context.SaveChanges();
            for (var index = 0; index < taskItemIds.Count; index++)
            {
                context.DraftItems.Add(new InspectionDraftItem
                {
                    DraftId = draft.Id,
                    TaskItemId = taskItemIds[index],
                    TaskId = task.Id,
                    CheckedQty = checkedQuantities[index],
                    ConfirmedAttentionVersion = confirmed[index]
                });
            }

            context.SaveChanges();
        }
        return new(database, product.Id, task.Id, taskItemIds.ToArray(), batchIds.ToArray());
    }

    private static string SnapshotGraph(StoreDbContext context)
    {
        return JsonSerializer.Serialize(new
        {
            Products = context.Products.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ProductCode,
                item.CurrentName,
                item.CurrentBarcode,
                item.CategoryCode,
                item.PolicyCode,
                item.ExcelStockQty,
                item.EffectiveStockQty,
                item.EffectiveStockSource,
                item.LifecycleGeneration,
                item.IsStockZeroTerminated,
                item.LastSeenImportId,
                item.CreatedAtUtc,
                item.UpdatedAtUtc
            }),
            Batches = context.Batches.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ProductId,
                item.ProductionDate,
                item.ExpiryDate,
                item.ShelfLifeValue,
                item.ShelfLifeUnit,
                item.CurrentArrivalQty,
                item.MaxArrivalQty,
                item.SourceDiscountReference,
                item.LifecycleGeneration,
                item.TrackingStatus,
                item.StopReason,
                item.StoppedAtUtc,
                item.CurrentStage,
                item.NextTriggerDate,
                item.AttentionVersion,
                item.HandledAttentionVersion,
                item.LastSeenImportId,
                item.CreatedAtUtc,
                item.UpdatedAtUtc
            }),
            Tasks = context.Tasks.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ProductId,
                item.Status,
                item.HighestStage,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.ClosedAtUtc,
                item.CloseReason
            }),
            TaskItems = context.TaskItems.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.TaskId,
                item.BatchId,
                item.ProductId,
                item.Stage,
                item.AttentionVersion,
                item.RequiresReconfirmation,
                item.CreatedAtUtc,
                item.UpdatedAtUtc
            }),
            Drafts = context.Drafts.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.TaskId,
                item.InspectorName,
                item.CheckDate,
                item.IsInvalid,
                item.InvalidReason,
                item.InvalidatedAtUtc,
                item.CreatedAtUtc,
                item.UpdatedAtUtc
            }),
            DraftItems = context.DraftItems.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.DraftId,
                item.TaskItemId,
                item.TaskId,
                item.CheckedQty,
                item.ConfirmedAttentionVersion
            }),
            Inspections = context.Inspections.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.TaskId,
                item.ProductId,
                item.ProductCodeSnapshot,
                item.ProductNameSnapshot,
                item.BarcodeSnapshot,
                item.StageSnapshot,
                item.StockQtySnapshot,
                item.InspectorName,
                item.CheckDate,
                item.SubmittedAtUtc
            }),
            InspectionItems = context.InspectionItems.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.InspectionId,
                item.ProductId,
                item.BatchId,
                item.ProductionDateSnapshot,
                item.ExpiryDateSnapshot,
                item.StageSnapshot,
                item.ArrivalQtySnapshot,
                item.CheckedQty,
                item.UpdatedAtUtc
            }),
            Revisions = context.InspectionItemRevisions.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.InspectionItemId,
                item.PreviousCheckedQty,
                item.NewCheckedQty,
                item.ChangedAtUtc
            }),
            InventoryAdjustments = context.InventoryAdjustments.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ProductId,
                item.ExcelStockQtySnapshot,
                item.AdjustedStockQty,
                item.AdjustedAtUtc
            }),
            LifecycleEvents = context.LifecycleEvents.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ProductId,
                item.BatchId,
                item.EventType,
                item.Reason,
                item.OccurredAtUtc,
                item.SourceImportId,
                item.SourceInspectionId,
                item.SourceAdjustmentId
            }),
            Imports = context.Imports.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.SourceFileName,
                item.SourceFileSha256,
                item.ParsedAtUtc,
                item.ConfirmedAtUtc,
                item.Status,
                item.ProductCount,
                item.BatchCount,
                item.NewProductCount,
                item.NewBatchCount,
                item.UpdatedBatchCount,
                item.IssueCount,
                item.UnsupportedCategoryCount,
                item.NewTaskProductCount,
                item.PreImportSnapshotPath,
                item.IsUndone,
                item.UndoneAtUtc
            }),
            Workbooks = context.ImportWorkbooks.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ImportId,
                item.OriginalFileName,
                Content = item.Content == null ? null : Convert.ToBase64String(item.Content),
                item.Sha256,
                item.SavedAtUtc
            }),
            ImportIssues = context.ImportIssues.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ImportId,
                item.RowNumber,
                item.IssueType,
                item.FieldName,
                item.SafeSummary
            }),
            Backups = context.BackupRecords.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.BackupType,
                item.FilePath,
                item.Sha256,
                item.CreatedAtUtc,
                item.VerificationStatus
            }),
            Settings = context.Settings.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.ReminderMinuteOfDay,
                item.AutoStartEnabled
            }),
            AppStates = context.AppStates.AsNoTracking().OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.LastReminderDate,
                item.LastNormalRunDate
            })
        });
    }

    private sealed record Scenario(
        SqliteTestDatabase Database,
        long ProductId,
        long TaskId,
        long[] TaskItemIds,
        long[] BatchIds) : IDisposable
    {
        public StoreDbContext Open() => Database.Open();

        public void Dispose() => Database.Dispose();
    }
}
