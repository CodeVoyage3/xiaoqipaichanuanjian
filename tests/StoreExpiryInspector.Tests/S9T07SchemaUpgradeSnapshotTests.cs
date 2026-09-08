using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T07SchemaUpgradeSnapshotTests
{
    [Fact]
    public void SchemaVerificationConnectionKeepsSourceImmutableAndCandidateWalCapable()
    {
        var factory = typeof(StoreExpiryInspector.App).GetMethod("VerificationConnectionString", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(factory);
        var database = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        var source = Assert.IsType<string>(factory!.Invoke(null, [database, false]));
        var candidate = Assert.IsType<string>(factory.Invoke(null, [database, true]));
        Assert.Contains("immutable=1", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("immutable=1", candidate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mode=ReadOnly", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mode=ReadOnly", candidate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaProcessIdentityRejectsUnrelatedExecutableBeforeAuthorizationWithoutKillingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var app = Path.Combine(root, "app"); Directory.CreateDirectory(app);
        var expected = Path.Combine(app, "StoreExpiryInspector.exe"); File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), expected);
        var now = DateTimeOffset.UtcNow; var journal = new UpdateJournal(Guid.NewGuid().ToString(), "StoreExpiryInspector", root, root, app, Path.Combine(root, "staging"), Path.Combine(root, "old"), new string('a', 64), "1.0.2", "1.0.3", 0, now, UpdatePhase.CandidateStarted, new TreeFingerprint([], "old"), new TreeFingerprint([], "candidate"), now, now);
        using var unrelated = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ping 127.0.0.1 -n 30 > nul") { UseShellExecute = false })!; Process? matching = null; var unrelatedIdentity = new SchemaCandidateIdentity(journal.OperationId, Guid.NewGuid().ToString(), unrelated.Id, unrelated.StartTime.ToUniversalTime(), []);
        try
        {
            Assert.Throws<InvalidDataException>(() => { using var ignored = UpdateTransaction.RequireProcessIdentity(journal, unrelatedIdentity); });
            Assert.False(unrelated.HasExited);
            matching = Process.Start(new ProcessStartInfo(expected, "/c ping 127.0.0.1 -n 30 > nul") { UseShellExecute = false })!;
            var matchingIdentity = unrelatedIdentity with { Pid = matching.Id, StartedUtc = matching.StartTime.ToUniversalTime() };
            using var identified = UpdateTransaction.RequireProcessIdentity(journal, matchingIdentity);
            Assert.Equal(matching.Id, identified.Id);
        }
        finally
        {
            if (!unrelated.HasExited) { unrelated.Kill(true); unrelated.WaitForExit(5000); }
            if (matching is not null) { try { if (!matching.HasExited) { matching.Kill(true); matching.WaitForExit(5000); } } finally { matching.Dispose(); } }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RealAppMaintenancePreparerAndExternalUpdaterCommitFixture10()
    {
        var install = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var updaterRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var packageRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(install); Directory.CreateDirectory(updaterRoot); Directory.CreateDirectory(packageRoot);
        var root = FindRoot(); var oldSource = Path.Combine(root, "src", "StoreExpiryInspector", "bin", "Release", "net10.0-windows", "s9t07test", "net10.0-windows"); var fixture = Path.Combine(root, "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"); var updaterSource = Path.Combine(root, "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64"); var app = Path.Combine(install, "app"); CopyDirectory(oldSource, app); CopyDirectory(updaterSource, updaterRoot); var oldExe = Path.Combine(app, "StoreExpiryInspector.exe"); var updaterIdentity = Path.Combine(data, "updater-identity.json"); string? operation = null; Process? parent = null; DateTime parentStarted = default;
        try
        {
            parent = Process.Start(new ProcessStartInfo(oldExe) { UseShellExecute = false, WorkingDirectory = install, ArgumentList = { "--data-root", data, "--s9-t01-smoke-exit" } })!; parentStarted = parent.StartTime.ToUniversalTime(); using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30))) await parent.WaitForExitAsync(timeout.Token); Assert.Equal(0, parent.ExitCode); parent.Dispose(); parent = null;
            var database = Path.Combine(data, "data", "app.db"); SeedBlob(database); SeedRollbackHistoryAndSettings(database); SqliteConnection.ClearAllPools(); Execute(database, "PRAGMA journal_mode=DELETE;");
            Assert.Equal(new Version(1, 0, 5, 0), AssemblyName.GetAssemblyName(Path.Combine(app, "StoreExpiryInspector.dll")).Version); Assert.Equal(new Version(1, 0, 4, 0), AssemblyName.GetAssemblyName(Path.Combine(fixture, "StoreExpiryInspector.dll")).Version);
            var zip = Path.Combine(packageRoot, "StoreExpiryInspector-1.0.4-win-x64.zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { AddTree(archive, fixture, ""); AddTree(archive, updaterSource, "Updater"); }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(); var target = ExpectedMigrations.Concat(["20260905120000_S9T07Fixture10"]).ToArray(); var manifest = System.Text.Encoding.UTF8.GetBytes($"{{\"schemaVersion\":1,\"version\":\"1.0.4\",\"releaseTag\":\"v1.0.4\",\"repository\":\"CodeVoyage3/xiaoqipaichanuanjian\",\"channel\":\"stable\",\"rid\":\"win-x64\",\"minimumProtocolVersion\":2,\"package\":{{\"fileName\":\"{Path.GetFileName(zip)}\",\"bytes\":{new FileInfo(zip).Length},\"sha256\":\"{hash}\"}},\"targetMigrations\":[{string.Join(',', target.Select(x => $"\"{x}\""))}],\"source\":{{\"minVersion\":\"1.0.3\",\"maxVersion\":\"1.0.3\",\"minMigration\":\"{target[0]}\",\"maxMigration\":\"{ExpectedMigrations[^1]}\"}}}}"); using var rsa = RSA.Create(2048); var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); var manifestPath = Path.Combine(packageRoot, "update-manifest.json"); var signaturePath = Path.Combine(packageRoot, "update-manifest.sig"); File.WriteAllBytes(manifestPath, manifest); File.WriteAllBytes(signaturePath, signature);
            var marker = Path.Combine(data, "test-install.marker"); using var process = Process.Start(new ProcessStartInfo(oldExe) { UseShellExecute = false, WorkingDirectory = install, ArgumentList = { "--data-root", data, "--allow-existing-isolated-data-root", "--s9-t07-test-install" }, Environment = { ["S9_T07_TEST_INSTALL_ROOT"] = install, ["S9_T07_TEST_UPDATER_ROOT"] = updaterRoot, ["S9_T07_TEST_PACKAGE"] = zip, ["S9_T07_TEST_MANIFEST"] = manifestPath, ["S9_T07_TEST_SIGNATURE"] = signaturePath, ["S9_T07_TEST_PUBLIC_KEY"] = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), ["S9_T07_TEST_INSTALL_MARKER"] = marker, ["S9_T07_TEST_UPDATER_IDENTITY"] = updaterIdentity } })!; using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45))) await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, File.Exists(marker) ? File.ReadAllText(marker) : "test entry did not write a marker"); await WaitForFile(marker, TimeSpan.FromSeconds(5)); await WaitForFile(updaterIdentity, TimeSpan.FromSeconds(5));
            operation = Directory.GetDirectories(Path.Combine(data, "updates")).Single(); await WaitForFile(Path.Combine(operation, "health-ack.json"), TimeSpan.FromSeconds(45)); await WaitForHandshake(data, Path.GetFileName(operation), NormalLaunchState.Loaded, TimeSpan.FromSeconds(45)); await WaitForTerminalJournal(Path.Combine(operation, "journal.json"), TimeSpan.FromSeconds(45)); await WaitForRecordedExit(updaterIdentity, TimeSpan.FromSeconds(45)); using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(operation, "journal.json"))); Assert.Equal("1.0.3", journal.RootElement.GetProperty("SourceVersion").GetString()); Assert.Equal("1.0.4", journal.RootElement.GetProperty("TargetVersion").GetString()); Assert.Equal((int)UpdatePhase.Completed, journal.RootElement.GetProperty("Phase").GetInt32()); Assert.Equal((int)SchemaPhase.CandidateCommitted, journal.RootElement.GetProperty("Schema").GetProperty("Phase").GetInt32()); using var ack = JsonDocument.Parse(File.ReadAllText(Path.Combine(operation, "health-ack.json"))); Assert.Equal("1.0.4", ack.RootElement.GetProperty("version").GetString()); Assert.Equal(target, UpgradeHealthAck.VerifyDatabase(database, true)); using var settings = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); settings.Open(); using var command = settings.CreateCommand(); command.CommandText = "SELECT reminder_minute_of_day FROM settings WHERE id=1; SELECT length(content) FROM import_workbooks WHERE import_id=1; SELECT count(*) FROM lifecycle_events WHERE reason='s9-t07 rollback history';"; using var rows = command.ExecuteReader(); Assert.True(rows.Read()); Assert.Equal(602L, rows.GetInt64(0)); Assert.True(rows.NextResult()); Assert.True(rows.Read()); Assert.Equal(131073L, rows.GetInt64(0)); Assert.True(rows.NextResult()); Assert.True(rows.Read()); Assert.Equal(1L, rows.GetInt64(0));
        }
        finally
        {
            if (parent is not null) { StopExactProcess(parent, parentStarted, oldExe); parent.Dispose(); }
            StopUpdaterIdentity(updaterIdentity, Path.Combine(updaterRoot, "StoreExpiryInspector.Updater.exe"));
            if (operation is not null)
            {
                StopCandidateIdentity(Path.Combine(operation, "candidate-identity.json"), Path.Combine(app, "StoreExpiryInspector.exe"));
                StopNormalLaunch(Path.Combine(operation, "normal-launch.json"), Path.Combine(app, "StoreExpiryInspector.exe"));
                StopRecordedProcess(Path.Combine(operation, "normal-process.txt"), Path.Combine(app, "StoreExpiryInspector.exe"));
            }
        }
    }

    private static void AddTree(ZipArchive archive, string source, string prefix)
    { foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(file => Path.GetExtension(file) is ".dll" or ".json" || Path.GetFileName(file) is "StoreExpiryInspector.exe" or "StoreExpiryInspector.Updater.exe" or "createdump.exe")) archive.CreateEntryFromFile(file, string.IsNullOrEmpty(prefix) ? Path.GetRelativePath(source, file).Replace('\\', '/') : prefix + "/" + Path.GetRelativePath(source, file).Replace('\\', '/')); }
    [Fact]
    public async Task ExternalUpdaterLegacyTerminalAckAccepted()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); var install = Path.Combine(root, "install"); var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); Directory.CreateDirectory(app); Directory.CreateDirectory(staging); Directory.CreateDirectory(old); Directory.CreateDirectory(Path.Combine(data, "updates", operation)); File.WriteAllText(Path.Combine(app, "payload.txt"), "old"); File.WriteAllText(Path.Combine(staging, "payload.txt"), "candidate");
        using var verifier = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var pid = verifier.Id; var started = verifier.StartTime.ToUniversalTime(); await verifier.WaitForExitAsync(); var now = DateTimeOffset.UtcNow; var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.2", 0, now, UpdatePhase.Completed, TreeFingerprint.Create(app), TreeFingerprint.Create(staging), now, now); var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal)); await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, version = "1.0.2", pid, startedUtc = started, migrationCount = 9, lastMigration = ExpectedMigrations[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe"); Assert.True(File.Exists(updater)); using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath } })!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token); Assert.Equal(0, process.ExitCode); using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal((int)UpdatePhase.Completed, final.RootElement.GetProperty("Phase").GetInt32()); Assert.True(!final.RootElement.TryGetProperty("Schema", out var schema) || schema.ValueKind == JsonValueKind.Null); Assert.False(File.Exists(Path.Combine(data, "updates", operation, "normal-launch.json")));
    }

    [Fact]
    public void PendingCompletedSchemaWithMissingCandidateIdentityBlocksStartup()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); var directory = Path.Combine(root, "updates", operation); Directory.CreateDirectory(directory);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var tree = new TreeFingerprint([], new string('A', 64));
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", Path.Combine(root, "install"), root, Path.Combine(root, "install", "app"), Path.Combine(root, "install", "stage"), Path.Combine(root, "install", "old"), new string('A', 64), "1.0.0", "1.0.2", 0, DateTimeOffset.UtcNow, UpdatePhase.Completed, tree, tree, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Schema: new SchemaUpdateJournal(SchemaPhase.CandidateCommitted, snapshot, ExpectedMigrations, [.. ExpectedMigrations, "20260905120000_Test"], Guid.NewGuid().ToString()));
        File.WriteAllText(Path.Combine(directory, "journal.json"), JsonSerializer.Serialize(journal));
        Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
    }

    [Fact]
    public void PendingTerminalSchemaRequiresCompleteBoundEvidence()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); var directory = Path.Combine(root, "updates", operation); Directory.CreateDirectory(directory);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var tree = new TreeFingerprint([], new string('A', 64)); var target = ExpectedMigrations.Concat(["20260905120000_Test"]).ToArray();
        var valid = new SchemaUpdateJournal(SchemaPhase.CandidateCommitted, snapshot, ExpectedMigrations, target, Guid.NewGuid().ToString(), 123, DateTimeOffset.UtcNow);
        AssertPendingTerminal(root, operation, tree, valid, false);
        AssertPendingTerminal(root, operation, tree, valid with { LaunchToken = "not-a-guid" }, true);
        AssertPendingTerminal(root, operation, tree, valid with { CandidatePid = 0 }, true);
        AssertPendingTerminal(root, operation, tree, valid with { SourceMigrations = [] }, true);
        AssertPendingTerminal(root, operation, tree, valid with { Snapshot = snapshot with { OperationId = Guid.NewGuid().ToString() } }, true);
    }

    [Fact]
    public async Task BadSnapshotBytesBlockBeforeFirstTreeMove()
    {
        var root = CreateCleanRoot(); var install = Path.Combine(root, "install"); var operation = Guid.NewGuid().ToString(); var directory = Path.Combine(root, "updates", operation); Directory.CreateDirectory(directory);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); File.AppendAllText(snapshot.SnapshotPath, "tamper");
        var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); Directory.CreateDirectory(app); Directory.CreateDirectory(staging); File.WriteAllText(Path.Combine(app, "old.txt"), "old"); File.WriteAllText(Path.Combine(staging, "new.txt"), "new");
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, root, app, staging, old, new string('A', 64), "1.0.0", "1.0.2", 0, DateTimeOffset.UtcNow.AddMinutes(-1), UpdatePhase.MainExited, TreeFingerprint.Create(app), TreeFingerprint.Create(staging), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Schema: new SchemaUpdateJournal(SchemaPhase.SnapshotVerified, snapshot, ExpectedMigrations, [.. ExpectedMigrations, "20260905120000_Test"], Guid.NewGuid().ToString()));
        var path = Path.Combine(directory, "journal.json"); File.WriteAllText(path, JsonSerializer.Serialize(journal));
        Assert.Equal(1, await UpdateTransaction.ResumeAsync(path)); Assert.True(File.Exists(Path.Combine(app, "old.txt"))); Assert.True(File.Exists(Path.Combine(staging, "new.txt"))); Assert.False(Directory.Exists(old));
        using var final = JsonDocument.Parse(await File.ReadAllTextAsync(path)); Assert.Contains("升级快照", final.RootElement.GetProperty("LastError").GetString());
    }

    [Fact]
    public void RestoreRejectsDuplicateQuarantineKeyAndAcceptsCanonicalStringStage()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); var directory = Path.Combine(root, "updates", operation); Directory.CreateDirectory(directory); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var original = Sha256(database);
        File.WriteAllText(Path.Combine(directory, "schema-restore.json"), $"{{\"OperationId\":\"{operation}\",\"SnapshotSha256\":\"{snapshot.SnapshotSha256}\",\"Stage\":\"None\",\"Quarantined\":{{\"-wal\":\"{new string('A', 64)}\",\"-wal\":\"{new string('A', 64)}\"}},\"MainSha256\":null}}");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); Assert.Equal(original, Sha256(database));
        File.WriteAllText(Path.Combine(directory, "schema-restore.json"), $"{{\"OperationId\":\"{operation}\",\"SnapshotSha256\":\"{snapshot.SnapshotSha256}\",\"Stage\":\"None\",\"Quarantined\":{{}},\"MainSha256\":null}}");
        SchemaUpgradeSnapshots.Restore(root, snapshot); Assert.Equal(snapshot.SnapshotSha256, Sha256(database));
    }
    [Theory]
    [InlineData("{\"OperationId\":\"%OP%\",\"SnapshotSha256\":\"%SHA%\",\"Quarantined\":{},\"MainSha256\":null}")]
    [InlineData("{\"OperationId\":\"%OP%\",\"SnapshotSha256\":\"%SHA%\",\"Stage\":0,\"Stage\":0,\"Quarantined\":{},\"MainSha256\":null}")]
    public void RestoreRejectsAmbiguousOrMissingStageBeforeMovingFiles(string template)
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); var operationRoot = Path.Combine(root, "updates", operation); Directory.CreateDirectory(operationRoot);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var original = Sha256(database);
        File.WriteAllText(Path.Combine(operationRoot, "schema-restore.json"), template.Replace("%OP%", snapshot.OperationId, StringComparison.Ordinal).Replace("%SHA%", snapshot.SnapshotSha256, StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot));
        Assert.Equal(original, Sha256(database));
    }

    [Fact]
    public void DurableProtocolWriteKeepsResidualScratchOutsideCommittedContent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root); var path = Path.Combine(root, "protocol.json");
        DurableFile.Replace(path, "before"); Assert.Equal("before", File.ReadAllText(path));
        File.WriteAllText(path + ".tmp", "interrupted");
        DurableFile.Replace(path, "after"); Assert.Equal("after", File.ReadAllText(path)); Assert.True(Directory.EnumerateFiles(root, "protocol.json.tmp.interrupted-*", SearchOption.TopDirectoryOnly).Any());
    }

    [Fact]
    public async Task ControlledExceptionStillCleansExactChildProcess()
    {
        Process? child = null; DateTimeOffset started = default;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 30 > nul") { UseShellExecute = false })!;
            started = child.StartTime.ToUniversalTime();
            try { throw new InvalidOperationException("controlled verification failure"); }
            finally
            {
                if (!child.HasExited && Math.Abs((child.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1) child.Kill(true);
                await child.WaitForExitAsync();
            }
        });
        Assert.True(child!.HasExited);
        child.Dispose();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public async Task ExternalUpdaterCompletesSchemaFixtureMigration(int target)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SeedBlob(database); var sourceBlobSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Enumerable.Range(0, 131073).Select(value => (byte)(value % 251)).ToArray())); SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        var source = ExpectedMigrations; string[] migrations = target == 10 ? [.. source, "20260905120000_S9T07Fixture10"] : [.. source, "20260905120000_S9T07Fixture10", "20260905121000_S9T07Fixture11"];
        var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.2", source);
        var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation);
        Directory.CreateDirectory(app); File.WriteAllText(Path.Combine(app, "source.txt"), "source"); CopyDirectory(Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"), staging);
        var token = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow;
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var parentPid = parent.Id; var parentStarted = parent.StartTime.ToUniversalTime(); await parent.WaitForExitAsync();
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.2", "1.0.4", parentPid, parentStarted, UpdatePhase.Prepared, TreeFingerprint.Create(app), TreeFingerprint.Create(staging), now, now, Schema: new SchemaUpdateJournal(SchemaPhase.SnapshotVerified, snapshot, source, migrations, token));
        var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal));
        var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe");
        var durablePhases = Path.Combine(data, "updates", operation, "durable-phases.txt");
        var normalRecord = Path.Combine(data, "updates", operation, "normal-process.txt");
        using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T07_DURABLE_PHASE_MARKER"] = durablePhases, ["S9_T07_NORMAL_PROCESS_RECORD"] = normalRecord } })!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token);
        Process? normalProcess = null; DateTimeOffset? normalStarted = null;
        try
        {
        Assert.Equal(0, process.ExitCode);
        var phases = await File.ReadAllLinesAsync(durablePhases); Assert.True(Array.IndexOf(phases, "WaitingForHealthAck/MigrationApplied") < Array.IndexOf(phases, "WaitingForHealthAck/SchemaHealthVerified")); Assert.True(Array.IndexOf(phases, "WaitingForHealthAck/SchemaHealthVerified") < Array.IndexOf(phases, "Committed/CandidateCommitted"));
        using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal((int)UpdatePhase.Completed, final.RootElement.GetProperty("Phase").GetInt32()); Assert.Equal((int)SchemaPhase.CandidateCommitted, final.RootElement.GetProperty("Schema").GetProperty("Phase").GetInt32());
        using var ack = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"))); Assert.Equal(token, ack.RootElement.GetProperty("launchToken").GetString()); Assert.Equal("1.0.4", ack.RootElement.GetProperty("version").GetString()); Assert.True(ack.RootElement.GetProperty("uiLoaded").GetBoolean()); Assert.Equal(migrations, ack.RootElement.GetProperty("migrations").EnumerateArray().Select(x => x.GetString()).ToArray()); Assert.Equal(final.RootElement.GetProperty("CandidatePid").GetInt32(), ack.RootElement.GetProperty("pid").GetInt32()); Assert.Equal(final.RootElement.GetProperty("CandidateStartedUtc").GetDateTimeOffset(), ack.RootElement.GetProperty("startedUtc").GetDateTimeOffset());
        var marker = Path.Combine(data, "updates", operation, "normal-loaded.marker"); for (var until = DateTime.UtcNow.AddSeconds(5); !File.Exists(marker) && DateTime.UtcNow < until; Thread.Sleep(50)) { } Assert.True(File.Exists(marker));
        using var normal = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(data, "updates", operation, "normal-launch.json"))); var normalPid = normal.RootElement.GetProperty("pid").GetInt32(); normalStarted = normal.RootElement.GetProperty("startedUtc").GetDateTimeOffset(); normalProcess = Process.GetProcessById(normalPid); Assert.Equal((int)NormalLaunchState.Loaded, normal.RootElement.GetProperty("state").GetInt32());
        Assert.True(UpgradeHealthAck.VerifyDatabase(database, true).SequenceEqual(migrations, StringComparer.Ordinal));
        using var check = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); check.Open(); using var command = check.CreateCommand(); command.CommandText = "SELECT content FROM import_workbooks WHERE import_id=1; SELECT length(payload),stage FROM s9t07_fixture WHERE id=1;"; using var reader = command.ExecuteReader(); Assert.True(reader.Read()); Assert.Equal(sourceBlobSha, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData((byte[])reader.GetValue(0)))); Assert.True(reader.NextResult()); Assert.True(reader.Read()); Assert.Equal(131073, reader.GetInt32(0)); Assert.Equal(target == 11 ? 11 : 10, reader.GetInt32(1));
        Assert.True(Math.Abs((normalProcess.StartTime.ToUniversalTime() - normalStarted!.Value.UtcDateTime).TotalSeconds) <= 1);
        }
        finally
        {
            if (normalProcess is not null)
            {
                try { if (normalStarted is not null && Math.Abs((normalProcess.StartTime.ToUniversalTime() - normalStarted.Value.UtcDateTime).TotalSeconds) <= 1 && !normalProcess.HasExited) normalProcess.Kill(true); normalProcess.WaitForExit(5000); }
                finally { normalProcess.Dispose(); }
            }
            else if (File.Exists(normalRecord))
            {
                var parts = File.ReadAllText(normalRecord).Split('|');
                if (parts.Length == 2 && int.TryParse(parts[0], out var pid) && DateTimeOffset.TryParse(parts[1], out var started)) try { using var recovered = Process.GetProcessById(pid); if (Math.Abs((recovered.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1 && !recovered.HasExited) { recovered.Kill(true); recovered.WaitForExit(5000); } } catch (ArgumentException) { }
            }
        }
    }

    [Fact]
    public async Task RealProductionOldRollbackRestoresSchema9AndLoadsOldShell()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString();
        var productionPublish = Environment.GetEnvironmentVariable("S9_T07_REAL_OLD_PUBLISH"); Assert.False(string.IsNullOrWhiteSpace(productionPublish)); productionPublish = Path.GetFullPath(productionPublish); Assert.True(Guid.TryParse(Path.GetRelativePath(Path.GetTempPath(), productionPublish), out _)); Assert.True(File.Exists(Path.Combine(productionPublish, "StoreExpiryInspector.exe")));
        var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); CopyDirectory(productionPublish, app); CopyDirectory(Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"), staging);
        Process? seed = Process.Start(new ProcessStartInfo(Path.Combine(app, "StoreExpiryInspector.exe")) { UseShellExecute = false, WorkingDirectory = install, ArgumentList = { "--data-root", data, "--s9-t01-smoke-exit" } })!; var seedStarted = seed.StartTime.ToUniversalTime(); try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await seed.WaitForExitAsync(timeout.Token); Assert.Equal(0, seed.ExitCode); } finally { StopExactProcess(seed, seedStarted, Path.Combine(app, "StoreExpiryInspector.exe")); seed.Dispose(); }
        Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); SeedBlob(database); SeedRollbackHistoryAndSettings(database); SqliteConnection.ClearAllPools(); using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        Assert.Equal(new Version(1, 0, 3, 0), AssemblyName.GetAssemblyName(Path.Combine(app, "StoreExpiryInspector.dll")).Version); Assert.Equal(new Version(1, 0, 4, 0), AssemblyName.GetAssemblyName(Path.Combine(staging, "StoreExpiryInspector.dll")).Version);
        var source = ExpectedMigrations; var target = source.Concat(["20260905120000_S9T07Fixture10"]).ToArray(); var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.3", source); var oldTree = TreeFingerprint.Create(app); var token = Guid.NewGuid().ToString();
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var parentPid = parent.Id; var parentStarted = parent.StartTime.ToUniversalTime(); await parent.WaitForExitAsync();
        var now = DateTimeOffset.UtcNow; var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.3", "1.0.4", parentPid, parentStarted, UpdatePhase.Prepared, oldTree, TreeFingerprint.Create(staging), now, now, Schema: new SchemaUpdateJournal(SchemaPhase.SnapshotVerified, snapshot, source, target, token)); var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal));
        var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe"); Assert.True(File.Exists(updater)); var normalRecord = Path.Combine(data, "updates", operation, "normal-process.txt"); var migrationMarker = Path.Combine(data, "updates", operation, "fixture-migrated.json"); var sourceVerifiedMarker = Path.Combine(data, "updates", operation, "source-verified.json");
        Process? updaterProcess = null; DateTime updaterStarted = default;
        try
        {
            updaterProcess = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, WorkingDirectory = install, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T07_FIXTURE_FAIL_AFTER_MIGRATION"] = Environment.GetEnvironmentVariable("S9_T07_HARD_KILL_SUCCESS") == "1" ? "0" : "1", ["S9_T07_FIXTURE_MIGRATION_MARKER"] = migrationMarker, ["S9_T07_SOURCE_VERIFIED_MARKER"] = sourceVerifiedMarker, ["S9_T07_NORMAL_PROCESS_RECORD"] = normalRecord } })!; updaterStarted = updaterProcess.StartTime.ToUniversalTime(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await updaterProcess.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, updaterProcess.ExitCode); Assert.True(File.Exists(migrationMarker)); using var migration = JsonDocument.Parse(await File.ReadAllTextAsync(migrationMarker)); Assert.Equal(target, migration.RootElement.GetProperty("migrations").EnumerateArray().Select(item => item.GetString()).ToArray()); Assert.True(File.Exists(sourceVerifiedMarker)); Assert.Equal(snapshot.LogicalFingerprint, await File.ReadAllTextAsync(sourceVerifiedMarker)); using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal("1.0.3", final.RootElement.GetProperty("SourceVersion").GetString()); Assert.Equal("1.0.4", final.RootElement.GetProperty("TargetVersion").GetString()); Assert.Equal((int)UpdatePhase.RolledBack, final.RootElement.GetProperty("Phase").GetInt32()); Assert.Equal((int)SchemaPhase.RolledBack, final.RootElement.GetProperty("Schema").GetProperty("Phase").GetInt32()); Assert.Equal(oldTree.Hash, TreeFingerprint.Create(app).Hash); Assert.Equal(source, UpgradeHealthAck.VerifyDatabase(database, true));
            using var ack = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"))); Assert.Equal("1.0.3", ack.RootElement.GetProperty("version").GetString()); Assert.Equal(source, ack.RootElement.GetProperty("migrations").EnumerateArray().Select(item => item.GetString()).ToArray()); Assert.True(ack.RootElement.GetProperty("uiLoaded").GetBoolean());
            using (var facts = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False")) { facts.Open(); using var command = facts.CreateCommand(); command.CommandText = "SELECT reminder_minute_of_day FROM settings WHERE id=1; SELECT length(content) FROM import_workbooks WHERE import_id=1; SELECT count(*) FROM lifecycle_events WHERE reason='s9-t07 rollback history';"; using var rows = command.ExecuteReader(); Assert.True(rows.Read()); Assert.Equal(602L, rows.GetInt64(0)); Assert.True(rows.NextResult()); Assert.True(rows.Read()); Assert.Equal(131073L, rows.GetInt64(0)); Assert.True(rows.NextResult()); Assert.True(rows.Read()); Assert.Equal(1L, rows.GetInt64(0)); }
            Assert.True(File.Exists(normalRecord)); var normal = File.ReadAllText(normalRecord).Split('|'); Assert.Equal(2, normal.Length); Assert.True(int.TryParse(normal[0], out var normalPid)); Assert.True(DateTimeOffset.TryParse(normal[1], out var normalStarted)); using var normalProcess = Process.GetProcessById(normalPid); Assert.False(normalProcess.HasExited); Assert.True(Math.Abs((normalProcess.StartTime.ToUniversalTime() - normalStarted.UtcDateTime).TotalSeconds) <= 1); Assert.Equal(NormalLaunchState.Loaded, NormalLaunchHandshake.Read(data, operation).State);
        }
        finally { if (updaterProcess is not null) { StopExactProcess(updaterProcess, updaterStarted, updater); updaterProcess.Dispose(); } StopCandidateIdentity(Path.Combine(data, "updates", operation, "candidate-identity.json"), Path.Combine(app, "StoreExpiryInspector.exe")); StopRecordedProcess(normalRecord, Path.Combine(app, "StoreExpiryInspector.exe")); }
    }
    [Fact]
    public async Task CandidateCommittedTargetMigrationTamperBlocksNormalLaunchAndPreservesTamperedDatabaseBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        var source = ExpectedMigrations; var target = source.Concat(["20260905120000_S9T07Fixture10"]).ToArray();
        var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.0", source);
        var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation);
        CopyDirectory(Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"), app); Directory.CreateDirectory(staging); Directory.CreateDirectory(old);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE s9t07_fixture (id INTEGER PRIMARY KEY, payload BLOB NOT NULL, stage INTEGER NOT NULL DEFAULT 10); INSERT INTO s9t07_fixture(id,payload) VALUES (1,zeroblob(1)); INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ($migration,'1.0.2');"; command.Parameters.AddWithValue("$migration", target[^1]); command.ExecuteNonQuery(); }
        using var verification = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var verificationPid = verification.Id; var verificationStarted = verification.StartTime.ToUniversalTime(); await verification.WaitForExitAsync();
        var token = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow; var schema = new SchemaUpdateJournal(SchemaPhase.CandidateCommitted, snapshot, source, target, token, verificationPid, verificationStarted);
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.2", 0, now.AddMinutes(-1), UpdatePhase.Committed, TreeFingerprint.Create(old), TreeFingerprint.Create(app), now, now, CandidatePid: verificationPid, CandidateStartedUtc: verificationStarted, Schema: schema);
        var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal));
        await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, launchToken = token, version = "1.0.2", pid = verificationPid, startedUtc = verificationStarted, migrations = target, migrationCount = target.Length, lastMigration = target[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        Execute(database, "DELETE FROM __EFMigrationsHistory WHERE MigrationId='20260905120000_S9T07Fixture10';"); Assert.Equal(source, UpgradeHealthAck.VerifyDatabase(database, true)); Assert.False(UpgradeHealthAck.VerifyDatabase(database, true).SequenceEqual(target, StringComparer.Ordinal)); var targetSha = Sha256(database); var appTree = TreeFingerprint.Create(app); var normalRecord = Path.Combine(data, "updates", operation, "normal-process.txt"); var normalMarker = Path.Combine(data, "updates", operation, "normal-launch.marker");
        var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe"); Assert.True(File.Exists(updater));
        try
        {
            using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T07_NORMAL_PROCESS_RECORD"] = normalRecord, ["S9_T05_NORMAL_LAUNCH_MARKER"] = normalMarker } })!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal(1, process.ExitCode); Assert.Equal((int)UpdatePhase.FailedNeedsManualRecovery, final.RootElement.GetProperty("Phase").GetInt32()); Assert.Equal((int)SchemaPhase.FailedNeedsManualRecovery, final.RootElement.GetProperty("Schema").GetProperty("Phase").GetInt32());
            Assert.False(File.Exists(normalMarker)); Assert.False(File.Exists(normalRecord)); Assert.Equal(targetSha, Sha256(database)); Assert.Equal(appTree.Hash, TreeFingerprint.Create(app).Hash); var manualRecovery = Path.Combine(data, "updates", operation, "manual-recovery.log"); Assert.True(File.Exists(manualRecovery)); Assert.Contains("Normal application database state is invalid.", await File.ReadAllTextAsync(manualRecovery));
            using var check = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); check.Open(); using var table = check.CreateCommand(); table.CommandText = "SELECT count(*) FROM s9t07_fixture;"; Assert.Equal(1L, (long)table.ExecuteScalar()!);
        }
        finally
        {
            if (File.Exists(normalRecord))
            {
                var parts = File.ReadAllText(normalRecord).Split('|');
                if (parts.Length == 2 && int.TryParse(parts[0], out var pid) && DateTimeOffset.TryParse(parts[1], out var started)) try { using var normal = Process.GetProcessById(pid); if (Math.Abs((normal.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1 && !normal.HasExited) { normal.Kill(true); normal.WaitForExit(5000); } } catch (ArgumentException) { }
            }
        }
    }

    [Fact]
    public async Task CandidateNormalLoadTimeoutStopsExactStartedProcess()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools(); using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        var source = ExpectedMigrations; var target = source.Concat(["20260905120000_S9T07Fixture10"]).ToArray(); var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.0", source);
        var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); CopyDirectory(Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"), app); Directory.CreateDirectory(staging); Directory.CreateDirectory(old);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE s9t07_fixture (id INTEGER PRIMARY KEY, payload BLOB NOT NULL, stage INTEGER NOT NULL DEFAULT 10); INSERT INTO s9t07_fixture(id,payload) VALUES (1,zeroblob(1)); INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ($migration,'1.0.2');"; command.Parameters.AddWithValue("$migration", target[^1]); command.ExecuteNonQuery(); }
        using var verification = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var verificationPid = verification.Id; var verificationStarted = verification.StartTime.ToUniversalTime(); await verification.WaitForExitAsync();
        var token = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow; var schema = new SchemaUpdateJournal(SchemaPhase.CandidateCommitted, snapshot, source, target, token, verificationPid, verificationStarted); var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.2", 0, now.AddMinutes(-1), UpdatePhase.Committed, TreeFingerprint.Create(old), TreeFingerprint.Create(app), now, now, CandidatePid: verificationPid, CandidateStartedUtc: verificationStarted, Schema: schema);
        var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal)); await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, launchToken = token, version = "1.0.2", pid = verificationPid, startedUtc = verificationStarted, migrations = target, migrationCount = target.Length, lastMigration = target[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        var record = Path.Combine(data, "updates", operation, "normal-process.txt"); var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T07_NORMAL_PROCESS_RECORD"] = record, ["S9_T07_NORMAL_WAIT_MS"] = "500", ["S9_T07_NORMAL_DELAY_LOADED_MS"] = "2000" } })!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
            Assert.True(File.Exists(record)); var parts = await File.ReadAllTextAsync(record); var values = parts.Split('|'); Assert.Equal(2, values.Length); Assert.True(int.TryParse(values[0], out var pid)); Assert.True(DateTimeOffset.TryParse(values[1], out var started));
            var exactExited = false; try { using var normal = Process.GetProcessById(pid); exactExited = normal.HasExited || Math.Abs((normal.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) > 1; } catch (ArgumentException) { exactExited = true; } Assert.True(exactExited);
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal(1, process.ExitCode); Assert.Equal((int)UpdatePhase.FailedNeedsManualRecovery, final.RootElement.GetProperty("Phase").GetInt32()); Assert.NotEqual((int)UpdatePhase.Completed, final.RootElement.GetProperty("Phase").GetInt32());
        }
        finally
        {
            if (File.Exists(record)) { var values = File.ReadAllText(record).Split('|'); if (values.Length == 2 && int.TryParse(values[0], out var pid) && DateTimeOffset.TryParse(values[1], out var started)) try { using var normal = Process.GetProcessById(pid); if (!normal.HasExited && Math.Abs((normal.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1) { normal.Kill(); normal.WaitForExit(5000); } } catch (ArgumentException) { } }
        }
    }

    [Fact]
    public async Task OldPendingGraceIdentityAllowsLegitimateSettingsChangeAfterExactProcessExit()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools(); using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        var source = ExpectedMigrations; var target = source.Concat(["20260905120000_S9T07Fixture10"]).ToArray(); var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.0", source); var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); var fixture = Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"); CopyDirectory(fixture, app); CopyDirectory(fixture, old); Directory.CreateDirectory(staging);
        using var verification = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var verificationPid = verification.Id; var verificationStarted = verification.StartTime.ToUniversalTime(); await verification.WaitForExitAsync();
        var token = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow; var schema = new SchemaUpdateJournal(SchemaPhase.OldCandidateHealthVerified, snapshot, source, target, token, verificationPid, verificationStarted); var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.2", 0, now.AddMinutes(-1), UpdatePhase.OldAppRestored, TreeFingerprint.Create(old), TreeFingerprint.Create(staging), now, now, CandidatePid: verificationPid, CandidateStartedUtc: verificationStarted, Schema: schema);
        var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal)); await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, launchToken = token, version = "1.0.0", pid = verificationPid, startedUtc = verificationStarted, migrations = source, migrationCount = source.Length, lastMigration = source[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        NormalLaunchHandshake.Write(data, new NormalLaunchIntent(operation, token, NormalLaunchRole.Old, TreeFingerprint.Create(app).Hash, (int)UpdatePhase.OldAppRestored, (int)SchemaPhase.OldCandidateHealthVerified, NormalLaunchState.Pending, 0, null, DateTimeOffset.UtcNow));
        var record = Path.Combine(data, "updates", operation, "normal-process.txt"); var identified = Path.Combine(data, "updates", operation, "observed-identity.marker"); var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe"); Process? first = null; DateTimeOffset? firstStarted = null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T07_NORMAL_PROCESS_RECORD"] = record, ["S9_T07_NORMAL_WAIT_MS"] = "15000", ["S9_T07_UPDATER_OBSERVED_IDENTITY_MARKER"] = identified } })!;
            first = Process.Start(new ProcessStartInfo(Path.Combine(app, "StoreExpiryInspector.exe")) { UseShellExecute = false, ArgumentList = { "--data-root", data, "--allow-existing-isolated-data-root", "--s9-t07-normal-launch", operation, token }, Environment = { ["S9_T07_NORMAL_DELAY_LOADED_MS"] = "2000" } })!; firstStarted = first.StartTime.ToUniversalTime();
            await WaitForFile(identified, TimeSpan.FromSeconds(30)); Assert.Equal(NormalLaunchState.Identified, NormalLaunchHandshake.Read(data, operation).State); Execute(database, "UPDATE settings SET reminder_minute_of_day=601 WHERE id=1;"); if (!first.HasExited) first.Kill(); await first.WaitForExitAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token); Assert.Equal(0, process.ExitCode); using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal((int)UpdatePhase.RolledBack, final.RootElement.GetProperty("Phase").GetInt32()); Assert.Equal((int)SchemaPhase.RolledBack, final.RootElement.GetProperty("Schema").GetProperty("Phase").GetInt32()); Assert.True(File.Exists(record)); Assert.True(UpgradeHealthAck.VerifyDatabase(database, true).SequenceEqual(source, StringComparer.Ordinal));
        }
        finally
        {
            if (first is not null) { try { if (firstStarted is not null && !first.HasExited && Math.Abs((first.StartTime.ToUniversalTime() - firstStarted.Value.UtcDateTime).TotalSeconds) <= 1) first.Kill(); first.WaitForExit(5000); } finally { first.Dispose(); } }
            if (File.Exists(record)) { var values = File.ReadAllText(record).Split('|'); if (values.Length == 2 && int.TryParse(values[0], out var pid) && DateTimeOffset.TryParse(values[1], out var started)) try { using var normal = Process.GetProcessById(pid); if (!normal.HasExited && Math.Abs((normal.StartTime.ToUniversalTime() - started.UtcDateTime).TotalSeconds) <= 1) { normal.Kill(); normal.WaitForExit(5000); } } catch (ArgumentException) { } }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateLoadedDatabaseOrAckMismatchStopsExactLiveNormal(bool corruptAck)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools(); using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        var source = ExpectedMigrations; var target = source.Concat(["20260905120000_S9T07Fixture10"]).ToArray(); var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.0", source); var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); var fixture = Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"); CopyDirectory(fixture, app); Directory.CreateDirectory(staging); Directory.CreateDirectory(old);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE s9t07_fixture (id INTEGER PRIMARY KEY, payload BLOB NOT NULL, stage INTEGER NOT NULL DEFAULT 10); INSERT INTO s9t07_fixture(id,payload) VALUES (1,zeroblob(1)); INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ($migration,'1.0.2');"; command.Parameters.AddWithValue("$migration", target[^1]); command.ExecuteNonQuery(); }
        using var verification = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var verificationPid = verification.Id; var verificationStarted = verification.StartTime.ToUniversalTime(); await verification.WaitForExitAsync(); var token = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow;
        var schema = new SchemaUpdateJournal(SchemaPhase.CandidateCommitted, snapshot, source, target, token, verificationPid, verificationStarted); var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.2", 0, now.AddMinutes(-1), UpdatePhase.Committed, TreeFingerprint.Create(old), TreeFingerprint.Create(app), now, now, CandidatePid: verificationPid, CandidateStartedUtc: verificationStarted, Schema: schema); var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal)); await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, launchToken = token, version = "1.0.2", pid = verificationPid, startedUtc = verificationStarted, migrations = target, migrationCount = target.Length, lastMigration = target[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        NormalLaunchHandshake.Write(data, new NormalLaunchIntent(operation, token, NormalLaunchRole.Candidate, TreeFingerprint.Create(app).Hash, (int)UpdatePhase.Committed, (int)SchemaPhase.CandidateCommitted, NormalLaunchState.Pending, 0, null, DateTimeOffset.UtcNow)); Process? normal = null; DateTimeOffset? normalStarted = null;
        try
        {
            normal = Process.Start(new ProcessStartInfo(Path.Combine(app, "StoreExpiryInspector.exe")) { UseShellExecute = false, ArgumentList = { "--data-root", data, "--allow-existing-isolated-data-root", "--s9-t07-normal-launch", operation, token } })!; normalStarted = normal.StartTime.ToUniversalTime(); await WaitForHandshake(data, operation, NormalLaunchState.Loaded, TimeSpan.FromSeconds(30));
            if (corruptAck) await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), "bad"); else Execute(database, "DELETE FROM __EFMigrationsHistory WHERE MigrationId='20260905120000_S9T07Fixture10';"); var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe"); using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath } })!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
            Assert.True(normal.HasExited); using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)); Assert.Equal(1, process.ExitCode); Assert.Equal((int)UpdatePhase.FailedNeedsManualRecovery, final.RootElement.GetProperty("Phase").GetInt32());
        }
        finally { if (normal is not null) { try { if (normalStarted is not null && !normal.HasExited && Math.Abs((normal.StartTime.ToUniversalTime() - normalStarted.Value.UtcDateTime).TotalSeconds) <= 1) normal.Kill(); normal.WaitForExit(5000); } finally { normal.Dispose(); } } }
    }

    [Fact]
    public async Task CandidateLoadedMismatchStopsHeldLoserAndAuthoritativeNormalIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(root, "install"); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools(); using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=DELETE;"; pragma.ExecuteScalar(); }
        var source = ExpectedMigrations; var target = source.Concat(["20260905120000_S9T07Fixture10"]).ToArray(); var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.0", source); var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation); var fixture = Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"); CopyDirectory(fixture, app); Directory.CreateDirectory(staging); Directory.CreateDirectory(old);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE s9t07_fixture (id INTEGER PRIMARY KEY, payload BLOB NOT NULL, stage INTEGER NOT NULL DEFAULT 10); INSERT INTO s9t07_fixture(id,payload) VALUES (1,zeroblob(1)); INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ($migration,'1.0.3');"; command.Parameters.AddWithValue("$migration", target[^1]); command.ExecuteNonQuery(); }
        using var verification = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false })!; var verificationPid = verification.Id; var verificationStarted = verification.StartTime.ToUniversalTime(); await verification.WaitForExitAsync(); var token = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow;
        var schema = new SchemaUpdateJournal(SchemaPhase.CandidateCommitted, snapshot, source, target, token, verificationPid, verificationStarted); var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.3", 0, now.AddMinutes(-1), UpdatePhase.Committed, TreeFingerprint.Create(old), TreeFingerprint.Create(app), now, now, CandidatePid: verificationPid, CandidateStartedUtc: verificationStarted, Schema: schema); var journalPath = Path.Combine(data, "updates", operation, "journal.json"); await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal)); await File.WriteAllTextAsync(Path.Combine(data, "updates", operation, "health-ack.json"), JsonSerializer.Serialize(new { operationId = operation, launchToken = token, version = "1.0.3", pid = verificationPid, startedUtc = verificationStarted, migrations = target, migrationCount = target.Length, lastMigration = target[^1], integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true }));
        var beforeIdentify = Path.Combine(data, "updates", operation, "before-identify.marker"); var release = Path.Combine(data, "updates", operation, "release-before-identify.marker"); var started = Path.Combine(data, "updates", operation, "updater-started.marker"); var record = Path.Combine(data, "updates", operation, "normal-process.txt"); var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe"); Process? authoritative = null; DateTimeOffset? authoritativeStarted = null; Process? updaterProcess = null; DateTimeOffset? updaterStarted = null;
        try
        {
            authoritative = Process.Start(new ProcessStartInfo(Path.Combine(app, "StoreExpiryInspector.exe")) { UseShellExecute = false, ArgumentList = { "--data-root", data, "--allow-existing-isolated-data-root", "--s9-t07-normal-launch", operation, token }, Environment = { ["S9_T07_NORMAL_BEFORE_IDENTIFY_MARKER"] = beforeIdentify, ["S9_T07_NORMAL_BEFORE_IDENTIFY_RELEASE"] = release } })!; authoritativeStarted = authoritative.StartTime.ToUniversalTime(); await WaitForFile(beforeIdentify, TimeSpan.FromSeconds(5));
            updaterProcess = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T07_NORMAL_PROCESS_RECORD"] = record, ["S9_T07_NORMAL_STARTED_MARKER"] = started, ["S9_T07_NORMAL_WAIT_MS"] = "3000" } })!; updaterStarted = updaterProcess.StartTime.ToUniversalTime();
            var loser = (await WaitForFile(started, TimeSpan.FromSeconds(5), content => content.Split('|').Length == 2)).Split('|'); Assert.Equal(2, loser.Length); Assert.NotEqual(authoritative.Id.ToString(), loser[0]); await WaitForExit(int.Parse(loser[0]), TimeSpan.FromSeconds(5)); Execute(database, "DELETE FROM __EFMigrationsHistory WHERE MigrationId='20260905120000_S9T07Fixture10';"); File.WriteAllText(release, "release");
            await WaitForHandshake(data, operation, NormalLaunchState.Loaded, TimeSpan.FromSeconds(5)); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await updaterProcess.WaitForExitAsync(timeout.Token); Assert.Equal(1, updaterProcess.ExitCode); Assert.True(authoritative.HasExited);
            var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)).RootElement; Assert.Equal((int)UpdatePhase.FailedNeedsManualRecovery, final.GetProperty("Phase").GetInt32()); Assert.Contains("Normal application database state is invalid.", final.GetProperty("LastError").GetString()); Assert.Contains("Normal application database state is invalid.", final.GetProperty("Schema").GetProperty("LastError").GetString());
            Assert.True(HasExited(int.Parse(loser[0])));
        }
        finally { if (updaterProcess is not null) { StopExactProcess(updaterProcess, updaterStarted?.UtcDateTime ?? DateTime.MinValue, updater); updaterProcess.Dispose(); } if (authoritative is not null) { StopExactProcess(authoritative, authoritativeStarted?.UtcDateTime ?? DateTime.MinValue, Path.Combine(app, "StoreExpiryInspector.exe")); authoritative.Dispose(); } StopRecordedProcess(record, Path.Combine(app, "StoreExpiryInspector.exe")); }
    }

    [Fact]
    public async Task ExternalUpdaterMissingCandidateIdentityEntersManualRecoveryOnceWithoutStartingOldApp()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var install = Path.Combine(root, "install"); var data = Path.Combine(root, "data-root"); var operation = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(data, "data")); Directory.CreateDirectory(Path.Combine(data, "updates", operation));
        var app = Path.Combine(install, "app"); var old = Path.Combine(install, "app.old-" + operation); var staging = Path.Combine(install, "app.staging-" + operation);
        Directory.CreateDirectory(app); Directory.CreateDirectory(old); Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(app, "StoreExpiryInspector.exe"), "source-main");
        File.WriteAllText(Path.Combine(old, "StoreExpiryInspector.exe"), "old-main");
        File.WriteAllText(Path.Combine(staging, "StoreExpiryInspector.exe"), "candidate-main");
        var database = Path.Combine(data, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode=DELETE;"; command.ExecuteScalar(); }
        var migrations = ExpectedMigrations; var snapshot = SchemaUpgradeSnapshots.Create(data, operation, "1.0.0", migrations);
        var journalPath = Path.Combine(data, "updates", operation, "journal.json");
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, data, app, staging, old, new string('a', 64), "1.0.0", "1.0.2", 0, DateTimeOffset.UtcNow.AddMinutes(-1), UpdatePhase.RollbackRequired, TreeFingerprint.Create(old), TreeFingerprint.Create(staging), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Schema: new SchemaUpdateJournal(SchemaPhase.RollbackRequired, snapshot, migrations, [.. migrations, "20260905120000_S9T07Fixture10"], Guid.NewGuid().ToString()));
        await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal));
        var sourceSha = Sha256(Path.Combine(app, "StoreExpiryInspector.exe")); var databaseSha = Sha256(database);
        var updater = Path.Combine(FindRoot(), "src", "StoreExpiryInspector.Updater", "bin", "Release", "net10.0", "win-x64", "s9t05test", "net10.0", "win-x64", "StoreExpiryInspector.Updater.exe");
        Assert.True(File.Exists(updater));
        var launchMarker = Path.Combine(data, "updates", operation, "old-normal-launch.marker");
        using var process = Process.Start(new ProcessStartInfo(updater) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, ArgumentList = { "--journal", journalPath }, Environment = { ["S9_T05_NORMAL_LAUNCH_MARKER"] = launchMarker } })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        var final = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath)).RootElement;
        Assert.Equal(1, process.ExitCode);
        Assert.Equal((int)UpdatePhase.FailedNeedsManualRecovery, final.GetProperty("Phase").GetInt32());
        Assert.Equal((int)SchemaPhase.FailedNeedsManualRecovery, final.GetProperty("Schema").GetProperty("Phase").GetInt32());
        Assert.False(File.Exists(Path.Combine(data, "updates", operation, "candidate-identity.json")));
        Assert.Equal(sourceSha, Sha256(Path.Combine(app, "StoreExpiryInspector.exe")));
        Assert.Equal(databaseSha, Sha256(database));
        Assert.False(File.Exists(database + "-wal")); Assert.False(File.Exists(database + "-shm")); Assert.False(File.Exists(database + "-journal"));
        Assert.False(File.Exists(launchMarker));
        var log = Path.Combine(data, "updates", operation, "manual-recovery.log");
        Assert.True(File.Exists(log)); var lines = await File.ReadAllLinesAsync(log); var line = Assert.Single(lines, item => !string.IsNullOrWhiteSpace(item)); Assert.Contains("FileNotFoundException", line); Assert.Contains("candidate-identity.json", line);
    }

    [Fact]
    public async Task ExternalFixtureRejectsUnsafeDataRootsWithoutCreatingOperationPaths()
    {
        var root = FindRoot();
        var fixture = Path.Combine(root, "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run", "StoreExpiryInspector.exe");
        Assert.True(File.Exists(fixture));
        var temporary = Path.GetTempPath();
        var reparseTarget = Path.Combine(temporary, Guid.NewGuid().ToString());
        var reparse = Path.Combine(temporary, Guid.NewGuid().ToString());
        Directory.CreateDirectory(reparseTarget);
        using (var link = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{reparse}\" \"{reparseTarget}\"") { UseShellExecute = false })!) { await link.WaitForExitAsync(); Assert.Equal(0, link.ExitCode); }
        try
        {
            foreach (var unsafeRoot in new[] { "relative-root", Path.Combine(temporary, Guid.NewGuid().ToString(), "..", "not-a-guid"), "\\\\localhost\\missing", Path.Combine(temporary, Guid.NewGuid() + ":blocked"), reparse })
            {
                using var process = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = false, ArgumentList = { "--data-root", unsafeRoot, "--allow-existing-isolated-data-root", "--s9-t07-verify", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "--s9-t07-fixture-target", "10" } })!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
                Assert.Equal(1, process.ExitCode);
                var resolved = Path.GetFullPath(unsafeRoot);
                Assert.False(Directory.Exists(Path.Combine(resolved, "updates")));
            }
        }
        finally
        {
            if (Directory.Exists(reparse)) Directory.Delete(reparse);
            if (Directory.Exists(reparseTarget)) Directory.Delete(reparseTarget);
        }
    }

    [Fact]
    public void CleanMigration9DatabaseCreatesBoundSnapshotAndRejectsNonPrefix()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        Assert.Equal(9, snapshot.SourceMigrations.Count); Assert.True(File.Exists(snapshot.SnapshotPath));
        Assert.True(SchemaUpgradeSnapshots.IsStrictPrefix(snapshot.SourceMigrations, [.. snapshot.SourceMigrations, "20260905120000_Fixture"]));
        Assert.False(SchemaUpgradeSnapshots.IsStrictPrefix(snapshot.SourceMigrations, ["20260905120000_Fixture", .. snapshot.SourceMigrations]));
    }

    [Fact]
    public void SourceSchemaContractRejectsHistoryOnlyMigration9Database()
    {
        var root = CreateHistoryOnlyRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations));
    }

    [Theory]
    [InlineData("DROP TABLE products;")]
    [InlineData("CREATE TABLE unexpected_structure (id INTEGER PRIMARY KEY);")]
    public void SourceSchemaContractRejectsMissingOrExtraTable(string sql)
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); Execute(Path.Combine(root, "data", "app.db"), sql);
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations));
    }

    [Fact]
    public void SourceSchemaContractRejectsMissingCoreColumn()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); Execute(Path.Combine(root, "data", "app.db"), "ALTER TABLE products RENAME COLUMN id TO product_key;");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations));
    }

    [Fact]
    public void SourceSchemaContractRejectsUnexpectedIndex()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); Execute(Path.Combine(root, "data", "app.db"), "CREATE INDEX ix_source_tamper ON products(current_name);");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations));
    }

    [Theory]
    [InlineData("DROP INDEX IX_products_product_code;")]
    [InlineData("DROP INDEX IX_batches_expiry_date;")]
    public void SourceSchemaContractRejectsMissingRequiredIndex(string sql)
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); Execute(Path.Combine(root, "data", "app.db"), sql);
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations));
    }

    [Fact]
    public void SnapshotSchemaIndexTamperingCannotBeVerified()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        Execute(snapshot.SnapshotPath, "CREATE INDEX ix_snapshot_tamper ON products(current_name);");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Verify(root, snapshot));
    }

    [Fact]
    public void SnapshotPreservesBlobAndRestoreReplacesChangedDatabase()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); SeedBlob(database);
        var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var update = connection.CreateCommand(); update.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion='changed';"; update.ExecuteNonQuery(); }
        SchemaUpgradeSnapshots.Restore(root, snapshot);
        Assert.Equal(snapshot.SourceSha256, Sha256(database));
        Assert.True(Directory.EnumerateFiles(Path.Combine(root, "updates", operation), "migrated-app.db", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void NormallyClosedWalDatabaseCanCreateRawSnapshotWithoutSidecars()
    {
        var root = CreateWalCleanRoot(); var database = Path.Combine(root, "data", "app.db");
        var header = File.ReadAllBytes(database); Assert.Equal((byte)2, header[18]); Assert.Equal((byte)2, header[19]);
        Assert.False(File.Exists(database + "-wal")); Assert.False(File.Exists(database + "-shm"));
        var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        Assert.Equal(snapshot.SourceSha256, snapshot.SnapshotSha256);
    }

    [Fact]
    public void TamperedSnapshotCannotRestoreAndLeavesCurrentDatabaseUntouched()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); File.AppendAllText(snapshot.SnapshotPath, "tamper"); var current = Sha256(database);
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot));
        Assert.Equal(current, Sha256(database));
    }

    [Theory]
    [InlineData("restore-copying-intent")]
    [InlineData("restore-staging-verified")]
    [InlineData("restore-replace-intent")]
    [InlineData("restore-replaced")]
    public void RestoreReentersAfterControlledInterruption(string checkpoint)
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var update = connection.CreateCommand(); update.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion='changed';"; update.ExecuteNonQuery(); }
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == checkpoint) throw new IOException("controlled"); };
        try { Assert.Throws<IOException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        SchemaUpgradeSnapshots.Restore(root, snapshot);
        Assert.Equal(snapshot.SnapshotSha256, Sha256(database));
    }

    [Theory]
    [InlineData("restore-sidecar-intent")]
    [InlineData("restore-sidecar-quarantined")]
    public void RestoreReentersAroundSidecarQuarantineAndKeepsEvidence(string checkpoint)
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); File.WriteAllText(database + "-wal", "candidate-wal"); File.WriteAllText(database + "-shm", "candidate-shm");
        var stopped = false; SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == checkpoint && !stopped) { stopped = true; throw new IOException("controlled"); } };
        try { Assert.Throws<IOException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        SchemaUpgradeSnapshots.Restore(root, snapshot);
        var quarantine = Path.Combine(root, "updates", operation, "quarantine"); Assert.True(File.Exists(Path.Combine(quarantine, "migrated-app.db-wal"))); Assert.True(File.Exists(Path.Combine(quarantine, "migrated-app.db-shm"))); Assert.Equal(snapshot.SnapshotSha256, Sha256(database));
    }

    [Fact]
    public void RestoreUsesCommittedStateWhenInterruptedStateScratchRemains()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); var operationRoot = Path.Combine(root, "updates", operation); Directory.CreateDirectory(operationRoot);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == "restore-copying-intent") throw new IOException("controlled"); };
        try { Assert.Throws<IOException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        File.WriteAllText(Path.Combine(operationRoot, "schema-restore.json.tmp"), "uncommitted");
        SchemaUpgradeSnapshots.Restore(root, snapshot);
        Assert.Equal(snapshot.SnapshotSha256, Sha256(database)); Assert.True(Directory.EnumerateFiles(operationRoot, "schema-restore.json.tmp.interrupted-*", SearchOption.TopDirectoryOnly).Any());
    }

    [Fact]
    public void RestoreReplacesPartialCopyingStagingFromVerifiedSnapshot()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); var operationRoot = Path.Combine(root, "updates", operation); Directory.CreateDirectory(operationRoot);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == "restore-staging-first-block") throw new IOException("controlled after first staging block"); };
        try { Assert.Throws<IOException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        var staging = Path.Combine(operationRoot, "schema-restore.tmp"); Assert.True(File.Exists(staging)); var partialLength = new FileInfo(staging).Length; Assert.True(partialLength > 0 && partialLength < new FileInfo(snapshot.SnapshotPath).Length);
        SchemaUpgradeSnapshots.Restore(root, snapshot);
        Assert.Equal(snapshot.SnapshotSha256, Sha256(database)); Assert.True(Directory.EnumerateFiles(operationRoot, "schema-restore.tmp.interrupted-*", SearchOption.TopDirectoryOnly).Any());
    }

    [Fact]
    public void RestoreReplacesMissingMainDatabase()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); File.Delete(database);
        SchemaUpgradeSnapshots.Restore(root, snapshot);
        Assert.Equal(snapshot.SnapshotSha256, Sha256(database));
    }

    [Fact]
    public void TamperedRecoveryStagingFailsClosed()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var original = Sha256(database);
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == "restore-staging-verified") throw new IOException("controlled"); };
        try { Assert.Throws<IOException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        File.AppendAllText(Path.Combine(root, "updates", operation, "schema-restore.tmp"), "tamper");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); Assert.Equal(original, Sha256(database));
    }

    [Fact]
    public void UnknownRecoveryResidueFailsClosed()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); var operationRoot = Path.Combine(root, "updates", operation); Directory.CreateDirectory(operationRoot);
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); Directory.CreateDirectory(Path.Combine(operationRoot, "quarantine")); File.WriteAllText(Path.Combine(operationRoot, "quarantine", "unknown.bin"), "unknown"); var original = Sha256(database);
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Restore(root, snapshot)); Assert.Equal(original, Sha256(database));
    }

    [Fact]
    public void TamperedSnapshotCannotBeAuthorizedForMigration()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); File.AppendAllText(snapshot.SnapshotPath, "tamper");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Verify(root, snapshot));
    }

    [Fact]
    public void FrozenSourceAuthorizationRejectsMainDriftAndLateSidecar()
    {
        var root = CreateCleanRoot(); var database = Path.Combine(root, "data", "app.db"); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion='drift';"; command.ExecuteNonQuery(); }
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.VerifyFrozenSource(root, snapshot));
        File.Copy(snapshot.SnapshotPath, database, true); File.WriteAllText(database + "-wal", "late");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.VerifyFrozenSource(root, snapshot));
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public void FrozenSourceLeaseBlocksLateSidecarUntilFinalGuard(string suffix)
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var database = Path.Combine(root, "data", "app.db"); var blocked = false;
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage != "frozen-source-verified") return; blocked = true; Assert.ThrowsAny<IOException>(() => File.WriteAllText(database + suffix, "late")); };
        try { SchemaUpgradeSnapshots.VerifyFrozenSource(root, snapshot); Assert.True(blocked); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        Assert.False(File.Exists(database + suffix));
    }

    [Fact]
    public void FrozenSourceTakeoverRejectsLateRollbackJournalBeforeCallerCanWrite()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var database = Path.Combine(root, "data", "app.db"); var mainSha = Sha256(database); var called = false;
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == "frozen-source-vfs-owned") File.WriteAllText(database + "-journal", "late rollback journal"); };
        try { Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.TakeOverFrozenSource(root, snapshot.SourceSha256, snapshot.SourceMigrations, _ => called = true)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        Assert.False(called); Assert.Equal(mainSha, Sha256(database)); Assert.Equal("late rollback journal", File.ReadAllText(database + "-journal"));
    }

    [Fact]
    public void FrozenSourceTakeoverRejectsRealWalAndShmBeforeCallerCanWrite()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var database = Path.Combine(root, "data", "app.db"); var called = false;
        using var writer = new SqliteConnection($"Data Source={database};Pooling=False"); writer.Open();
        using (var pragma = writer.CreateCommand()) { pragma.CommandText = "PRAGMA journal_mode=WAL;"; pragma.ExecuteScalar(); }
        using (var write = writer.CreateCommand()) { write.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion='wal-test' WHERE MigrationId=$id;"; write.Parameters.AddWithValue("$id", ExpectedMigrations[0]); write.ExecuteNonQuery(); }
        using var reader = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); reader.Open();
        using var hold = reader.CreateCommand(); hold.CommandText = "SELECT ProductVersion FROM __EFMigrationsHistory LIMIT 1;"; using var heldReader = hold.ExecuteReader(); Assert.True(heldReader.Read()); writer.Dispose();
        var mainSha = Sha256(database); var wal = database + "-wal"; var shm = database + "-shm"; Assert.True(new FileInfo(wal).Length > 0); var shmLength = new FileInfo(shm).Length; Assert.True(shmLength > 0); var walSha = Sha256(wal);
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.TakeOverFrozenSource(root, snapshot.SourceSha256, snapshot.SourceMigrations, _ => called = true));
        Assert.False(called); Assert.Equal(mainSha, Sha256(database)); Assert.Equal(walSha, Sha256(wal)); Assert.Equal(shmLength, new FileInfo(shm).Length);
    }

    [Fact]
    public async Task FrozenSourceTakeoverPassesTheVerifiedConnectionToMigration()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var migrated = false;
        await Task.Run(() => SchemaUpgradeSnapshots.TakeOverFrozenSource(root, snapshot.SourceSha256, snapshot.SourceMigrations, connection =>
        {
            using (var ownership = connection.CreateCommand()) { ownership.CommandText = "PRAGMA locking_mode;"; Assert.Equal("exclusive", ownership.ExecuteScalar()?.ToString()); }
            var marker = Path.Combine(root, "lock-probe.marker"); var fixture = Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run", "StoreExpiryInspector.exe");
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(fixture)!, "runtimes", "win-x64", "native", "e_sqlite3.dll")), "Lock probe native SQLite dependency is missing.");
            Process? probe = null; var probeStarted = default(DateTime);
            try
            {
                probe = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "--s9-t07-lock-probe", Path.Combine(root, "data", "app.db"), marker } })!; probeStarted = probe.StartTime.ToUniversalTime(); Assert.True(probe.WaitForExit(5000), "Lock probe did not exit before timeout."); var stdout = probe.StandardOutput.ReadToEnd(); var stderr = probe.StandardError.ReadToEnd(); Assert.True(probe.ExitCode == 0, $"Lock probe exit {probe.ExitCode}. stdout: {stdout} stderr: {stderr}"); Assert.Equal("blocked", WaitForFile(marker, TimeSpan.FromSeconds(5), content => content is "blocked" or "opened").GetAwaiter().GetResult());
            }
            finally { if (probe is not null) { StopExactProcess(probe, probeStarted, fixture); probe.Dispose(); } }
            using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE takeover_fixture (id INTEGER PRIMARY KEY);"; command.ExecuteNonQuery(); migrated = true;
        }));
        Assert.True(migrated);
    }

    [Fact]
    public void LockProbeFailsClosedWhenNativeDependencyIsMissing()
    {
        var root = CreateCleanRoot(); var app = Path.Combine(root, "probe-app"); CopyDirectory(Path.Combine(FindRoot(), "tests", "StoreExpiryInspector.S9T07Fixture", "bin", "Release", "net10.0-windows", "fixture-run"), app); File.Delete(Path.Combine(app, "runtimes", "win-x64", "native", "e_sqlite3.dll"));
        var marker = Path.Combine(root, "missing-native.marker"); var fixture = Path.Combine(app, "StoreExpiryInspector.exe"); using var probe = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "--s9-t07-lock-probe", Path.Combine(root, "data", "app.db"), marker } })!; var started = probe.StartTime.ToUniversalTime();
        try { Assert.True(probe.WaitForExit(10000), "Missing-native probe did not exit."); var stdout = probe.StandardOutput.ReadToEnd(); var stderr = probe.StandardError.ReadToEnd(); Assert.NotEqual(0, probe.ExitCode); Assert.Contains("did not become ready", stderr); Assert.Contains("e_sqlite3.dll", stderr); Assert.NotEqual("blocked", File.ReadAllText(marker)); Assert.True(string.IsNullOrEmpty(stdout), stdout); }
        finally { StopExactProcess(probe, started, fixture); }
    }

    [Fact]
    public void WalModeCleanSourceTakesOverAndInitializesOnTheOwnedConnection()
    {
        var root = CreateWalCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var database = Path.Combine(root, "data", "app.db");
        using (var mode = new SqliteConnection($"Data Source={database};Pooling=False")) { mode.Open(); using var command = mode.CreateCommand(); command.CommandText = "PRAGMA journal_mode;"; Assert.Equal("wal", command.ExecuteScalar()?.ToString()); }
        Assert.False(File.Exists(database + "-wal")); Assert.False(File.Exists(database + "-shm")); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var initialized = false;
        SchemaUpgradeSnapshots.TakeOverFrozenSource(root, snapshot.SourceSha256, snapshot.SourceMigrations, connection => { DatabaseInitializer.InitializeOpened(connection); initialized = true; });
        Assert.True(initialized); Assert.True(UpgradeHealthAck.VerifyDatabase(database, true).SequenceEqual(ExpectedMigrations, StringComparer.Ordinal));
    }

    [Fact]
    public void TakeoverRejectsSameMigrationBusinessDriftAfterPreopenHash()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var database = Path.Combine(root, "data", "app.db"); var drift = Path.Combine(root, "drift.db"); File.Copy(database, drift);
        using (var connection = new SqliteConnection($"Data Source={drift};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion='same-migrations-drift';"; command.ExecuteNonQuery(); }
        var called = false;
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage == "frozen-source-before-vfs-open") File.Copy(drift, database, true); };
        try { Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.TakeOverFrozenSource(root, snapshot.SourceSha256, snapshot.SourceMigrations, _ => called = true)); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        Assert.False(called);
    }

    [Fact]
    public void TakeoverReservationRejectsLateRealWalAndShmBeforeVfsOpen()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); var database = Path.Combine(root, "data", "app.db"); var evidence = Path.Combine(root, "evidence.db");
        using var writer = new SqliteConnection($"Data Source={evidence};Pooling=False"); writer.Open(); using (var pragma = writer.CreateCommand()) { pragma.CommandText = "PRAGMA journal_mode=WAL;"; pragma.ExecuteScalar(); } using (var write = writer.CreateCommand()) { write.CommandText = "CREATE TABLE evidence (id INTEGER); INSERT INTO evidence VALUES (1);"; write.ExecuteNonQuery(); }
        var wal = evidence + "-wal"; var shm = evidence + "-shm"; Assert.True(new FileInfo(wal).Length > 0); Assert.True(new FileInfo(shm).Length > 0); var attempted = 0; var called = false;
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage != "frozen-source-reservation-held") return; Assert.ThrowsAny<IOException>(() => File.Copy(wal, database + "-wal", true)); Assert.ThrowsAny<IOException>(() => File.Copy(shm, database + "-shm", true)); attempted = 2; };
        try { SchemaUpgradeSnapshots.TakeOverFrozenSource(root, snapshot.SourceSha256, snapshot.SourceMigrations, _ => called = true); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
        Assert.Equal(2, attempted); Assert.True(called); Assert.False(File.Exists(database + "-wal")); Assert.False(File.Exists(database + "-shm"));
    }

    [Fact]
    public void ControlledWalHealthReadIncludesUncheckpointedChanges()
    {
        var root = CreateWalCleanRoot(); var database = Path.Combine(root, "data", "app.db");
        using var writer = new SqliteConnection($"Data Source={database};Pooling=False"); writer.Open();
        using (var command = writer.CreateCommand()) { command.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion='wal-view';"; command.ExecuteNonQuery(); }
        Assert.True(File.Exists(database + "-wal"));
        using var health = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); health.Open();
        using var healthCommand = health.CreateCommand(); healthCommand.CommandText = "SELECT DISTINCT ProductVersion FROM __EFMigrationsHistory;";
        Assert.Equal("wal-view", healthCommand.ExecuteScalar());
    }

    [Fact]
    public void WrongTrustedMigrationDeclarationBlocksBeforeSnapshot()
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", [.. ExpectedMigrations, "fixture"]));
        Assert.False(File.Exists(Path.Combine(root, "updates", operation, "schema-source.db")));
    }

    [Theory]
    [InlineData("source-locked", "app.db-wal")]
    [InlineData("snapshot-copied", "schema-source.db.tmp-wal")]
    public void LateSidecarWriterIsBlockedByReservation(string checkpoint, string fileName)
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); var operationRoot = Path.Combine(root, "updates", operation); Directory.CreateDirectory(operationRoot); var attempted = false;
        SchemaUpgradeSnapshots.TestCheckpoint = stage => { if (stage != checkpoint) return; attempted = true; Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(Path.Combine(stage == "source-locked" ? Path.Combine(root, "data") : operationRoot, fileName), [1, 2, 3])); };
        try { var snapshot = SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations); Assert.True(attempted); Assert.Equal(snapshot.SourceSha256, Sha256(Path.Combine(root, "data", "app.db"))); }
        finally { SchemaUpgradeSnapshots.TestCheckpoint = null; }
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public void AnySidecarAtTrustBoundaryFailsClosed(string suffix)
    {
        var root = CreateCleanRoot(); var operation = Guid.NewGuid().ToString(); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); var database = Path.Combine(root, "data", "app.db");
        File.WriteAllText(database + suffix, "unknown");
        Assert.Throws<InvalidDataException>(() => SchemaUpgradeSnapshots.Create(root, operation, "1.0.0", ExpectedMigrations));
        Assert.False(File.Exists(Path.Combine(root, "updates", operation, "schema-source.db")));
    }

    private static string CreateCleanRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(Path.Combine(root, "data"));
        var database = Path.Combine(root, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode=DELETE;"; command.ExecuteScalar(); }
        return root;
    }

    private static void AssertPendingTerminal(string root, string operation, TreeFingerprint tree, SchemaUpdateJournal schema, bool rejects)
    {
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", Path.Combine(root, "install"), root, Path.Combine(root, "install", "app"), Path.Combine(root, "install", "stage"), Path.Combine(root, "install", "old"), new string('A', 64), "1.0.0", "1.0.2", 0, DateTimeOffset.UtcNow, UpdatePhase.Completed, tree, tree, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Schema: schema);
        File.WriteAllText(Path.Combine(root, "updates", operation, "journal.json"), JsonSerializer.Serialize(journal));
        if (rejects) Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
        else Assert.False(PendingUpdateRecovery.TryResume(root));
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
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
    }

    private static async Task<string> WaitForFile(string path, TimeSpan timeout, Func<string, bool>? contentIsReady = null)
    {
        contentIsReady ??= content => content.Length > 0;
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(50))
            try { var content = File.ReadAllText(path); if (contentIsReady(content)) return content; } catch (IOException) { }
        var final = File.ReadAllText(path);
        Assert.True(contentIsReady(final), $"File content was not ready before timeout: {path}");
        return final;
    }

    private static void DeleteTestDirectory(string directory)
    {
        for (var until = DateTime.UtcNow.AddSeconds(5); Directory.Exists(directory) && DateTime.UtcNow < until; Thread.Sleep(50))
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static async Task WaitForTerminalJournal(string path, TimeSpan timeout)
    {
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(50))
            try { using var journal = JsonDocument.Parse(File.ReadAllText(path)); if (journal.RootElement.GetProperty("Phase").GetInt32() == (int)UpdatePhase.Completed) return; } catch (IOException) { }
        using var final = JsonDocument.Parse(File.ReadAllText(path)); Assert.Equal((int)UpdatePhase.Completed, final.RootElement.GetProperty("Phase").GetInt32());
    }

    private static async Task WaitForRecordedExit(string path, TimeSpan timeout)
    {
        using var identity = JsonDocument.Parse(File.ReadAllText(path)); var pid = identity.RootElement.GetProperty("pid").GetInt32(); var started = identity.RootElement.GetProperty("startedUtc").GetDateTimeOffset().UtcDateTime;
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(50)) if (HasExited(pid)) return;
        using var process = Process.GetProcessById(pid); Assert.True(Math.Abs((process.StartTime.ToUniversalTime() - started).TotalSeconds) > 1 || process.HasExited);
    }

    private static async Task WaitForHandshake(string root, string operation, NormalLaunchState state, TimeSpan timeout)
    {
        for (var until = DateTime.UtcNow + timeout; DateTime.UtcNow < until; await Task.Delay(50))
            try { if (NormalLaunchHandshake.Read(root, operation).State == state) return; } catch (FileNotFoundException) { }
        Assert.Equal(state, NormalLaunchHandshake.Read(root, operation).State);
    }

    private static bool HasExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    private static async Task WaitForExit(int pid, TimeSpan timeout)
    {
        for (var until = DateTime.UtcNow + timeout; !HasExited(pid) && DateTime.UtcNow < until; await Task.Delay(50)) { }
        Assert.True(HasExited(pid));
    }

    private static void StopRecordedProcess(string record, string executable) => StopRecordedActor(record, executable);

    private static void StopExactProcess(Process process, DateTime started, string executable)
    {
        try
        {
            if (!process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - started).TotalSeconds) <= 1 && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            {
                process.Kill();
                _ = process.WaitForExit(5000);
            }
        }
        catch (Exception) { }
    }

    private static void StopCandidateIdentity(string identityPath, string executable) => StopJsonActor(identityPath, executable);
    private static void StopNormalLaunch(string path, string executable) => StopJsonActor(path, executable);
    private static void StopUpdaterIdentity(string path, string executable) => StopJsonActor(path, executable);

    private static void StopRecordedActor(string path, string executable)
    {
        try
        {
            if (!File.Exists(path)) return;
            var values = File.ReadAllText(path).Split('|');
            if (values.Length == 2 && int.TryParse(values[0], out var pid) && DateTimeOffset.TryParse(values[1], out var started)) StopProcess(pid, started.UtcDateTime, executable);
        }
        catch (Exception) { }
    }

    private static void StopJsonActor(string path, string executable)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            StopProcess(document.RootElement.GetProperty("pid").GetInt32(), document.RootElement.GetProperty("startedUtc").GetDateTimeOffset().UtcDateTime, executable);
        }
        catch (Exception) { }
    }

    private static void StopProcess(int pid, DateTime started, string executable)
    {
        try { using var process = Process.GetProcessById(pid); StopExactProcess(process, started, executable); }
        catch (Exception) { }
    }

    private static string CreateWalCleanRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(Path.Combine(root, "data"));
        var database = Path.Combine(root, "data", "app.db"); DatabaseInitializer.Initialize(database); SqliteConnection.ClearAllPools();
        return root;
    }

    private static string CreateHistoryOnlyRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(Path.Combine(root, "data")); var database = Path.Combine(root, "data", "app.db");
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False"); connection.Open(); using (var command = connection.CreateCommand()) { command.CommandText = "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);"; command.ExecuteNonQuery(); }
        foreach (var migration in ExpectedMigrations) { using var insert = connection.CreateCommand(); insert.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ($id, '10.0.0');"; insert.Parameters.AddWithValue("$id", migration); insert.ExecuteNonQuery(); }
        return root;
    }

    private static void Execute(string database, string sql) { using var connection = new SqliteConnection($"Data Source={database};Pooling=False"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }

    private static void SeedBlob(string database)
    {
        using var connection = new SqliteConnection($"Data Source={database};Foreign Keys=True;Pooling=False"); connection.Open();
        using (var import = connection.CreateCommand())
        {
            import.CommandText = "INSERT INTO imports (source_file_name,source_file_sha256,parsed_at_utc,status,product_count,batch_count,new_product_count,new_batch_count,updated_batch_count,issue_count,unsupported_category_count,new_task_product_count,is_undone) VALUES ('fixture.xlsx',$sha,'2026-09-05T00:00:00.0000000Z','parsed',0,0,0,0,0,0,0,0,0);";
            import.Parameters.AddWithValue("$sha", new string('a', 64)); import.ExecuteNonQuery();
        }
        using var workbook = connection.CreateCommand(); workbook.CommandText = "INSERT INTO import_workbooks (import_id,original_file_name,content,sha256,saved_at_utc) VALUES (1,'fixture.xlsx',$blob,$sha,'2026-09-05T00:00:00.0000000Z');";
        workbook.Parameters.AddWithValue("$blob", Enumerable.Range(0, 131_073).Select(value => (byte)(value % 251)).ToArray()); workbook.Parameters.AddWithValue("$sha", new string('b', 64)); workbook.ExecuteNonQuery();
    }

    private static void SeedRollbackHistoryAndSettings(string database)
    {
        using var connection = new SqliteConnection($"Data Source={database};Foreign Keys=True;Pooling=False"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE settings SET reminder_minute_of_day=602 WHERE id=1; INSERT INTO products(product_code,current_name,category_code,policy_code,policy_version,expiry_management_status,excel_stock_qty,effective_stock_qty,lifecycle_generation,created_at_utc,updated_at_utc) VALUES ('S9T07-HISTORY','rollback history','food','food_expiry',1,'managed',0,0,0,'2026-09-05T00:00:00Z','2026-09-05T00:00:00Z'); INSERT INTO lifecycle_events(product_id,event_type,reason,occurred_at_utc) VALUES (last_insert_rowid(),'product_stock_zero','s9-t07 rollback history','2026-09-05T00:00:00Z');"; command.ExecuteNonQuery();
    }

    private static string Sha256(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)); }

    private static readonly string[] ExpectedMigrations =
    [
        "20260826123739_InitialCreate", "20260826130822_AddTasksAndDrafts", "20260826135612_AddInspectionHistory",
        "20260826142429_AddInventoryAdjustments", "20260826152131_AddImportPersistence", "20260826155455_AddBackupMetadata",
        "20260826162033_AddSettingsAndAppState", "20260826170403_AddLifecycleEvents", "20260901155124_AddPolicyAndBaselineFoundation"
    ];
}
