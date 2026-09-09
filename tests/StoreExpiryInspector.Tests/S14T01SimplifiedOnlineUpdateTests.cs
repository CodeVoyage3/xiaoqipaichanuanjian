using System.Diagnostics;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S14T01SimplifiedOnlineUpdateTests
{
    [Fact]
    public void DesktopRuntimeKeepsMainTrayUpdateAndReminderInFrozenOrderAndFailureDomains()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var project = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "StoreExpiryInspector.csproj"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml.cs"));
        var dialogs = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "WpfDialogService.cs"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "StoreExpiryInspector.iss"));
        var release = File.ReadAllText(Path.Combine(root, "tests", "S9T06-BuildRelease.ps1"));

        Assert.True(app.IndexOf("MainWindow.Show();", StringComparison.Ordinal) < app.IndexOf("InitializeDesktopRuntime,", StringComparison.Ordinal));
        var desktop = app[app.IndexOf("private void InitializeDesktopRuntime", StringComparison.Ordinal)..app.IndexOf("private void StartUpdateCheck", StringComparison.Ordinal)];
        Assert.True(desktop.IndexOf("InitializeTray(mainWindow", StringComparison.Ordinal) < desktop.IndexOf("StartUpdateCheck(mainWindow)", StringComparison.Ordinal));
        Assert.True(desktop.IndexOf("StartUpdateCheck(mainWindow)", StringComparison.Ordinal) < desktop.IndexOf("InitializeReminderScheduler(mainWindow", StringComparison.Ordinal));
        var reminder = desktop[desktop.IndexOf("private void InitializeReminderScheduler", StringComparison.Ordinal)..];
        Assert.DoesNotContain("_trayIcon", reminder, StringComparison.Ordinal);
        Assert.DoesNotContain("ShutdownMode", reminder, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Visibility = Visibility.Collapsed", dialogs, StringComparison.Ordinal);
        Assert.Contains("当前版本暂时可以继续使用，请尽快完成升级。旧版本后续可能停止支持，届时可能无法继续使用软件。", dialogs, StringComparison.Ordinal);
        Assert.DoesNotContain("CompareText(DisplayVersion + '.0', AppVersion)", installer, StringComparison.Ordinal);
        Assert.Contains("GetVersionNumbersString(AddBackslash(ExistingInstallRoot) + 'app\\StoreExpiryInspector.exe', AppVersion)", installer, StringComparison.Ordinal);
        Assert.Contains("<Version>1.0.6</Version>", project, StringComparison.Ordinal);
        Assert.Contains("Version=$(Version)", project, StringComparison.Ordinal);
        Assert.Contains("$sourceMinVersion = '1.0.4'", release, StringComparison.Ordinal);
        Assert.Contains("$sourceMaxVersion = '1.0.5'", release, StringComparison.Ordinal);
        Assert.Contains("$version -ne '1.0.6'", release, StringComparison.Ordinal);
        Assert.Contains("Updater FileVersion does not match the candidate version", release, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyV104CommittedJournalWithoutNormalHandshakeDoesNotInterceptOrdinaryAppStartup()
    {
        var fixture = CreateJournal(phase: 9);
        try
        {
            Assert.False(PendingUpdateRecovery.TryResume(fixture.Root));
            Assert.False(File.Exists(Path.Combine(fixture.OperationDirectory, "normal-launch.json")));
        }
        finally { Delete(fixture.Root); }
    }

    [Fact]
    public void NewSameSchemaCommittedHandshakeIsRecoverableAndCompletedResidueIsTerminal()
    {
        var fixture = CreateJournal(phase: 9);
        try
        {
            var token = Guid.NewGuid().ToString();
            NormalLaunchHandshake.Write(fixture.Root, new NormalLaunchIntent(fixture.OperationId, token, NormalLaunchRole.Candidate, fixture.TreeHash, 9, -1, NormalLaunchState.Pending, 0, null, DateTimeOffset.UtcNow));
            Assert.True(NormalLaunchHandshake.IsBoundSameSchemaIntent(fixture.Root, fixture.OperationId, fixture.AppPath));

            var updaterDirectory = Path.Combine(fixture.OperationDirectory, "updater");
            Directory.CreateDirectory(updaterDirectory);
            File.Copy(Environment.GetEnvironmentVariable("ComSpec")!, Path.Combine(updaterDirectory, "StoreExpiryInspector.Updater.exe"));
            Assert.True(PendingUpdateRecovery.TryResume(fixture.Root));

            WriteJournal(fixture, phase: 10);
            Assert.True(NormalLaunchHandshake.IsBoundSameSchemaIntent(fixture.Root, fixture.OperationId, fixture.AppPath));
            Assert.False(PendingUpdateRecovery.TryResume(fixture.Root));
        }
        finally { Thread.Sleep(100); Delete(fixture.Root); }
    }

    [Fact]
    public async Task NewUpdaterCompletesSameSchemaOnlyAfterNormalAppLoadedHandshake()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var operationId = Guid.NewGuid().ToString();
        var installRoot = Path.Combine(root, "install");
        var appPath = Path.Combine(installRoot, "app");
        var dataRoot = root;
        var operationDirectory = Path.Combine(root, "updates", operationId);
        var recordPath = Path.Combine(root, "normal-process.txt");
        var previousRecord = Environment.GetEnvironmentVariable("S9_T07_NORMAL_PROCESS_RECORD");
        Process? normalProcess = null;
        try
        {
            CopyDirectory(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "bin", "Release", "net10.0-windows", "s9t07test", "net10.0-windows"), appPath);
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(operationDirectory);
            DatabaseInitializer.Initialize(Path.Combine(dataRoot, "data", "app.db"));

            using var verifier = Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!, "/c exit 0") { UseShellExecute = false })!;
            var candidatePid = verifier.Id;
            DateTimeOffset candidateStartedUtc = verifier.StartTime.ToUniversalTime();
            await verifier.WaitForExitAsync();

            var tree = TreeFingerprint.Create(appPath);
            var now = DateTimeOffset.UtcNow;
            var journalPath = Path.Combine(operationDirectory, "journal.json");
            File.WriteAllText(journalPath, JsonSerializer.Serialize(new UpdateJournal(
                operationId,
                "StoreExpiryInspector",
                installRoot,
                dataRoot,
                appPath,
                Path.Combine(installRoot, $"app.staging-{operationId}"),
                Path.Combine(installRoot, $"app.old-{operationId}"),
                new string('A', 64),
                "1.0.4",
                "1.0.5",
                0,
                now,
                UpdatePhase.Committed,
                tree,
                tree,
                now,
                now,
                candidatePid,
                candidateStartedUtc)));
            File.WriteAllText(Path.Combine(operationDirectory, "health-ack.json"), JsonSerializer.Serialize(new
            {
                operationId,
                version = "1.0.5",
                pid = candidatePid,
                startedUtc = candidateStartedUtc,
                migrationCount = 9,
                lastMigration = "20260901155124_AddPolicyAndBaselineFoundation",
                integrity = "ok",
                foreignKeys = "ok",
                coreRead = true,
                uiLoaded = true
            }));

            Environment.SetEnvironmentVariable("S9_T07_NORMAL_PROCESS_RECORD", recordPath);
            var result = await UpdateTransaction.ResumeAsync(journalPath);
            var completedJournal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(journalPath))!;
            Assert.True(result == 0, completedJournal.LastError);
            Assert.Equal(UpdatePhase.Completed, completedJournal.Phase);
            Assert.False(File.Exists(Path.Combine(operationDirectory, "normal-launch.json")));

            var identity = File.ReadAllText(recordPath).Split('|');
            normalProcess = Process.GetProcessById(int.Parse(identity[0]));
            Assert.False(normalProcess.HasExited);
        }
        finally
        {
            Environment.SetEnvironmentVariable("S9_T07_NORMAL_PROCESS_RECORD", previousRecord);
            if (normalProcess is { HasExited: false })
            {
                normalProcess.Kill(true);
                await normalProcess.WaitForExitAsync();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Delete(root);
        }
    }

    [Fact]
    public async Task LegacyV104OrdinaryLaunchContractShowsMainAndTrayWithoutNormalLaunchToken()
    {
        await AssertOrdinaryRuntime(null, "tray-ready", "reminder-ready", trayKeepsProcessAlive: true);
    }

    [Theory]
    [InlineData("tray", "tray-failed", "reminder-ready", "tray_icon_creation_failed", false)]
    [InlineData("reminder", "tray-ready", "reminder-failed", "daily_reminder_runtime_failed", true)]
    public async Task TrayAndReminderRuntimeFailuresStayIndependent(
        string failure,
        string firstState,
        string secondState,
        string logEvent,
        bool trayKeepsProcessAlive) =>
        await AssertOrdinaryRuntime(failure, firstState, secondState, trayKeepsProcessAlive, logEvent);

    private static JournalFixture CreateJournal(int phase)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var operationId = Guid.NewGuid().ToString();
        var operation = Path.Combine(root, "updates", operationId);
        var app = Path.Combine(root, "install", "app");
        Directory.CreateDirectory(operation);
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "StoreExpiryInspector.exe"), "synthetic candidate");
        var treeHash = NormalLaunchHandshake.TreeHash(app);
        var fixture = new JournalFixture(root, operationId, operation, app, treeHash);
        WriteJournal(fixture, phase);
        return fixture;
    }

    private static void WriteJournal(JournalFixture fixture, int phase)
    {
        var now = DateTimeOffset.UtcNow;
        var tree = new { Files = Array.Empty<string>(), Hash = fixture.TreeHash };
        File.WriteAllText(Path.Combine(fixture.OperationDirectory, "journal.json"), JsonSerializer.Serialize(new
        {
            OperationId = fixture.OperationId,
            ProductId = "StoreExpiryInspector",
            InstallRoot = Path.Combine(fixture.Root, "install"),
            DataRoot = fixture.Root,
            AppPath = fixture.AppPath,
            StagingPath = Path.Combine(fixture.Root, "staging"),
            OldPath = Path.Combine(fixture.Root, "old"),
            PackageSha256 = new string('A', 64),
            SourceVersion = "1.0.4",
            TargetVersion = "1.0.5",
            ParentPid = 0,
            ParentStartedUtc = now,
            Phase = phase,
            OldTree = tree,
            CandidateTree = tree,
            CreatedUtc = now,
            UpdatedUtc = now,
            CandidatePid = 0,
            CandidateStartedUtc = (DateTimeOffset?)null,
            LastError = (string?)null,
            Schema = (object?)null
        }));
    }

    private static async Task AssertOrdinaryRuntime(
        string? failure,
        string firstState,
        string secondState,
        bool trayKeepsProcessAlive,
        string? logEvent = null)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var appPath = Path.Combine(installRoot, "app");
        var marker = Path.Combine(installRoot, "desktop-runtime.marker");
        Process? process = null;
        try
        {
            CopyDirectory(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "bin", "Release", "net10.0-windows", "s9t07test", "net10.0-windows"), appPath);
            var operationId = Guid.NewGuid().ToString();
            var start = new ProcessStartInfo(Path.Combine(appPath, "StoreExpiryInspector.exe")) { UseShellExecute = false };
            start.ArgumentList.Add("--data-root");
            start.ArgumentList.Add(dataRoot);
            start.ArgumentList.Add("--allow-existing-isolated-data-root");
            start.Environment["S9_T05_NORMAL_LAUNCH"] = "1";
            start.Environment["S9_T05_OPERATION_ID"] = operationId;
            start.Environment["S9_T07_NORMAL_OPERATION"] = operationId;
            start.Environment["S14_T01_DESKTOP_RUNTIME_MARKER"] = marker;
            if (failure is not null) start.Environment["S14_T01_FAIL_" + failure.ToUpperInvariant()] = "1";
            Assert.DoesNotContain(start.ArgumentList, argument => argument == "--s9-t07-normal-launch");

            process = Process.Start(start)!;
            await WaitForMainWindow(process, TimeSpan.FromSeconds(30));
            await WaitForText(marker, firstState, TimeSpan.FromSeconds(10));
            await WaitForText(marker, secondState, TimeSpan.FromSeconds(10));
            Assert.NotEqual(IntPtr.Zero, process.MainWindowHandle);
            Assert.False(File.Exists(Path.Combine(dataRoot, "updates", operationId, "normal-launch.json")));
            if (logEvent is not null) await WaitForLog(dataRoot, logEvent, TimeSpan.FromSeconds(10));

            Assert.True(process.CloseMainWindow());
            if (trayKeepsProcessAlive)
            {
                await Task.Delay(1000);
                process.Refresh();
                Assert.False(process.HasExited);
            }
            else
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.HasExited);
            }
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync();
                    }
                }
                finally { process.Dispose(); }
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Delete(dataRoot);
            Delete(installRoot);
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private static void Delete(string root)
    {
        if (!Directory.Exists(root)) return;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { Directory.Delete(root, true); return; }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) when (attempt < 19) { Thread.Sleep(50); }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Assert.True(Directory.Exists(source), $"Missing test app output: {source}");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static async Task WaitForMainWindow(Process process, TimeSpan timeout)
    {
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(100))
        {
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException($"Candidate exited before showing its main window: {process.ExitCode}");
            if (process.MainWindowHandle != IntPtr.Zero) return;
        }
        throw new TimeoutException("Candidate main window was not shown.");
    }

    private static async Task WaitForText(string path, string expected, TimeSpan timeout)
    {
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(100))
            if (File.Exists(path) && File.ReadAllText(path).Contains(expected, StringComparison.Ordinal)) return;
        throw new TimeoutException($"Runtime marker did not contain {expected}.");
    }

    private static async Task WaitForLog(string dataRoot, string expected, TimeSpan timeout)
    {
        var logRoot = Path.Combine(dataRoot, "logs");
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(100))
        {
            if (!Directory.Exists(logRoot)) continue;
            foreach (var file in Directory.EnumerateFiles(logRoot, "app-*.log"))
                if (File.ReadAllText(file).Contains(expected, StringComparison.Ordinal)) return;
        }
        throw new TimeoutException($"Runtime log did not contain {expected}.");
    }

    private sealed record JournalFixture(string Root, string OperationId, string OperationDirectory, string AppPath, string TreeHash);
}
