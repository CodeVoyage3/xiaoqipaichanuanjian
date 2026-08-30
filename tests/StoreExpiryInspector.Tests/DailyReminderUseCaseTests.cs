using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class DailyReminderUseCaseTests
{
    private static readonly DateOnly Today = new(2026, 8, 30);

    [Fact]
    public void DefaultTimeIsTenAndBeforeItIsNotDue()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context, "SKU-1", ExpiryStageCalculator.Discount50);

        var result = new DailyReminderUseCase().Evaluate(
            context,
            Today.ToDateTime(new TimeOnly(9, 59)));

        Assert.Equal(600, result.ReminderMinuteOfDay);
        Assert.Equal(DailyReminderStatuses.NotDue, result.Status);
        Assert.Empty(result.Items);
        Assert.Null(context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(18, 45)]
    public void AtOrAfterConfiguredTimeWithItemsIsDue(int hour, int minute)
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context, "SKU-1", ExpiryStageCalculator.Withdraw);

        var result = new DailyReminderUseCase().Evaluate(
            context,
            Today.ToDateTime(new TimeOnly(hour, minute)));

        Assert.Equal(DailyReminderStatuses.Due, result.Status);
        Assert.Single(result.Items);
        Assert.Equal(ExpiryStageCalculator.Withdraw, result.Items[0].HighestStage);
    }

    [Fact]
    public void ConfiguredTimeIsReadFromExistingSettings()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context, "SKU-1", ExpiryStageCalculator.Discount20);
        context.Settings.Single().ReminderMinuteOfDay = 13 * 60 + 15;
        context.SaveChanges();

        var useCase = new DailyReminderUseCase();
        Assert.Equal(
            DailyReminderStatuses.NotDue,
            useCase.Evaluate(context, Today.ToDateTime(new TimeOnly(13, 14))).Status);
        Assert.Equal(
            DailyReminderStatuses.Due,
            useCase.Evaluate(context, Today.ToDateTime(new TimeOnly(13, 15))).Status);
    }

    [Fact]
    public void SuccessfulReminderBlocksTodayAndAllowsNextLocalDate()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context, "SKU-1", ExpiryStageCalculator.Expired);
        var useCase = new DailyReminderUseCase();

        Assert.True(useCase.RecordSuccessfulReminder(context, Today, true));
        Assert.Equal(
            DailyReminderStatuses.AlreadyRemindedToday,
            useCase.Evaluate(context, Today.ToDateTime(new TimeOnly(12, 0))).Status);
        Assert.Equal(
            DailyReminderStatuses.Due,
            useCase.Evaluate(context, Today.AddDays(1).ToDateTime(new TimeOnly(10, 0))).Status);
    }

    [Fact]
    public void ClockRollbackIsBlockedBeforeNotification()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context, "SKU-1", ExpiryStageCalculator.Expired);
        context.AppStates.Single().LastReminderDate = Today.AddDays(1);
        context.SaveChanges();

        var result = new DailyReminderUseCase().Evaluate(
            context,
            Today.ToDateTime(new TimeOnly(12, 0)));

        Assert.Equal(DailyReminderStatuses.ClockRollback, result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void NoItemsIsExplicitAndDoesNotWriteSuccess()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var result = new DailyReminderUseCase().Evaluate(
            context,
            Today.ToDateTime(new TimeOnly(10, 0)));

        Assert.Equal(DailyReminderStatuses.NoItems, result.Status);
        Assert.Empty(result.Items);
        Assert.Null(context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Fact]
    public void CandidatesReuseOpenTaskProductAndHighestStageWithoutDuplicates()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var (product, task) = AddOpenTask(context, "SKU-OPEN", ExpiryStageCalculator.Expired, "商品甲", "6900001");
        AddTaskItem(context, product, task, ExpiryStageCalculator.Discount50, new DateOnly(2027, 2, 1));
        AddClosedTask(context, "SKU-DONE", "completed");
        AddClosedTask(context, "SKU-STOPPED", "system_closed");

        var items = new InspectionTaskQuery().GetReminderCandidates(context);

        var item = Assert.Single(items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal("商品甲", item.ProductName);
        Assert.Equal("6900001", item.ProductBarcode);
        Assert.Equal("SKU-OPEN", item.ProductCode);
        Assert.Equal(ExpiryStageCalculator.Expired, item.HighestStage);
    }

    [Fact]
    public void CandidateQueryDoesNotWriteOrTrackEntities()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddOpenTask(seed, "SKU-1", ExpiryStageCalculator.Discount20);
        }

        using var context = database.Open();
        var before = Snapshot(context);
        _ = new InspectionTaskQuery().GetReminderCandidates(context);
        var after = Snapshot(context);

        Assert.Equal(before, after);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void OnlySuccessfulNotificationRecordsAndRepeatIsIdempotent()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var useCase = new DailyReminderUseCase();

        Assert.False(useCase.RecordSuccessfulReminder(context, Today, false));
        Assert.Null(context.AppStates.AsNoTracking().Single().LastReminderDate);
        Assert.True(useCase.RecordSuccessfulReminder(context, Today, true));
        Assert.True(useCase.RecordSuccessfulReminder(context, Today, true));
        Assert.Equal(Today, context.AppStates.AsNoTracking().Single().LastReminderDate);
        Assert.False(useCase.RecordSuccessfulReminder(context, Today.AddDays(-1), true));
        Assert.Equal(Today, context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Fact]
    public void PersistenceFailureDoesNotRemainPendingInTheContext()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_reminder_update
            BEFORE UPDATE OF last_reminder_date ON app_state
            BEGIN
                SELECT RAISE(ABORT, 'simulated reminder persistence failure');
            END;
            """);

        Assert.Throws<DbUpdateException>(() =>
            new DailyReminderUseCase().RecordSuccessfulReminder(context, Today, true));
        Assert.Null(context.AppStates.AsNoTracking().Single().LastReminderDate);
        Assert.Equal(EntityState.Unchanged, context.Entry(context.AppStates.Local.Single()).State);

        context.Database.ExecuteSqlRaw("DROP TRIGGER fail_reminder_update");
        context.Settings.Single().ReminderMinuteOfDay = 601;
        context.SaveChanges();
        Assert.Null(context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Fact]
    public void PersistenceFailurePreservesOtherPendingAppStateChanges()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var state = context.AppStates.Single();
        state.LastNormalRunDate = Today;
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_reminder_update
            BEFORE UPDATE OF last_reminder_date ON app_state
            BEGIN
                SELECT RAISE(ABORT, 'simulated reminder persistence failure');
            END;
            """);

        Assert.Throws<DbUpdateException>(() =>
            new DailyReminderUseCase().RecordSuccessfulReminder(context, Today, true));
        Assert.True(context.Entry(state).Property(item => item.LastNormalRunDate).IsModified);
        Assert.False(context.Entry(state).Property(item => item.LastReminderDate).IsModified);

        context.Database.ExecuteSqlRaw("DROP TRIGGER fail_reminder_update");
        context.SaveChanges();
        var saved = context.AppStates.AsNoTracking().Single();
        Assert.Equal(Today, saved.LastNormalRunDate);
        Assert.Null(saved.LastReminderDate);
    }

    [Fact]
    public void UtcInputIsRejectedInsteadOfBecomingTheLocalBusinessDate()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Throws<ArgumentException>(() => new DailyReminderUseCase().Evaluate(
            context,
            new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc)));
    }

    private static (Product Product, ProductTask Task) AddOpenTask(
        StoreDbContext context,
        string code,
        string stage,
        string? name = null,
        string? barcode = null)
    {
        var product = new Product
        {
            ProductCode = code,
            CurrentName = name,
            CurrentBarcode = barcode,
            ExcelStockQty = 10,
            EffectiveStockQty = 10,
            EffectiveStockSource = "excel"
        };
        context.Products.Add(product);
        context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id, HighestStage = stage };
        context.Tasks.Add(task);
        context.SaveChanges();
        AddTaskItem(context, product, task, stage, new DateOnly(2027, 1, 1));
        return (product, task);
    }

    private static void AddClosedTask(StoreDbContext context, string code, string status)
    {
        var product = new Product { ProductCode = code };
        context.Products.Add(product);
        context.SaveChanges();
        var task = new ProductTask
        {
            ProductId = product.Id,
            HighestStage = ExpiryStageCalculator.Discount50,
            Status = status,
            ClosedAtUtc = new DateTime(2026, 8, 30, 1, 0, 0, DateTimeKind.Utc),
            CloseReason = status == "system_closed" ? "product_stock_zero" : null
        };
        context.Tasks.Add(task);
        context.SaveChanges();
    }

    private static void AddTaskItem(
        StoreDbContext context,
        Product product,
        ProductTask task,
        string stage,
        DateOnly expiryDate)
    {
        var batch = new Batch
        {
            ProductId = product.Id,
            ExpiryDate = expiryDate,
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            CurrentStage = stage
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            Stage = stage
        });
        context.SaveChanges();
    }

    private static string Snapshot(StoreDbContext context) => JsonSerializer.Serialize(new
    {
        Products = context.Products.AsNoTracking().OrderBy(item => item.Id).ToArray(),
        Batches = context.Batches.AsNoTracking().OrderBy(item => item.Id).ToArray(),
        Tasks = context.Tasks.AsNoTracking().OrderBy(item => item.Id).ToArray(),
        TaskItems = context.TaskItems.AsNoTracking().OrderBy(item => item.Id).ToArray(),
        Settings = context.Settings.AsNoTracking().OrderBy(item => item.Id).ToArray(),
        AppStates = context.AppStates.AsNoTracking().OrderBy(item => item.Id).ToArray()
    });
}
