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
            Assert.Equal(2, context.Drafts.Count());
        }

        using var confirmed = database.Open();
        var result = Submit(confirmed, database.TaskIds, [new(database.TaskIds.Max(), database.ProductIds[^1], 5, 6)]);
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
        Assert.Equal(2, context.DraftItems.Count());
        Assert.All(context.Batches, batch => { Assert.Equal("active", batch.TrackingStatus); Assert.Equal(0, batch.HandledAttentionVersion); });
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

    [Fact]
    public void SingleTaskMultiBatchZeroAndPositiveMatchSingleSubmissionAndHistory()
    {
        using var bulk = CreateScenario(0);
        using var direct = CreateScenario(0);
        long bulkSecond;
        long directSecond;
        using (var context = bulk.Open()) bulkSecond = AddItem(context, bulk.TaskIds[0], 3);
        using (var context = direct.Open()) directSecond = AddItem(context, direct.TaskIds[0], 3);

        using (var context = bulk.Open())
        {
            var result = Submit(context, bulk.TaskIds);
            Assert.True(result.Submitted);
            Assert.Single(context.Inspections);
            Assert.Equal(new[] { 0, 3 }, context.InspectionItems.OrderBy(item => item.Id).Select(item => item.CheckedQty));
            Assert.Equal(0, context.InspectionItemRevisions.Count());
            Assert.Empty(context.Drafts);
            Assert.Equal("completed", context.Tasks.Single().Status);
            Assert.Equal(2, context.Batches.Single(batch => batch.Id != bulkSecond).HandledAttentionVersion + context.Batches.Single(batch => batch.Id == bulkSecond).HandledAttentionVersion);
            Assert.Single(context.LifecycleEvents);
            Assert.Contains(context.Batches, batch => batch.TrackingStatus == "stopped");
            Assert.Contains(context.Batches, batch => batch.TrackingStatus == "active");
            Assert.Single(new InspectionHistoryQuery().List(context));
        }

        using var directContext = direct.Open();
        var task = directContext.Tasks.Single();
        var directResult = new InspectionSubmissionUseCase().Submit(directContext, new(task.Id, task.ProductId, BusinessDate, Utc));
        Assert.True(directResult.Submitted);
        Assert.Equal(2, directContext.InspectionItems.Count());
        Assert.Equal(1, directContext.LifecycleEvents.Count());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("nonpositive")]
    [InlineData("blank_inspector")]
    [InlineData("long_inspector")]
    [InlineData("default_date")]
    [InlineData("default_business_date")]
    [InlineData("future_date")]
    [InlineData("default_utc")]
    [InlineData("local_utc")]
    [InlineData("duplicate_confirmation")]
    [InlineData("negative_confirmation")]
    [InlineData("negative_total_confirmation")]
    [InlineData("invalid_confirmation_product")]
    [InlineData("wrong_confirmation_product")]
    [InlineData("outside_confirmation")]
    public void RequestGateRejectsInvalidWholeRequest(string mode)
    {
        using var database = CreateScenario(1);
        using var context = database.Open();
        var id = database.TaskIds[0];
        var request = mode switch
        {
            "empty" => new BulkInspectionSubmissionRequest([], "Inspector", BusinessDate, BusinessDate, Utc),
            "duplicate" => new BulkInspectionSubmissionRequest([id, id], "Inspector", BusinessDate, BusinessDate, Utc),
            "nonpositive" => new BulkInspectionSubmissionRequest([0], "Inspector", BusinessDate, BusinessDate, Utc),
            "blank_inspector" => new BulkInspectionSubmissionRequest([id], "  ", BusinessDate, BusinessDate, Utc),
            "long_inspector" => new BulkInspectionSubmissionRequest([id], new string('x', 201), BusinessDate, BusinessDate, Utc),
            "default_date" => new BulkInspectionSubmissionRequest([id], "Inspector", default, BusinessDate, Utc),
            "default_business_date" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, default, Utc),
            "future_date" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate.AddDays(1), BusinessDate, Utc),
            "default_utc" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, DateTime.SpecifyKind(default, DateTimeKind.Utc)),
            "local_utc" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc.ToLocalTime()),
            "duplicate_confirmation" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc, [new(id, database.ProductIds[0], 5, 1), new(id, database.ProductIds[0], 5, 1)]),
            "negative_confirmation" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc, [new(id, database.ProductIds[0], -1, 1)]),
            "negative_total_confirmation" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc, [new(id, database.ProductIds[0], 1, -1)]),
            "invalid_confirmation_product" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc, [new(id, 0, 5, 1)]),
            "wrong_confirmation_product" => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc, [new(id, 999, 5, 1)]),
            _ => new BulkInspectionSubmissionRequest([id], "Inspector", BusinessDate, BusinessDate, Utc, [new(id + 100, database.ProductIds[0], 5, 1)])
        };
        Assert.Throws<ArgumentException>(() => new BulkInspectionSubmissionUseCase().Submit(context, request));
        Assert.Empty(context.Inspections);
    }

    [Theory]
    [InlineData("system_closed")]
    [InlineData("existing_inspection")]
    [InlineData("missing_draft")]
    [InlineData("invalid_draft")]
    [InlineData("incomplete_draft")]
    [InlineData("inspector_mismatch")]
    [InlineData("check_date_mismatch")]
    [InlineData("missing_quantity")]
    [InlineData("reconfirmation")]
    [InlineData("attention")]
    [InlineData("stage")]
    [InlineData("stopped")]
    [InlineData("arrival")]
    [InlineData("max_arrival")]
    [InlineData("ownership")]
    [InlineData("excluded")]
    [InlineData("unresolved")]
    [InlineData("stock_zero_terminated")]
    [InlineData("negative_stock")]
    [InlineData("invalid_policy")]
    [InlineData("no_baseline")]
    public void PrecheckRejectsCurrentLifecycleAndDraftViolationsBeforeSubmit(string mode)
    {
        using var database = CreateScenario(1);
        using var setup = database.Open();
        if (mode == "ownership")
        {
            setup.Database.OpenConnection();
            try
            {
                setup.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
                setup.Database.ExecuteSqlRaw("UPDATE task_items SET product_id = 999");
                setup.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
            }
            finally { setup.Database.CloseConnection(); }
            setup.ChangeTracker.Clear();
            Assert.Throws<InvalidOperationException>(() => Submit(setup, database.TaskIds));
            return;
        }
        var task = setup.Tasks.Single();
        switch (mode)
        {
            case "system_closed": task.Status = "system_closed"; task.ClosedAtUtc = Utc; task.CloseReason = "closed"; break;
            case "existing_inspection": setup.Inspections.Add(new Inspection { TaskId = task.Id, ProductId = task.ProductId, ProductCodeSnapshot = "OLD", StageSnapshot = ExpiryStageCalculator.Discount50, StockQtySnapshot = 5, InspectorName = "Inspector", CheckDate = BusinessDate, SubmittedAtUtc = Utc }); break;
            case "missing_draft": setup.DraftItems.RemoveRange(setup.DraftItems); setup.Drafts.RemoveRange(setup.Drafts); break;
            case "invalid_draft": var invalid = setup.Drafts.Single(); invalid.IsInvalid = true; invalid.InvalidReason = "invalid"; invalid.InvalidatedAtUtc = Utc; break;
            case "incomplete_draft": setup.DraftItems.Remove(setup.DraftItems.Single()); break;
            case "inspector_mismatch": setup.Drafts.Single().InspectorName = "Other"; break;
            case "check_date_mismatch": setup.Drafts.Single().CheckDate = BusinessDate.AddDays(-1); break;
            case "missing_quantity": setup.DraftItems.Single().CheckedQty = null; break;
            case "reconfirmation": setup.TaskItems.Single().RequiresReconfirmation = true; break;
            case "attention": setup.Batches.Single().AttentionVersion = 2; break;
            case "stage": setup.Batches.Single().CurrentStage = ExpiryStageCalculator.Withdraw; break;
            case "stopped": setup.Batches.Single().TrackingStatus = "stopped"; break;
            case "arrival": setup.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = ON"); setup.Database.ExecuteSqlRaw("UPDATE batches SET current_arrival_qty = -1"); break;
            case "max_arrival": setup.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = ON"); setup.Database.ExecuteSqlRaw("UPDATE batches SET max_arrival_qty = -1"); break;
            case "excluded": var excluded = setup.Products.Single(); excluded.ExpiryManagementStatus = ExpiryManagementStatus.Excluded; excluded.PolicyCode = null; excluded.PolicyVersion = null; break;
            case "unresolved": var unresolved = setup.Products.Single(); unresolved.ExpiryManagementStatus = ExpiryManagementStatus.Unresolved; unresolved.PolicyCode = null; unresolved.PolicyVersion = null; break;
            case "stock_zero_terminated": setup.Products.Single().IsStockZeroTerminated = true; break;
            case "negative_stock": setup.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = ON"); setup.Database.ExecuteSqlRaw("UPDATE products SET effective_stock_qty = -1"); break;
            case "no_baseline": setup.ScopeBaselines.RemoveRange(setup.ScopeBaselines); break;
            case "invalid_policy": setup.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = ON"); setup.Database.ExecuteSqlRaw("UPDATE products SET policy_code = 'invalid'"); break;
        }
        setup.SaveChanges();
        setup.ChangeTracker.Clear();
        var before = setup.Inspections.Count();
        Assert.Throws<InvalidOperationException>(() => Submit(setup, database.TaskIds));
        Assert.Equal(before, setup.Inspections.Count());
    }

    [Fact]
    public void MultipleOverStockWarningsAreCurrentExactAndAnyStaleConfirmationRollsBackNormalTask()
    {
        using var database = CreateScenario(1, 6, 7);
        using (var context = database.Open())
        {
            var warning = Submit(context, database.TaskIds);
            Assert.Equal(BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation, warning.Outcome);
            Assert.Equal(2, warning.OverStockConfirmations.Count);
            Assert.Empty(context.Inspections);
        }

        using (var stale = database.Open())
        {
            var result = Submit(stale, database.TaskIds, [new(database.TaskIds[1], database.ProductIds[1], 5, 6)]);
            Assert.Equal(BulkInspectionSubmissionOutcome.OverStockConfirmationStale, result.Outcome);
            Assert.Equal(2, result.OverStockConfirmations.Count);
            Assert.Empty(stale.Inspections);
        }

        using (var extra = database.Open())
        {
            var result = Submit(extra, database.TaskIds, [new(database.TaskIds[1], database.ProductIds[1], 5, 6), new(database.TaskIds[2], database.ProductIds[2], 5, 7), new(database.TaskIds[0], database.ProductIds[0], 5, 1)]);
            Assert.Equal(BulkInspectionSubmissionOutcome.OverStockConfirmationStale, result.Outcome);
            Assert.Empty(extra.Inspections);
        }

        using var confirmed = database.Open();
        var success = Submit(confirmed, database.TaskIds, [new(database.TaskIds[1], database.ProductIds[1], 5, 6), new(database.TaskIds[2], database.ProductIds[2], 5, 7)]);
        Assert.True(success.Submitted);
        Assert.Equal(3, confirmed.Inspections.Count());
    }

    [Fact]
    public void ChangedStockOrDraftTotalMakesEveryPriorOverStockConfirmationStale()
    {
        using var database = CreateScenario(6, 7);
        IReadOnlyList<OverStockConfirmation> confirmations;
        using (var context = database.Open()) confirmations = Submit(context, database.TaskIds).OverStockConfirmations;
        using (var update = database.Open())
        {
            update.Products.Single(product => product.ProductCode == "BULK-0").EffectiveStockQty = 4;
            update.DraftItems.Single(item => item.TaskId == database.TaskIds[1]).CheckedQty = 8;
            update.SaveChanges();
        }
        using var stale = database.Open();
        var result = Submit(stale, database.TaskIds, confirmations);
        Assert.Equal(BulkInspectionSubmissionOutcome.OverStockConfirmationStale, result.Outcome);
        Assert.Equal([new(database.TaskIds[0], database.ProductIds[0], 4, 6), new(database.TaskIds[1], database.ProductIds[1], 5, 8)], result.OverStockConfirmations);
        Assert.Empty(stale.Inspections);
    }

    [Fact]
    public void OldConfirmationWhenNoTaskIsNowOverStockRollsBackAndReturnsNoWarnings()
    {
        using var database = CreateScenario(6);
        IReadOnlyList<OverStockConfirmation> confirmations;
        using (var context = database.Open()) confirmations = Submit(context, database.TaskIds).OverStockConfirmations;
        using (var update = database.Open())
        {
            update.DraftItems.Single().CheckedQty = 3;
            update.SaveChanges();
        }
        using var stale = database.Open();
        var result = Submit(stale, database.TaskIds, confirmations);
        Assert.Equal(BulkInspectionSubmissionOutcome.OverStockConfirmationStale, result.Outcome);
        Assert.Empty(result.OverStockConfirmations);
        Assert.Empty(stale.Inspections);
        Assert.Equal("open", stale.Tasks.Single().Status);
        Assert.Single(stale.Drafts);
    }

    [Fact]
    public void CompletedSignatureConflictAndCompletedConfirmationsFollowFrozenIdempotencyContract()
    {
        using var database = CreateScenario(1);
        using var context = database.Open();
        _ = Submit(context, database.TaskIds);
        Assert.Equal(BulkInspectionSubmissionOutcome.AlreadySubmitted, Submit(context, database.TaskIds, [new(database.TaskIds[0], database.ProductIds[0], 5, 1)]).Outcome);
        Assert.Throws<InvalidOperationException>(() => new BulkInspectionSubmissionUseCase().Submit(context, new(database.TaskIds, "Other", BusinessDate, BusinessDate, Utc)));
        Assert.Throws<InvalidOperationException>(() => new BulkInspectionSubmissionUseCase().Submit(context, new(database.TaskIds, "Inspector", BusinessDate.AddDays(-1), BusinessDate, Utc)));
        Assert.Throws<InvalidOperationException>(() => new BulkInspectionSubmissionUseCase().Submit(context, new(database.TaskIds, "Inspector", BusinessDate, BusinessDate, Utc.AddSeconds(1))));
        Assert.Single(context.Inspections);
    }

    [Fact]
    public void MissingTaskAndPostCompletionEligibilityChangesAreConflicts()
    {
        using var database = CreateScenario(1);
        using var context = database.Open();
        Assert.Throws<KeyNotFoundException>(() => Submit(context, [999]));
        _ = Submit(context, database.TaskIds);
        var product = context.Products.Single();
        product.ExpiryManagementStatus = ExpiryManagementStatus.Excluded;
        product.PolicyCode = null;
        product.PolicyVersion = null;
        context.SaveChanges();
        Assert.Throws<InvalidOperationException>(() => Submit(context, database.TaskIds));
        Assert.Single(context.Inspections);
    }

    private static BulkInspectionSubmissionResult Submit(StoreDbContext context, IReadOnlyCollection<long> taskIds, IReadOnlyCollection<OverStockConfirmation>? confirmations = null) =>
        new BulkInspectionSubmissionUseCase().Submit(context, new(taskIds, " Inspector ", BusinessDate, BusinessDate, Utc, confirmations));

    private static long AddItem(StoreDbContext context, long taskId, int quantity)
    {
        var task = context.Tasks.Single(item => item.Id == taskId);
        var product = context.Products.Single(item => item.Id == task.ProductId);
        var batch = new Batch { ProductId = product.Id, ProductionDate = BusinessDate.AddDays(-20), ExpiryDate = BusinessDate.AddDays(19), ShelfLifeValue = 30, ShelfLifeUnit = "D", CurrentArrivalQty = 5, MaxArrivalQty = 5, LifecycleGeneration = 1, TrackingStatus = "active", CurrentStage = ExpiryStageCalculator.Discount50, NextTriggerDate = BusinessDate.AddDays(1), AttentionVersion = 1, HandledAttentionVersion = 0, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
        context.Batches.Add(batch);
        context.SaveChanges();
        var item = new ProductTaskItem { TaskId = task.Id, ProductId = product.Id, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
        context.TaskItems.Add(item);
        context.SaveChanges();
        var draft = context.Drafts.Single(item => item.TaskId == task.Id);
        context.DraftItems.Add(new InspectionDraftItem { DraftId = draft.Id, TaskId = task.Id, TaskItemId = item.Id, CheckedQty = quantity, ConfirmedAttentionVersion = 1 });
        context.SaveChanges();
        return batch.Id;
    }

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
        var productIds = new List<long>();
        foreach (var (quantity, index) in quantities.Select((value, index) => (value, index)))
        {
            var product = new Product { ProductCode = $"BULK-{index}", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, EffectiveStockQty = 5, ExcelStockQty = 5, EffectiveStockSource = "excel", LifecycleGeneration = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
            context.Products.Add(product);
            context.SaveChanges();
            productIds.Add(product.Id);
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
        return new(database, taskIds.ToArray(), productIds.ToArray());
    }

    private sealed record Scenario(SqliteTestDatabase Database, long[] TaskIds, long[] ProductIds) : IDisposable
    {
        public StoreDbContext Open() => Database.Open();
        public void Dispose() => Database.Dispose();
    }
}
