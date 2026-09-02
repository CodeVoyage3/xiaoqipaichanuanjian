using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class BulkInspectionSubmissionUseCaseTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 2);
    private static readonly DateTime Utc = new(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SubmitsInTaskIdOrderAndExactReplayIsAlreadySubmitted()
    {
        using var database = CreateScenario(1, 0);
        using (var context = database.Open())
        {
            var result = Submit(context, database.TaskIds.Reverse().ToArray());
            Assert.True(result.Submitted);
            Assert.Equal(database.TaskIds.Order(), result.Tasks.Select(item => item.TaskId));
            Assert.Equal(2, context.Inspections.Count());
            Assert.Equal(2, new InspectionHistoryQuery().List(context).Count);
        }

        using var replay = database.Open();
        var again = Submit(replay, database.TaskIds);
        Assert.Equal(BulkInspectionSubmissionOutcome.AlreadySubmitted, again.Outcome);
        Assert.Equal(2, replay.Inspections.Count());
        Assert.Empty(replay.InspectionItemRevisions);
    }

    [Fact]
    public void WarningRollsBackEveryTaskUntilExactCurrentConfirmationsAreProvided()
    {
        using var database = CreateScenario(1, 6);
        using (var context = database.Open())
        {
            var warning = Submit(context, database.TaskIds);
            Assert.Equal(BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation, warning.Outcome);
            var fact = Assert.Single(warning.OverStockConfirmations);
            Assert.Equal(database.TaskIds.Max(), fact.TaskId);
            Assert.Empty(context.Inspections);
            Assert.All(context.Tasks, task => Assert.Equal("open", task.Status));
        }

        using var confirmed = database.Open();
        var result = Submit(confirmed, database.TaskIds, [new(database.TaskIds.Max(), 5, 6)]);
        Assert.True(result.Submitted);
        Assert.Equal(2, confirmed.Inspections.Count());
    }

    [Fact]
    public void SecondTaskFailureRollsBackFirstTask()
    {
        using var database = CreateScenario(0, 1);
        using (var setup = database.Open())
        {
            setup.Database.ExecuteSqlRaw(string.Format("CREATE TRIGGER fail_second_task BEFORE UPDATE OF status ON tasks WHEN NEW.id = {0} AND NEW.status = 'completed' BEGIN SELECT RAISE(ABORT, 'second task'); END;", database.TaskIds.Max()));
        }

        using var context = database.Open();
        Assert.Throws<DbUpdateException>(() => Submit(context, database.TaskIds));
        Assert.Empty(context.Inspections);
        Assert.All(context.Tasks, task => Assert.Equal("open", task.Status));
        Assert.Equal(2, context.Drafts.Count());
        Assert.Empty(context.LifecycleEvents);
    }

    [Fact]
    public void RequestAndMixedCompletionConflictsWriteNothing()
    {
        using var database = CreateScenario(1, 1);
        using var context = database.Open();
        Assert.Throws<ArgumentException>(() => Submit(context, [database.TaskIds[0], database.TaskIds[0]]));
        _ = Submit(context, [database.TaskIds[0]]);
        Assert.Throws<InvalidOperationException>(() => Submit(context, database.TaskIds));
        Assert.Single(context.Inspections);
        Assert.Equal("open", context.Tasks.Single(task => task.Id == database.TaskIds[1]).Status);
    }

    private static BulkInspectionSubmissionResult Submit(StoreDbContext context, IReadOnlyCollection<long> taskIds, IReadOnlyCollection<OverStockConfirmation>? confirmations = null) =>
        new BulkInspectionSubmissionUseCase().Submit(context, new(taskIds, " Inspector ", BusinessDate, BusinessDate, Utc, confirmations));

    private static Scenario CreateScenario(params int[] quantities)
    {
        var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = new ImportRecord { SourceFileName = "source.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = Utc, ConfirmedAtUtc = Utc, Status = "succeeded" };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = ExpiryPolicies.Version1, CreatedImportId = import.Id, BusinessDate = BusinessDate, IsCompleted = true, CompletedAtUtc = Utc });
        context.SaveChanges();

        var taskIds = new List<long>();
        foreach (var (quantity, index) in quantities.Select((value, index) => (value, index)))
        {
            var product = new Product { ProductCode = $"BULK-{index}", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, EffectiveStockQty = 5, ExcelStockQty = 5, EffectiveStockSource = "excel", LifecycleGeneration = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
            context.Products.Add(product);
            context.SaveChanges();
            var task = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Discount50, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
            context.Tasks.Add(task);
            context.SaveChanges();
            var batch = new Batch { ProductId = product.Id, ProductionDate = BusinessDate.AddDays(-10 - index), ExpiryDate = BusinessDate.AddDays(20), ShelfLifeValue = 30, ShelfLifeUnit = "D", CurrentArrivalQty = 5, MaxArrivalQty = 5, LifecycleGeneration = 1, TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount50, NextTriggerDate = BusinessDate.AddDays(1), AttentionVersion = 1, HandledAttentionVersion = 0, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
            context.Batches.Add(batch);
            context.SaveChanges();
            var item = new ProductTaskItem { TaskId = task.Id, ProductId = product.Id, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
            context.TaskItems.Add(item);
            context.SaveChanges();
            var draft = new InspectionDraft { TaskId = task.Id, InspectorName = "Inspector", CheckDate = BusinessDate, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
            context.Drafts.Add(draft);
            context.SaveChanges();
            context.DraftItems.Add(new InspectionDraftItem { DraftId = draft.Id, TaskId = task.Id, TaskItemId = item.Id, CheckedQty = quantity, ConfirmedAttentionVersion = 1 });
            context.SaveChanges();
            taskIds.Add(task.Id);
        }
        return new(database, taskIds.ToArray());
    }

    private sealed record Scenario(SqliteTestDatabase Database, long[] TaskIds) : IDisposable
    {
        public StoreDbContext Open() => Database.Open();
        public void Dispose() => Database.Dispose();
    }
}
