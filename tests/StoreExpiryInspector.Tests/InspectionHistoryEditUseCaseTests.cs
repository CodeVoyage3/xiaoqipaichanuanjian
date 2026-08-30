using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InspectionHistoryEditUseCaseTests
{
    private static readonly DateTime SubmittedAtUtc =
        new(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime SeedAtUtc =
        new(2026, 8, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SubmittedFormalItemCanBeRevisedAndCreatesRevision()
    {
        using var scenario = CreateSubmittedScenario(2);
        using var context = scenario.Database.Open();

        var result = Execute(context, scenario, 5, SubmittedAtUtc.AddHours(1));

        Assert.Equal("changed", result.Status);
        Assert.True(result.Changed);
        Assert.False(result.NoChange);
        Assert.Equal(2, result.PreviousCheckedQty);
        Assert.Equal(5, result.NewCheckedQty);
        Assert.NotNull(result.RevisionId);
        Assert.Equal(SubmittedAtUtc.AddHours(1), result.UpdatedAtUtc);

        using var verify = scenario.Database.Open();
        var item = Assert.Single(verify.InspectionItems.AsNoTracking());
        var revision = Assert.Single(verify.InspectionItemRevisions.AsNoTracking());
        Assert.Equal(5, item.CheckedQty);
        Assert.Equal(SubmittedAtUtc.AddHours(1), item.UpdatedAtUtc);
        Assert.Equal(scenario.InspectionItemId, revision.InspectionItemId);
        Assert.Equal(2, revision.PreviousCheckedQty);
        Assert.Equal(5, revision.NewCheckedQty);
        Assert.Equal(SubmittedAtUtc.AddHours(1), revision.ChangedAtUtc);
    }

    [Fact]
    public void ConsecutiveEditsChainPreviousAndNewValuesInStableOrder()
    {
        using var scenario = CreateFormalScenario(0);
        using var context = scenario.Database.Open();
        var useCase = new InspectionHistoryEditUseCase();
        var changedAtUtc = SubmittedAtUtc.AddHours(1);

        var first = useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 5, changedAtUtc));
        var second = useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 2, changedAtUtc));

        Assert.Equal("changed", first.Status);
        Assert.Equal("changed", second.Status);
        Assert.Equal(0, first.PreviousCheckedQty);
        Assert.Equal(5, first.NewCheckedQty);
        Assert.Equal(5, second.PreviousCheckedQty);
        Assert.Equal(2, second.NewCheckedQty);
        Assert.NotEqual(first.RevisionId, second.RevisionId);

        var history = new InspectionHistoryQuery()
            .GetItemRevisions(context, scenario.InspectionId, scenario.InspectionItemId);
        Assert.Equal("found", history.Status);
        Assert.NotNull(history.History);
        Assert.Equal(2, history.History!.CurrentCheckedQty);
        Assert.Equal(
            new[] { 0, 5 },
            history.History.Revisions.Select(revision => revision.PreviousCheckedQty));
        Assert.Equal(
            new[] { 5, 2 },
            history.History.Revisions.Select(revision => revision.NewCheckedQty));
        Assert.Equal(
            new[] { first.RevisionId, second.RevisionId },
            history.History.Revisions.Select(revision => (long?)revision.RevisionId));
    }

    [Fact]
    public void ZeroPositiveZeroEditsDoNotChangeTaskDraftOrLifecycleState()
    {
        using var scenario = CreateSubmittedScenario(0);
        using var context = scenario.Database.Open();
        var before = CaptureStableState(context, scenario);
        var useCase = new InspectionHistoryEditUseCase();

        useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 4, SubmittedAtUtc.AddHours(1)));
        useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 0, SubmittedAtUtc.AddHours(2)));

        Assert.Equal(before, CaptureStableState(context, scenario));
        var item = context.InspectionItems.AsNoTracking().Single();
        Assert.Equal(0, item.CheckedQty);
        Assert.Equal(SubmittedAtUtc.AddHours(2), item.UpdatedAtUtc);
        var revisions = context.InspectionItemRevisions.AsNoTracking()
            .OrderBy(revision => revision.ChangedAtUtc)
            .ThenBy(revision => revision.Id)
            .Select(revision => new { revision.PreviousCheckedQty, revision.NewCheckedQty })
            .ToArray();
        Assert.Equal(new[] { 0, 4 }, revisions.Select(revision => revision.PreviousCheckedQty));
        Assert.Equal(new[] { 4, 0 }, revisions.Select(revision => revision.NewCheckedQty));
    }

    [Fact]
    public void SameValueReturnsNoChangeWithoutRevisionOrTimestampUpdate()
    {
        var itemUpdatedAtUtc = SubmittedAtUtc.AddHours(2);
        using var scenario = CreateFormalScenario(3, itemUpdatedAtUtc: itemUpdatedAtUtc);
        using var context = scenario.Database.Open();

        var result = Execute(context, scenario, 3, itemUpdatedAtUtc.AddHours(1));

        Assert.Equal("no_change", result.Status);
        Assert.False(result.Changed);
        Assert.True(result.NoChange);
        Assert.Equal(3, result.PreviousCheckedQty);
        Assert.Equal(3, result.NewCheckedQty);
        Assert.Null(result.RevisionId);
        Assert.Equal(itemUpdatedAtUtc, result.UpdatedAtUtc);
        Assert.Empty(context.InspectionItemRevisions.AsNoTracking());
        Assert.Equal(itemUpdatedAtUtc, context.InspectionItems.AsNoTracking().Single().UpdatedAtUtc);
    }

    [Fact]
    public void MissingNonCompletedCrossInspectionAndDraftOnlyRecordsReturnNotFound()
    {
        using (var missingDatabase = SqliteTestDatabase.Create())
        using (var context = missingDatabase.Open())
        {
            var result = new InspectionHistoryEditUseCase().Execute(
                context,
                new(999, 999, 1, SubmittedAtUtc.AddHours(1)));

            Assert.Equal("not_found", result.Status);
            Assert.Null(result.PreviousCheckedQty);
            Assert.Null(result.RevisionId);
        }

        using (var nonCompleted = CreateFormalScenario(1, taskStatus: "open"))
        using (var context = nonCompleted.Database.Open())
        {
            var result = Execute(context, nonCompleted, 2, SubmittedAtUtc.AddHours(1));

            Assert.Equal("not_found", result.Status);
            Assert.Equal(1, context.InspectionItems.AsNoTracking().Single().CheckedQty);
            Assert.Empty(context.InspectionItemRevisions.AsNoTracking());
        }

        using (var database = SqliteTestDatabase.Create())
        {
            ScenarioIds first;
            ScenarioIds second;
            using (var seed = database.Open())
            {
                first = AddFormalGraph(seed, "CROSS-FIRST", 1);
                second = AddFormalGraph(seed, "CROSS-SECOND", 2);
            }

            using var context = database.Open();
            var result = new InspectionHistoryEditUseCase().Execute(
                context,
                new(first.InspectionId, second.InspectionItemId, 9, SubmittedAtUtc.AddHours(1)));

            Assert.Equal("not_found", result.Status);
            Assert.Equal(
                new[] { 1, 2 },
                context.InspectionItems.AsNoTracking().OrderBy(item => item.Id).Select(item => item.CheckedQty));
            Assert.Empty(context.InspectionItemRevisions.AsNoTracking());
        }

        using (var draftOnlyDatabase = SqliteTestDatabase.Create())
        using (var context = draftOnlyDatabase.Open())
        {
            var product = new Product { ProductCode = "DRAFT-ONLY" };
            context.Products.Add(product);
            context.SaveChanges();
            var task = new ProductTask { ProductId = product.Id, Status = "open" };
            context.Tasks.Add(task);
            context.SaveChanges();
            context.Drafts.Add(new InspectionDraft { TaskId = task.Id });
            context.SaveChanges();

            var result = new InspectionHistoryEditUseCase().Execute(
                context,
                new(1, 1, 2, SubmittedAtUtc.AddHours(1)));

            Assert.Equal("not_found", result.Status);
            Assert.Empty(context.InspectionItemRevisions.AsNoTracking());
        }
    }

    [Fact]
    public void InvalidIdsNegativeQuantityAndNonUtcTimeAreRejectedBeforeWrites()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        var useCase = new InspectionHistoryEditUseCase();

        Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute(
            context,
            new(0, scenario.InspectionItemId, 2, SubmittedAtUtc.AddHours(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute(
            context,
            new(scenario.InspectionId, 0, 2, SubmittedAtUtc.AddHours(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, -1, SubmittedAtUtc.AddHours(1))));
        Assert.Throws<ArgumentException>(() => useCase.Execute(
            context,
            new(
                scenario.InspectionId,
                scenario.InspectionItemId,
                2,
                new DateTime(2026, 8, 29, 9, 0, 0, DateTimeKind.Local))));

        Assert.Equal(1, context.InspectionItems.AsNoTracking().Single().CheckedQty);
        Assert.Empty(context.InspectionItemRevisions.AsNoTracking());
    }

    [Fact]
    public void ChangedAtCannotPrecedeSubmissionItemOrExistingRevisionTime()
    {
        var itemUpdatedAtUtc = SubmittedAtUtc.AddHours(2);
        using var scenario = CreateFormalScenario(1, itemUpdatedAtUtc: itemUpdatedAtUtc);
        using var context = scenario.Database.Open();
        var useCase = new InspectionHistoryEditUseCase();

        Assert.Throws<ArgumentException>(() => useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 2, SubmittedAtUtc.AddHours(1))));

        var firstChangedAtUtc = itemUpdatedAtUtc.AddHours(1);
        useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 2, firstChangedAtUtc));
        Assert.Throws<ArgumentException>(() => useCase.Execute(
            context,
            new(scenario.InspectionId, scenario.InspectionItemId, 3, firstChangedAtUtc.AddMinutes(-1))));

        var item = context.InspectionItems.AsNoTracking().Single();
        Assert.Equal(2, item.CheckedQty);
        Assert.Equal(firstChangedAtUtc, item.UpdatedAtUtc);
        Assert.Single(context.InspectionItemRevisions.AsNoTracking());
    }

    [Fact]
    public void FreshDatabaseValueWinsOverUnchangedTrackedEntity()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        _ = context.InspectionItems.Single();
        var externalUpdatedAtUtc = SubmittedAtUtc.AddHours(1);
        using (var external = scenario.Database.Open())
        {
            var item = external.InspectionItems.Single();
            item.CheckedQty = 7;
            item.UpdatedAtUtc = externalUpdatedAtUtc;
            external.SaveChanges();
        }

        var result = Execute(context, scenario, 9, externalUpdatedAtUtc.AddHours(1));

        Assert.Equal(7, result.PreviousCheckedQty);
        Assert.Equal(9, result.NewCheckedQty);
        using var verify = scenario.Database.Open();
        Assert.Equal(7, verify.InspectionItemRevisions.AsNoTracking().Single().PreviousCheckedQty);
        Assert.Equal(9, verify.InspectionItems.AsNoTracking().Single().CheckedQty);
    }

    [Fact]
    public void WriteQueryTracksItemWhenContextDefaultsToNoTracking()
    {
        using var scenario = CreateFormalScenario(1);
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = scenario.Database.Path,
                ForeignKeys = true
            }.ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        using var context = new StoreDbContext(options);

        var result = Execute(context, scenario, 8, SubmittedAtUtc.AddHours(1));

        Assert.Equal("changed", result.Status);
        using var verify = scenario.Database.Open();
        Assert.Equal(8, verify.InspectionItems.AsNoTracking().Single().CheckedQty);
        Assert.Equal(1, verify.InspectionItemRevisions.AsNoTracking().Single().PreviousCheckedQty);
    }

    [Fact]
    public void PendingCallerChangesAreRejectedAndNeverSavedByHistoryEdit()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        var product = context.Products.Single();
        product.CurrentName = "caller pending change";

        Assert.Throws<InvalidOperationException>(() => Execute(
            context,
            scenario,
            2,
            SubmittedAtUtc.AddHours(1)));
        Assert.True(context.ChangeTracker.HasChanges());

        using var verify = scenario.Database.Open();
        Assert.Equal("Product FORMAL-HISTORY", verify.Products.AsNoTracking().Single().CurrentName);
        Assert.Equal(1, verify.InspectionItems.AsNoTracking().Single().CheckedQty);
        Assert.Empty(verify.InspectionItemRevisions.AsNoTracking());
    }

    [Fact]
    public void RevisionInsertFailureRollsBackCurrentValueAndClearsTracker()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_history_revision_insert
            BEFORE INSERT ON inspection_item_revisions
            BEGIN
                SELECT RAISE(ABORT, 'forced revision insert failure');
            END;
            """);

        Assert.Throws<DbUpdateException>(() => Execute(
            context,
            scenario,
            2,
            SubmittedAtUtc.AddHours(1)));
        Assert.False(context.ChangeTracker.HasChanges());

        using var verify = scenario.Database.Open();
        Assert.Equal(1, verify.InspectionItems.AsNoTracking().Single().CheckedQty);
        Assert.Empty(verify.InspectionItemRevisions.AsNoTracking());
    }

    [Fact]
    public void CurrentValueUpdateFailureDoesNotLeaveAnOrphanRevision()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_history_item_update
            BEFORE UPDATE OF checked_qty ON inspection_items
            BEGIN
                SELECT RAISE(ABORT, 'forced item update failure');
            END;
            """);

        Assert.Throws<DbUpdateException>(() => Execute(
            context,
            scenario,
            2,
            SubmittedAtUtc.AddHours(1)));
        Assert.False(context.ChangeTracker.HasChanges());

        using var verify = scenario.Database.Open();
        Assert.Equal(1, verify.InspectionItems.AsNoTracking().Single().CheckedQty);
        Assert.Empty(verify.InspectionItemRevisions.AsNoTracking());
    }

    [Fact]
    public void ExistingOuterTransactionIsRejectedWithoutWrites()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        using var transaction = context.Database.BeginTransaction();

        Assert.Throws<InvalidOperationException>(() => Execute(
            context,
            scenario,
            2,
            SubmittedAtUtc.AddHours(1)));
        Assert.NotNull(context.Database.CurrentTransaction);
        transaction.Rollback();

        using var verify = scenario.Database.Open();
        Assert.Equal(1, verify.InspectionItems.AsNoTracking().Single().CheckedQty);
        Assert.Empty(verify.InspectionItemRevisions.AsNoTracking());
    }

    [Fact]
    public void HistoryQueriesReadCurrentValueAndRevisionsWithoutTrackingOrOtherWrites()
    {
        using var scenario = CreateFormalScenario(1);
        using (var editContext = scenario.Database.Open())
        {
            Execute(editContext, scenario, 6, SubmittedAtUtc.AddHours(1));
        }

        using var context = scenario.Database.Open();
        var before = CaptureState(context, scenario);
        var query = new InspectionHistoryQuery();
        var detail = query.GetDetail(context, scenario.InspectionId);
        var history = query.GetItemRevisions(context, scenario.InspectionId, scenario.InspectionItemId);
        var after = CaptureState(context, scenario);

        Assert.Equal(before, after);
        Assert.Equal("found", detail.Status);
        Assert.Equal(6, detail.Detail!.Items.Single().CheckedQty);
        Assert.Equal("found", history.Status);
        Assert.NotNull(history.History);
        Assert.Equal(6, history.History!.CurrentCheckedQty);
        var revision = Assert.Single(history.History.Revisions);
        Assert.Equal(1, revision.PreviousCheckedQty);
        Assert.Equal(6, revision.NewCheckedQty);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void RevisionQueryUsesNotFoundAndArgumentValidationStyle()
    {
        using var scenario = CreateFormalScenario(1);
        using var context = scenario.Database.Open();
        var query = new InspectionHistoryQuery();

        var result = query.GetItemRevisions(context, scenario.InspectionId, 999);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.History);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            query.GetItemRevisions(context, 0, scenario.InspectionItemId));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            query.GetItemRevisions(context, scenario.InspectionId, 0));
    }

    [Fact]
    public void RevisionQueryLoadsCurrentAndHistoryWithOneReaderCommand()
    {
        using var scenario = CreateFormalScenario(1);
        using (var editContext = scenario.Database.Open())
        {
            Execute(editContext, scenario, 4, SubmittedAtUtc.AddHours(1));
        }

        var interceptor = new CountingCommandInterceptor();
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(
                new SqliteConnectionStringBuilder
                {
                    DataSource = scenario.Database.Path,
                    ForeignKeys = true
                }.ToString(),
                sqliteOptions => sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(interceptor)
            .Options;
        using var context = new StoreDbContext(options);

        var result = new InspectionHistoryQuery()
            .GetItemRevisions(context, scenario.InspectionId, scenario.InspectionItemId);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.History);
        Assert.Equal(4, result.History!.CurrentCheckedQty);
        Assert.Single(result.History.Revisions);
        Assert.Equal(1, interceptor.ReaderCommandCount);
    }

    private static InspectionHistoryEditResult Execute(
        StoreDbContext context,
        Scenario scenario,
        int newCheckedQty,
        DateTime changedAtUtc) => new InspectionHistoryEditUseCase().Execute(
        context,
        new(
            scenario.InspectionId,
            scenario.InspectionItemId,
            newCheckedQty,
            changedAtUtc));

    private static Scenario CreateSubmittedScenario(int checkedQty)
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var product = new Product
        {
            ProductCode = "SUBMITTED-HISTORY",
            CurrentName = "Submitted history product",
            CurrentBarcode = "690000000099",
            ExcelStockQty = 20,
            EffectiveStockQty = 20,
            EffectiveStockSource = "excel",
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.Products.Add(product);
        context.SaveChanges();

        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = "open",
            HighestStage = ExpiryStageCalculator.Discount50,
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();

        var batch = new Batch
        {
            ProductId = product.Id,
            ProductionDate = new DateOnly(2026, 8, 1),
            ExpiryDate = new DateOnly(2026, 9, 1),
            ShelfLifeValue = 1,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            CurrentStage = ExpiryStageCalculator.Discount50,
            TrackingStatus = "active",
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.Batches.Add(batch);
        context.SaveChanges();

        var taskItem = new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            Stage = ExpiryStageCalculator.Discount50,
            AttentionVersion = 0,
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.TaskItems.Add(taskItem);
        context.SaveChanges();

        var draft = new InspectionDraft
        {
            TaskId = task.Id,
            InspectorName = "Submitted inspector",
            CheckDate = new DateOnly(2026, 8, 29),
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.Drafts.Add(draft);
        context.SaveChanges();
        context.DraftItems.Add(new InspectionDraftItem
        {
            DraftId = draft.Id,
            TaskItemId = taskItem.Id,
            TaskId = task.Id,
            CheckedQty = checkedQty,
            ConfirmedAttentionVersion = 0
        });
        context.SaveChanges();

        var submission = new InspectionSubmissionUseCase().Submit(
            context,
            new(task.Id, product.Id, new DateOnly(2026, 8, 29), SubmittedAtUtc));
        Assert.True(submission.Submitted);
        var inspection = context.Inspections.Single();
        var item = context.InspectionItems.Single();
        return new(database, product.Id, task.Id, inspection.Id, item.Id);
    }

    private static Scenario CreateFormalScenario(
        int checkedQty,
        string taskStatus = "completed",
        DateTime? itemUpdatedAtUtc = null)
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var ids = AddFormalGraph(context, "FORMAL-HISTORY", checkedQty, taskStatus, itemUpdatedAtUtc);
        return new(
            database,
            ids.ProductId,
            ids.TaskId,
            ids.InspectionId,
            ids.InspectionItemId);
    }

    private static ScenarioIds AddFormalGraph(
        StoreDbContext context,
        string productCode,
        int checkedQty,
        string taskStatus = "completed",
        DateTime? itemUpdatedAtUtc = null)
    {
        var itemTime = itemUpdatedAtUtc ?? SubmittedAtUtc;
        var product = new Product
        {
            ProductCode = productCode,
            CurrentName = $"Product {productCode}",
            CurrentBarcode = $"BAR-{productCode}",
            ExcelStockQty = 20,
            EffectiveStockQty = 20,
            EffectiveStockSource = "excel",
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.Products.Add(product);
        context.SaveChanges();

        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = taskStatus,
            HighestStage = ExpiryStageCalculator.Discount50,
            ClosedAtUtc = taskStatus == "open" ? null : SubmittedAtUtc,
            CloseReason = taskStatus == "system_closed" ? "test" : null,
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SubmittedAtUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();

        var batch = new Batch
        {
            ProductId = product.Id,
            ProductionDate = new DateOnly(2026, 8, 1),
            ExpiryDate = new DateOnly(2026, 9, 1),
            ShelfLifeValue = 1,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            CurrentStage = ExpiryStageCalculator.Discount50,
            TrackingStatus = "active",
            CreatedAtUtc = SeedAtUtc,
            UpdatedAtUtc = SeedAtUtc
        };
        context.Batches.Add(batch);
        context.SaveChanges();

        var inspection = new Inspection
        {
            TaskId = task.Id,
            ProductId = product.Id,
            ProductCodeSnapshot = productCode,
            ProductNameSnapshot = product.CurrentName,
            BarcodeSnapshot = product.CurrentBarcode,
            StageSnapshot = ExpiryStageCalculator.Discount50,
            StockQtySnapshot = 20,
            InspectorName = "History inspector",
            CheckDate = new DateOnly(2026, 8, 29),
            SubmittedAtUtc = SubmittedAtUtc
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();

        var item = new InspectionItem
        {
            InspectionId = inspection.Id,
            ProductId = product.Id,
            BatchId = batch.Id,
            ProductionDateSnapshot = batch.ProductionDate,
            ExpiryDateSnapshot = batch.ExpiryDate,
            StageSnapshot = batch.CurrentStage,
            ArrivalQtySnapshot = batch.CurrentArrivalQty,
            CheckedQty = checkedQty,
            UpdatedAtUtc = itemTime
        };
        context.InspectionItems.Add(item);
        context.SaveChanges();
        return new(product.Id, task.Id, inspection.Id, item.Id);
    }

    private static string CaptureStableState(StoreDbContext context, Scenario scenario)
    {
        var batchId = context.InspectionItems.AsNoTracking()
            .Where(item => item.Id == scenario.InspectionItemId)
            .Select(item => item.BatchId)
            .Single();
        return JsonSerializer.Serialize(new
        {
            Product = context.Products.AsNoTracking()
                .Where(product => product.Id == scenario.ProductId)
                .Select(product => new
                {
                    product.Id,
                    product.ProductCode,
                    product.CurrentName,
                    product.CurrentBarcode,
                    product.ExcelStockQty,
                    product.EffectiveStockQty,
                    product.EffectiveStockSource,
                    product.LifecycleGeneration,
                    product.IsStockZeroTerminated,
                    product.LastSeenImportId,
                    product.CreatedAtUtc,
                    product.UpdatedAtUtc
                })
                .Single(),
            Batch = context.Batches.AsNoTracking()
                .Where(batch => batch.Id == batchId)
                .Select(batch => new
                {
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
                    batch.UpdatedAtUtc
                })
                .Single(),
            Task = context.Tasks.AsNoTracking()
                .Where(task => task.Id == scenario.TaskId)
                .Select(task => new
                {
                    task.Id,
                    task.ProductId,
                    task.Status,
                    task.HighestStage,
                    task.CreatedAtUtc,
                    task.UpdatedAtUtc,
                    task.ClosedAtUtc,
                    task.CloseReason
                })
                .Single(),
            TaskItems = context.TaskItems.AsNoTracking()
                .Where(item => item.TaskId == scenario.TaskId)
                .OrderBy(item => item.Id)
                .Select(item => new
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
                })
                .ToArray(),
            Inspection = context.Inspections.AsNoTracking()
                .Where(inspection => inspection.Id == scenario.InspectionId)
                .Select(inspection => new
                {
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
                    inspection.SubmittedAtUtc
                })
                .Single(),
            InspectionItems = context.InspectionItems.AsNoTracking()
                .Where(item => item.InspectionId == scenario.InspectionId)
                .OrderBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.InspectionId,
                    item.ProductId,
                    item.BatchId,
                    item.ProductionDateSnapshot,
                    item.ExpiryDateSnapshot,
                    item.StageSnapshot,
                    item.ArrivalQtySnapshot
                })
                .ToArray(),
            Drafts = context.Drafts.AsNoTracking()
                .Where(draft => draft.TaskId == scenario.TaskId)
                .OrderBy(draft => draft.Id)
                .Select(draft => new
                {
                    draft.Id,
                    draft.TaskId,
                    draft.InspectorName,
                    draft.CheckDate,
                    draft.IsInvalid,
                    draft.InvalidReason,
                    draft.InvalidatedAtUtc,
                    draft.CreatedAtUtc,
                    draft.UpdatedAtUtc
                })
                .ToArray(),
            DraftItems = context.DraftItems.AsNoTracking()
                .Where(item => item.TaskId == scenario.TaskId)
                .OrderBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.DraftId,
                    item.TaskItemId,
                    item.TaskId,
                    item.CheckedQty,
                    item.ConfirmedAttentionVersion
                })
                .ToArray(),
            LifecycleEvents = context.LifecycleEvents.AsNoTracking()
                .Where(lifecycleEvent => lifecycleEvent.ProductId == scenario.ProductId)
                .OrderBy(lifecycleEvent => lifecycleEvent.Id)
                .Select(lifecycleEvent => new
                {
                    lifecycleEvent.Id,
                    lifecycleEvent.ProductId,
                    lifecycleEvent.BatchId,
                    lifecycleEvent.EventType,
                    lifecycleEvent.Reason,
                    lifecycleEvent.OccurredAtUtc,
                    lifecycleEvent.SourceImportId,
                    lifecycleEvent.SourceInspectionId,
                    lifecycleEvent.SourceAdjustmentId
                })
                .ToArray()
        });
    }

    private static string CaptureState(StoreDbContext context, Scenario scenario)
    {
        var task = context.Tasks.AsNoTracking().Single(task => task.Id == scenario.TaskId);
        var item = context.InspectionItems.AsNoTracking().Single(item => item.Id == scenario.InspectionItemId);
        return JsonSerializer.Serialize(new
        {
            Stable = CaptureStableState(context, scenario),
            TaskStatus = task.Status,
            Item = new { item.Id, item.CheckedQty, item.UpdatedAtUtc },
            Revisions = context.InspectionItemRevisions.AsNoTracking()
                .Where(revision => revision.InspectionItemId == scenario.InspectionItemId)
                .OrderBy(revision => revision.Id)
                .Select(revision => new
                {
                    revision.Id,
                    revision.InspectionItemId,
                    revision.PreviousCheckedQty,
                    revision.NewCheckedQty,
                    revision.ChangedAtUtc
                })
                .ToArray()
        });
    }

    private sealed record Scenario(
        SqliteTestDatabase Database,
        long ProductId,
        long TaskId,
        long InspectionId,
        long InspectionItemId) : IDisposable
    {
        public void Dispose() => Database.Dispose();
    }

    private sealed record ScenarioIds(
        long ProductId,
        long TaskId,
        long InspectionId,
        long InspectionItemId);


    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommandCount++;
            return result;
        }
    }
}
