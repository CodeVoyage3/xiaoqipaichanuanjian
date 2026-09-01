using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F01I04BoundaryTests
{
    [Theory]
    [InlineData(80, 30, ExpiryStageCalculator.Discount20)]
    [InlineData(50, 40, ExpiryStageCalculator.Withdraw)]
    public void ColdStartDiscountBaselineDoesNotReplayUntilHigherStage(int remainingDays, int advanceDays, string expectedStage)
    {
        using var database = SqliteTestDatabase.Create();
        var day = new DateOnly(2026, 9, 1);
        var utc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        long batchId;
        using (var context = database.Open())
        {
            var import = new ImportRecord { SourceFileName = "i04.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = utc, ConfirmedAtUtc = utc, Status = ImportStatuses.Succeeded };
            context.Imports.Add(import); context.SaveChanges();
            var product = new Product { ProductCode = "I04-BOUNDARY", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = 5, LastSeenImportId = import.Id };
            context.Products.Add(product); context.SaveChanges();
            var batch = new Batch { ProductId = product.Id, ProductionDate = day.AddDays(remainingDays - 360), ExpiryDate = day.AddDays(remainingDays), ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 5, MaxArrivalQty = 5, LastSeenImportId = import.Id, NextTriggerDate = day };
            context.Batches.Add(batch); context.SaveChanges(); batchId = batch.Id;
            Assert.True(new ColdStartScopeBaselineUseCase().Execute(context, new("food", ExpiryPolicies.Food, 1, import.Id, day, utc)).Started);
            Assert.Empty(context.Tasks);
            var baseline = context.ScopeBaselines.Single();
            var completed = baseline.CompletedAtUtc;
            var snapshot = context.BatchBaselines.Select(item => new { item.BatchId, item.ColdStartDisposition, item.StageAtBaseline }).ToArray();
            new StartupRecalculationUseCase().Execute(context, day, utc.AddMinutes(1));
            Assert.Empty(context.Tasks);
            Assert.Equal(completed, context.ScopeBaselines.Single().CompletedAtUtc);
            Assert.Equal(snapshot, context.BatchBaselines.Select(item => new { item.BatchId, item.ColdStartDisposition, item.StageAtBaseline }).ToArray());
            new StartupRecalculationUseCase().Execute(context, day.AddDays(advanceDays), utc.AddDays(advanceDays));
            Assert.Equal(expectedStage, context.Batches.Single(item => item.Id == batchId).CurrentStage);
            Assert.Single(context.Tasks);
            Assert.Equal(completed, context.ScopeBaselines.Single().CompletedAtUtc);
            Assert.Equal(snapshot, context.BatchBaselines.Select(item => new { item.BatchId, item.ColdStartDisposition, item.StageAtBaseline }).ToArray());
        }
    }

    [Fact]
    public void FormalSubmissionDoesNotBlockLaterHigherStageTask()
    {
        using var database = SqliteTestDatabase.Create();
        var day = new DateOnly(2026, 9, 1);
        var utc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        using var context = database.Open();
        var import = new ImportRecord { SourceFileName = "i04-submit.xlsx", SourceFileSha256 = new string('b', 64), ParsedAtUtc = utc, ConfirmedAtUtc = utc, Status = ImportStatuses.Succeeded };
        context.Imports.Add(import); context.SaveChanges();
        var product = new Product { ProductCode = "I04-SUBMIT", CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = 5, LastSeenImportId = import.Id };
        context.Products.Add(product); context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = day, IsCompleted = true, CompletedAtUtc = utc }); context.SaveChanges();
        var batch = new Batch { ProductId = product.Id, ProductionDate = day.AddDays(-280), ExpiryDate = day.AddDays(80), ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 5, MaxArrivalQty = 5, CurrentStage = ExpiryStageCalculator.Discount50, NextTriggerDate = day.AddDays(20), AttentionVersion = 2, HandledAttentionVersion = 1 };
        context.Batches.Add(batch); context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id, HighestStage = ExpiryStageCalculator.Discount50 };
        context.Tasks.Add(task); context.SaveChanges();
        var item = new ProductTaskItem { TaskId = task.Id, ProductId = product.Id, BatchId = batch.Id, Stage = ExpiryStageCalculator.Discount50, AttentionVersion = 2 };
        context.TaskItems.Add(item); context.SaveChanges();
        var draft = new InspectionDraft { TaskId = task.Id, InspectorName = "I04", CheckDate = day };
        context.Drafts.Add(draft); context.SaveChanges();
        context.DraftItems.Add(new InspectionDraftItem { DraftId = draft.Id, TaskId = task.Id, TaskItemId = item.Id, CheckedQty = 5, ConfirmedAttentionVersion = 2 }); context.SaveChanges();
        var submitted = new InspectionSubmissionUseCase().Submit(context, new(task.Id, product.Id, day, utc));
        Assert.True(submitted.Submitted);
        Assert.Equal("completed", context.Tasks.Single().Status);
        var inspectionId = submitted.InspectionId!.Value;
        Assert.Equal(2, context.Batches.Single().HandledAttentionVersion);
        new StartupRecalculationUseCase().Execute(context, day.AddDays(30), utc.AddDays(30));
        var open = Assert.Single(context.Tasks.Where(candidate => candidate.Status == "open"));
        Assert.Equal(ExpiryStageCalculator.Discount20, open.HighestStage);
        Assert.Equal(ExpiryStageCalculator.Discount20, Assert.Single(context.TaskItems.Where(candidate => candidate.TaskId == open.Id)).Stage);
        Assert.Equal(inspectionId, context.Inspections.Single().Id);
        Assert.Equal(2, context.Batches.Single().HandledAttentionVersion);
        Assert.Empty(context.InspectionItemRevisions);
    }
}
