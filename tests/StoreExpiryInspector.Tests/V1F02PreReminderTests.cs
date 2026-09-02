using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F02PreReminderTests
{
    [Fact]
    public void PolicyStageDatesShareAllV1ThresholdsAndCalendarBoundaries()
    {
        var expiry = new DateOnly(2028, 3, 1);

        Assert.Equal(
            new ExpiryPolicyStageDates(expiry.AddDays(-30), expiry.AddDays(-14), expiry.AddDays(-7), expiry),
            ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.Food, 1, expiry, 270));
        Assert.Equal(
            new ExpiryPolicyStageDates(expiry.AddDays(-90), expiry.AddDays(-60), expiry.AddDays(-14), expiry),
            ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.Pet, 1, expiry, 365));
        Assert.Equal(
            new ExpiryPolicyStageDates(expiry.AddDays(-180), expiry.AddDays(-90), expiry.AddDays(-14), expiry),
            ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.GeneralLong, 1, expiry, 365));
        Assert.Null(ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.GeneralLong, 1, expiry, 180));
    }

    [Fact]
    public void CandidateWindowStartsThreeCalendarDaysBeforeAndEndsAtEffectiveDateWithoutWrites()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var batch = AddEligibleBatch(context, "SKU", new DateOnly(2026, 10, 20), 365, "D");
        var before = Snapshot(context);
        var reminder = new DailyReminderUseCase();

        Assert.Empty(reminder.Evaluate(context, new DateTime(2026, 10, 2, 10, 0, 0)).PreReminderItems);
        var due = reminder.Evaluate(context, new DateTime(2026, 10, 3, 10, 0, 0));
        var candidate = Assert.Single(due.PreReminderItems);
        Assert.Equal((batch.Id, ExpiryStageCalculator.Withdraw), (candidate.BatchId, candidate.TargetStage));
        Assert.DoesNotContain(reminder.Evaluate(context, new DateTime(2026, 10, 6, 10, 0, 0)).PreReminderItems,
            item => item.BatchId == batch.Id && item.TargetStage == ExpiryStageCalculator.Withdraw);
        Assert.Equal(before, Snapshot(context));
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public void CandidateQueryExcludesInvalidTrackingPolicyScopeAndStock()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddEligibleBatch(context, "VALID", new DateOnly(2026, 10, 20), 365, "D");
        AddEligibleBatch(context, "ZERO", new DateOnly(2026, 10, 20), 365, "D", stock: 0);
        AddEligibleBatch(context, "STOPPED", new DateOnly(2026, 10, 20), 365, "D", trackingStatus: "stopped");
        AddEligibleBatch(context, "UNRESOLVED", new DateOnly(2026, 10, 20), 365, "D", status: ExpiryManagementStatus.Unresolved);

        var items = new DailyReminderUseCase().Evaluate(context, new DateTime(2026, 10, 3, 10, 0, 0)).PreReminderItems;

        Assert.Single(items);
        Assert.Equal(context.Products.Single(product => product.ProductCode == "VALID").Id, items[0].ProductId);
    }

    [Fact]
    public void RuntimeCombinesFormalAndPreReminderProductsOnceAndKeepsSectionsDistinct()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var batch = AddEligibleBatch(context, "BOTH", new DateOnly(2026, 10, 20), 365, "D");
        var task = new ProductTask { ProductId = batch.ProductId, HighestStage = ExpiryStageCalculator.Expired };
        context.Tasks.Add(task);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = batch.ProductId, BatchId = batch.Id, Stage = ExpiryStageCalculator.Expired });
        context.SaveChanges();
        var channel = new RecordingChannel();
        var logDirectory = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = new DailyReminderRuntimeCoordinator(channel, new LocalFileLogger(logDirectory)).Run(context, new DateTime(2026, 10, 3, 10, 0, 0));

            Assert.True(result.ReminderRecorded);
            var notification = Assert.IsType<ReminderNotification>(channel.Notification);
            Assert.Equal(1, notification.ItemCount);
            Assert.Equal(1, notification.FormalTaskItemCount);
            Assert.Equal(1, notification.UpcomingWithdrawCount);
            var message = WindowsMessageBoxReminderChannel.FormatMessage(notification);
            Assert.Contains("今日待排查", message, StringComparison.Ordinal);
            Assert.Contains("提前 3 天预提醒", message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public void PreReminderOnlyMessageHasNoFakeTaskDirection()
    {
        var message = WindowsMessageBoxReminderChannel.FormatMessage(new ReminderNotification(
            2, ExpiryStageCalculator.None, 0, 1, 0, 1, 0));

        Assert.Contains("提前 3 天预提醒", message, StringComparison.Ordinal);
        Assert.DoesNotContain("待排查任务", message, StringComparison.Ordinal);
    }

    private static Batch AddEligibleBatch(StoreDbContext context, string code, DateOnly expiry, int life, string unit,
        int stock = 10, string trackingStatus = "active", ExpiryManagementStatus status = ExpiryManagementStatus.Managed, int version = 1)
    {
        var product = new Product
        {
            ProductCode = code,
            CategoryCode = "food",
            PolicyCode = status == ExpiryManagementStatus.Managed ? ExpiryPolicies.Food : null,
            PolicyVersion = status == ExpiryManagementStatus.Managed ? version : null,
            ExpiryManagementStatus = status,
            ExcelStockQty = stock,
            EffectiveStockQty = stock
        };
        context.Products.Add(product);
        context.SaveChanges();
        if (!context.ScopeBaselines.Any())
        {
            var import = new ImportRecord { SourceFileName = "v1-f02.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = ImportStatuses.Succeeded };
            context.Imports.Add(import);
            context.SaveChanges();
            context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = expiry, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
            context.SaveChanges();
        }
        var batch = new Batch { ProductId = product.Id, ExpiryDate = expiry, ShelfLifeValue = life, ShelfLifeUnit = unit, CurrentArrivalQty = stock, MaxArrivalQty = stock, TrackingStatus = trackingStatus };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static string Snapshot(StoreDbContext context) => string.Join('|', context.Products.Count(), context.Batches.Count(), context.Tasks.Count(), context.TaskItems.Count(), context.Inspections.Count(), context.LifecycleEvents.Count(), context.ScopeBaselines.Count());

    private sealed class RecordingChannel : IReminderChannel
    {
        public ReminderNotification? Notification { get; private set; }

        public bool TryShow(ReminderNotification notification)
        {
            Notification = notification;
            return true;
        }
    }
}
