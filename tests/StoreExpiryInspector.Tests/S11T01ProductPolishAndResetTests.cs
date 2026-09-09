using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Domain;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S11T01ProductPolishAndResetTests
{
    [Fact]
    public void ResetCreatesVerifiedBackupClearsBusinessAndPreservesSettingsAndBackups()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = Path.Combine(database.Directory, "backups");
        using (var seed = database.Open())
        {
            seed.Products.Add(new Product { ProductCode = "S11-RESET" });
            seed.BackupRecords.Add(new BackupRecord
            {
                BackupType = "manual",
                FilePath = Path.Combine(database.Directory, "old.db"),
                Sha256 = new string('a', 64),
                VerificationStatus = "verified"
            });
            seed.Settings.Single().ReminderMinuteOfDay = 805;
            seed.Settings.Single().AutoStartEnabled = false;
            seed.AppStates.Single().LastReminderDate = new DateOnly(2026, 9, 6);
            seed.AppStates.Single().LastNormalRunDate = new DateOnly(2026, 9, 6);
            seed.SaveChanges();
        }

        var result = new ResetBusinessDataUseCase().Execute(database.Path, backups);

        Assert.True(result.Succeeded);
        Assert.Equal(ResetBusinessDataCodes.Success, result.Code);
        Assert.NotNull(result.BackupId);
        Assert.True(File.Exists(result.BackupPath));
        using (var verify = database.Open())
        {
            AssertBusinessTablesEmpty(verify);
            Assert.Equal(805, verify.Settings.AsNoTracking().Single().ReminderMinuteOfDay);
            Assert.False(verify.Settings.AsNoTracking().Single().AutoStartEnabled);
            Assert.Null(verify.AppStates.AsNoTracking().Single().LastReminderDate);
            Assert.Null(verify.AppStates.AsNoTracking().Single().LastNormalRunDate);
            Assert.Equal(2, verify.BackupRecords.Count());
            Assert.Equal(9, verify.Database.GetAppliedMigrations().Count());
        }
        using var protectedBackup = Infrastructure.DatabaseInitializer.CreateContext(result.BackupPath);
        Assert.Single(protectedBackup.Products.AsNoTracking());
    }

    [Fact]
    public void BackupFailureLeavesBusinessDataUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        var blockedDestination = Path.Combine(database.Directory, "not-a-directory");
        File.WriteAllText(blockedDestination, "blocked");
        using (var seed = database.Open())
        {
            seed.Products.Add(new Product { ProductCode = "S11-BACKUP-FAIL" });
            seed.SaveChanges();
        }

        var result = new ResetBusinessDataUseCase().Execute(database.Path, blockedDestination);

        Assert.False(result.Succeeded);
        Assert.Equal(ResetBusinessDataCodes.BackupFailed, result.Code);
        using var verify = database.Open();
        Assert.Single(verify.Products.AsNoTracking());
        Assert.Empty(verify.BackupRecords.AsNoTracking());
    }

    [Fact]
    public void BackupVerificationFailureLeavesBusinessDataUnchanged()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = Path.Combine(database.Directory, "backups");
        using (var seed = database.Open())
        {
            seed.Products.Add(new Product { ProductCode = "S11-BACKUP-VERIFY-FAIL" });
            seed.SaveChanges();
            seed.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsHistory WHERE MigrationId = '20260901155124_AddPolicyAndBaselineFoundation';");
        }

        var result = new ResetBusinessDataUseCase().Execute(database.Path, backups);

        Assert.False(result.Succeeded);
        Assert.Equal(ResetBusinessDataCodes.BackupFailed, result.Code);
        using var verify = database.Open();
        Assert.Single(verify.Products.AsNoTracking());
        Assert.Empty(verify.BackupRecords.AsNoTracking());
    }

    [Fact]
    public void TransactionFailureRollsBackAllBusinessChangesAndKeepsProtectionBackup()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = Path.Combine(database.Directory, "backups");
        using (var seed = database.Open())
        {
            var product = new Product { ProductCode = "S11-ROLLBACK" };
            seed.Products.Add(product);
            seed.SaveChanges();
            var adjustment = new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = 10,
                AdjustedStockQty = 8
            };
            seed.InventoryAdjustments.Add(adjustment);
            seed.SaveChanges();
            seed.LifecycleEvents.Add(new LifecycleEvent
            {
                ProductId = product.Id,
                EventType = "product_stock_zero",
                Reason = "S11 rollback proof",
                OccurredAtUtc = DateTime.UtcNow,
                SourceAdjustmentId = adjustment.Id
            });
            seed.AppStates.Single().LastReminderDate = new DateOnly(2026, 9, 6);
            seed.SaveChanges();
            seed.Database.ExecuteSqlRaw("CREATE TRIGGER fail_s11_reset BEFORE DELETE ON products BEGIN SELECT RAISE(ABORT, 'injected reset failure'); END;");
        }

        var result = new ResetBusinessDataUseCase().Execute(database.Path, backups);

        Assert.False(result.Succeeded);
        Assert.Equal(ResetBusinessDataCodes.ClearFailed, result.Code);
        using var verify = database.Open();
        Assert.Single(verify.Products.AsNoTracking());
        Assert.Single(verify.InventoryAdjustments.AsNoTracking());
        Assert.Single(verify.LifecycleEvents.AsNoTracking());
        Assert.Equal(new DateOnly(2026, 9, 6), verify.AppStates.AsNoTracking().Single().LastReminderDate);
        Assert.Single(verify.BackupRecords.AsNoTracking());
        Assert.Single(Directory.GetFiles(backups, "backup-*.db"));
    }

    [Fact]
    public void AppStateOnlyResetCreatesOneBackupThenBecomesNoData()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = Path.Combine(database.Directory, "backups");
        using (var seed = database.Open())
        {
            seed.Settings.Single().ReminderMinuteOfDay = 725;
            seed.AppStates.Single().LastReminderDate = new DateOnly(2026, 9, 5);
            seed.AppStates.Single().LastNormalRunDate = new DateOnly(2026, 9, 6);
            seed.SaveChanges();
        }

        var first = new ResetBusinessDataUseCase().Execute(database.Path, backups);
        var second = new ResetBusinessDataUseCase().Execute(database.Path, backups);

        Assert.Equal(ResetBusinessDataCodes.Success, first.Code);
        Assert.Equal(ResetBusinessDataCodes.NoData, second.Code);
        Assert.Single(Directory.GetFiles(backups, "backup-*.db"));
        using var verify = database.Open();
        Assert.Equal(725, verify.Settings.AsNoTracking().Single().ReminderMinuteOfDay);
        Assert.Null(verify.AppStates.AsNoTracking().Single().LastReminderDate);
        Assert.Null(verify.AppStates.AsNoTracking().Single().LastNormalRunDate);
        Assert.Single(verify.BackupRecords.AsNoTracking());
        Assert.Equal(9, verify.Database.GetAppliedMigrations().Count());
    }

    [Fact]
    public void TrulyEmptyDatabaseIsIdempotentAndDoesNotCreateBackup()
    {
        using var database = SqliteTestDatabase.Create();
        var backups = Path.Combine(database.Directory, "backups");

        var first = new ResetBusinessDataUseCase().Execute(database.Path, backups);
        var second = new ResetBusinessDataUseCase().Execute(database.Path, backups);

        Assert.Equal(ResetBusinessDataCodes.NoData, first.Code);
        Assert.Equal(ResetBusinessDataCodes.NoData, second.Code);
        Assert.False(Directory.Exists(backups));
        using var verify = database.Open();
        Assert.Empty(verify.BackupRecords.AsNoTracking());
        Assert.Equal(9, verify.Database.GetAppliedMigrations().Count());
    }

    [Fact]
    public void IconDashboardAndSettingsContractsAreBoundToTheSharedImplementation()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "StoreExpiryInspector.csproj"));
        var window = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        var tray = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WindowsTrayIcon.cs"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "StoreExpiryInspector.iss"));
        var resetUseCase = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "Application", "ResetBusinessDataUseCase.cs"));
        var iconPath = Path.Combine(root, "src", "StoreExpiryInspector", "Assets", "StoreExpiryInspector.ico");

        Assert.Contains("<Version>1.0.6</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\StoreExpiryInspector.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("Icon=\"/StoreExpiryInspector;component/Assets/StoreExpiryInspector.ico\"", window, StringComparison.Ordinal);
        Assert.Equal(5, Count(window, "Style=\"{StaticResource DashboardMetricTextStyle}\""));
        foreach (var binding in new[] { "OpenTaskCount", "ExpiredCount", "WithdrawCount", "Discount20Count", "Discount50Count" })
            Assert.Contains($"<Run Text=\"{{Binding Dashboard.{binding}, Mode=OneWay}}\"", window, StringComparison.Ordinal);
        Assert.Contains("LineStackingStrategy\" Value=\"BlockLineHeight", window, StringComparison.Ordinal);
        Assert.Contains("再次确认重置", codeBehind, StringComparison.Ordinal);
        Assert.Contains("IsDefault = false", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ResetBusinessDataAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ExtractIconEx(executablePath", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationIconId", tray, StringComparison.Ordinal);
        Assert.Contains("SetupIconFile=..\\src\\StoreExpiryInspector\\Assets\\StoreExpiryInspector.ico", installer, StringComparison.Ordinal);
        Assert.Contains("UninstallDisplayIcon={app}\\app\\StoreExpiryInspector.exe", installer, StringComparison.Ordinal);
        foreach (var table in new[]
                 {
                     "inspection_item_revisions", "draft_items", "batch_baselines", "lifecycle_events",
                     "inspection_items", "drafts", "inspections", "task_items", "tasks",
                     "inventory_adjustments", "import_issues", "import_workbooks", "scope_baselines",
                     "batches", "products", "imports"
                 })
            Assert.Contains($"DELETE FROM {table};", resetUseCase, StringComparison.Ordinal);
        Assert.Contains("UPDATE app_state SET last_reminder_date = NULL, last_normal_run_date = NULL", resetUseCase, StringComparison.Ordinal);
        Assert.Equal(
            new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 },
            ReadIcoSizes(iconPath));
    }

    private static void AssertBusinessTablesEmpty(Infrastructure.StoreDbContext context)
    {
        Assert.Empty(context.Products.AsNoTracking());
        Assert.Empty(context.Batches.AsNoTracking());
        Assert.Empty(context.Tasks.AsNoTracking());
        Assert.Empty(context.Inspections.AsNoTracking());
        Assert.Empty(context.Imports.AsNoTracking());
        Assert.Empty(context.LifecycleEvents.AsNoTracking());
        Assert.Empty(context.ScopeBaselines.AsNoTracking());
    }

    private static int[] ReadIcoSizes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = BitConverter.ToUInt16(bytes, 4);
        return Enumerable.Range(0, count)
            .Select(index => bytes[6 + index * 16] is 0 ? 256 : bytes[6 + index * 16])
            .Order()
            .ToArray();
    }

    private static int Count(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
