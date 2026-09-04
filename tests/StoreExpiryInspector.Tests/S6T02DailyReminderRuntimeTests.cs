using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S6T02DailyReminderRuntimeTests
{
    private static readonly DateOnly Today = new(2026, 8, 30);

    [Fact]
    public void NotDueDoesNotCallChannel()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Discount50);

        var result = fixture.Run(new TimeOnly(9, 59));

        Assert.Equal(DailyReminderStatuses.NotDue, result.Status);
        Assert.Equal(0, fixture.Channel.CallCount);
    }

    [Fact]
    public void AlreadyRemindedTodayDoesNotCallChannel()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Discount50);
        fixture.Context.AppStates.Single().LastReminderDate = Today;
        fixture.Context.SaveChanges();

        var result = fixture.Run(new TimeOnly(10, 0));

        Assert.Equal(DailyReminderStatuses.AlreadyRemindedToday, result.Status);
        Assert.Equal(0, fixture.Channel.CallCount);
    }

    [Fact]
    public void NoItemsDoesNotCallChannel()
    {
        using var fixture = new Fixture();

        var result = fixture.Run(new TimeOnly(10, 0));

        Assert.Equal(DailyReminderStatuses.NoItems, result.Status);
        Assert.Equal(0, fixture.Channel.CallCount);
    }

    [Fact]
    public void MultipleItemsProduceOneCentralNotificationWithCountAndHighestStage()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Discount50);
        fixture.AddOpenTask("SKU-2", ExpiryStageCalculator.Expired);

        var result = fixture.Run(new TimeOnly(10, 0));

        Assert.True(result.NotificationSucceeded);
        Assert.Equal(1, fixture.Channel.CallCount);
        Assert.Equal(2, fixture.Channel.LastNotification?.ItemCount);
        Assert.Equal(ExpiryStageCalculator.Expired, fixture.Channel.LastNotification?.HighestStage);
    }

    [Fact]
    public void SuccessfulChannelRecordsTodayOnlyAfterShowReturnsSuccess()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Withdraw);

        var result = fixture.Run(new TimeOnly(10, 0));

        Assert.True(result.ReminderRecorded);
        Assert.Equal(Today, fixture.Context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Fact]
    public void FailedChannelDoesNotRecordToday()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Withdraw);
        fixture.Channel.Succeeds = false;

        var result = fixture.Run(new TimeOnly(10, 0));

        Assert.True(result.NotificationAttempted);
        Assert.False(result.NotificationSucceeded);
        Assert.Null(fixture.Context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Fact]
    public void ChannelExceptionIsIsolatedLoggedAndDoesNotRecordToday()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Withdraw);
        fixture.Channel.Exception = new InvalidOperationException("simulated channel failure");

        var result = fixture.Run(new TimeOnly(10, 0));

        Assert.Equal("error", result.Status);
        Assert.True(result.NotificationAttempted);
        Assert.False(result.NotificationSucceeded);
        Assert.Null(fixture.Context.AppStates.AsNoTracking().Single().LastReminderDate);
        Assert.Contains("daily_reminder_failed", File.ReadAllText(fixture.LogPath), StringComparison.Ordinal);
        Assert.Contains("simulated channel failure", File.ReadAllText(fixture.LogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void FailedReminderCanRetryAndSucceedOnTheSameDay()
    {
        using var fixture = new Fixture();
        fixture.AddOpenTask("SKU-1", ExpiryStageCalculator.Discount20);
        fixture.Channel.Succeeds = false;

        Assert.False(fixture.Run(new TimeOnly(10, 0)).NotificationSucceeded);
        fixture.Channel.Succeeds = true;
        var retry = fixture.Run(new TimeOnly(10, 1));

        Assert.True(retry.ReminderRecorded);
        Assert.Equal(2, fixture.Channel.CallCount);
        Assert.Equal(Today, fixture.Context.AppStates.AsNoTracking().Single().LastReminderDate);
    }

    [Fact]
    public void WindowsMessageContainsCountUrgencyAndPendingTaskDirection()
    {
        var message = WindowsMessageBoxReminderChannel.FormatMessage(
            new ReminderNotification(3, ExpiryStageCalculator.Expired));

        Assert.Contains("3 个商品", message, StringComparison.Ordinal);
        Assert.Contains("过期", message, StringComparison.Ordinal);
        Assert.Contains("待排查任务", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppUsesOneIdleRuntimeTriggerWithoutTimerTrayOrNewDependency()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var project = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "StoreExpiryInspector.csproj"));

        Assert.Contains("DispatcherPriority.ApplicationIdle", app, StringComparison.Ordinal);
        Assert.Contains("DailyReminderRuntimeCoordinator", app, StringComparison.Ordinal);
        Assert.Contains("daily_reminder_runtime_failed", app, StringComparison.Ordinal);
        var normalRuntime = app[..app.IndexOf("private void VerifySmokeStartupAndExit", StringComparison.Ordinal)];
        var smokeOnly = app[app.IndexOf("private void VerifySmokeStartupAndExit", StringComparison.Ordinal)..app.IndexOf("protected override void OnExit", StringComparison.Ordinal)];
        Assert.DoesNotContain("Timer", normalRuntime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuntimeDataRoot.IsSmokeRun", app, StringComparison.Ordinal);
        Assert.Contains("DispatcherTimer", smokeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyIcon", app, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Toast", app, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunityToolkit", project, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
        private readonly string _logDirectory = Path.Combine(
            Path.GetTempPath(),
            "StoreExpiryInspectorTests",
            Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Context = _database.Open();
            Channel = new RecordingChannel();
            Coordinator = new DailyReminderRuntimeCoordinator(
                Channel,
                new LocalFileLogger(_logDirectory));
        }

        public StoreDbContext Context { get; }

        public RecordingChannel Channel { get; }

        public DailyReminderRuntimeCoordinator Coordinator { get; }

        public string LogPath => Directory.GetFiles(_logDirectory, "app-*.log").Single();

        public DailyReminderRuntimeResult Run(TimeOnly time) => Coordinator.Run(
            Context,
            Today.ToDateTime(time));

        public void AddOpenTask(string productCode, string stage)
        {
            var product = new Product
            {
                ProductCode = productCode,
                CurrentName = productCode,
                ExcelStockQty = 10,
                EffectiveStockQty = 10,
                EffectiveStockSource = "excel"
            };
            Context.Products.Add(product);
            Context.SaveChanges();
            if (!Context.ScopeBaselines.Any())
            {
                var import = new ImportRecord
                {
                    SourceFileName = "reminder-runtime.xlsx",
                    SourceFileSha256 = new string('a', 64),
                    ParsedAtUtc = DateTime.UtcNow,
                    ConfirmedAtUtc = DateTime.UtcNow,
                    Status = ImportStatuses.Succeeded
                };
                Context.Imports.Add(import);
                Context.SaveChanges();
                Context.ScopeBaselines.Add(new ScopeBaseline
                {
                    ScopeKey = product.CategoryCode,
                    PolicyCode = product.PolicyCode!,
                    PolicyVersion = product.PolicyVersion!.Value,
                    CreatedImportId = import.Id,
                    BusinessDate = Today,
                    IsCompleted = true,
                    CompletedAtUtc = DateTime.UtcNow
                });
                Context.SaveChanges();
            }
            var task = new ProductTask { ProductId = product.Id, HighestStage = stage };
            Context.Tasks.Add(task);
            Context.SaveChanges();
            var batch = new Batch
            {
                ProductId = product.Id,
                ExpiryDate = new DateOnly(2027, 1, 1),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 10,
                MaxArrivalQty = 10,
                CurrentStage = stage
            };
            Context.Batches.Add(batch);
            Context.SaveChanges();
            Context.TaskItems.Add(new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = stage
            });
            Context.SaveChanges();
        }

        public void Dispose()
        {
            Context.Dispose();
            _database.Dispose();
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
    }

    private sealed class RecordingChannel : IReminderChannel
    {
        public int CallCount { get; private set; }

        public bool Succeeds { get; set; } = true;

        public Exception? Exception { get; set; }

        public ReminderNotification? LastNotification { get; private set; }

        public bool TryShow(ReminderNotification notification)
        {
            CallCount++;
            LastNotification = notification;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Succeeds;
        }
    }
}
