using System.Diagnostics;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S13T01SameSchemaNormalLaunchTests
{
    [Theory]
    [InlineData(800, 5000, 0, 10)]
    [InlineData(5000, 2000, 1, 16)]
    public async Task CommittedRequiresTrustedNormalLoaded(int loadedDelayMs, int waitMs, int exitCode, int finalPhase)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var operation = Guid.NewGuid().ToString();
        var install = Path.Combine(root, "install");
        var app = Path.Combine(install, "app");
        var staging = Path.Combine(install, "app.staging-" + operation);
        var old = Path.Combine(install, "app.old-" + operation);
        CopyDirectory(Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"), app);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(old);
        var operationRoot = Path.Combine(data, "updates", operation);
        Directory.CreateDirectory(operationRoot);
        using var verifier = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!;
        var candidatePid = verifier.Id;
        var candidateStarted = verifier.StartTime.ToUniversalTime();
        await verifier.WaitForExitAsync();
        var now = DateTimeOffset.UtcNow;
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.4", "1.0.5", 0, now, UpdatePhase.Committed, TreeFingerprint.Create(old), TreeFingerprint.Create(app), now, now, candidatePid, candidateStarted);
        var journalPath = Path.Combine(operationRoot, "journal.json");
        await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal));
        await File.WriteAllTextAsync(Path.Combine(operationRoot, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, version = "1.0.5", pid = candidatePid, startedUtc = candidateStarted, migrationCount = 9, lastMigration = "20260901155124_AddPolicyAndBaselineFoundation", integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        var identified = Path.Combine(operationRoot, "identified.marker");
        var record = Path.Combine(operationRoot, "normal-process.txt");
        try
        {
            var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe");
            var info = new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath } };
            info.Environment["S9_T07_NORMAL_WAIT_MS"] = waitMs.ToString();
            info.Environment["S9_T07_NORMAL_DELAY_LOADED_MS"] = loadedDelayMs.ToString();
            info.Environment["S9_T07_NORMAL_IDENTIFIED_MARKER"] = identified;
            info.Environment["S9_T07_NORMAL_PROCESS_RECORD"] = record;
            using var process = Process.Start(info)!;
            await WaitForFile(identified, TimeSpan.FromSeconds(10));
            using (var observed = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath))) Assert.Equal((int)UpdatePhase.Committed, observed.RootElement.GetProperty("Phase").GetInt32());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(exitCode, process.ExitCode);
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath));
            Assert.Equal(finalPhase, final.RootElement.GetProperty("Phase").GetInt32());
            var intent = NormalLaunchHandshake.Read(data, operation);
            Assert.Equal(NormalLaunchRole.Candidate, intent.Role);
            Assert.Equal(TreeFingerprint.Create(app).Hash, intent.ExpectedTreeHash);
            Assert.Equal(-1, intent.ExpectedSchemaPhase);
            if (finalPhase == (int)UpdatePhase.Completed)
            {
                Assert.Equal(NormalLaunchState.Loaded, intent.State);
                var executable = Path.Combine(app, "StoreExpiryInspector.exe");
                Assert.True(NormalLaunchHandshake.IsLive(intent, executable));
                Assert.False(NormalLaunchHandshake.IsLive(intent with { Pid = int.MaxValue }, executable));
                Assert.False(NormalLaunchHandshake.IsLive(intent with { StartedUtc = intent.StartedUtc!.Value.AddMinutes(-1) }, executable));
                var wrongTree = intent with { ExpectedTreeHash = new string(intent.ExpectedTreeHash[0] == 'A' ? 'B' : 'A', 64), State = NormalLaunchState.Pending, Pid = 0, StartedUtc = null };
                NormalLaunchHandshake.Write(data, wrongTree);
                Assert.Throws<InvalidDataException>(() => NormalLaunchHandshake.Identify(data, operation, wrongTree.LaunchToken, app));
            }
            else Assert.False(NormalLaunchHandshake.IsLive(intent, Path.Combine(app, "StoreExpiryInspector.exe")));
        }
        finally
        {
            StopRecorded(record, Path.Combine(app, "StoreExpiryInspector.exe"));
            DeleteDirectory(data);
            DeleteDirectory(root);
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static async Task WaitForFile(string path, TimeSpan timeout)
    {
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(50))
            if (File.Exists(path)) return;
        Assert.True(File.Exists(path), path);
    }

    private static void StopRecorded(string record, string executable)
    {
        if (!File.Exists(record)) return;
        var values = File.ReadAllText(record).Split('|');
        if (values.Length != 2 || !int.TryParse(values[0], out var pid) || !DateTimeOffset.TryParse(values[1], out var started)) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1 && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
        catch (ArgumentException) { }
    }

    private static void DeleteDirectory(string path)
    {
        for (var until = DateTime.UtcNow.AddSeconds(5); Directory.Exists(path) && DateTime.UtcNow < until; Thread.Sleep(50))
            try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
