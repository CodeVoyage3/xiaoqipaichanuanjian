using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;
using StoreExpiryInspector.UI;

namespace StoreExpiryInspector;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private DailyReminderScheduler? _reminderScheduler;
    private WindowsTrayIcon? _trayIcon;
    private LocalFileLogger? _logger;
    private IDisposable? _databaseMaintenanceLease;
    private bool _explicitExit;
    private UpdateCheckRuntime? _updateCheckRuntime;
    private int _updateCheckStarted;
    private UpdateNetworkDiagnostics? _updateDiagnostics;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (InstallerPreflight.TryHandle(e.Args, out var preflightExitCode))
        {
            Shutdown(preflightExitCode);
            return;
        }

        try
        {
            RuntimeDataRoot.Configure(e.Args);
#if S9T07_TEST
            if (RuntimeDataRoot.IsS9T07TestInstall) TestInstallMarker("configured");
            if (RuntimeDataRoot.IsS11PreReleaseInstall) PreReleaseTestMarker("configured");
#endif
            if (UpdateNetworkDiagnostics.IsRequested(e.Args))
            {
                if (!RuntimeDataRoot.IsIsolated) throw new ArgumentException("网络诊断必须使用隔离数据目录。");
                _updateDiagnostics = UpdateNetworkDiagnostics.OpenIfRequested(e.Args, RuntimeDataRoot.RootDirectory);
            }
            _updateDiagnostics?.Add("diagnostic-enabled", new { actualCandidateVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3), simulatedSourceVersion = _updateDiagnostics.SimulatedSourceVersion.ToString(3), dataRoot = "TEMP/GUID", installDisabled = true });
        }
        catch (Exception exception)
        {
#if S9T07_TEST
            var marker = Environment.GetEnvironmentVariable("S9_T07_TEST_INSTALL_MARKER");
            if (!string.IsNullOrWhiteSpace(marker) && Path.IsPathFullyQualified(marker) && Path.GetFullPath(marker).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) File.WriteAllText(marker, "configure-error-" + exception.GetType().Name + "-" + exception.Message);
            marker = Environment.GetEnvironmentVariable("S11_PRE_RELEASE_MARKER");
            if (!string.IsNullOrWhiteSpace(marker) && Path.IsPathFullyQualified(marker) && Path.GetFullPath(marker).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) File.WriteAllText(marker, "configure-error-" + exception.GetType().Name + "-" + exception.Message);
#endif
            WpfDialogService.Show(
                owner: null,
                "门店效期排查软件",
                exception.Message,
                "知道了",
                WpfDialogKind.Error,
                showCancel: false);
            Shutdown();
            return;
        }

        try
        {
            if (!RuntimeDataRoot.IsIsolated && RuntimeDataRoot.UpgradeVerificationOperationId is null && RuntimeDataRoot.NormalLaunchOperationId is null && PendingUpdateRecovery.TryResume(RuntimeDataRoot.RootDirectory))
            {
                Shutdown();
                return;
            }
        }
        catch (Exception exception)
        {
            WpfDialogService.Show(null, "门店效期排查软件", exception.Message, "知道了", WpfDialogKind.Error, showCancel: false);
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, RuntimeDataRoot.MutexName, out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            if (RuntimeDataRoot.UpgradeVerificationOperationId is not null)
            {
                Shutdown(1);
                return;
            }
            WpfDialogService.Show(
                owner: null,
                "门店效期排查软件",
                "门店效期排查软件已在运行，请从系统托盘打开。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            Shutdown();
            return;
        }

        _logger = new LocalFileLogger(RuntimeDataRoot.LogDirectory);
        if (RuntimeDataRoot.NormalLaunchOperationId is { } normalOperation)
        {
            try { _ = NormalLaunchHandshake.Identify(RuntimeDataRoot.RootDirectory, normalOperation, RuntimeDataRoot.NormalLaunchToken!, AppContext.BaseDirectory, schema => SchemaUpgradeSnapshots.ValidateMetadata(RuntimeDataRoot.RootDirectory, schema.Snapshot!)); }
            catch (Exception exception) { _logger.TryWrite("error", "normal_launch_identity_failed", "普通启动授权无效。", exception.ToString()); Shutdown(1); return; }
        }
        if (RuntimeDataRoot.UpgradeVerificationOperationId is { } operationId)
        {
            var schemaToken = RuntimeDataRoot.SchemaUpgradeVerificationLaunchToken;
            var sourceVerification = false;
            if (schemaToken is not null)
            {
                // The candidate cannot open SQLite until the external updater has persisted its PID/start identity.
                var authorization = UpgradeHealthAck.WaitForSchemaAuthorization(RuntimeDataRoot.RootDirectory, operationId, schemaToken, StaticMigrations(), TimeSpan.FromSeconds(30));
                if (authorization is null) { Shutdown(1); return; }
                try
                {
                    var migrations = StaticMigrations();
                    sourceVerification = migrations.SequenceEqual(authorization.SourceMigrations, StringComparer.Ordinal) && authorization.Migrations.SequenceEqual(authorization.SourceMigrations, StringComparer.Ordinal);
                    if (sourceVerification)
                        SchemaUpgradeSnapshots.VerifyFrozenSource(RuntimeDataRoot.RootDirectory, authorization.SourceSha256, authorization.SourceMigrations);
                    else
                        SchemaUpgradeSnapshots.TakeOverFrozenSource(RuntimeDataRoot.RootDirectory, authorization.SourceSha256, authorization.SourceMigrations, connection => { DatabaseInitializer.InitializeOpened(connection); UpgradeHealthAck.WriteSchemaMigrationApplied(RuntimeDataRoot.RootDirectory, operationId, schemaToken!, migrations); });
                }
                catch (Exception exception) { _logger.TryWrite("error", "schema_upgrade_takeover_failed", "跨 Schema 升级源数据库接管失败，候选程序未启动。", exception.ToString()); Shutdown(1); return; }
            }
            base.OnStartup(e);
            MainWindow = new UI.MainWindow(CreateVerificationShell(schemaToken is not null && !sourceVerification)) { IsEnabled = false };
            MainWindow.Show();
            StartUpgradeVerification(operationId, schemaToken, schemaToken is not null && !sourceVerification);
            return;
        }
        try
        {
            DatabaseInitializer.Initialize();
            var businessDate = DateOnly.FromDateTime(DateTime.Now);
            var occurredAtUtc = DateTime.UtcNow;
            using var context = DatabaseInitializer.CreateContext();
            var result = new ApplicationStartupCoordinator().Execute(
                context,
                businessDate,
                occurredAtUtc);
            _logger.TryWrite(
                result.ClockRollback ? "warning" : "info",
                result.ClockRollback
                    ? "startup_clock_rollback"
                    : "startup_recalculation_completed",
                result.ClockRollback
                    ? "检测到系统日期回拨，已跳过启动补算。"
                    : "启动补算已完成。");
        }
        catch (Exception exception)
        {
            _logger.TryWrite(
                "error",
                "startup_failed",
                "启动初始化或补算失败，已停止启动。",
                exception.ToString());
            WpfDialogService.Show(
                owner: null,
                "门店效期排查软件",
                "数据库初始化失败，应用未启动且未自动恢复。请联系管理员并查看日志。",
                "知道了",
                WpfDialogKind.Error,
                showCancel: false);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        if (_updateDiagnostics is null)
        {
            MainWindow = new UI.MainWindow();
        }
        else
        {
            MainWindow = new UI.MainWindow(_updateDiagnostics);
        }
        if (RuntimeDataRoot.NormalLaunchOperationId is { } loadedOperation)
            MainWindow.Loaded += (_, _) => { try { NormalLaunchHandshake.Loaded(RuntimeDataRoot.RootDirectory, loadedOperation, RuntimeDataRoot.NormalLaunchToken!); } catch { Shutdown(1); } };
        MainWindow.Show();
#if S9T07_TEST
        if (RuntimeDataRoot.IsTestUpdateInstall)
        {
            InitializeDesktopRuntime();
            Dispatcher.BeginInvoke(RuntimeDataRoot.IsS11PreReleaseInstall ? RunS11PreReleaseInstall : RunS9T07TestInstall);
            return;
        }
#endif
        if (RuntimeDataRoot.IsSmokeRun)
        {
            Dispatcher.BeginInvoke(
                VerifySmokeStartupAndExit,
                DispatcherPriority.ApplicationIdle);
            return;
        }

        Dispatcher.BeginInvoke(
            InitializeDesktopRuntime,
            DispatcherPriority.ApplicationIdle);
    }

    private void VerifySmokeStartupAndExit()
    {
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (MainWindow is UI.MainWindow { IsLoaded: true, DataContext: ShellViewModel shell } &&
                !shell.Dashboard.IsLoading && !shell.PendingTasks.IsLoading)
            {
                timer.Stop();
                if (!shell.Dashboard.HasError && !shell.PendingTasks.HasError)
                {
                    _logger?.TryWrite("info", "s9_t01_smoke_ready", "隔离发布 smoke 已完成 WPF Shell 初始化与首轮读取。");
                    MainWindow?.Close();
                    Shutdown();
                }
                else
                {
                    _logger?.TryWrite("error", "s9_t01_smoke_failed", "隔离发布 smoke 的 WPF Shell 首轮读取失败。");
                    MainWindow?.Close();
                    Shutdown(1);
                }

                return;
            }

            if (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                return;
            }

            timer.Stop();
            _logger?.TryWrite("error", "s9_t01_smoke_failed", "隔离发布 smoke 未在时限内完成 WPF Shell 初始化与首轮读取。");
            MainWindow?.Close();
            Shutdown(1);
        };
        timer.Start();
    }

    private void StartUpgradeVerification(string operationId, string? schemaLaunchToken = null, bool includeWal = false)
    {
        // This path is deliberately before Initialize/Migrate/recalculation and scheduler setup.
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
                if (MainWindow is UI.MainWindow { IsLoaded: true, DataContext: ShellViewModel shell } &&
                    !shell.Dashboard.IsLoading && !shell.PendingTasks.IsLoading)
                {
                    timer.Stop();
                    VerifyUpgradeAndExit(operationId, schemaLaunchToken, includeWal);
                    return;
                }
                if (elapsed.Elapsed < TimeSpan.FromSeconds(30)) return;
                timer.Stop();
                if (MainWindow?.DataContext is ShellViewModel timedOutShell)
                    _logger?.TryWrite("error", "s9_t05_verification_shell_timeout", $"startup={timedOutShell.StartupLoadTask.Status}; dashboardLoading={timedOutShell.Dashboard.IsLoading}; dashboardError={timedOutShell.Dashboard.HasError}; pendingLoading={timedOutShell.PendingTasks.IsLoading}; pendingError={timedOutShell.PendingTasks.HasError}", timedOutShell.StartupLoadTask.Exception?.ToString());
                Shutdown(1);
        };
        timer.Start();
    }

    private static ShellViewModel CreateVerificationShell(bool includeWal)
    {
        var connection = VerificationConnectionString(includeWal: includeWal);
        return new ShellViewModel(defaultContextFactory: () => new StoreDbContext(
            new DbContextOptionsBuilder<StoreDbContext>().UseSqlite(connection).Options));
    }

    private static IReadOnlyList<string> StaticMigrations()
    {
        using var context = new StoreDbContextFactory().CreateDbContext([]);
        return context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    private void VerifyUpgradeAndExit(string operationId, string? schemaLaunchToken = null, bool includeWal = false)
    {
        try
        {
            if (MainWindow is not UI.MainWindow { IsLoaded: true, DataContext: ShellViewModel shell } ||
                shell.Dashboard.HasError || shell.PendingTasks.HasError)
            {
                Shutdown(1);
                return;
            }

            var databasePath = RuntimeDataRoot.DatabasePath;
            using var connection = new SqliteConnection(VerificationConnectionString(databasePath, includeWal));
            connection.Open();
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            if (integrity.ExecuteScalar() is not string result || !string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) { Shutdown(1); return; }
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var reader = foreignKeys.ExecuteReader();
            if (reader.Read()) { Shutdown(1); return; }
            using var migrationsCommand = connection.CreateCommand();
            migrationsCommand.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            using var migrationsReader = migrationsCommand.ExecuteReader();
            var migrations = new List<string>(); while (migrationsReader.Read()) migrations.Add(migrationsReader.GetString(0));
            if (schemaLaunchToken is null && (migrations.Count != 9 || migrations[^1] != "20260901155124_AddPolicyAndBaselineFoundation")) { Shutdown(1); return; }
            if (schemaLaunchToken is null) UpgradeHealthAck.Write(RuntimeDataRoot.RootDirectory, operationId, Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown", migrations.Count, migrations[^1]);
            else UpgradeHealthAck.WriteSchema(RuntimeDataRoot.RootDirectory, operationId, schemaLaunchToken, Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown", migrations);
            MainWindow?.Close();
            Shutdown();
        }
        catch { MainWindow?.Close(); Shutdown(1); }
    }

    private static string VerificationConnectionString(string? databasePath = null, bool includeWal = false) => new SqliteConnectionStringBuilder
    {
        // A schema candidate must read its own controlled WAL; ordinary verification remains immutable.
        DataSource = includeWal ? databasePath ?? RuntimeDataRoot.DatabasePath : new Uri(databasePath ?? RuntimeDataRoot.DatabasePath).AbsoluteUri + "?immutable=1",
        Mode = SqliteOpenMode.ReadOnly,
        ForeignKeys = true,
        Pooling = false
    }.ToString();

    protected override void OnExit(ExitEventArgs e)
    {
        _updateCheckRuntime?.Dispose();
        _updateCheckRuntime = null;
        _updateDiagnostics?.Add("app-exit", new { threadId = Environment.CurrentManagedThreadId });
        (MainWindow as UI.MainWindow)?.StopUpdatePreparation();
        _updateDiagnostics?.Dispose();
        _updateDiagnostics = null;
        StopRuntime();
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void InitializeDesktopRuntime()
    {
        if (MainWindow is not UI.MainWindow mainWindow || _logger is null)
        {
            return;
        }

        if (mainWindow.DataContext is ShellViewModel shell)
        {
            shell.ConfigureDatabaseProtectionRuntime(
                BeginDatabaseMaintenanceAsync,
                EndDatabaseMaintenance,
                ExitApplication);
            if (_updateDiagnostics is null) mainWindow.ConfigureUpdateInstallation(InstallPreparedUpdateAsync);
            mainWindow.Closing += MainWindow_Closing;
            mainWindow.ReminderTimeChanged += ReminderTimeChanged;
        }

        InitializeTray(mainWindow, _logger);
        StartUpdateCheck(mainWindow);
        InitializeReminderScheduler(mainWindow, _logger);
    }

    private void InitializeTray(UI.MainWindow mainWindow, LocalFileLogger logger)
    {
        try
        {
#if S9T07_TEST
            if (Environment.GetEnvironmentVariable("S14_T01_FAIL_TRAY") == "1") throw new InvalidOperationException("S14-T01 simulated tray failure.");
#endif
            _trayIcon = new WindowsTrayIcon(mainWindow, ShowMainWindow, ExitApplication);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
#if S9T07_TEST
            DesktopRuntimeMarker("tray-ready");
#endif
        }
        catch (Exception exception)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
#if S9T07_TEST
            DesktopRuntimeMarker("tray-failed");
#endif
            logger.TryWrite(
                "error",
                "tray_icon_creation_failed",
                "系统托盘图标创建失败，关闭主窗口将正常退出应用。",
                exception.ToString());
        }
    }

    private void InitializeReminderScheduler(UI.MainWindow mainWindow, LocalFileLogger logger)
    {
        DailyReminderScheduler? scheduler = null;
        try
        {
#if S9T07_TEST
            if (Environment.GetEnvironmentVariable("S14_T01_FAIL_REMINDER") == "1") throw new InvalidOperationException("S14-T01 simulated reminder failure.");
#endif
            int reminderMinuteOfDay;
            using (var context = DatabaseInitializer.CreateContext())
            {
                reminderMinuteOfDay = context.Settings
                    .AsNoTracking()
                    .Select(setting => setting.ReminderMinuteOfDay)
                    .Single();
            }
            var coordinator = new DailyReminderRuntimeCoordinator(
                new WindowsMessageBoxReminderChannel(
                    () => mainWindow.IsVisible ? mainWindow : null),
                logger);
            scheduler = new DailyReminderScheduler(
                reminderMinuteOfDay,
                localNow =>
                {
                    try
                    {
                        return DatabaseRuntimeGate.Run(() =>
                        {
                            using var reminderContext = DatabaseInitializer.CreateContext();
                            return coordinator.Run(reminderContext, localNow);
                        });
                    }
                    catch (DatabaseRuntimeStoppedException)
                    {
                        return new(
                            "paused",
                            NotificationAttempted: false,
                            NotificationSucceeded: false,
                            ReminderRecorded: false);
                    }
                },
                logger);
            scheduler.Start();
            _reminderScheduler = scheduler;
#if S9T07_TEST
            DesktopRuntimeMarker("reminder-ready");
#endif
        }
        catch (Exception exception)
        {
            scheduler?.Dispose();
            _reminderScheduler = null;
#if S9T07_TEST
            DesktopRuntimeMarker("reminder-failed");
#endif
            logger.TryWrite(
                "error",
                "daily_reminder_runtime_failed",
                "每日集中提醒运行时初始化失败，托盘和主界面继续运行。",
                exception.ToString());
        }
    }

    private void StartUpdateCheck(UI.MainWindow mainWindow)
    {
        if (Interlocked.Exchange(ref _updateCheckStarted, 1) != 0) return;
        if (!GitHubReleaseUpdateChecker.TryGetCurrentVersion(out var currentVersion))
        {
            _logger?.TryWrite("warning", "update_version_unavailable", "无法确认更新状态，已跳过本次检查。");
            return;
        }
        if (_updateDiagnostics is not null) currentVersion = _updateDiagnostics.SimulatedSourceVersion;
        var github = new GitHubReleaseUpdateChecker(diagnostics: _updateDiagnostics);
        var gitee = new GiteeManualUpdateChecker();
        var checker = new GiteeFallbackUpdateChecker(github.CheckAsync, gitee.CheckAsync);
        _updateDiagnostics?.Add("gui-check-start", new { simulatedSourceVersion = currentVersion.ToString(3), threadId = Environment.CurrentManagedThreadId });
        _updateCheckRuntime = new UpdateCheckRuntime(
            cancellationToken => checker.CheckAsync(currentVersion, cancellationToken),
            result => Dispatcher.BeginInvoke(() =>
            {
                if (!_explicitExit && !mainWindow.IsClosed && result.Outcome == UpdateCheckOutcome.UpdateAvailable)
                    mainWindow.ShowUpdateAvailable(result);
            }), eventName => _updateDiagnostics?.Add("gui-" + eventName, new { threadId = Environment.CurrentManagedThreadId }));
        _updateCheckRuntime.StartAfter(((ShellViewModel)mainWindow.DataContext).StartupLoadTask);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_explicitExit || MainWindow is null)
        {
            return;
        }

        if (MainWindow.DataContext is ShellViewModel shell && shell.IsDatabaseProtectionBusy)
        {
            e.Cancel = true;
            WpfDialogService.Show(
                MainWindow,
                "操作进行中",
                "数据备份或恢复正在进行，请等待操作完成后再关闭应用。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            return;
        }

        if (MainWindow.DataContext is ShellViewModel { IsDatabaseProtectionLocked: true })
        {
            e.Cancel = true;
            WpfDialogService.Show(
                MainWindow,
                "请退出应用",
                "当前数据保护操作已完成或遇到严重错误，请使用页面中的“退出应用”完成正常退出。",
                "知道了",
                WpfDialogKind.Warning,
                showCancel: false);
            return;
        }

        // If tray setup failed, preserve the original fallback: closing the
        // window must be able to terminate the process.
        if (_trayIcon is null)
        {
            return;
        }

        e.Cancel = true;
        _updateDiagnostics?.Add("gui-window-hide", new { threadId = Environment.CurrentManagedThreadId });
        MainWindow.Hide();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
    }

    private void ExitApplication()
    {
        if (MainWindow?.DataContext is ShellViewModel { IsDatabaseProtectionBusy: true })
        {
            WpfDialogService.Show(
                MainWindow,
                "操作进行中",
                "数据备份或恢复正在进行，请等待操作完成后再退出应用。",
                "知道了",
                WpfDialogKind.Information,
                showCancel: false);
            return;
        }

        _explicitExit = true;
        StopRuntime();
        MainWindow?.Close();
        Shutdown();
    }

    private async Task<UpdatePackageResult> InstallPreparedUpdateAsync(VerifiedUpdatePackage package, SignedUpdatePackageDownloader downloader, Action installing)
    {
        if (!await BeginDatabaseMaintenanceAsync())
            return new(UpdatePackageOutcome.IoFailure, "当前仍有写入操作，未进入升级维护状态。");
        try
        {
            var prepared = await Task.Run(() =>
            {
                using var parent = Process.GetCurrentProcess();
                var preparer = new UpdateInstallationPreparer(downloader);
#if S9T07_TEST
                if (RuntimeDataRoot.IsTestUpdateInstall)
                    return preparer.PrepareForTest(package, parent, RequiredTestPath(RuntimeDataRoot.IsS11PreReleaseInstall ? "S11_PRE_RELEASE_INSTALL_ROOT" : "S9_T07_TEST_INSTALL_ROOT"), RuntimeDataRoot.RootDirectory, RequiredTestPath(RuntimeDataRoot.IsS11PreReleaseInstall ? "S11_PRE_RELEASE_UPDATER_ROOT" : "S9_T07_TEST_UPDATER_ROOT"), CancellationToken.None);
#endif
                return preparer.Prepare(package, parent, CancellationToken.None);
            });
            installing();
            var updater = Process.Start(UpdaterLaunch.Create(prepared.UpdaterPath, prepared.JournalPath)) ?? throw new InvalidOperationException("独立 Updater 未启动。");
#if S9T07_TEST
            if (RuntimeDataRoot.IsTestUpdateInstall)
                File.WriteAllText(RequiredTestPath(RuntimeDataRoot.IsS11PreReleaseInstall ? "S11_PRE_RELEASE_UPDATER_IDENTITY" : "S9_T07_TEST_UPDATER_IDENTITY"), System.Text.Json.JsonSerializer.Serialize(new { operationId = prepared.OperationId, pid = updater.Id, startedUtc = updater.StartTime.ToUniversalTime(), executable = updater.MainModule?.FileName }));
#endif
            _explicitExit = true;
            StopRuntime();
            MainWindow?.Close();
            Shutdown();
            return new(UpdatePackageOutcome.Verified, "独立 Updater 已接管程序切换。");
        }
        catch (Exception exception)
        {
#if S9T07_TEST
            if (RuntimeDataRoot.IsS9T07TestInstall) TestInstallMarker("prepare-error-" + exception.GetType().Name + "-" + exception.Message);
            if (RuntimeDataRoot.IsS11PreReleaseInstall) PreReleaseTestMarker("prepare-error-" + exception.GetType().Name + "-" + exception.Message);
#else
            _ = exception;
#endif
            EndDatabaseMaintenance(true);
            return new(UpdatePackageOutcome.IoFailure, "更新安装准备失败，原程序继续运行。");
        }
    }

#if S9T07_TEST
    private async void RunS11PreReleaseInstall()
    {
        var stage = "started";
        try
        {
            PreReleaseTestMarker(stage);
            if (!GitHubReleaseUpdateChecker.TryGetCurrentVersion(out var current) || current != new Version(1, 0, 2)) throw new InvalidDataException("pre-release source host must be 1.0.2");
            using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Environment.GetEnvironmentVariable("S11_PRE_RELEASE_PUBLIC_KEY") ?? throw new InvalidDataException()), out _);
            using var handler = new PreReleaseTransportHandler();
            var check = await new GitHubReleaseUpdateChecker(handler, TimeSpan.FromSeconds(10)).CheckAsync(current, CancellationToken.None);
            if (check.Outcome != UpdateCheckOutcome.UpdateAvailable || check.Release is null || check.LatestVersion != new Version(1, 0, 3)) throw new InvalidDataException("pre-release check identity mismatch");
            PreReleaseTestMarker(stage = "checked");
            var downloader = new SignedUpdatePackageDownloader(handler, new UpdatePackageOptions(rsa.ExportParameters(false), CacheRoot: RequiredTestPath("S11_PRE_RELEASE_CACHE_ROOT")));
            var prepared = await downloader.PrepareAsync(check.Release, current, null, CancellationToken.None);
            if (prepared.Outcome != UpdatePackageOutcome.Verified || prepared.Package is null) throw new InvalidDataException("pre-release package was not verified: " + prepared.Outcome);
            handler.RequireCompleteSequence();
            PreReleaseTestMarker(stage = "downloaded-and-verified");
            var result = await InstallPreparedUpdateAsync(prepared.Package, downloader, () => { });
            if (result.Outcome != UpdatePackageOutcome.Verified) { PreReleaseTestMarker("install-failed-" + result.Outcome); Shutdown(1); return; }
            PreReleaseTestMarker("updater-started");
        }
        catch (Exception exception) { PreReleaseTestMarker("error-" + stage + "-" + exception.GetType().Name + "-" + exception.Message); Shutdown(1); }
    }

    private async void RunS9T07TestInstall()
    {
        var stage = "started";
        try
        {
            TestInstallMarker(stage);
            var packagePath = RequiredTestPath("S9_T07_TEST_PACKAGE"); TestInstallMarker(stage = "package-path"); var manifest = File.ReadAllBytes(RequiredTestPath("S9_T07_TEST_MANIFEST")); var signature = File.ReadAllBytes(RequiredTestPath("S9_T07_TEST_SIGNATURE"));
            var key = Convert.FromBase64String(Environment.GetEnvironmentVariable("S9_T07_TEST_PUBLIC_KEY") ?? throw new InvalidDataException());
            using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(key, out _); TestInstallMarker(stage = "key-imported"); var version = new Version(99, 0, 0); var target = StaticMigrations().Concat(["20260905120000_S9T07Fixture10"]).ToArray();
            var verified = new VerifiedUpdatePackage(Path.GetDirectoryName(packagePath)!, packagePath, version, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant(), target, manifest, signature, new CheckedRelease(version, 1, "v99.0.0", []), 2, new Version(1, 0, 5), new Version(1, 0, 5), target[0], target[^2]);
            TestInstallMarker("verified-input"); var result = await InstallPreparedUpdateAsync(verified, new SignedUpdatePackageDownloader(options: new UpdatePackageOptions(rsa.ExportParameters(false), CacheRoot: Path.GetDirectoryName(packagePath)!)), () => { });
            if (result.Outcome != UpdatePackageOutcome.Verified) { Shutdown(1); return; }
            TestInstallMarker("install-returned-" + result.Outcome);
        }
        catch (Exception exception) { TestInstallMarker("error-" + stage + "-" + exception.GetType().Name + "-" + exception.Message); Shutdown(1); }
    }

    private static void TestInstallMarker(string value)
    {
        var marker = Environment.GetEnvironmentVariable("S9_T07_TEST_INSTALL_MARKER");
        if (!string.IsNullOrWhiteSpace(marker)) File.WriteAllText(marker, value);
    }

    private static void PreReleaseTestMarker(string value) =>
        File.WriteAllText(RequiredTestPath("S11_PRE_RELEASE_MARKER"), value);
    private static void DesktopRuntimeMarker(string value)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("S14_T01_DESKTOP_RUNTIME_MARKER")))
            File.AppendAllText(
                RequiredTestPath("S14_T01_DESKTOP_RUNTIME_MARKER"),
                value + Environment.NewLine);
    }


    private static string RequiredTestPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidDataException();
        path = Path.GetFullPath(path);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = File.Exists(path) ? new FileInfo(path).Directory : new DirectoryInfo(path);
        while (current is not null && !string.Equals(current.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase)) current = current.Parent;
        if (current is null || !Guid.TryParse(current.Name, out _) || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        for (var entry = current; entry is not null && !string.Equals(entry.FullName, temp, StringComparison.OrdinalIgnoreCase); entry = entry.Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        return path;
    }
#endif

    private void ReminderTimeChanged(int reminderMinuteOfDay) =>
        _reminderScheduler?.Reschedule(reminderMinuteOfDay);

    private void StopRuntime()
    {
        _reminderScheduler?.Dispose();
        _reminderScheduler = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private async Task<bool> BeginDatabaseMaintenanceAsync()
    {
        if (_databaseMaintenanceLease is not null ||
            MainWindow?.DataContext is not ShellViewModel shell)
        {
            return false;
        }

        MainWindow.IsEnabled = false;
        // Draft autosave must settle before the gate starts rejecting database
        // workers. This preserves the existing Stage 4 save contract.
        if (!await shell.Detail.WaitForStableSaveAsync())
        {
            MainWindow.IsEnabled = true;
            return false;
        }

        var wasSchedulerRunning = _reminderScheduler?.IsRunning == true;
        _reminderScheduler?.Stop();
        try
        {
            var lease = await DatabaseRuntimeGate.EnterMaintenanceAsync();
            if (lease is null)
            {
                if (wasSchedulerRunning && !_explicitExit)
                {
                    _reminderScheduler?.Start();
                }
                MainWindow.IsEnabled = true;

                return false;
            }

            _databaseMaintenanceLease = lease;
            return true;
        }
        catch
        {
            if (wasSchedulerRunning && !_explicitExit)
            {
                _reminderScheduler?.Start();
            }
            MainWindow.IsEnabled = true;

            throw;
        }
    }

    private void EndDatabaseMaintenance(bool resumeScheduler)
    {
        var lease = _databaseMaintenanceLease;
        _databaseMaintenanceLease = null;
        lease?.Dispose();
        if (resumeScheduler && !_explicitExit)
        {
            _reminderScheduler?.Start();
        }
        if (!_explicitExit && MainWindow is not null) MainWindow.IsEnabled = true;
    }
}
