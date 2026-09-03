using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S6T04SettingsAndAutoStartTests
{
    private static readonly DateOnly Today = new(2026, 8, 31);

    [Fact]
    public void DefaultReminderTimeRemainsTenOClock()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var minute = new ReminderSettingsUseCase().GetReminderMinuteOfDay(context);

        Assert.Equal(600, minute);
        Assert.Equal("10:00", ReminderSettingsUseCase.Format(minute));
    }

    [Fact]
    public void ReminderTimePickerKeepsDraftSelectionUntilExplicitConfirm()
    {
        var picker = new ReminderTimePickerState(13 * 60 + 25);

        Assert.Equal(Enumerable.Range(0, 24).Select(value => value.ToString("00")), ReminderTimePickerState.Hours);
        Assert.Equal(Enumerable.Range(0, 60).Select(value => value.ToString("00")), ReminderTimePickerState.Minutes);
        Assert.Equal((13, 25), (picker.Hour, picker.Minute));

        picker.Select(8, 5);
        picker.Cancel();
        Assert.Equal((13, 25), (picker.Hour, picker.Minute));

        picker.Select(8, 5);
        Assert.Equal("08:05", ReminderSettingsUseCase.Format(picker.Confirm()));
        picker.Open(8 * 60 + 5);
        Assert.Equal((8, 5), (picker.Hour, picker.Minute));
    }

    [Theory]
    [InlineData("9:30", 570)]
    [InlineData("09:30", 570)]
    [InlineData("23:59", 1439)]
    [InlineData("", -1)]
    [InlineData("24:00", -1)]
    [InlineData("9:3", -1)]
    public void R6DirectTimeInputNormalizesOnlyValidValues(string value, int expected)
    {
        var valid = ReminderTimePickerState.TryApplyText(value, 600, out var minute);
        Assert.Equal(expected >= 0, valid);
        Assert.Equal(valid ? expected : 600, minute);
    }

    [Fact]
    public void ValidTimeSavesAndReloadsFromExistingSettings()
    {
        using var database = SqliteTestDatabase.Create();
        using (var context = database.Open())
        {
            var result = new ReminderSettingsUseCase().SaveReminderTime(context, "13:25");
            Assert.True(result.Succeeded);
            Assert.Equal(805, result.ReminderMinuteOfDay);
        }

        using var reloaded = database.Open();
        Assert.Equal(805, reloaded.Settings.AsNoTracking().Single().ReminderMinuteOfDay);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("9:00")]
    [InlineData("not-a-time")]
    public void InvalidTimeIsRejectedWithoutPersistence(string input)
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var result = new ReminderSettingsUseCase().SaveReminderTime(context, input);

        Assert.False(result.Succeeded);
        Assert.Equal(600, context.Settings.AsNoTracking().Single().ReminderMinuteOfDay);
    }

    [Fact]
    public void RunningSchedulerRecalculatesForNewFutureTime()
    {
        var now = new DateTime(2026, 8, 31, 9, 0, 0);
        var logDirectory = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorTests", Guid.NewGuid().ToString("N"));
        using var scheduler = new DailyReminderScheduler(
            600,
            _ => new(DailyReminderStatuses.NotDue, false, false, false),
            new LocalFileLogger(logDirectory),
            () => now);
        try
        {
            scheduler.Start();
            scheduler.Reschedule(11 * 60);

            Assert.Equal(new DateTime(2026, 8, 31, 11, 0, 0), scheduler.NextCheckAt);
        }
        finally
        {
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void MovingTimeIntoPastUsesDailyReminderAuthorityAndBecomesDue()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context);
        new ReminderSettingsUseCase().SaveReminderTime(context, "08:00");

        var result = new DailyReminderUseCase().Evaluate(context, Today.ToDateTime(new TimeOnly(9, 0)));

        Assert.Equal(DailyReminderStatuses.Due, result.Status);
    }

    [Fact]
    public void MovingTimeAfterSuccessfulReminderCannotRepeatSameDay()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context);
        context.AppStates.Single().LastReminderDate = Today;
        context.SaveChanges();
        new ReminderSettingsUseCase().SaveReminderTime(context, "08:00");

        var result = new DailyReminderUseCase().Evaluate(context, Today.ToDateTime(new TimeOnly(9, 0)));

        Assert.Equal(DailyReminderStatuses.AlreadyRemindedToday, result.Status);
    }

    [Fact]
    public void NewTimeAppliesOnNextLocalBusinessDay()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        AddOpenTask(context);
        context.AppStates.Single().LastReminderDate = Today;
        context.SaveChanges();
        new ReminderSettingsUseCase().SaveReminderTime(context, "08:00");

        var result = new DailyReminderUseCase().Evaluate(
            context,
            Today.AddDays(1).ToDateTime(new TimeOnly(8, 0)));

        Assert.Equal(DailyReminderStatuses.Due, result.Status);
    }

    [Fact]
    public void MissingRegistryValueMeansAutoStartIsOff()
    {
        var service = Service(new FakeRegistry());

        var state = service.ReadState();

        Assert.True(state.Succeeded);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public void EnableIsIdempotentAndWritesQuotedCurrentExecutable()
    {
        var registry = new FakeRegistry();
        var service = Service(registry);

        Assert.True(service.SetEnabled(true).IsEnabled);
        Assert.True(service.SetEnabled(true).IsEnabled);

        Assert.Equal("\"C:\\Apps\\StoreExpiryInspector.exe\"", registry.Value);
        Assert.Equal(2, registry.WriteCount);
    }

    [Fact]
    public void DisableOnlyDeletesThisApplicationValueAndIsIdempotent()
    {
        var registry = new FakeRegistry
        {
            Value = "\"C:\\Apps\\StoreExpiryInspector.exe\"",
            OtherApplicationValue = "keep-me"
        };
        var service = Service(registry);

        Assert.False(service.SetEnabled(false).IsEnabled);
        Assert.False(service.SetEnabled(false).IsEnabled);

        Assert.Null(registry.Value);
        Assert.Equal("keep-me", registry.OtherApplicationValue);
    }

    [Fact]
    public void ExternalRegistryChangeIsReflectedOnNextRead()
    {
        var registry = new FakeRegistry();
        var service = Service(registry);
        Assert.False(service.ReadState().IsEnabled);

        registry.Value = "\"C:\\Apps\\StoreExpiryInspector.exe\"";

        Assert.True(service.ReadState().IsEnabled);
    }

    [Fact]
    public void RegistryWriteFailureReturnsErrorWithoutThrowing()
    {
        var service = Service(new FakeRegistry { ThrowOnWrite = true });

        var result = service.SetEnabled(true);

        Assert.False(result.Succeeded);
        Assert.False(result.IsEnabled);
        Assert.Contains("simulated", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryReadFailureReturnsErrorWithoutThrowing()
    {
        var service = Service(new FakeRegistry { ThrowOnRead = true });

        var result = service.ReadState();

        Assert.False(result.Succeeded);
        Assert.False(result.IsEnabled);
        Assert.Contains("simulated", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoStartAuthorityDoesNotUseDatabaseCache()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "StoreExpiryInspector",
            "Infrastructure",
            "WindowsAutoStartService.cs"));

        Assert.Contains("Registry.CurrentUser", source, StringComparison.Ordinal);
        Assert.Contains("CurrentVersion\\Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoStartEnabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalMachine", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingSettingsEntryUsesMinimalDialogAndRuntimeReschedule()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));

        Assert.Contains("AutomationProperties.Name=\"设置\"", window, StringComparison.Ordinal);
        Assert.Contains("Click=\"Settings_Click\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("设置将在后续阶段开放", window, StringComparison.Ordinal);
        Assert.Contains("每日提醒时间", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ReminderTimePickerState.Hours", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ReminderTimePickerState.Minutes", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ScrollSelectedToCenter", codeBehind, StringComparison.Ordinal);
        Assert.Contains("pickerCancel", codeBehind, StringComparison.Ordinal);
        Assert.Contains("pickerConfirm", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ReminderSettingsUseCase.Format(selectedReminderMinuteOfDay)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ReminderTimePickerState.TryApplyText", codeBehind, StringComparison.Ordinal);
        Assert.Contains("请输入有效时间（00:00–23:59）", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new Popup", codeBehind, StringComparison.Ordinal);
        Assert.Contains("pickerBorder.PreviewKeyDown", codeBehind, StringComparison.Ordinal);
        Assert.Contains("每天在该时间集中提醒，修改后立即重新安排提醒。", codeBehind, StringComparison.Ordinal);
        Assert.Contains("timeValidation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("settingsValidation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FrameworkElement.HeightProperty, 24d", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignmentProperty, HorizontalAlignment.Center", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(234, 240, 247)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SecondaryTextBrush", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpacityProperty, 0.55", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog.Height = 460", codeBehind, StringComparison.Ordinal);
        Assert.Contains("开机自启动（仅当前 Windows 用户）", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ReminderTimeChanged?.Invoke", codeBehind, StringComparison.Ordinal);
        var saveBlock = codeBehind[codeBehind.IndexOf("save.Click +=", StringComparison.Ordinal)..];
        Assert.True(saveBlock.IndexOf("if (!CommitText()) return;", StringComparison.Ordinal) < saveBlock.IndexOf("DatabaseInitializer.CreateContext", StringComparison.Ordinal));
        Assert.True(saveBlock.IndexOf("if (savedMinuteOfDay.HasValue)", StringComparison.Ordinal) < saveBlock.IndexOf("ReminderTimeChanged?.Invoke", StringComparison.Ordinal));
        Assert.Contains("_reminderScheduler?.Reschedule", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Stage 7", codeBehind, StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsAutoStartService Service(FakeRegistry registry) =>
        new(registry, @"C:\Apps\StoreExpiryInspector.exe");

    private static void AddOpenTask(StoreDbContext context)
    {
        var product = new Product
        {
            ProductCode = "S6T04-001",
            CurrentName = "S6-T04 product",
            ExcelStockQty = 10,
            EffectiveStockQty = 10,
            EffectiveStockSource = "excel"
        };
        context.Products.Add(product);
        context.SaveChanges();
        var import = new ImportRecord { SourceFileName = "s6t04.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = ImportStatuses.Succeeded };
        context.Imports.Add(import);
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = product.CategoryCode, PolicyCode = product.PolicyCode!, PolicyVersion = product.PolicyVersion!.Value, CreatedImportId = import.Id, BusinessDate = Today, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
        context.SaveChanges();
        var batch = new Batch
        {
            ProductId = product.Id,
            ExpiryDate = Today,
            ShelfLifeValue = 30,
            ShelfLifeUnit = "D",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10,
            CurrentStage = ExpiryStageCalculator.Expired
        };
        context.Batches.Add(batch);
        var task = new ProductTask { ProductId = product.Id, HighestStage = ExpiryStageCalculator.Expired };
        context.Tasks.Add(task);
        context.SaveChanges();
        context.TaskItems.Add(new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            Stage = ExpiryStageCalculator.Expired
        });
        context.SaveChanges();
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

    private sealed class FakeRegistry : IAutoStartRegistry
    {
        public string? Value { get; set; }

        public string? OtherApplicationValue { get; set; }

        public bool ThrowOnWrite { get; set; }

        public bool ThrowOnRead { get; set; }

        public int WriteCount { get; private set; }

        public string? Read()
        {
            if (ThrowOnRead)
            {
                throw new UnauthorizedAccessException("simulated registry read failure");
            }

            return Value;
        }

        public void Write(string command)
        {
            if (ThrowOnWrite)
            {
                throw new UnauthorizedAccessException("simulated registry failure");
            }

            WriteCount++;
            Value = command;
        }

        public void Delete() => Value = null;
    }
}
