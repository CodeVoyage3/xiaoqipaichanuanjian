using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UpdateSafety;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S24T01CandidateTransactionTests
{
    [Fact]
    public async Task Signed_v113_payload_preserves_v112_data_and_completes_normal_launch()
    {
        // Like the existing pre-release harness, this needs explicit local assets.
        if (Environment.GetEnvironmentVariable("S24_T01_TRANSACTION_MODE") is null) return;
        Assert.Equal("PRODUCTION_PAYLOAD_TEST_UPDATER_TEMP_GUID", Required("S24_T01_TRANSACTION_MODE"));
        var candidate = RequiredPath("S24_T01_CANDIDATE_RUN");
        var source = RequiredPath("S24_T01_SOURCE_PUBLISH");
        var sample = RequiredPath("S24_T01_SAMPLE_DATABASE");
        var evidence = RequiredPath("S24_T01_TRANSACTION_EVIDENCE");
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(candidate, "release-receipt.json")));
        Assert.Equal("e21792f83a4217364aebaf4460f9d407e2f9835e", receipt.RootElement.GetProperty("candidateSha").GetString());
        Assert.Equal(new Version(1, 1, 2, 0), AssemblyName.GetAssemblyName(Path.Combine(source, "StoreExpiryInspector.dll")).Version);
        var install = NewRoot(); var data = NewRoot(); var cache = NewRoot();
        var app = Path.Combine(install, "app"); CopyTree(source, app);
        var database = Path.Combine(data, "data", "app.db"); Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        using (var input = new SqliteConnection($"Data Source={sample};Mode=ReadOnly;Pooling=False"))
        using (var output = new SqliteConnection($"Data Source={database};Pooling=False"))
        { input.Open(); output.Open(); input.BackupDatabase(output); }
        Assert.Equal(CurrentSchemaIdentity.Migrations, UpgradeHealthAck.VerifyDatabase(database, true));
        var before = S8T03ImportPerformanceTests.BusinessFingerprint(database);
        var assets = Path.Combine(candidate, "assets");
        var packagePath = Path.Combine(cache, "StoreExpiryInspector-1.1.3-win-x64.zip");
        File.Copy(Path.Combine(assets, Path.GetFileName(packagePath)), packagePath);
        var package = new VerifiedUpdatePackage(cache, packagePath, new Version(1, 1, 3),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant(),
            CurrentSchemaIdentity.Migrations, File.ReadAllBytes(Path.Combine(assets, "update-manifest.json")),
            File.ReadAllBytes(Path.Combine(assets, "update-manifest.sig")), new(new Version(1, 1, 3), 0, "v1.1.3", []),
            2, new Version(1, 1, 2), new Version(1, 1, 2), CurrentSchemaIdentity.LastMigration, CurrentSchemaIdentity.LastMigration);
        var downloader = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options);
        Assert.Equal(UpdatePackageOutcome.Verified, downloader.RevalidateForInstall(package, CancellationToken.None).Outcome);
        using var bootstrap = Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!, "/c ping 127.0.0.1 -n 120 > nul") { UseShellExecute = false, CreateNoWindow = true })!;
        var bootstrapStarted = bootstrap.StartTime.ToUniversalTime();
        PreparedUpdateInstallation? prepared = null;
        var normalRecord = Path.Combine(data, "normal-process.txt");
        Process? updater = null;
        try
        {
            // Exercise the installer core with an explicit TEMP updater source; the public wrapper uses the host assembly directory.
            prepared = (PreparedUpdateInstallation)typeof(UpdateInstallationPreparer).GetMethod("PrepareCore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new UpdateInstallationPreparer(downloader), [package, bootstrap, install, data, Path.Combine(app, "Updater"), true, CancellationToken.None, "1.1.2", CurrentSchemaIdentity.Migrations, true, true])!;
            bootstrap.Kill(true); await bootstrap.WaitForExitAsync();
            // Only the transaction adapter can authorize TEMP roots. Signed App and
            // candidate ZIP remain production bytes; this is not a formal-root run.
            var testUpdater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64");
            CopyTree(testUpdater, Path.GetDirectoryName(prepared.UpdaterPath)!);
            updater = Process.Start(new ProcessStartInfo(prepared.UpdaterPath) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--journal", prepared.JournalPath }, Environment = { ["S9_T07_NORMAL_PROCESS_RECORD"] = normalRecord } })!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await updater.WaitForExitAsync(timeout.Token); Assert.Equal(0, updater.ExitCode);
            using var final = JsonDocument.Parse(File.ReadAllText(prepared.JournalPath));
            Assert.Equal("1.1.2", final.RootElement.GetProperty("SourceVersion").GetString());
            Assert.Equal("1.1.3", final.RootElement.GetProperty("TargetVersion").GetString());
            Assert.Equal((int)UpdatePhase.Completed, final.RootElement.GetProperty("Phase").GetInt32());
            Assert.Equal(JsonValueKind.Null, final.RootElement.GetProperty("Schema").ValueKind);
            var operation = Path.GetDirectoryName(prepared.JournalPath)!;
            using var ack = JsonDocument.Parse(File.ReadAllText(Path.Combine(operation, "health-ack.json")));
            Assert.Equal("1.1.3", ack.RootElement.GetProperty("version").GetString());
            Assert.True(ack.RootElement.GetProperty("uiLoaded").GetBoolean());
            Assert.Equal(10, ack.RootElement.GetProperty("migrationCount").GetInt32());
            // The protocol deletes the same-schema intent only after observing
            // Loaded and persisting Completed. Do not race its atomic writes.
            Assert.False(File.Exists(NormalLaunchHandshake.PathFor(data, prepared.OperationId)));
            using var normal = RecordedNormal(normalRecord, Path.Combine(app, "StoreExpiryInspector.exe"));
            Assert.False(normal.HasExited);
            Assert.NotEqual(IntPtr.Zero, normal.MainWindowHandle);
            Assert.Equal(CurrentSchemaIdentity.Migrations, UpgradeHealthAck.VerifyDatabase(database, true));
            Assert.Equal(before, S8T03ImportPerformanceTests.BusinessFingerprint(database));
            Assert.Equal(new Version(1, 1, 3, 0), AssemblyName.GetAssemblyName(Path.Combine(app, "StoreExpiryInspector.dll")).Version);
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { status = "PASS", evidenceKind = "PRODUCTION_PAYLOAD_TEST_UPDATER_TEMP_GUID", productSource = receipt.RootElement.GetProperty("candidateSha").GetString(), install, data, journal = prepared.JournalPath, source = "1.1.2", target = "1.1.3", migrationCount = 10, integrity = "ok", foreignKeys = 0, businessFingerprint = before, normalLaunch = "Loaded", schema = "UNCHANGED" }));
        }
        finally
        {
            if (!bootstrap.HasExited && bootstrap.StartTime.ToUniversalTime() == bootstrapStarted) { bootstrap.Kill(true); bootstrap.WaitForExit(5000); }
            if (updater is not null) { if (!updater.HasExited) { updater.Kill(true); updater.WaitForExit(5000); } updater.Dispose(); }
            if (File.Exists(normalRecord))
            {
                try { using var normal = RecordedNormal(normalRecord, Path.Combine(app, "StoreExpiryInspector.exe")); if (!normal.HasExited) { normal.Kill(true); normal.WaitForExit(5000); } }
                catch (ArgumentException) { }
            }
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
    private static string RequiredPath(string name)
    {
        var path = Path.GetFullPath(Required(name)); var relative = Path.GetRelativePath(Path.GetTempPath(), path);
        Assert.False(Path.IsPathRooted(relative)); Assert.True(Guid.TryParse(relative.Split(Path.DirectorySeparatorChar)[0], out _));
        for (var directory = new DirectoryInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            Assert.Equal(0, (int)(directory.Attributes & FileAttributes.ReparsePoint));
        if (File.Exists(path)) Assert.Equal(0, (int)(File.GetAttributes(path) & FileAttributes.ReparsePoint));
        return path;
    }
    private static Process RecordedNormal(string record, string executable)
    {
        var parts = File.ReadAllText(record).Split('|'); Assert.Equal(2, parts.Length);
        var process = Process.GetProcessById(int.Parse(parts[0]));
        try { Assert.True(Math.Abs((process.StartTime.ToUniversalTime() - DateTimeOffset.Parse(parts[1]).UtcDateTime).TotalSeconds) <= 1); Assert.Equal(Path.GetFullPath(executable), Path.GetFullPath(process.MainModule!.FileName), ignoreCase: true); return process; }
        catch { process.Dispose(); throw; }
    }
    private static string NewRoot() { var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(path); return path; }
    private static string FindRoot() { var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreExpiryInspector.slnx"))) root = root.Parent; return root?.FullName ?? throw new DirectoryNotFoundException(); }
    private static void CopyTree(string source, string target) { Directory.CreateDirectory(target); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var destination = Path.Combine(target, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true); } }
}
