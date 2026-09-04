using System.Diagnostics;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Backups;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S8T01PerformanceBaselineTests
{
    private const string Gate = "S8_T01_PERF";
    private const int BatchCount = 100_000;
    private const int InspectionCount = 300_000;

    [Fact]
    public void LargeHistoricalBaselineIsExplicitlyGated()
    {
        if (Environment.GetEnvironmentVariable(Gate) == "1") return;
        Assert.NotEqual("1", Environment.GetEnvironmentVariable(Gate));
    }

    [Fact]
    public void S8T01PathGuardRejectsProductionAndUnsafeRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T01", "S8-T01-guard");
        Assert.Throws<ArgumentException>(() => ValidateRoot("relative", "relative.db", root));
        Assert.Throws<ArgumentException>(() => ValidateRoot(Path.GetTempPath(), DatabaseInitializer.GetDefaultDatabasePath(), root));
        Assert.Throws<ArgumentException>(() => ValidateRoot(root, Path.Combine(root, "app.db"), DatabaseInitializer.GetDefaultBackupDirectory()));
        Assert.Throws<ArgumentException>(() => ValidateRoot(Environment.CurrentDirectory, Path.Combine(Environment.CurrentDirectory, "S8-T01.db"), root));
        Assert.Throws<ArgumentException>(() => ValidateRoot(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "S8-T01.db"), Path.Combine(Path.GetTempPath(), "S8-T01-snapshot")));
        Assert.Throws<ArgumentException>(() => ValidateRoot(root, Path.Combine(Path.GetTempPath(), "elsewhere", "S8-T01.db"), Path.Combine(root, "S8-T01-snapshot")));
        Assert.Throws<ArgumentException>(() => ValidateRoot(root, Path.Combine(root, "S8-T01.db"), Path.Combine(Path.GetTempPath(), "elsewhere", "S8-T01-snapshot")));
    }

    [Fact]
    [Trait("Category", "S8-T01")]
    public void MeasuresIsolated100kBatch300kInspectionBaseline()
    {
        if (Environment.GetEnvironmentVariable(Gate) != "1") return;
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "S8-T01-app.db");
        var backupDirectory = Path.Combine(root, "S8-T01-snapshot");
        DatabaseInitializer.Initialize(databasePath);
        Seed(databasePath);
        var before = ReadCounts(databasePath);
        Assert.Equal(BatchCount, before.Batches);
        Assert.Equal(InspectionCount, before.Inspections);
        Assert.Equal(InspectionCount, before.InspectionItems);
        var databaseVerificationBefore = Verify(databasePath);
        Assert.True(databaseVerificationBefore.IntegrityOk); Assert.Equal(0, databaseVerificationBefore.ForeignKeyViolations); Assert.Equal(9, databaseVerificationBefore.MigrationCount);
        var fingerprintBefore = Fingerprint(databasePath);

        var measures = new List<Measure>();
        var query = new InspectionTaskQuery();
        MeasurePath(measures, databasePath, "dashboard", context => query.Dashboard(context));
        MeasurePath(measures, databasePath, "open_first_page", context => query.SearchOpenTasks(context, new()));
        MeasurePath(measures, databasePath, "open_deep_page", context => query.SearchOpenTasks(context, new(Page: 1000)));
        MeasurePath(measures, databasePath, "open_search", context =>
        {
            var result = query.SearchOpenTasks(context, new(SearchText: "S8-OPEN-00003"));
            Assert.Equal(1, result.TotalCount);
            return result;
        }, condition: "hit_count=1");
        MeasurePath(measures, databasePath, "open_stage", context => query.SearchOpenTasks(context, new(Stage: "expired")));
        MeasurePath(measures, databasePath, "pending_category", context => query.SearchOpenTasks(context, new(CategoryName: "食品")));
        MeasurePath(measures, databasePath, "pending_search_stage", context => query.SearchOpenTasks(context, new("S8-OPEN", "expired")));
        MeasurePath(measures, databasePath, "pending_search_category", context => query.SearchOpenTasks(context, new("S8-OPEN", CategoryName: "食品")));
        MeasurePath(measures, databasePath, "pending_stage_category", context => query.SearchOpenTasks(context, new(Stage: "expired", CategoryName: "食品")));
        MeasurePath(measures, databasePath, "pending_search_stage_category", context => query.SearchOpenTasks(context, new("S8-OPEN", "expired", 1, 50, "食品")));
        MeasurePath(measures, databasePath, "task_detail", context => query.GetDetail(context, 3));
        MeasurePath(measures, databasePath, "today_initial_load", context => query.SearchOpenTasks(context, new(Page: 1, PageSize: 50)));
        MeasurePath(measures, databasePath, "today_category", context => query.SearchOpenTasks(context, new(CategoryName: "食品")));
        var history = new InspectionHistoryQuery();
        MeasurePath(measures, databasePath, "history_list", context => history.ListPage(context, new()));
        MeasurePath(measures, databasePath, "history_detail", context => history.GetDetail(context, 3));
        MeasurePath(measures, databasePath, "history_revision", context => history.GetItemRevisions(context, 3, 3));
        var productTaskBefore = ProductTaskFingerprint(databasePath, 3);
        MeasurePath(measures, databasePath, "product_task_aggregator_no_change", context =>
        {
            var result = new ProductTaskAggregator().Aggregate(context, new(3, [new(3, "expired", 1, false)], new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc)));
            Assert.False(result.Changed);
            Assert.Equal(productTaskBefore, ProductTaskFingerprint(databasePath, 3));
            return result;
        }, root);
        MeasurePath(measures, databasePath, "reminder_candidates", context => query.GetReminderCandidates(context));
        MeasurePath(measures, databasePath, "reminder_and_pre_reminder", context => new DailyReminderUseCase(query).Evaluate(context, new DateTime(2026, 9, 3, 12, 0, 0)));
        PreImportSnapshotResult? snapshot = null;
        var snapshotWatch = Stopwatch.StartNew();
        try { snapshot = new PreImportSnapshotService().Create(databasePath, backupDirectory); }
        catch (Exception exception) { measures.Add(FailedMeasure("sqlite_backupdatabase_snapshot", exception, snapshotWatch.Elapsed, [])); throw; }
        finally { snapshotWatch.Stop(); }
        Assert.NotNull(snapshot); Assert.True(snapshot.CanProceed, snapshot.Code); Assert.NotNull(snapshot.Metadata);
        var snapshotVerification = Verify(snapshot.Metadata.SnapshotPath);
        Assert.True(snapshotVerification.IntegrityOk); Assert.Equal(0, snapshotVerification.ForeignKeyViolations); Assert.Equal(9, snapshotVerification.MigrationCount);
        measures.Add(new("sqlite_backupdatabase_snapshot", [snapshotWatch.Elapsed.TotalMilliseconds], snapshotWatch.Elapsed.TotalMilliseconds, snapshotWatch.Elapsed.TotalMilliseconds, 0, [], "BackupDatabase via PreImportSnapshotService; warm=not_applicable", null, Environment.WorkingSet, GC.GetTotalAllocatedBytes(), "snapshot", "not_proven", 0, 0, []));

        var after = ReadCounts(databasePath);
        AssertCountsEqual(before, after);
        var databaseVerificationAfter = Verify(databasePath);
        Assert.True(databaseVerificationAfter.IntegrityOk); Assert.Equal(0, databaseVerificationAfter.ForeignKeyViolations); Assert.Equal(9, databaseVerificationAfter.MigrationCount);
        var fingerprintAfter = Fingerprint(databasePath);
        Assert.Equal(fingerprintBefore, fingerprintAfter);
        var excluded = ExcludedChecks(databasePath);
        Assert.Equal(2, excluded.Products); Assert.Equal(2, excluded.Batches); Assert.Equal(0, excluded.OpenTasks); Assert.Equal(0, excluded.ReminderEligibleBatches);
        var result = new Evidence(Environment.GetEnvironmentVariable("S8_T01_COMMIT") ?? "not_proven", root, databasePath, new FileInfo(databasePath).Length, FileHash(databasePath), databaseVerificationAfter, before, after, fingerprintBefore, fingerprintAfter, measures, Indexes(databasePath), BuildDiagnostics(measures), excluded, DateTime.UtcNow, RuntimeInformation.OSDescription, Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "not_proven", Environment.Version.ToString(), SqliteVersion(databasePath), typeof(SqliteConnection).Assembly.GetName().Version?.ToString() ?? "not_proven", Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, "fixed_seed=S8-T01-20260903; iterations=3; warm=1; cold=process_start_not_measured", snapshot.Metadata.SnapshotPath, snapshot.Metadata.Sha256, snapshot.Metadata.FileSize, snapshotVerification);
        var json = Path.Combine(root, "S8-T01-baseline.json");
        File.WriteAllText(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"S8-T01 JSON: {json}");
        Console.WriteLine($"S8-T01 counts: batches={before.Batches}; inspections={before.Inspections}; inspection_items={before.InspectionItems}");
        Assert.DoesNotContain(measures, measure => measure.Blocker is not null);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T01", $"S8-T01-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        ValidateRoot(root, Path.Combine(root, "S8-T01-app.db"), Path.Combine(root, "S8-T01-snapshot"));
        return root;
    }

    private static void ValidateRoot(string root, string databasePath, string backupPath)
    {
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(databasePath) || !Path.IsPathFullyQualified(backupPath))
            throw new ArgumentException("S8-T01 requires absolute paths.");
        root = Path.GetFullPath(root); databasePath = Path.GetFullPath(databasePath); backupPath = Path.GetFullPath(backupPath);
        var parent = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "StoreExpiryInspectorS8T01");
        if (!IsChild(parent, root) || !Path.GetFileName(root).StartsWith("S8-T01-", StringComparison.OrdinalIgnoreCase) || !IsChild(root, databasePath) || !IsChild(root, backupPath))
            throw new ArgumentException("S8-T01 requires a uniquely marked TEMP root.");
    }
    private static bool IsChild(string parent, string child) { var relative = Path.GetRelativePath(parent, child); return !Path.IsPathRooted(relative) && !relative.Equals("..", StringComparison.Ordinal) && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal); }

    private static StoreDbContext Open(string path) => DatabaseInitializer.CreateContext(path);


    private static void Seed(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "INSERT INTO imports (id,source_file_name,source_file_sha256,parsed_at_utc,confirmed_at_utc,status,product_count,batch_count,new_product_count,new_batch_count,updated_batch_count,issue_count,unsupported_category_count,new_task_product_count,is_undone) VALUES (1,'S8-T01','0000000000000000000000000000000000000000000000000000000000000000','2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z','succeeded',0,0,0,0,0,0,0,0,0);");
        Execute(connection, transaction, "INSERT INTO scope_baselines (id,scope_key,policy_code,policy_version,created_import_id,business_date,created_at_utc,is_completed,completed_at_utc) VALUES (1,'food','food_expiry',1,1,'2026-09-03','2026-09-03T00:00:00.0000000Z',1,'2026-09-03T00:00:00.0000000Z');");
        Execute(connection, transaction, "WITH d(v) AS (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)), n(i) AS (SELECT a.v+10*b.v+100*c.v+1000*e.v+10000*f.v+1 FROM d a,d b,d c,d e,d f) INSERT INTO products(id,product_code,current_name,current_barcode,category_code,policy_code,policy_version,expiry_management_status,excel_stock_qty,effective_stock_qty,effective_stock_source,lifecycle_generation,is_stock_zero_terminated,created_at_utc,updated_at_utc) SELECT i,printf('S8-OPEN-%05d',i),'食品',printf('B%05d',i),'food','food_expiry',1,'managed',10,10,'seed',0,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z' FROM n;");
        Execute(connection, transaction, "UPDATE products SET current_name='应季搭配',category_code='seasonal_assortment',policy_code=NULL,policy_version=NULL,expiry_management_status='excluded' WHERE id=1; UPDATE products SET current_name='赠品小样',category_code='gift_sample',policy_code=NULL,policy_version=NULL,expiry_management_status='excluded' WHERE id=2;");
        Execute(connection, transaction, "INSERT INTO batches(id,product_id,production_date,expiry_date,shelf_life_value,shelf_life_unit,current_arrival_qty,max_arrival_qty,lifecycle_generation,tracking_status,current_stage,attention_version,handled_attention_version,created_at_utc,updated_at_utc) SELECT id,id,'2026-01-01','2026-09-04',246,'D',10,10,0,'active',CASE id%4 WHEN 0 THEN 'discount_50' WHEN 1 THEN 'discount_20' WHEN 2 THEN 'withdraw' ELSE 'expired' END,1,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z' FROM products;");
        Execute(connection, transaction, "INSERT INTO tasks(id,product_id,status,highest_stage,created_at_utc,updated_at_utc,closed_at_utc) SELECT id,id,'open',CASE id%4 WHEN 0 THEN 'discount_50' WHEN 1 THEN 'discount_20' WHEN 2 THEN 'withdraw' ELSE 'expired' END,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z',NULL FROM products WHERE id>2;");
        Execute(connection, transaction, "INSERT INTO task_items(id,task_id,batch_id,product_id,stage,attention_version,requires_reconfirmation,created_at_utc,updated_at_utc) SELECT id,id,id,id,CASE id%4 WHEN 0 THEN 'discount_50' WHEN 1 THEN 'discount_20' WHEN 2 THEN 'withdraw' ELSE 'expired' END,1,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z' FROM products WHERE id>2;");
        Execute(connection, transaction, "WITH d(v) AS (VALUES(0),(1),(2)), n(i) AS (SELECT p.id+99998+99998*d.v FROM products p,d WHERE p.id>2) INSERT INTO tasks(id,product_id,status,highest_stage,created_at_utc,updated_at_utc,closed_at_utc) SELECT i,((i-100001)%99998)+3,'completed','expired','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z' FROM n;");
        Execute(connection, transaction, "WITH n(i) AS (VALUES(399995),(399996),(399997),(399998),(399999),(400000)) INSERT INTO tasks(id,product_id,status,highest_stage,created_at_utc,updated_at_utc,closed_at_utc) SELECT i,i-399992,'completed','expired','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z' FROM n;");
        Execute(connection, transaction, "INSERT INTO inspections(id,task_id,product_id,product_code_snapshot,product_name_snapshot,barcode_snapshot,stage_snapshot,stock_qty_snapshot,inspector_name,check_date,submitted_at_utc) SELECT id-99998,id,((id-100001)%99998)+3,printf('S8-HISTORY-%06d',id),'S8-T01 history',printf('HB%06d',id),'expired',10,'S8-T01','2026-09-01','2026-09-01T00:00:00.0000000Z' FROM tasks WHERE id>100000;");
        Execute(connection, transaction, "INSERT INTO inspection_items(id,inspection_id,product_id,batch_id,production_date_snapshot,expiry_date_snapshot,stage_snapshot,arrival_qty_snapshot,checked_qty,updated_at_utc) SELECT id,id,product_id,product_id,'2026-01-01','2026-09-04','expired',10,9,'2026-09-01T00:00:00.0000000Z' FROM inspections;");
        Execute(connection, transaction, "INSERT INTO inspection_item_revisions(id,inspection_item_id,previous_checked_qty,new_checked_qty,changed_at_utc) VALUES(1,3,8,9,'2026-09-02T00:00:00.0000000Z');");
        Execute(connection, transaction, "INSERT INTO drafts(id,task_id,inspector_name,check_date,is_invalid,invalid_reason,invalidated_at_utc,created_at_utc,updated_at_utc) VALUES(1,3,'S8-T01','2026-09-03',0,NULL,NULL,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z'),(2,4,'S8-T01','2026-09-03',1,'S8-T01 invalid sample','2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z');");
        Execute(connection, transaction, "INSERT INTO draft_items(id,draft_id,task_item_id,task_id,checked_qty,confirmed_attention_version) VALUES(1,1,3,3,9,1),(2,2,4,4,9,1);");
        transaction.Commit();
        using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM tasks t JOIN products p ON p.id=t.product_id WHERE p.expiry_management_status='excluded'";
        Assert.Equal(0L, (long)verify.ExecuteScalar()!);
    }

    private static void MeasurePath(List<Measure> target, string path, string name, Func<StoreDbContext, object> action, string? root = null, string? condition = null)
    {
        var watch = Stopwatch.StartNew();
        var captures = new List<CapturedCommand>();
        try
        {
            using (var warm = Open(path)) _ = action(warm); // Interceptor starts only after warm-up.
            var samples = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                using var context = Open(path);
                var sampleWatch = Stopwatch.StartNew(); _ = action(context); sampleWatch.Stop();
                samples.Add(sampleWatch.Elapsed.TotalMilliseconds);
            }
            var interceptor = new Capture();
            var options = new DbContextOptionsBuilder<StoreDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString()).AddInterceptors(interceptor).Options;
            object capturedResult;
            using (var captured = new StoreDbContext(options)) capturedResult = action(captured);
            captures.AddRange(interceptor.Commands);
            var ordered = samples.Order().ToArray();
            target.Add(new(name, samples, ordered[ordered.Length / 2], ordered[^1], captures.Count, CreateCommandEvidence(path, root ?? Path.GetDirectoryName(path)!, captures), $"warm=1; measured=3 interceptor-free; capture=one unmeasured; cold=process-start not measured; {condition}".TrimEnd(';', ' '), null, Environment.WorkingSet, GC.GetTotalAllocatedBytes(), "snapshot", "not_proven", ReturnedDtoRows(capturedResult), interceptor.ReaderReadOperations, []));
        }
        catch (Exception exception) { target.Add(FailedMeasure(name, exception, watch.Elapsed, captures, CreateCommandEvidence(path, root ?? Path.GetDirectoryName(path)!, captures))); }
        finally { watch.Stop(); }
    }

    private static Counts ReadCounts(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True"); connection.Open();
        return new(new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["products"] = Scalar(connection, "products"), ["batches"] = Scalar(connection, "batches"), ["tasks"] = Scalar(connection, "tasks"), ["task_items"] = Scalar(connection, "task_items"), ["drafts"] = Scalar(connection, "drafts"), ["draft_items"] = Scalar(connection, "draft_items"), ["inspections"] = Scalar(connection, "inspections"), ["inspection_items"] = Scalar(connection, "inspection_items"), ["revisions"] = Scalar(connection, "inspection_item_revisions"), ["scope_baselines"] = Scalar(connection, "scope_baselines")
        });
    }
    private static long Scalar(SqliteConnection c, string table) { using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT COUNT(*) FROM {table}"; return (long)cmd.ExecuteScalar()!; }
    private static void AssertCountsEqual(Counts before, Counts after) { foreach (var pair in before.Tables) Assert.Equal(pair.Value, after.Tables[pair.Key]); }
    private static Verification Verify(string path) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = "PRAGMA integrity_check;"; var integrity = string.Equals("ok", x.ExecuteScalar()?.ToString(), StringComparison.OrdinalIgnoreCase); x.CommandText = "PRAGMA foreign_key_check;"; using var r = x.ExecuteReader(); var fk = 0; while (r.Read()) fk++; r.Close(); x.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory"; return new(integrity, fk, Convert.ToInt32(x.ExecuteScalar())); }
    private static string[] Indexes(string path) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = "SELECT name FROM sqlite_master WHERE type='index' ORDER BY name"; using var r = x.ExecuteReader(); var rows = new List<string>(); while (r.Read()) rows.Add(r.GetString(0)); return rows.ToArray(); }
    private static void Execute(SqliteConnection c, SqliteTransaction t, string sql) { using var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private static long ScalarSql(string path, string sql) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = sql; return Convert.ToInt64(x.ExecuteScalar()); }
    private static string Fingerprint(string path)
    {
        var fields = new Dictionary<string, string> { ["products"] = "product_code", ["batches"] = "current_stage", ["tasks"] = "status", ["task_items"] = "stage", ["drafts"] = "is_invalid", ["draft_items"] = "checked_qty", ["inspections"] = "product_code_snapshot", ["inspection_items"] = "checked_qty", ["inspection_item_revisions"] = "new_checked_qty", ["scope_baselines"] = "scope_key" };
        using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open();
        return HashText(string.Join(";", fields.Select(pair => $"{pair.Key}:{StableRows(c, pair.Key, pair.Value)}")));
    }
    private static string StableRows(SqliteConnection c, string table, string field) { using var x = c.CreateCommand(); x.CommandText = $"SELECT COUNT(*),MIN(id),MAX(id),COALESCE((SELECT group_concat(id || ':' || {field}, '|') FROM (SELECT id,{field} FROM {table} ORDER BY id LIMIT 2)),''),COALESCE((SELECT group_concat(id || ':' || {field}, '|') FROM (SELECT id,{field} FROM {table} ORDER BY id DESC LIMIT 2)), '') FROM {table}"; using var r = x.ExecuteReader(); r.Read(); return string.Join("|", Enumerable.Range(0, 5).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i).ToString())); }
    private static string ProductTaskFingerprint(string path, long productId) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = "SELECT p.id || ':' || p.product_code || ':' || t.id || ':' || t.status || ':' || t.highest_stage || ':' || ti.id || ':' || ti.stage || ':' || ti.attention_version || ':' || ti.requires_reconfirmation FROM products p LEFT JOIN tasks t ON t.product_id=p.id AND t.status='open' LEFT JOIN task_items ti ON ti.task_id=t.id WHERE p.id=$id ORDER BY ti.id"; x.Parameters.AddWithValue("$id", productId); using var r = x.ExecuteReader(); var rows = new List<string>(); while (r.Read()) rows.Add(r.GetString(0)); return HashText(string.Join(";", rows)); }
    private static ExcludedCheck ExcludedChecks(string path) => new(ScalarSql(path, "SELECT COUNT(*) FROM products WHERE expiry_management_status='excluded'"), ScalarSql(path, "SELECT COUNT(*) FROM batches b JOIN products p ON p.id=b.product_id WHERE p.expiry_management_status='excluded'"), ScalarSql(path, "SELECT COUNT(*) FROM tasks t JOIN products p ON p.id=t.product_id WHERE t.status='open' AND p.expiry_management_status='excluded'"), ScalarSql(path, "SELECT COUNT(*) FROM batches b JOIN products p ON p.id=b.product_id WHERE p.expiry_management_status='excluded' AND b.tracking_status='active' AND p.effective_stock_qty>0 AND p.expiry_management_status='managed' AND p.policy_version=1 AND p.policy_code IN ('food_expiry','pet_expiry','general_long_expiry') AND EXISTS(SELECT 1 FROM scope_baselines s WHERE s.is_completed=1 AND s.scope_key=p.category_code AND s.policy_code=p.policy_code AND s.policy_version=p.policy_version)"));
    private static string FileHash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string SqliteVersion(string path) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); return c.ServerVersion; }
    private static int ReturnedDtoRows(object result) => result switch { InspectionTaskSearchResult value => value.Items.Count, InspectionDashboardResult value => value.UrgentTasks.Count, InspectionHistoryPageResult value => value.Items.Count, System.Collections.ICollection value => value.Count, _ => 0 };
    private static Measure FailedMeasure(string name, Exception exception, TimeSpan elapsed, IReadOnlyList<CapturedCommand> captures, IReadOnlyList<CommandEvidence>? evidence = null) => new(name, [], elapsed.TotalMilliseconds, elapsed.TotalMilliseconds, captures.Count, evidence ?? [], "warm=1; measured=3; cold=process-start not measured", $"{Classify(exception)}; type={exception.GetType().FullName}; message={exception.Message}; elapsed_ms={elapsed.TotalMilliseconds:F3}", Environment.WorkingSet, GC.GetTotalAllocatedBytes(), "snapshot", "not_proven", 0, 0, captures);
    private static string Classify(Exception exception) => exception is OutOfMemoryException ? "oom" : exception is TimeoutException || exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ? "timeout" : exception is SqliteException && exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) ? "lock" : exception is SqliteException && exception.Message.Contains("too many SQL variables", StringComparison.OrdinalIgnoreCase) ? "too_many_sql_variables" : "crash_or_error";
    private static IReadOnlyList<CommandEvidence> CreateCommandEvidence(string databasePath, string root, IReadOnlyList<CapturedCommand> commands) => commands.Select((command, index) =>
    {
        var hash = HashText(command.Sql);
        string? artifact = null; var inline = command.Sql;
        if (command.Sql.Length > 1000) { artifact = Path.Combine(root, $"S8-T01-command-{index:D4}-{hash[..12]}.sql"); File.WriteAllText(artifact, command.Sql); inline = null; }
        return new CommandEvidence(command.Kind, command.Sql.Length, hash, command.Parameters, inline, artifact, Explain(databasePath, command));
    }).ToArray();
    private static IReadOnlyList<string> Explain(string path, CapturedCommand command)
    {
        if (!command.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return [];
        try
        {
            using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = "EXPLAIN QUERY PLAN " + command.Sql;
            foreach (var parameter in command.Parameters) x.Parameters.AddWithValue(parameter.Name, (object?)parameter.Value ?? DBNull.Value);
            using var r = x.ExecuteReader(); var plan = new List<string>(); while (r.Read()) plan.Add(r.GetString(3)); return plan;
        }
        catch (Exception exception) { return [$"ExplainError: {exception.GetType().Name}: {exception.Message}"]; }
    }
    private static Diagnostics BuildDiagnostics(IEnumerable<Measure> measures)
    {
        var plans = measures.SelectMany(measure => measure.Commands).SelectMany(command => command.Plan).ToArray();
        return new(plans.Any(plan => plan.Contains("SCAN", StringComparison.OrdinalIgnoreCase)), plans.Any(plan => plan.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase)), "not_proven", "per-path returned_dto_rows and reader_read_operations are recorded from one unmeasured capture; reader reads can include EOF", "not_proven", measures.Any(measure => measure.Blocker?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true), measures.Any(measure => measure.Blocker?.Contains("lock", StringComparison.OrdinalIgnoreCase) == true), measures.Any(measure => measure.Blocker?.Contains("crash", StringComparison.OrdinalIgnoreCase) == true), measures.Any(measure => measure.Blocker?.Contains("oom", StringComparison.OrdinalIgnoreCase) == true));
    }
    private sealed class Capture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];
        public int ReaderReadOperations { get; private set; }
        private void Add(DbCommand command, string kind) => Commands.Add(new(kind, command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => new CapturedParameter(parameter.ParameterName, parameter.DbType.ToString(), parameter.Value is DBNull ? null : parameter.Value?.ToString())).ToArray()));
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { Add(command, "reader"); return result; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result) { Add(command, "scalar"); return result; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result) { Add(command, "nonquery"); return result; }
        public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData data, InterceptionResult result) { ReaderReadOperations += data.ReadCount; return result; }
        public override void CommandFailed(DbCommand command, CommandErrorEventData data) => Add(command, "failed");
    }
    private sealed record Counts(IReadOnlyDictionary<string, long> Tables) { public long Batches => Tables["batches"]; public long Inspections => Tables["inspections"]; public long InspectionItems => Tables["inspection_items"]; }
    private sealed record CapturedParameter(string Name, string DbType, string? Value);
    private sealed record CapturedCommand(string Kind, string Sql, IReadOnlyList<CapturedParameter> Parameters);
    private sealed record CommandEvidence(string Kind, int SqlLength, string SqlSha256, IReadOnlyList<CapturedParameter> Parameters, string? Sql, string? ArtifactPath, IReadOnlyList<string> Plan);
    private sealed record Measure(string Name, IReadOnlyList<double> SamplesMs, double MedianMs, double MaxMs, int CommandCount, IReadOnlyList<CommandEvidence> Commands, string Conditions, string? Blocker, long WorkingSetBytes, long ManagedAllocatedBytes, string MemoryKind, string AllocationDelta, int ReturnedDtoRows, int ReaderReadOperations, IReadOnlyList<CapturedCommand> CapturedCommands);
    private sealed record Diagnostics(bool FullScan, bool TempBTree, string NPlusOne, string OverMaterialization, string InMemoryFiltering, bool Timeout, bool Lock, bool Crash, bool Oom);
    private sealed record Verification(bool IntegrityOk, int ForeignKeyViolations, int MigrationCount);
    private sealed record ExcludedCheck(long Products, long Batches, long OpenTasks, long ReminderEligibleBatches);
    private sealed record Evidence(string SourceCommit, string Root, string DatabasePath, long DatabaseBytes, string DatabaseSha256, Verification DatabaseVerification, Counts Before, Counts After, string LogicalFingerprintBefore, string LogicalFingerprintAfter, IReadOnlyList<Measure> Measures, IReadOnlyList<string> ExistingIndexes, Diagnostics Diagnostics, ExcludedCheck ExcludedReminderCheck, DateTime CreatedUtc, string OsDescription, string CpuIdentifier, string DotNet, string SqliteVersion, string SqliteProviderVersion, int LogicalProcessors, long TotalAvailableMemoryBytes, string Conditions, string? SnapshotPath, string? SnapshotSha256, long? SnapshotBytes, Verification? SnapshotVerification);
}
