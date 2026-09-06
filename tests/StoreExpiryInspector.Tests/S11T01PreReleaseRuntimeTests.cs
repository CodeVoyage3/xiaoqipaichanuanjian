using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S11T01PreReleaseRuntimeTests
{
    [Fact]
    public async Task PreReleaseProductionEquivalentUpgradeAndRuntimeReset()
    {
        if (Environment.GetEnvironmentVariable("S11_PRE_RELEASE_MODE") is null) return;
        Assert.Equal("PRE_RELEASE_PRODUCTION_EQUIVALENT", Required("S11_PRE_RELEASE_MODE"));
        var install = RequiredPath("S11_PRE_RELEASE_INSTALL_ROOT"); var data = RequiredPath("S11_PRE_RELEASE_DATA_ROOT"); var evidence = RequiredPath("S11_PRE_RELEASE_EVIDENCE_ROOT");
        var app = Path.Combine(install, "app"); var exe = Path.Combine(app, "StoreExpiryInspector.exe"); var database = Path.Combine(data, "data", "app.db");
        Assert.Equal("1.0.2", Version.Parse(FileVersionInfo.GetVersionInfo(exe).FileVersion!).ToString(3));
        Seed(database, data); var before = Fingerprint(database);
        var marker = Path.Combine(evidence, "source-host.marker"); var requests = Path.Combine(evidence, "transport-requests.json"); var updaterIdentity = Path.Combine(evidence, "updater-identity.json");
        var normalProcess = Path.Combine(evidence, "normal-process.txt");
        using var source = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = app, ArgumentList = { "--data-root", data, "--allow-existing-isolated-data-root", "--s11-pre-release-install" }, Environment =
        {
            ["S11_PRE_RELEASE_MARKER"] = marker, ["S11_PRE_RELEASE_REQUESTS"] = requests, ["S11_PRE_RELEASE_MANIFEST"] = RequiredPath("S11_PRE_RELEASE_MANIFEST"), ["S11_PRE_RELEASE_SIGNATURE"] = RequiredPath("S11_PRE_RELEASE_SIGNATURE"), ["S11_PRE_RELEASE_PACKAGE"] = RequiredPath("S11_PRE_RELEASE_PACKAGE"), ["S11_PRE_RELEASE_PUBLIC_KEY"] = Required("S11_PRE_RELEASE_PUBLIC_KEY"), ["S11_PRE_RELEASE_CACHE_ROOT"] = RequiredPath("S11_PRE_RELEASE_CACHE_ROOT"), ["S11_PRE_RELEASE_INSTALL_ROOT"] = install, ["S11_PRE_RELEASE_UPDATER_ROOT"] = Path.Combine(app, "Updater"), ["S11_PRE_RELEASE_UPDATER_IDENTITY"] = updaterIdentity, ["S9_T07_NORMAL_PROCESS_RECORD"] = normalProcess
        } })!;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90))) await source.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, source.ExitCode); await WaitFor(updaterIdentity, TimeSpan.FromSeconds(10));
        var operationRoot = await WaitForOperation(data, TimeSpan.FromSeconds(90)); var operation = Path.GetFileName(operationRoot); var journalPath = Path.Combine(operationRoot, "journal.json");
        await WaitForJournal(journalPath, TimeSpan.FromSeconds(90)); await WaitFor(Path.Combine(operationRoot, "health-ack.json"), TimeSpan.FromSeconds(30)); await WaitFor(normalProcess, TimeSpan.FromSeconds(30));
        using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal("1.0.2", journal.RootElement.GetProperty("SourceVersion").GetString()); Assert.Equal("1.0.3", journal.RootElement.GetProperty("TargetVersion").GetString()); Assert.Equal((int)InstallationUpdatePhase.Completed, journal.RootElement.GetProperty("Phase").GetInt32());
        using var ack = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(operationRoot, "health-ack.json"))); Assert.Equal("1.0.3", ack.RootElement.GetProperty("version").GetString()); Assert.True(ack.RootElement.GetProperty("uiLoaded").GetBoolean()); Assert.Equal(9, ack.RootElement.GetProperty("migrationCount").GetInt32());
        Assert.Equal(8, JsonDocument.Parse(await File.ReadAllTextAsync(requests)).RootElement.GetArrayLength());
        Assert.Equal("1.0.3", Version.Parse(FileVersionInfo.GetVersionInfo(exe).FileVersion!).ToString(3)); Assert.Equal("1.0.3", AssemblyName.GetAssemblyName(Path.Combine(app, "StoreExpiryInspector.dll")).Version!.ToString(3));
        Assert.Equal(InstallationTreeFingerprint.Create(RequiredPath("S11_PRE_RELEASE_CANDIDATE_PUBLISH")).Hash, InstallationTreeFingerprint.Create(app).Hash); Assert.Equal(before, Fingerprint(database));
        StopRecorded(normalProcess, exe);

        using var ui = Start(exe, data); var main = await MainWindow(ui, TimeSpan.FromSeconds(30)); Assert.True(WindowIconPresent(main), "WPF main-window/taskbar icon missing"); var dataMetrics = Metrics(main); AssertAligned(dataMetrics); Capture(main, Path.Combine(evidence, "dashboard-data.png"));
        var backupCount = BackupCount(database); var unchanged = Fingerprint(database, includeBackups: false);
        Invoke(Find(main, "设置", ControlType.Button)); var settings = await Window(ui.Id, "提醒设置", TimeSpan.FromSeconds(10)); Invoke(Find(settings, "重置业务数据", ControlType.Button)); var warning = await Window(ui.Id, "重置业务数据", TimeSpan.FromSeconds(10)); Invoke(Find(warning, "取消重置业务数据", ControlType.Button)); Assert.Equal(unchanged, Fingerprint(database, includeBackups: false)); Assert.Equal(backupCount, BackupCount(database));
        Invoke(Find(settings, "重置业务数据", ControlType.Button)); warning = await Window(ui.Id, "重置业务数据", TimeSpan.FromSeconds(10)); Invoke(Find(warning, "继续", ControlType.Button)); var second = await Window(ui.Id, "再次确认重置", TimeSpan.FromSeconds(10)); Invoke(Find(second, "取消再次确认重置", ControlType.Button)); Assert.Equal(unchanged, Fingerprint(database, includeBackups: false)); Assert.Equal(backupCount, BackupCount(database));
        CreateResetFailureTrigger(database); Invoke(Find(settings, "重置业务数据", ControlType.Button)); warning = await Window(ui.Id, "重置业务数据", TimeSpan.FromSeconds(10)); Invoke(Find(warning, "继续", ControlType.Button)); second = await Window(ui.Id, "再次确认重置", TimeSpan.FromSeconds(10)); Invoke(Find(second, "确认重置", ControlType.Button)); var failed = await Window(ui.Id, "重置未完成", TimeSpan.FromSeconds(30)); Invoke(Find(failed, "知道了", ControlType.Button)); Assert.Equal(unchanged, Fingerprint(database, includeBackups: false)); Assert.Equal(backupCount + 1, BackupCount(database)); DropResetFailureTrigger(database); backupCount++;
        Invoke(Find(settings, "重置业务数据", ControlType.Button)); warning = await Window(ui.Id, "重置业务数据", TimeSpan.FromSeconds(10)); Invoke(Find(warning, "继续", ControlType.Button)); second = await Window(ui.Id, "再次确认重置", TimeSpan.FromSeconds(10)); Invoke(Find(second, "确认重置", ControlType.Button)); var done = await Window(ui.Id, "重置业务数据", TimeSpan.FromSeconds(30)); Invoke(Find(done, "知道了", ControlType.Button));
        await WaitForBusinessCount(database, 0, TimeSpan.FromSeconds(20)); await WaitForWindowMissing(ui.Id, "提醒设置", TimeSpan.FromSeconds(10)); Assert.Equal(backupCount + 1, BackupCount(database)); var emptyMetrics = Metrics(main); AssertAligned(emptyMetrics); Assert.NotNull(Find(main, "暂无导入数据", ControlType.Text)); Capture(main, Path.Combine(evidence, "dashboard-empty.png")); Stop(ui);
        using var reopened = Start(exe, data); var reopenedMain = await MainWindow(reopened, TimeSpan.FromSeconds(30)); Assert.NotNull(Find(reopenedMain, "暂无导入数据", ControlType.Text)); Stop(reopened);
        SeedAppStateDates(database); var stateBackupCount = BackupCount(database); using var stateUi = Start(exe, data); var stateMain = await MainWindow(stateUi, TimeSpan.FromSeconds(30)); Invoke(Find(stateMain, "设置", ControlType.Button)); settings = await Window(stateUi.Id, "提醒设置", TimeSpan.FromSeconds(10)); Invoke(Find(settings, "重置业务数据", ControlType.Button)); warning = await Window(stateUi.Id, "重置业务数据", TimeSpan.FromSeconds(10)); Invoke(Find(warning, "继续", ControlType.Button)); second = await Window(stateUi.Id, "再次确认重置", TimeSpan.FromSeconds(10)); Invoke(Find(second, "确认重置", ControlType.Button)); done = await Window(stateUi.Id, "重置业务数据", TimeSpan.FromSeconds(30)); Invoke(Find(done, "知道了", ControlType.Button)); Assert.Equal(stateBackupCount + 1, BackupCount(database)); Assert.True(AppStateDatesAreNull(database)); Stop(stateUi);
        Reimport(database, data); Assert.True(BusinessCount(database) > 0);

        var result = new { marker = "PRE_RELEASE_PRODUCTION_EQUIVALENT", officialGitHubBytes = false, sourceVersion = "1.0.2", targetVersion = "1.0.3", migrationCount = 9, requestCount = 8, dataPreserved = true, runtimeReset = true, resetCancelNoWrite = true, resetFailureAtomic = true, resetRestartEmpty = true, appStateOnlyReset = true, windowAndTaskbarIconPresent = true, reimportViaExistingApplicationUseCase = true, dataMetrics, emptyMetrics, screenshots = new[] { "dashboard-data.png", "dashboard-empty.png" } };
        await File.WriteAllTextAsync(Path.Combine(evidence, "runtime-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Seed(string database, string dataRoot)
    {
        DatabaseInitializer.Initialize(database); using var context = DatabaseInitializer.CreateContext(database); var import = new ImportRecord { SourceFileName = "s11-pre-release.xlsx", SourceFileSha256 = new string('a', 64), ParsedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, Status = "succeeded", ProductCount = 12, BatchCount = 12, NewProductCount = 12, NewBatchCount = 12, NewTaskProductCount = 12 }; context.Imports.Add(import); context.SaveChanges();
        for (var index = 0; index < 12; index++) { var product = new Product { ProductCode = $"S11-{index:00}", CurrentName = "发布前合成商品" + index, CurrentBarcode = "690000000" + index, ExcelStockQty = 20, EffectiveStockQty = 20, LastSeenImportId = import.Id }; var batch = new Batch { Product = product, ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(index % 2 == 0 ? -1 : 20), ShelfLifeValue = 30, CurrentArrivalQty = 10, MaxArrivalQty = 10, CurrentStage = index % 2 == 0 ? "expired" : "discount_50", LastSeenImportId = import.Id }; var task = new ProductTask { Product = product, HighestStage = batch.CurrentStage }; task.Items.Add(new ProductTaskItem { Product = product, Batch = batch, Stage = batch.CurrentStage }); context.Products.Add(product); context.Tasks.Add(task); }
        context.SaveChanges(); var first = context.Products.Include(item => item.Batches).Include(item => item.Tasks).ThenInclude(item => item.Items).First(); var inspection = new Inspection { TaskId = first.Tasks.Single().Id, ProductId = first.Id, ProductCodeSnapshot = first.ProductCode, ProductNameSnapshot = first.CurrentName, BarcodeSnapshot = first.CurrentBarcode, StageSnapshot = "expired", StockQtySnapshot = 20, InspectorName = "发布前验收", CheckDate = DateOnly.FromDateTime(DateTime.Today) }; inspection.Items.Add(new InspectionItem { ProductId = first.Id, BatchId = first.Batches.Single().Id, ExpiryDateSnapshot = first.Batches.Single().ExpiryDate, StageSnapshot = "expired", ArrivalQtySnapshot = 10, CheckedQty = 9 }); context.Inspections.Add(inspection); context.ImportWorkbooks.Add(new ImportWorkbook { ImportId = import.Id, OriginalFileName = import.SourceFileName, Content = Enumerable.Range(0, 131073).Select(i => (byte)(i % 251)).ToArray(), Sha256 = new string('b', 64), SavedAtUtc = DateTime.UtcNow }); context.LifecycleEvents.Add(new LifecycleEvent { ProductId = first.Id, BatchId = first.Batches.Single().Id, EventType = "batch_checked_zero", Reason = "S11 pre-release", OccurredAtUtc = DateTime.UtcNow, SourceImportId = import.Id });
        var backupPath = Path.Combine(dataRoot, "backups", "existing-s11.db"); Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!); File.WriteAllBytes(backupPath, [1, 2, 3, 4]); context.BackupRecords.Add(new BackupRecord { BackupType = "manual", FilePath = backupPath, Sha256 = Convert.ToHexString(SHA256.HashData([1, 2, 3, 4])).ToLowerInvariant(), VerificationStatus = "verified" }); context.Settings.Single().ReminderMinuteOfDay = 805; context.Settings.Single().AutoStartEnabled = false; context.AppStates.Single().LastReminderDate = DateOnly.FromDateTime(DateTime.Today); context.AppStates.Single().LastNormalRunDate = DateOnly.FromDateTime(DateTime.Today); context.SaveChanges(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static string Fingerprint(string database, bool includeBackups = true)
    {
        using var context = DatabaseInitializer.CreateContext(database); var value = new { products = context.Products.OrderBy(x => x.Id).Select(x => new { x.ProductCode, x.CurrentName, x.EffectiveStockQty }).ToArray(), batches = context.Batches.OrderBy(x => x.Id).Select(x => new { x.ExpiryDate, x.CurrentStage, x.CurrentArrivalQty }).ToArray(), tasks = context.Tasks.Count(), taskItems = context.TaskItems.Count(), inspections = context.Inspections.Count(), workbooks = context.ImportWorkbooks.Select(x => new { x.OriginalFileName, size = x.Content.Length, sha = Convert.ToHexString(SHA256.HashData(x.Content)) }).ToArray(), settings = context.Settings.Select(x => new { x.ReminderMinuteOfDay, x.AutoStartEnabled }).Single(), state = context.AppStates.Select(x => new { x.LastReminderDate, x.LastNormalRunDate }).Single(), backups = includeBackups ? context.BackupRecords.OrderBy(x => x.Id).Select(x => new { x.BackupType, x.FilePath, x.Sha256, x.VerificationStatus }).ToArray() : null, migrations = context.Database.GetAppliedMigrations().ToArray() }; return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    }

    private static void Reimport(string database, string dataRoot)
    {
        var source = Path.Combine(dataRoot, "s11-reimport.xlsx");
        S8T03ImportPerformanceTests.WriteWorkbook(source, 1, 1, seed: true, productOffset: 910000);
        ImportConfirmationContract contract;
        using (var preview = DatabaseInitializer.CreateContext(database))
        {
            var workbook = new ExcelTemplateReader().Read(source);
            var plan = new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source, workbook, plan)).Contract);
        }

        using var context = DatabaseInitializer.CreateContext(database);
        var result = new ConfirmedImportLifecycleOrchestrator().Execute(context, new(
            contract, Path.Combine(dataRoot, "snapshots"), DateTime.UtcNow,
            DateOnly.FromDateTime(DateTime.Today), DateTime.UtcNow));
        Assert.True(result.Succeeded, result.SafeSummary);
    }
    private static int BusinessCount(string database) { using var context = DatabaseInitializer.CreateContext(database); return context.Products.Count(); }
    private static int BackupCount(string database) { using var context = DatabaseInitializer.CreateContext(database); return context.BackupRecords.Count(); }
    private static void CreateResetFailureTrigger(string database) { using var context = DatabaseInitializer.CreateContext(database); context.Database.ExecuteSqlRaw("CREATE TRIGGER s11_reset_failure BEFORE DELETE ON products BEGIN SELECT RAISE(ABORT, 's11 reset injection'); END;"); }
    private static void DropResetFailureTrigger(string database) { using var context = DatabaseInitializer.CreateContext(database); context.Database.ExecuteSqlRaw("DROP TRIGGER s11_reset_failure;"); }
    private static void SeedAppStateDates(string database) { using var context = DatabaseInitializer.CreateContext(database); var state = context.AppStates.Single(); state.LastReminderDate = DateOnly.FromDateTime(DateTime.Today); state.LastNormalRunDate = DateOnly.FromDateTime(DateTime.Today); context.SaveChanges(); }
    private static bool AppStateDatesAreNull(string database) { using var context = DatabaseInitializer.CreateContext(database); var state = context.AppStates.Single(); return state.LastReminderDate is null && state.LastNormalRunDate is null; }
    private static async Task WaitForBusinessCount(string database, int count, TimeSpan timeout) { for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end; await Task.Delay(100)) if (BusinessCount(database) == count) return; Assert.Equal(count, BusinessCount(database)); }
    private static Process Start(string exe, string data) => Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)!, ArgumentList = { "--data-root", data, "--allow-existing-isolated-data-root" } })!;
    private static async Task<AutomationElement> MainWindow(Process process, TimeSpan timeout) { process.WaitForInputIdle(10000); for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end; await Task.Delay(100)) { process.Refresh(); if (process.MainWindowHandle != IntPtr.Zero) return AutomationElement.FromHandle(process.MainWindowHandle); } throw new TimeoutException("main window missing"); }
    private static async Task<AutomationElement> Window(int pid, string name, TimeSpan timeout) { var condition = new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, pid), new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)); for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end; await Task.Delay(100)) { var found = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition); if (found is not null) return found; } throw new TimeoutException("window missing: " + name); }
    private static AutomationElement Find(AutomationElement root, string name, ControlType type) => root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, type))) ?? throw new InvalidOperationException("automation element missing: " + name);
    private static void Invoke(AutomationElement element) => ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    private static Metric[] Metrics(AutomationElement main) { var texts = main.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>(); return new[] { "待排查 ", "过期 ", "收仓 ", "2折 ", "5折 " }.Select(prefix => { var item = texts.FirstOrDefault(x => x.Current.Name.StartsWith(prefix, StringComparison.Ordinal)) ?? throw new InvalidOperationException("metric missing: " + prefix); var box = item.Current.BoundingRectangle; return new Metric(item.Current.Name, box.Top, box.Height); }).ToArray(); }
    private static void AssertAligned(Metric[] metrics) { Assert.Equal(5, metrics.Length); Assert.Single(metrics.Select(x => Math.Round(x.Top, 1)).Distinct()); Assert.Single(metrics.Select(x => Math.Round(x.Height, 1)).Distinct()); Assert.All(metrics, item => Assert.True(item.Height >= 24)); }
    private static void Capture(AutomationElement window, string path) { var box = window.Current.BoundingRectangle; using var bitmap = new System.Drawing.Bitmap((int)box.Width, (int)box.Height); using var graphics = System.Drawing.Graphics.FromImage(bitmap); graphics.CopyFromScreen((int)box.Left, (int)box.Top, 0, 0, bitmap.Size); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
    private static bool WindowIconPresent(AutomationElement window) { var handle = new IntPtr(window.Current.NativeWindowHandle); return handle != IntPtr.Zero && (SendMessage(handle, 0x007F, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero || GetClassLongPtr(handle, -14) != IntPtr.Zero || GetClassLongPtr(handle, -34) != IntPtr.Zero); }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr handle, int index);
    private static void Stop(Process process) { try { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(3000)) { process.Kill(true); process.WaitForExit(5000); } } } finally { process.Dispose(); } }
    private static void StopRecorded(string path, string exe) { if (!File.Exists(path)) return; var parts = File.ReadAllText(path).Split('|'); if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !DateTimeOffset.TryParse(parts[1], out var started)) return; try { using var process = Process.GetProcessById(pid); if (Math.Abs((process.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1 && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) { process.Kill(true); process.WaitForExit(5000); } } catch (ArgumentException) { } }
    private static async Task<string> WaitForOperation(string data, TimeSpan timeout) { var updates = Path.Combine(data, "updates"); for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end; await Task.Delay(100)) { if (Directory.Exists(updates) && Directory.GetDirectories(updates).Length == 1) return Directory.GetDirectories(updates).Single(); } throw new TimeoutException("operation missing"); }
    private static async Task WaitFor(string path, TimeSpan timeout) { for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end && !File.Exists(path); await Task.Delay(100)) { } Assert.True(File.Exists(path), path); }
    private static async Task WaitForWindowMissing(int pid, string name, TimeSpan timeout) { var condition = new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, pid), new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)); for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end; await Task.Delay(100)) if (AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition) is null) return; throw new TimeoutException("window did not close: " + name); }
    private static async Task WaitForJournal(string path, TimeSpan timeout) { for (var end = DateTime.UtcNow + timeout; DateTime.UtcNow < end; await Task.Delay(100)) try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); if (doc.RootElement.GetProperty("Phase").GetInt32() == (int)InstallationUpdatePhase.Completed) return; } catch (IOException) { } throw new TimeoutException("journal did not complete"); }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
    private static string RequiredPath(string name) { var path = Path.GetFullPath(Required(name)); var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), path); Assert.False(Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)); Assert.Contains(relative.Split(Path.DirectorySeparatorChar), part => Guid.TryParse(part, out _)); return path; }
    public sealed record Metric(string Text, double Top, double Height);
}
