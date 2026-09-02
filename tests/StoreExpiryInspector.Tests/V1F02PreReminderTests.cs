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
            ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.Food, 1, expiry, 271));
        Assert.Equal(
            new ExpiryPolicyStageDates(expiry.AddDays(-90), expiry.AddDays(-60), expiry.AddDays(-14), expiry),
            ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.Pet, 1, expiry, 365));
        Assert.Equal(
            new ExpiryPolicyStageDates(expiry.AddDays(-180), expiry.AddDays(-90), expiry.AddDays(-14), expiry),
            ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.GeneralLong, 1, expiry, 365));
        Assert.Null(ExpiryPolicyCalculator.CalculateStageDates(ExpiryPolicies.GeneralLong, 1, expiry, 180));
    }

    [Theory]
    [InlineData(ExpiryStageCalculator.Discount50, 30)]
    [InlineData(ExpiryStageCalculator.Discount20, 14)]
    [InlineData(ExpiryStageCalculator.Withdraw, 7)]
    [InlineData(ExpiryStageCalculator.Expired, 0)]
    public void EveryTargetStageUsesItsOwnThreeDayWindow(string targetStage, int threshold)
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var effectiveDate = new DateOnly(2026, 10, 20);
        var batch = AddEligibleBatch(context, targetStage, effectiveDate.AddDays(threshold), 270, "D");
        var reminder = new DailyReminderUseCase();

        Assert.Empty(reminder.Evaluate(context, effectiveDate.AddDays(-4).ToDateTime(new TimeOnly(10, 0))).PreReminderItems);
        Assert.Contains(reminder.Evaluate(context, effectiveDate.AddDays(-3).ToDateTime(new TimeOnly(10, 0))).PreReminderItems,
            item => item.BatchId == batch.Id && item.TargetStage == targetStage);
        Assert.Contains(reminder.Evaluate(context, effectiveDate.AddDays(-1).ToDateTime(new TimeOnly(10, 0))).PreReminderItems,
            item => item.BatchId == batch.Id && item.TargetStage == targetStage);
        Assert.DoesNotContain(reminder.Evaluate(context, effectiveDate.ToDateTime(new TimeOnly(10, 0))).PreReminderItems,
            item => item.BatchId == batch.Id && item.TargetStage == targetStage);
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
        AddEligibleBatch(context, "EXCLUDED", new DateOnly(2026, 10, 20), 365, "D", status: ExpiryManagementStatus.Excluded);
        AddEligibleBatch(context, "UNRESOLVED", new DateOnly(2026, 10, 20), 365, "D", status: ExpiryManagementStatus.Unresolved);
        AddEligibleBatch(context, "NO-BASELINE", new DateOnly(2026, 10, 20), 365, "D", category: "other", baseline: false);

        var items = new DailyReminderUseCase().Evaluate(context, new DateTime(2026, 10, 3, 10, 0, 0)).PreReminderItems;

        Assert.Single(items);
        Assert.Equal(context.Products.Single(product => product.ProductCode == "VALID").Id, items[0].ProductId);
    }

    [Fact]
    public void DaysMonthsAndYearsUseTheSameShelfLifeConversionInTheCandidateQuery()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var businessDate = new DateOnly(2026, 10, 1);
        AddEligibleBatch(context, "D", businessDate.AddDays(33), 270, "D");
        AddEligibleBatch(context, "M", businessDate.AddDays(33), 9, "M");
        AddEligibleBatch(context, "Y", businessDate.AddDays(93), 1, "Y");

        var items = new DailyReminderUseCase().Evaluate(context, businessDate.ToDateTime(new TimeOnly(10, 0))).PreReminderItems;

        Assert.Equal(3, items.Count(item => item.TargetStage == ExpiryStageCalculator.Discount50));
    }

    [Fact]
    public void CandidateQueryRejectsOverflowingShelfLifeConversion()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddEligibleBatch(context, "OVERFLOW", new DateOnly(2026, 10, 20), int.MaxValue, "M");

        Assert.Throws<OverflowException>(() => new DailyReminderUseCase().Evaluate(context, new DateTime(2026, 10, 3, 10, 0, 0)));
    }

    [Fact]
    public void MissedReminderDateCanCatchUpUntilButNotAtTheEffectiveDate()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var batch = AddEligibleBatch(context, "LATE", new DateOnly(2026, 10, 20), 365, "D");
        var reminder = new DailyReminderUseCase();

        Assert.Contains(reminder.Evaluate(context, new DateTime(2026, 10, 4, 10, 0, 0)).PreReminderItems,
            item => item.BatchId == batch.Id && item.TargetStage == ExpiryStageCalculator.Withdraw);
        Assert.DoesNotContain(reminder.Evaluate(context, new DateTime(2026, 10, 6, 10, 0, 0)).PreReminderItems,
            item => item.BatchId == batch.Id && item.TargetStage == ExpiryStageCalculator.Withdraw);
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
            Assert.Equal(1, notification.UpcomingWithdrawBatchCount);
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

    [Fact]
    public void SummaryKeepsBatchCountsWhileDeduplicatingProductsAcrossTypesAndFormalTasks()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var day = new DateOnly(2026, 10, 1);
        var first = AddEligibleBatch(context, "MULTI", day.AddDays(33), 270, "D");
        context.Batches.Add(new Batch { ProductId = first.ProductId, ProductionDate = day.AddDays(-270), ExpiryDate = day.AddDays(33), ShelfLifeValue = 270, ShelfLifeUnit = "D", CurrentArrivalQty = 10, MaxArrivalQty = 10 });
        context.Batches.Add(new Batch { ProductId = first.ProductId, ProductionDate = day.AddDays(-280), ExpiryDate = day.AddDays(17), ShelfLifeValue = 270, ShelfLifeUnit = "D", CurrentArrivalQty = 10, MaxArrivalQty = 10 });
        var formalBatch = AddEligibleBatch(context, "FORMAL", new DateOnly(2030, 1, 1), 270, "D");
        context.Tasks.Add(new ProductTask { ProductId = formalBatch.ProductId, HighestStage = ExpiryStageCalculator.Expired });
        context.SaveChanges();
        var task = context.Tasks.Single();
        context.TaskItems.Add(new ProductTaskItem { TaskId = task.Id, ProductId = formalBatch.ProductId, BatchId = formalBatch.Id, Stage = ExpiryStageCalculator.Expired });
        context.SaveChanges();
        var channel = new RecordingChannel();
        var directory = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorTests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new DailyReminderRuntimeCoordinator(channel, new LocalFileLogger(directory)).Run(context, day.ToDateTime(new TimeOnly(10, 0))).ReminderRecorded);
            var notification = Assert.IsType<ReminderNotification>(channel.Notification);
            Assert.Equal(2, notification.ItemCount);
            Assert.Equal(1, notification.UpcomingDiscount50Count);
            Assert.Equal(2, notification.UpcomingDiscount50BatchCount);
            Assert.Equal(1, notification.UpcomingDiscount20Count);
            Assert.Equal(1, notification.UpcomingDiscount20BatchCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static Batch AddEligibleBatch(StoreDbContext context, string code, DateOnly expiry, int life, string unit,
        int stock = 10, string trackingStatus = "active", ExpiryManagementStatus status = ExpiryManagementStatus.Managed,
        int version = 1, string category = "food", bool baseline = true)
    {
        var product = new Product
        {
            ProductCode = code,
            CategoryCode = category,
            PolicyCode = status == ExpiryManagementStatus.Managed ? ExpiryPolicies.Food : null,
            PolicyVersion = status == ExpiryManagementStatus.Managed ? version : null,
            ExpiryManagementStatus = status,
            ExcelStockQty = stock,
            EffectiveStockQty = stock
        };
        context.Products.Add(product);
        context.SaveChanges();
        if (baseline && !context.ScopeBaselines.Any(item => item.ScopeKey == category && item.PolicyCode == ExpiryPolicies.Food))
        {
            var import = new ImportRecord { SourceFileName = "v1-f02.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = ImportStatuses.Succeeded };
            context.Imports.Add(import);
            context.SaveChanges();
            context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = category, PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, BusinessDate = expiry, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
            context.SaveChanges();
        }
        var batch = new Batch { ProductId = product.Id, ExpiryDate = expiry, ShelfLifeValue = life, ShelfLifeUnit = unit, CurrentArrivalQty = stock, MaxArrivalQty = stock, TrackingStatus = trackingStatus };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static string Snapshot(StoreDbContext context) => string.Join('|',
        context.Products.Count(), context.Batches.Count(), context.Tasks.Count(), context.TaskItems.Count(),
        context.Inspections.Count(), context.InspectionItems.Count(), context.InspectionItemRevisions.Count(),
        context.LifecycleEvents.Count(), context.ScopeBaselines.Count(), context.BatchBaselines.Count(),
        string.Join(',', context.Batches.AsNoTracking().OrderBy(item => item.Id).Select(item => $"{item.CurrentStage}/{item.NextTriggerDate}/{item.AttentionVersion}/{item.HandledAttentionVersion}")));

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
