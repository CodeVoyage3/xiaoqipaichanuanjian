using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S23T03ProductCatalogHistoryTests
{
    [Fact]
    public void Detail_splits_current_and_historical_batches_using_persisted_evidence_only()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var utc = new DateTime(2026, 9, 17, 8, 0, 0, DateTimeKind.Utc);
        var import = new ImportRecord { SourceFileName = "s23.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = utc, ConfirmedAtUtc = utc, Status = ImportStatuses.Succeeded };
        context.Add(import); context.SaveChanges();
        var product = new Product { ProductCode = "S23-H", CurrentName = "历史批次", CategoryCode = "food", EffectiveStockQty = 10, LastSeenImportId = import.Id };
        context.Add(product); context.SaveChanges();
        var expired = AddBatch(context, product, new(2026, 9, 1), "expired", "active", null, 0, 0);
        var pending = AddBatch(context, product, new(2026, 9, 2), "expired", "stopped", null, 0, 0);
        var future = AddBatch(context, product, new(2026, 10, 1), "none", "active", null, 0, 0);
        var noCatchup = AddBatch(context, product, new(2026, 9, 3), "expired", "active", null, 0, 0);
        var completed = AddBatch(context, product, new(2026, 9, 4), "expired", "active", null, 1, 1);
        var stopped = AddBatch(context, product, new(2026, 9, 5), "expired", "stopped", null, 0, 0);
        var ambiguous = AddBatch(context, product, new(2026, 9, 6), "expired", "closed", null, 0, 0);
        var resumedCompleted = AddBatch(context, product, new(2026, 9, 7), "expired", "active", null, 1, 1);
        var unhandledCompleted = AddBatch(context, product, new(2026, 9, 8), "expired", "active", null, 2, 1);
        var taskWithoutInspection = AddBatch(context, product, new(2026, 9, 9), "expired", "active", null, 0, 0);
        context.SaveChanges();

        var open = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Expired };
        context.Add(open); context.SaveChanges();
        context.Add(new ProductTaskItem { TaskId = open.Id, ProductId = product.Id, BatchId = pending.Id, Stage = ExpiryStageCalculator.Expired });
        var completeTask = new ProductTask { ProductId = product.Id, Status = "completed", HighestStage = ExpiryStageCalculator.Expired, ClosedAtUtc = utc };
        context.Add(completeTask); context.SaveChanges();
        var inspection = new Inspection { TaskId = completeTask.Id, ProductId = product.Id, ProductCodeSnapshot = product.ProductCode, StageSnapshot = ExpiryStageCalculator.Expired, InspectorName = "tester", CheckDate = new DateOnly(2026, 9, 17), SubmittedAtUtc = utc };
        context.Add(inspection); context.SaveChanges();
        context.Add(new InspectionItem { InspectionId = inspection.Id, ProductId = product.Id, BatchId = completed.Id, ExpiryDateSnapshot = completed.ExpiryDate, StageSnapshot = ExpiryStageCalculator.Expired, UpdatedAtUtc = utc });
        context.Add(new InspectionItem { InspectionId = inspection.Id, ProductId = product.Id, BatchId = resumedCompleted.Id, ExpiryDateSnapshot = resumedCompleted.ExpiryDate, StageSnapshot = ExpiryStageCalculator.Expired, UpdatedAtUtc = utc });
        context.Add(new InspectionItem { InspectionId = inspection.Id, ProductId = product.Id, BatchId = unhandledCompleted.Id, ExpiryDateSnapshot = unhandledCompleted.ExpiryDate, StageSnapshot = ExpiryStageCalculator.Expired, UpdatedAtUtc = utc });
        context.Add(new ProductTaskItem { TaskId = completeTask.Id, ProductId = product.Id, BatchId = taskWithoutInspection.Id, Stage = ExpiryStageCalculator.Expired });
        context.Add(new LifecycleEvent { ProductId = product.Id, BatchId = resumedCompleted.Id, EventType = "batch_tracking_resumed", Reason = "test", OccurredAtUtc = utc.AddMinutes(1) });
        var baseline = new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = new DateOnly(2026, 9, 17), IsCompleted = true, CompletedAtUtc = utc };
        context.Add(baseline); context.SaveChanges();
        context.Add(new BatchBaseline { BaselineId = baseline.Id, BatchId = noCatchup.Id, StageAtBaseline = ExpiryStageCalculator.Expired, ColdStartDisposition = ColdStartDispositions.ExpiredHistoricalBaseline });
        context.SaveChanges();

        var detail = Assert.IsType<ProductCatalogDetail>(new ProductCatalogQuery().GetDetail(context, product.Id));
        Assert.Equal(new[] { pending.Id, future.Id }, detail.CurrentBatches.Select(batch => batch.BatchId).Order());
        Assert.Equal(8, detail.HistoricalBatchCount);
        Assert.Equal("待处理", detail.CurrentBatches.Single(batch => batch.BatchId == pending.Id).TaskStatus);
        Assert.Equal("无需排查", detail.HistoricalBatches.Single(batch => batch.BatchId == noCatchup.Id).TaskStatus);
        Assert.Equal("已完成", detail.HistoricalBatches.Single(batch => batch.BatchId == completed.Id).TaskStatus);
        Assert.Equal("已结束", detail.HistoricalBatches.Single(batch => batch.BatchId == stopped.Id).TaskStatus);
        Assert.Equal("无待处理", detail.HistoricalBatches.Single(batch => batch.BatchId == expired.Id).TaskStatus);
        Assert.Equal("无待处理", detail.HistoricalBatches.Single(batch => batch.BatchId == ambiguous.Id).TaskStatus);
        Assert.Equal("无待处理", detail.HistoricalBatches.Single(batch => batch.BatchId == resumedCompleted.Id).TaskStatus);
        Assert.Equal("无待处理", detail.HistoricalBatches.Single(batch => batch.BatchId == unhandledCompleted.Id).TaskStatus);
        Assert.Equal("无待处理", detail.HistoricalBatches.Single(batch => batch.BatchId == taskWithoutInspection.Id).TaskStatus);
        Assert.All(detail.HistoricalBatches, batch => Assert.False(batch.IsPending));
        Assert.False(context.ChangeTracker.HasChanges());
        var tasksBefore = context.Tasks.Count();
        noCatchup.LifecycleGeneration = 1; context.SaveChanges();
        Assert.Equal("无待处理", new ProductCatalogQuery().GetDetail(context, product.Id)!.HistoricalBatches.Single(batch => batch.BatchId == noCatchup.Id).TaskStatus);
        noCatchup.LifecycleGeneration = 0;
        context.Add(new LifecycleEvent { ProductId = product.Id, BatchId = noCatchup.Id, EventType = "batch_tracking_resumed", Reason = "test", OccurredAtUtc = utc.AddMinutes(1) });
        context.SaveChanges();
        Assert.Equal("无待处理", new ProductCatalogQuery().GetDetail(context, product.Id)!.HistoricalBatches.Single(batch => batch.BatchId == noCatchup.Id).TaskStatus);
        Assert.Equal(tasksBefore, context.Tasks.Count());
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task History_toggle_is_local_and_resets_for_each_detail()
    {
        var detail = new ProductCatalogDetail(new(1, "商品", "P", null, "食品", 2, 1, null, "none", 0, null, null),
            [new(1, null, new DateOnly(2026, 10, 1), 1, "none", false), new(2, null, new DateOnly(2026, 9, 1), 1, "expired", false, true, "无待处理")], null, null);
        var loads = 0;
        var catalog = new StoreExpiryInspector.UI.ProductCatalogViewModel(_ => new([], 0, 1, 50), _ => { loads++; return detail; });
        var changes = new List<string?>(); catalog.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        await catalog.OpenAsync(1);
        Assert.True(catalog.HasHistoricalBatches); Assert.Equal("查看历史批次（1）", catalog.HistoryToggleText);
        Assert.Contains(nameof(catalog.HistoryToggleText), changes);
        catalog.ToggleHistoryCommand.Execute(null);
        Assert.True(catalog.IsHistoryExpanded); Assert.Equal("收起历史批次（1）", catalog.HistoryToggleText);
        catalog.ToggleHistoryCommand.Execute(null);
        Assert.False(catalog.IsHistoryExpanded); Assert.Equal(1, loads);
        catalog.ClearDetail();
        Assert.False(catalog.IsHistoryExpanded);
        var emptyHistory = detail with { Batches = detail.CurrentBatches };
        var noHistoryCatalog = new StoreExpiryInspector.UI.ProductCatalogViewModel(_ => new([], 0, 1, 50), _ => emptyHistory);
        await noHistoryCatalog.OpenAsync(1);
        Assert.False(noHistoryCatalog.HasHistoricalBatches);
        Assert.Equal(0, noHistoryCatalog.Selected!.HistoricalBatchCount);
    }

    private static Batch AddBatch(StoreExpiryInspector.Infrastructure.StoreDbContext context, Product product, DateOnly expiry, string stage, string trackingStatus, DateOnly? nextTrigger, int attention, int handled)
    {
        var batch = new Batch { ProductId = product.Id, ExpiryDate = expiry, CurrentStage = stage, TrackingStatus = trackingStatus, NextTriggerDate = nextTrigger, AttentionVersion = attention, HandledAttentionVersion = handled, CurrentArrivalQty = 1, MaxArrivalQty = 1 };
        context.Add(batch); context.SaveChanges(); return batch;
    }
}
