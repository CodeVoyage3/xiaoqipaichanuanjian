using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

// Process.Kill is intentionally exercised through a separately launched vstest worker.
public sealed class S8T04CrashConsistencyTests
{
    private const string WorkerName = "StoreExpiryInspector.Tests.S8T04CrashConsistencyTests.Worker";
    private static readonly DateTime Utc = new(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 9, 4);
    private static int WorkerInitializationCount;

    [Fact]
    [Trait("Category", "S8T04")]
    public void Worker_input_validation_rejects_unsafe_or_incomplete_values_before_any_initialization()
    {
        var token = Guid.NewGuid().ToString("N"); var root = NewRoot(token); var before = WorkerInitializationCount;
        Action<string> initialize = _ => WorkerInitializationCount++;
        Assert.ThrowsAny<Exception>(() => ValidateThenInitialize(root, token, Path.Combine(root, "database", "app.db"), Path.Combine(root, "marker.json"), "inspection", "precommit", "invalid", "0", null, null, null, initialize));
        Assert.ThrowsAny<Exception>(() => ValidateThenInitialize(root, token, Path.Combine(root, "database", "app.db"), Path.Combine(root, "marker.json"), "invalid", "precommit", "crash", "1", null, null, null, initialize));
        Assert.ThrowsAny<Exception>(() => ValidateThenInitialize(Path.GetTempPath(), token, Path.Combine(root, "database", "app.db"), Path.Combine(root, "marker.json"), "inspection", "precommit", "crash", "1", null, null, null, initialize));
        Assert.Equal(before, WorkerInitializationCount);
    }

    [Theory]
    [InlineData("inspection", "partial")]
    [InlineData("inspection", "items_partial")]
    [InlineData("inspection", "task_handled")]
    [InlineData("inspection", "precommit")]
    [InlineData("inspection", "postcommit")]
    [InlineData("inventory", "partial")]
    [InlineData("inventory", "precommit")]
    [InlineData("inventory", "postcommit")]
    [Trait("Category", "S8T04")]
    public void Process_kill_matrix_uses_only_explicit_temp_databases(string scenario, string checkpoint)
    {
        for (var iteration = 1; iteration <= 3; iteration++) RunParent(scenario, checkpoint, iteration);
    }

    [Theory]
    [InlineData("precommit")]
    [InlineData("postcommit")]
    [InlineData("started")]
    [InlineData("products_partial")]
    [InlineData("batches_partial")]
    [InlineData("post_task")]
    [Trait("Category", "S8T04")]
    public void Import_10k_process_kill_commit_boundaries_are_atomic(string checkpoint)
    {
        for (var iteration = 1; iteration <= 3; iteration++) RunImportParent(checkpoint, iteration, 5_000);
    }

    [Theory]
    [InlineData("precommit")]
    [InlineData("postcommit")]
    [Trait("Category", "S8T04")]
    public void Import_100k_process_kill_commit_boundaries_are_atomic(string checkpoint)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("S8_T04_RUN_100K"), "1", StringComparison.Ordinal)) return; // Gate closed: this pass is intentionally not 100k evidence.
        for (var iteration = 1; iteration <= 3; iteration++) RunImportParent(checkpoint, iteration, 50_000);
    }

    [Fact]
    [Trait("Category", "S8T04")]
    public void Worker()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("S8_T04_WORKER"), "1", StringComparison.Ordinal)) return;
        var input = ValidateThenInitialize(Required("S8_T04_ROOT"), Required("S8_T04_TOKEN"), Required("S8_T04_DATABASE"), Required("S8_T04_MARKER"), Required("S8_T04_SCENARIO"), Required("S8_T04_CHECKPOINT"), Required("S8_T04_MODE"), Required("S8_T04_PARENT_PID"), Environment.GetEnvironmentVariable("S8_T04_IMPORT_SOURCE"), Environment.GetEnvironmentVariable("S8_T04_IMPORT_SEED"), Environment.GetEnvironmentVariable("S8_T04_IMPORT_SHA256"), path => { WorkerInitializationCount++; DatabaseInitializer.Initialize(path); });
        var (root, token, databasePath, markerPath, scenario, checkpoint, mode, parentPid, importSource, importSeed, importSha256) = input;
        Directory.CreateDirectory(root);
        AssertSafePath(root, databasePath);
        if (scenario == "import") SeedImport(databasePath, importSeed!, importSource!, importSha256!); else Seed(databasePath, scenario);
        var before = Snapshot(databasePath);
        File.WriteAllText(Path.Combine(root, "before.json"), JsonSerializer.Serialize(new { token, before }));
        var checkpointState = new CrashCheckpoint(token, markerPath, mode == "reference" ? "reference" : checkpoint, scenario, parentPid);
        using var context = Open(databasePath, new CrashCommandInterceptor(checkpointState), new CrashTransactionInterceptor(checkpointState));
        if (scenario == "inspection")
        {
            var task = context.Tasks.Single();
            var result = new InspectionSubmissionUseCase().Submit(context, new(task.Id, task.ProductId, Day, Utc));
            Assert.True(result.Submitted);
        }
        else if (scenario == "inventory")
        {
            var product = context.Products.Single();
            var result = new ManualInventoryAdjustmentUseCase().Execute(context, new(product.Id, 0, true, Utc));
            Assert.True(result.Changed);
        }
        else if (scenario == "import")
        {
            var contract = S8T03ImportPerformanceTests.ReadConfirmedContract(databasePath, importSource!);
            void Measure(string stage, TimeSpan _) { if (stage == "stock_zero") checkpointState.MarkPostEnabled(); }
            var result = new ConfirmedImportLifecycleOrchestrator(measure: Measure).Execute(context, new(contract, Path.Combine(root, "snapshots"), Utc, Day, Utc.AddMinutes(1)));
            Assert.True(result.Succeeded, result.Code);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (mode == "reference")
        {
            File.WriteAllText(Path.Combine(root, "completed.json"), JsonSerializer.Serialize(new { token, snapshot = Snapshot(databasePath) }));
            return;
        }
        throw new InvalidOperationException("S8-T04 worker returned instead of being killed at its checkpoint.");
    }

    [Fact]
    [Trait("Category", "S8T04")]
    public void Readonly_reopen_diagnostic()
    {
        var root = Environment.GetEnvironmentVariable("S8_T04_READONLY_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var token = Path.GetFileName(Path.GetFullPath(root));
        AssertSafeRoot(root, token);
        var databasePath = Path.Combine(root, "database", "app.db");
        AssertSafePath(root, databasePath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", command.ExecuteScalar()?.ToString());
    }

    private static void RunParent(string scenario, string checkpoint, int iteration)
    {
        var token = Guid.NewGuid().ToString("N");
        var root = NewRoot(token);
        var databasePath = Path.Combine(root, "database", "app.db");
        var markerPath = Path.Combine(root, "marker.json");
        var evidencePath = Path.Combine(root, "evidence", $"{scenario}-{checkpoint}-{iteration}.json");
        AssertContained(root, databasePath, markerPath, evidencePath);
        Directory.CreateDirectory(root);
        try
        {
        var expected = RunReference(scenario, checkpoint);
        using var worker = StartWorker(root, databasePath, markerPath, token, scenario, checkpoint, "crash");
        JsonElement marker;
        try
        {
            marker = WaitForMarker(markerPath, token);
            Assert.Equal(scenario, marker.GetProperty("scenario").GetString());
            Assert.Equal(checkpoint, marker.GetProperty("checkpoint").GetString());
            Assert.Contains(checkpoint switch { "precommit" => "transaction_committing", "postcommit" => "transaction_committed", "task_handled" => "UPDATE \"tasks\"", "items_partial" => "INSERT INTO \"inspection_items\"", _ => scenario == "inspection" ? "INSERT INTO \"inspections\"" : "INSERT INTO \"inventory_adjustments\"" }, marker.GetProperty("stage").GetString());
            Assert.False(worker.HasExited);
            using var markerWorker = Process.GetProcessById(marker.GetProperty("worker_pid").GetInt32());
            Assert.Equal(marker.GetProperty("worker_start_utc_ticks").GetInt64(), markerWorker.StartTime.ToUniversalTime().Ticks);
            // Only the Process.Start object is killed. The marker handle is verification-only.
            worker.Kill(entireProcessTree: true);
            worker.WaitForExit(15_000);
            Assert.True(worker.HasExited);
            Assert.True(markerWorker.WaitForExit(15_000));
            AssertWorkerExited(marker.GetProperty("worker_pid").GetInt32(), marker.GetProperty("worker_start_utc_ticks").GetInt64());
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
        }
        var sidecarsBeforeReopen = Sidecars(databasePath);
        var before = JsonSerializer.Deserialize<DatabaseSnapshot>(JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "before.json"))).RootElement.GetProperty("before").GetRawText())!;
        var after = Snapshot(databasePath);
        var committed = checkpoint == "postcommit";
        AssertSnapshotEqual(committed ? expected : before, after, canonical: committed);
        var verification = Verify(databasePath);
        Assert.True(verification.IntegrityOk);
        Assert.Equal(0, verification.ForeignKeyViolations);
        Assert.Equal(9, verification.Migrations);
        AssertAuthorityReadsAndWritable(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            card = "S8-T04", scenario, checkpoint, iteration, root, database_path = databasePath,
            child_pid = worker.Id, marker, kill_mechanism = "Process.Kill(entireProcessTree:true)", child_exit_code = worker.ExitCode,
            before_raw_fingerprint = before.RawFingerprint, expected_raw_fingerprint = expected.RawFingerprint, after_raw_fingerprint = after.RawFingerprint,
            before_canonical_fingerprint = before.CanonicalFingerprint, expected_canonical_fingerprint = expected.CanonicalFingerprint, after_canonical_fingerprint = after.CanonicalFingerprint,
            before_counts = before.Counts, expected_counts = expected.Counts, after_counts = after.Counts, committed, integrity_check = verification.IntegrityOk ? "ok" : "failed",
            foreign_key_check_count = verification.ForeignKeyViolations, migrations = verification.Migrations,
            sidecars_before_reopen = sidecarsBeforeReopen, sidecars_after_reopen = Sidecars(databasePath), pass = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            var sqlite = exception as SqliteException ?? exception.InnerException as SqliteException;
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { card = "S8-T04", scenario, checkpoint, iteration, root, database_path = databasePath, token, pass = false, occurred_at_utc = DateTime.UtcNow, exception_type = exception.GetType().FullName, exception_message = exception.Message, exception_stack = exception.ToString(), sqlite_error_code = sqlite?.SqliteErrorCode, sqlite_extended_error_code = sqlite?.SqliteExtendedErrorCode, sidecars_after_failure = Sidecars(databasePath) }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }

    private static void RunImportParent(string checkpoint, int iteration, int products)
    {
        var token = Guid.NewGuid().ToString("N");
        var root = NewRoot(token); var database = Path.Combine(root, "database", "app.db"); var marker = Path.Combine(root, "marker.json");
        var seed = Path.Combine(root, "source", "seed.xlsx"); var source = Path.Combine(root, "source", "import.xlsx"); var evidence = Path.Combine(root, "evidence", $"import-{products * 2}-{checkpoint}-{iteration}.json");
        AssertSafeRoot(root, token); AssertContained(root, database, marker, seed, source, evidence); Directory.CreateDirectory(root);
        S8T03ImportPerformanceTests.WriteWorkbook(seed, 10, 1, seed: true);
        S8T03ImportPerformanceTests.WriteWorkbook(source, products, 2, seed: false);
        try
        {
            var expected = RunReference("import", checkpoint, seed, source);
            using var worker = StartWorker(root, database, marker, token, "import", checkpoint, "crash", seed, source, HashFile(source));
            JsonElement markerData;
            try
            {
                markerData = WaitForMarker(marker, token);
            Assert.Contains(checkpoint switch { "precommit" => "transaction_committing", "postcommit" => "transaction_committed", "started" => "transaction_started", "products_partial" => "INSERT INTO \"products\"", "batches_partial" => "INSERT INTO \"batches\"", "post_task" => "INSERT INTO \"tasks\"", _ => throw new ArgumentOutOfRangeException(nameof(checkpoint)) }, markerData.GetProperty("stage").GetString());
                Assert.False(worker.HasExited);
                using var markerWorker = Process.GetProcessById(markerData.GetProperty("worker_pid").GetInt32());
                Assert.Equal(markerData.GetProperty("worker_start_utc_ticks").GetInt64(), markerWorker.StartTime.ToUniversalTime().Ticks);
                worker.Kill(entireProcessTree: true); Assert.True(worker.WaitForExit(120_000)); Assert.True(markerWorker.WaitForExit(120_000));
                AssertWorkerExited(markerData.GetProperty("worker_pid").GetInt32(), markerData.GetProperty("worker_start_utc_ticks").GetInt64());
            }
            finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
            var sidecarsBeforeReopen = Sidecars(database);
            var before = JsonSerializer.Deserialize<DatabaseSnapshot>(JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "before.json"))).RootElement.GetProperty("before").GetRawText())!;
            var after = Snapshot(database); var committed = checkpoint == "postcommit";
            AssertSnapshotEqual(committed ? expected : before, after, canonical: committed);
            var verification = Verify(database); Assert.True(verification.IntegrityOk); Assert.Equal(0, verification.ForeignKeyViolations); Assert.Equal(9, verification.Migrations); AssertSnapshotMetadata(database); AssertAuthorityReadsAndWritable(database);
            Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { card = "S8-T04", scenario = "import", rows = products * 2, scale = products == 5_000 ? "10k" : "100k", checkpoint, iteration, root, database_path = database, source_path = source, seed_path = seed, child_pid = worker.Id, marker = markerData, kill_mechanism = "Process.Kill(entireProcessTree:true)", child_exit_code = worker.ExitCode, before_raw_fingerprint = before.RawFingerprint, expected_raw_fingerprint = expected.RawFingerprint, after_raw_fingerprint = after.RawFingerprint, before_canonical_fingerprint = before.CanonicalFingerprint, expected_canonical_fingerprint = expected.CanonicalFingerprint, after_canonical_fingerprint = after.CanonicalFingerprint, before_counts = before.Counts, expected_counts = expected.Counts, after_counts = after.Counts, committed, integrity_check = verification.IntegrityOk ? "ok" : "failed", foreign_key_check_count = verification.ForeignKeyViolations, migrations = verification.Migrations, sidecars_before_reopen = sidecarsBeforeReopen, sidecars_after_reopen = Sidecars(database), pass = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
            var sqlite = exception as SqliteException ?? exception.InnerException as SqliteException;
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { card = "S8-T04", scenario = "import", rows = products * 2, checkpoint, iteration, root, database_path = database, source_path = source, seed_path = seed, token, pass = false, occurred_at_utc = DateTime.UtcNow, exception_type = exception.GetType().FullName, exception_message = exception.Message, exception_stack = exception.ToString(), sqlite_error_code = sqlite?.SqliteErrorCode, sqlite_extended_error_code = sqlite?.SqliteExtendedErrorCode }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }

    private static DatabaseSnapshot RunReference(string scenario, string checkpoint, string? importSeed = null, string? importSource = null)
    {
        var token = Guid.NewGuid().ToString("N"); var root = NewRoot(token); var database = Path.Combine(root, "database", "app.db"); var marker = Path.Combine(root, "marker.json");
        Directory.CreateDirectory(root);
        if (scenario == "import")
        {
            var seed = Path.Combine(root, "source", "seed.xlsx"); var source = Path.Combine(root, "source", "import.xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(seed)!);
            File.Copy(importSeed!, seed); File.Copy(importSource!, source);
            importSeed = seed; importSource = source;
        }
        using var worker = StartWorker(root, database, marker, token, scenario, checkpoint, "reference", importSeed, importSource, importSource is null ? null : HashFile(importSource));
        try { Assert.True(worker.WaitForExit(scenario == "import" ? 600_000 : 30_000)); Assert.Equal(0, worker.ExitCode); }
        finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "completed.json")));
        Assert.Equal(token, document.RootElement.GetProperty("token").GetString());
        return JsonSerializer.Deserialize<DatabaseSnapshot>(document.RootElement.GetProperty("snapshot").GetRawText()) ?? throw new InvalidOperationException("Reference snapshot missing.");
    }

    private static Process StartWorker(string root, string databasePath, string markerPath, string token, string scenario, string checkpoint, string mode, string? importSeed = null, string? importSource = null, string? importSha256 = null)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            Arguments = $"vstest \"{typeof(S8T04CrashConsistencyTests).Assembly.Location}\" --TestCaseFilter:\"FullyQualifiedName={WorkerName}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.Environment["S8_T04_WORKER"] = "1";
        info.Environment["S8_T04_ROOT"] = root;
        info.Environment["S8_T04_DATABASE"] = databasePath;
        info.Environment["S8_T04_MARKER"] = markerPath;
        info.Environment["S8_T04_TOKEN"] = token;
        info.Environment["S8_T04_SCENARIO"] = scenario;
        info.Environment["S8_T04_CHECKPOINT"] = checkpoint;
        info.Environment["S8_T04_MODE"] = mode;
        info.Environment["S8_T04_PARENT_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (scenario == "import") { info.Environment["S8_T04_IMPORT_SEED"] = importSeed; info.Environment["S8_T04_IMPORT_SOURCE"] = importSource; info.Environment["S8_T04_IMPORT_SHA256"] = importSha256; }
        return Process.Start(info) ?? throw new InvalidOperationException("Unable to start the S8-T04 worker.");
    }

    private static JsonElement WaitForMarker(string markerPath, string token)
    {
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(600))
        {
            if (File.Exists(markerPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
                    var root = document.RootElement;
                    if (root.GetProperty("token").GetString() == token && root.GetProperty("parent_pid").GetInt32() == Environment.ProcessId && root.GetProperty("worker_pid").GetInt32() > 0)
                    {
                        return root.Clone();
                    }
                }
                catch (JsonException) { }
            }
            Thread.Sleep(25);
        }
        throw new TimeoutException("S8-T04 worker did not reach its requested checkpoint.");
    }

    private static void AssertWorkerExited(int pid, long startedAtUtcTicks)
    {
        try { using var process = Process.GetProcessById(pid); Assert.NotEqual(startedAtUtcTicks, process.StartTime.ToUniversalTime().Ticks); }
        catch (ArgumentException) { }
    }

    private static StoreDbContext Open(string databasePath, params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<StoreDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString()).AddInterceptors(interceptors).Options);

    private static void Seed(string databasePath, string scenario)
    {
        using var context = DatabaseInitializer.CreateContext(databasePath);
        var product = new Product { ProductCode = "S8T04-" + scenario, CurrentName = "synthetic", CurrentBarcode = "690000000001", ExcelStockQty = 10, EffectiveStockQty = 10, EffectiveStockSource = "excel", LifecycleGeneration = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
        context.Products.Add(product); context.SaveChanges();
        var batches = new[] { 5, 8 }.Select((days, index) => new Batch { ProductId = product.Id, ExpiryDate = Day.AddDays(days), ShelfLifeValue = 1, ShelfLifeUnit = "M", CurrentArrivalQty = 10, MaxArrivalQty = 10, LifecycleGeneration = 1, TrackingStatus = "active", CurrentStage = index == 0 ? ExpiryStageCalculator.Expired : ExpiryStageCalculator.Discount20, NextTriggerDate = Day, AttentionVersion = 1, HandledAttentionVersion = 0, CreatedAtUtc = Utc, UpdatedAtUtc = Utc }).ToArray();
        context.Batches.AddRange(batches); context.SaveChanges();
        var task = new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Expired, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
        context.Tasks.Add(task); context.SaveChanges();
        var items = batches.Select((batch, index) => new ProductTaskItem { TaskId = task.Id, BatchId = batch.Id, ProductId = product.Id, Stage = index == 0 ? ExpiryStageCalculator.Expired : ExpiryStageCalculator.Discount20, AttentionVersion = 1, CreatedAtUtc = Utc, UpdatedAtUtc = Utc }).ToArray();
        context.TaskItems.AddRange(items); context.SaveChanges();
        var draft = new InspectionDraft { TaskId = task.Id, InspectorName = "S8T04", CheckDate = Day, CreatedAtUtc = Utc, UpdatedAtUtc = Utc };
        context.Drafts.Add(draft); context.SaveChanges();
        context.DraftItems.AddRange(items.Select((item, index) => new InspectionDraftItem { DraftId = draft.Id, TaskItemId = item.Id, TaskId = task.Id, CheckedQty = index == 0 ? 0 : 3, ConfirmedAttentionVersion = 1 })); context.SaveChanges();
    }

    private static void SeedImport(string databasePath, string seed, string source, string sourceSha256)
    {
        S8T03ImportPerformanceTests.SeedExisting(databasePath, seed, Path.Combine(Path.GetDirectoryName(databasePath)!, "seed-snapshots"));
        Assert.Equal(sourceSha256, HashFile(source));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static object[] Sidecars(string databasePath) => new[] { databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" }.Where(File.Exists).Select(path => new { name = Path.GetFileName(path), bytes = new FileInfo(path).Length, last_write_utc = File.GetLastWriteTimeUtc(path) }).Cast<object>().ToArray();

    private static DatabaseSnapshot Snapshot(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name";
        using var tables = command.ExecuteReader(); var raw = new StringBuilder(); var canonical = new StringBuilder(); var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (tables.Read()) { var name = tables.GetString(0); using var values = connection.CreateCommand(); values.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"")}\" ORDER BY rowid"; using var reader = values.ExecuteReader(); raw.Append(name).Append('|'); canonical.Append(name).Append('|'); var count = 0; while (reader.Read()) { for (var i = 0; i < reader.FieldCount; i++) { AppendFingerprintValue(raw, reader, i, name, false); AppendFingerprintValue(canonical, reader, i, name, true); } raw.Append('\u001e'); canonical.Append('\u001e'); count++; } counts[name] = count; }
        return new(Hash(raw), Hash(canonical), counts);
    }

    private static (bool IntegrityOk, int ForeignKeyViolations, int Migrations) Verify(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False"); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check"; var integrity = string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        command.CommandText = "PRAGMA foreign_key_check"; var violations = 0; using (var fk = command.ExecuteReader()) while (fk.Read()) violations++;
        command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory"; return (integrity, violations, Convert.ToInt32(command.ExecuteScalar()));
    }

    private static void AssertAuthorityReadsAndWritable(string databasePath)
    {
        using var context = DatabaseInitializer.CreateContext(databasePath);
        _ = new InspectionTaskQuery().Dashboard(context); _ = new InspectionTaskQuery().SearchOpenTasks(context, new());
        var history = new InspectionHistoryQuery().ListPage(context, new());
        if (history.Items.Count > 0) Assert.NotNull(new InspectionHistoryQuery().GetDetail(context, history.Items[0].InspectionId).Detail);
        var setting = context.Settings.Single(); setting.ReminderMinuteOfDay = setting.ReminderMinuteOfDay == 601 ? 602 : 601; context.SaveChanges();
    }

    private static void AssertSnapshotMetadata(string databasePath)
    {
        using var context = DatabaseInitializer.CreateContext(databasePath);
        var root = Directory.GetParent(Path.GetDirectoryName(databasePath)!)!.FullName;
        foreach (var import in context.Imports.Where(item => item.Status == ImportStatuses.Succeeded))
        {
            AssertSafePath(root, import.PreImportSnapshotPath!);
            var backup = Assert.Single(context.BackupRecords.Where(item => item.FilePath == import.PreImportSnapshotPath));
            Assert.Equal("verified", backup.VerificationStatus);
            Assert.True(backup.CreatedAtUtc > DateTime.UnixEpoch && backup.CreatedAtUtc <= DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(backup.Sha256, HashFile(backup.FilePath));
        }
    }

    private static void AssertSnapshotEqual(DatabaseSnapshot expected, DatabaseSnapshot actual, bool canonical = false)
    {
        Assert.Equal(canonical ? expected.CanonicalFingerprint : expected.RawFingerprint, canonical ? actual.CanonicalFingerprint : actual.RawFingerprint);
        Assert.Equal(expected.Counts.OrderBy(item => item.Key), actual.Counts.OrderBy(item => item.Key));
    }

    private static string Hash(StringBuilder value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()))).ToLowerInvariant();
    private static void AppendFingerprintValue(StringBuilder text, SqliteDataReader reader, int index, string table, bool normalize) { text.Append(reader.GetName(index)).Append(':').Append(reader.GetDataTypeName(index)).Append(':'); if (reader.IsDBNull(index)) text.Append("null:0"); else if (normalize && ((table == "imports" && reader.GetName(index) == "pre_import_snapshot_path") || (table == "backups" && (reader.GetName(index) == "file_path" || reader.GetName(index) == "sha256" || reader.GetName(index) == "created_at_utc")))) text.Append("snapshot-metadata:normalized"); else if (reader.GetValue(index) is byte[] bytes) text.Append("blob:").Append(bytes.Length).Append(':').Append(Convert.ToHexString(bytes)); else { var value = Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty; text.Append(reader.GetFieldType(index).FullName).Append(':').Append(value.Length).Append(':').Append(value); } text.Append('\u001f'); }
    private static string NewRoot(string token) => Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T04", token);
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing {name}.");
    private static WorkerInput ValidateWorkerInputs(string root, string token, string databasePath, string markerPath, string scenario, string checkpoint, string mode, string parentPid, string? importSource, string? importSeed, string? importSha256)
    {
        Assert.Contains(scenario, new[] { "inspection", "inventory", "import" }); Assert.Contains(mode, new[] { "crash", "reference" });
        Assert.Contains(checkpoint, new[] { "partial", "items_partial", "task_handled", "precommit", "postcommit", "started", "products_partial", "batches_partial", "post_task" });
        Assert.True(int.TryParse(parentPid, out var pid) && pid > 0); AssertSafeRoot(root, token); AssertContained(root, databasePath, markerPath); AssertSafePath(root, databasePath); AssertSafePath(root, markerPath);
        if (scenario == "import") { Assert.False(string.IsNullOrWhiteSpace(importSource)); Assert.False(string.IsNullOrWhiteSpace(importSeed)); Assert.Matches("^[0-9a-f]{64}$", importSha256!); AssertSafePath(root, importSource!); AssertSafePath(root, importSeed!); }
        else Assert.True(importSource is null && importSeed is null && importSha256 is null);
        return new(root, token, databasePath, markerPath, scenario, checkpoint, mode, pid, importSource, importSeed, importSha256);
    }
    private static WorkerInput ValidateThenInitialize(string root, string token, string databasePath, string markerPath, string scenario, string checkpoint, string mode, string parentPid, string? importSource, string? importSeed, string? importSha256, Action<string> initialize)
    {
        var input = ValidateWorkerInputs(root, token, databasePath, markerPath, scenario, checkpoint, mode, parentPid, importSource, importSeed, importSha256);
        initialize(input.DatabasePath);
        return input;
    }
    private static void AssertSafeRoot(string root, string token) { var baseRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T04")); var full = Path.GetFullPath(root); Assert.True(Guid.TryParseExact(token, "N", out _)); Assert.Equal(Path.Combine(baseRoot, token), full, ignoreCase: true); AssertSafePath(baseRoot, full); }
    private static void AssertSafePath(string root, string path) { AssertContained(root, path); var fullRoot = Path.GetFullPath(root); for (var current = Path.GetFullPath(path); ; current = Directory.GetParent(current)?.FullName ?? throw new InvalidOperationException("Path escaped the S8-T04 root.")) { if (File.Exists(current) || Directory.Exists(current)) Assert.False(File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)); if (string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase)) return; Assert.StartsWith(fullRoot + Path.DirectorySeparatorChar, current, StringComparison.OrdinalIgnoreCase); } }
    private static void AssertContained(string root, params string[] paths) { var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; foreach (var path in paths) Assert.StartsWith(fullRoot, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase); }

    private sealed class CrashCheckpoint(string token, string markerPath, string checkpoint, string scenario, int parentPid)
    {
        private bool _blocked;
        private bool PostEnabled { get; set; }
        public void MarkPostEnabled() => PostEnabled = true;
        public void OnSqlSucceeded(string sql) { if (_blocked) return; var expected = checkpoint switch { "partial" => scenario == "inspection" ? "INSERT INTO \"inspections\"" : "INSERT INTO \"inventory_adjustments\"", "items_partial" => "INSERT INTO \"inspection_items\"", "task_handled" => "UPDATE \"tasks\"", "products_partial" => "INSERT INTO \"products\"", "batches_partial" => "INSERT INTO \"batches\"", "post_task" when PostEnabled => "INSERT INTO \"tasks\"", _ => null }; if (expected is not null && sql.Contains(expected, StringComparison.OrdinalIgnoreCase)) Block("sql_succeeded:" + expected); }
        public void OnStarted() { if (checkpoint == "started") Block("transaction_started"); }
        public void OnCommitting() { if (checkpoint == "precommit") Block("transaction_committing"); }
        public void OnCommitted() { if (checkpoint == "postcommit") Block("transaction_committed"); }
        private void Block(string stage) { if (_blocked) return; _blocked = true; Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!); using var process = Process.GetCurrentProcess(); File.WriteAllText(markerPath, JsonSerializer.Serialize(new { token, parent_pid = parentPid, worker_pid = Environment.ProcessId, worker_start_utc_ticks = process.StartTime.ToUniversalTime().Ticks, scenario, checkpoint, stage })); Thread.Sleep(Timeout.Infinite); }
    }

    private sealed class CrashCommandInterceptor(CrashCheckpoint checkpoint) : DbCommandInterceptor
    {
        public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result) { checkpoint.OnSqlSucceeded(command.CommandText); return result; }
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) { checkpoint.OnSqlSucceeded(command.CommandText); return result; }
    }

    private sealed class CrashTransactionInterceptor(CrashCheckpoint checkpoint) : DbTransactionInterceptor
    {
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) { checkpoint.OnStarted(); return result; }
        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) { checkpoint.OnCommitting(); return result; }
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => checkpoint.OnCommitted();
    }

    private sealed record DatabaseSnapshot(string RawFingerprint, string CanonicalFingerprint, Dictionary<string, int> Counts);
    private sealed record WorkerInput(string Root, string Token, string DatabasePath, string MarkerPath, string Scenario, string Checkpoint, string Mode, int ParentPid, string? ImportSource, string? ImportSeed, string? ImportSha256);
}
