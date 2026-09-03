using System.Diagnostics;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
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
        AssertIntegrity(databasePath);

        var measures = new List<Measure>();
        var query = new InspectionTaskQuery();
        MeasurePath(measures, databasePath, "dashboard", context => query.Dashboard(context));
        MeasurePath(measures, databasePath, "open_first_page", context => query.SearchOpenTasks(context, new()));
        MeasurePath(measures, databasePath, "open_deep_page", context => query.SearchOpenTasks(context, new(Page: 1000)));
        MeasurePath(measures, databasePath, "open_search", context => query.SearchOpenTasks(context, new(SearchText: "S8-OPEN-00001")));
        MeasurePath(measures, databasePath, "open_stage", context => query.SearchOpenTasks(context, new(Stage: "expired")));
        MeasurePath(measures, databasePath, "pending_category_memory_filter", context => PendingMemoryFilter(query, context, null, null, "食品", 1));
        MeasurePath(measures, databasePath, "pending_search_stage_category_memory_filter", context => PendingMemoryFilter(query, context, "S8-OPEN", "expired", "食品", 1));
        MeasurePath(measures, databasePath, "task_detail", context => query.GetDetail(context, 1));
        MeasurePath(measures, databasePath, "today_initial_load", context => query.SearchOpenTasks(context, new(PageSize: int.MaxValue)));
        MeasurePath(measures, databasePath, "today_category_memory_filter", context => PendingMemoryFilter(query, context, null, null, "食品", 1));
        var history = new InspectionHistoryQuery();
        MeasurePath(measures, databasePath, "history_list", context => history.List(context));
        MeasurePath(measures, databasePath, "history_detail", context => history.GetDetail(context, 1));
        MeasurePath(measures, databasePath, "history_revision", context => history.GetItemRevisions(context, 1, 1));
        MeasurePath(measures, databasePath, "product_task_aggregator_no_change", context => new ProductTaskAggregator().Aggregate(context, new(1, [new(1, "discount_20", 1, false)], new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc))));
        MeasurePath(measures, databasePath, "reminder_and_pre_reminder", context => new DailyReminderUseCase(query).Evaluate(context, new DateTime(2026, 9, 3, 12, 0, 0)));
        var snapshotWatch = Stopwatch.StartNew();
        var snapshot = new PreImportSnapshotService().Create(databasePath, backupDirectory);
        snapshotWatch.Stop();
        Assert.True(snapshot.CanProceed, snapshot.Code);
        measures.Add(new("sqlite_backupdatabase_snapshot", [snapshotWatch.Elapsed.TotalMilliseconds], snapshotWatch.Elapsed.TotalMilliseconds, snapshotWatch.Elapsed.TotalMilliseconds, 0, [], "BackupDatabase via PreImportSnapshotService", snapshot.Metadata?.SnapshotPath, Environment.WorkingSet, GC.GetTotalAllocatedBytes()));

        var after = ReadCounts(databasePath);
        Assert.Equal(before, after);
        AssertIntegrity(databasePath);
        var result = new Evidence(root, databasePath, new FileInfo(databasePath).Length, before, after, measures, Indexes(databasePath), Plans(databasePath), DateTime.UtcNow, Environment.Version.ToString(), Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, snapshot.Metadata?.SnapshotPath, snapshot.Metadata?.Sha256);
        var json = Path.Combine(root, "S8-T01-baseline.json");
        File.WriteAllText(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"S8-T01 JSON: {json}");
        Console.WriteLine($"S8-T01 counts: batches={before.Batches}; inspections={before.Inspections}; inspection_items={before.InspectionItems}");
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorS8T01", $"S8-T01-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Assert.True(Path.IsPathFullyQualified(root) && root.Contains("S8-T01-", StringComparison.Ordinal) && !root.StartsWith(Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase));
        return root;
    }

    private static StoreDbContext Open(string path) => DatabaseInitializer.CreateContext(path);

    // This is the PendingTasksViewModel shape: one PageSize=int.MaxValue query, then CategoryName Where and UI paging in memory.
    private static object PendingMemoryFilter(InspectionTaskQuery query, StoreDbContext context, string? search, string? stage, string category, int page)
    {
        var all = query.SearchOpenTasks(context, new(search, stage, 1, int.MaxValue)).Items;
        return all.Where(item => item.CategoryName == category).Skip((page - 1) * 50).Take(50).ToArray();
    }

    private static void Seed(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "INSERT INTO imports (id,source_file_name,source_file_sha256,parsed_at_utc,confirmed_at_utc,status,product_count,batch_count,new_product_count,new_batch_count,updated_batch_count,issue_count,unsupported_category_count,new_task_product_count,is_undone) VALUES (1,'S8-T01','0000000000000000000000000000000000000000000000000000000000000000','2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z','succeeded',0,0,0,0,0,0,0,0,0);");
        Execute(connection, transaction, "INSERT INTO scope_baselines (id,scope_key,policy_code,policy_version,created_import_id,business_date,created_at_utc,is_completed,completed_at_utc) VALUES (1,'food','food_expiry',1,1,'2026-09-03','2026-09-03T00:00:00.0000000Z',1,'2026-09-03T00:00:00.0000000Z');");
        Execute(connection, transaction, "WITH d(v) AS (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)), n(i) AS (SELECT a.v+10*b.v+100*c.v+1000*e.v+10000*f.v+1 FROM d a,d b,d c,d e,d f) INSERT INTO products(id,product_code,current_name,current_barcode,category_code,policy_code,policy_version,expiry_management_status,excel_stock_qty,effective_stock_qty,effective_stock_source,lifecycle_generation,is_stock_zero_terminated,created_at_utc,updated_at_utc) SELECT i,printf('S8-OPEN-%05d',i),CASE WHEN i%20=0 THEN '应季搭配' WHEN i%21=0 THEN '赠品小样' ELSE '食品' END,printf('B%05d',i),'food','food_expiry',1,'managed',10,10,'seed',0,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z' FROM n;");
        Execute(connection, transaction, "INSERT INTO products(id,product_code,current_name,current_barcode,category_code,policy_code,policy_version,expiry_management_status,excel_stock_qty,effective_stock_qty,effective_stock_source,lifecycle_generation,is_stock_zero_terminated,created_at_utc,updated_at_utc) VALUES(100001,'S8-EXCLUDED-SEASONAL','应季搭配','S8-E1','seasonal_assortment',NULL,NULL,'excluded',1,1,'seed',0,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z'),(100002,'S8-EXCLUDED-GIFT','赠品小样','S8-E2','gift_sample',NULL,NULL,'excluded',1,1,'seed',0,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z');");
        Execute(connection, transaction, "INSERT INTO batches(id,product_id,production_date,expiry_date,shelf_life_value,shelf_life_unit,current_arrival_qty,max_arrival_qty,lifecycle_generation,tracking_status,current_stage,attention_version,handled_attention_version,created_at_utc,updated_at_utc) SELECT id,id,'2026-01-01','2026-09-04',246,'D',10,10,0,'active',CASE id%4 WHEN 0 THEN 'discount_50' WHEN 1 THEN 'discount_20' WHEN 2 THEN 'withdraw' ELSE 'expired' END,1,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z' FROM products;");
        Execute(connection, transaction, "INSERT INTO tasks(id,product_id,status,highest_stage,created_at_utc,updated_at_utc,closed_at_utc) SELECT id,id,'open',CASE id%4 WHEN 0 THEN 'discount_50' WHEN 1 THEN 'discount_20' WHEN 2 THEN 'withdraw' ELSE 'expired' END,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z',NULL FROM products;");
        Execute(connection, transaction, "INSERT INTO task_items(id,task_id,batch_id,product_id,stage,attention_version,requires_reconfirmation,created_at_utc,updated_at_utc) SELECT id,id,id,id,CASE id%4 WHEN 0 THEN 'discount_50' WHEN 1 THEN 'discount_20' WHEN 2 THEN 'withdraw' ELSE 'expired' END,1,0,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z' FROM products;");
        Execute(connection, transaction, "WITH d(v) AS (VALUES(0),(1),(2)), n(i) AS (SELECT p.id+100000+100000*d.v FROM products p,d) INSERT INTO tasks(id,product_id,status,highest_stage,created_at_utc,updated_at_utc,closed_at_utc) SELECT i,((i-100001)%100000)+1,'completed','expired','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z' FROM n;");
        Execute(connection, transaction, "INSERT INTO inspections(id,task_id,product_id,product_code_snapshot,product_name_snapshot,barcode_snapshot,stage_snapshot,stock_qty_snapshot,inspector_name,check_date,submitted_at_utc) SELECT id-100000,id,((id-100001)%100000)+1,printf('S8-HISTORY-%06d',id),'S8-T01 history',printf('HB%06d',id),'expired',10,'S8-T01','2026-09-01','2026-09-01T00:00:00.0000000Z' FROM tasks WHERE id>100000;");
        Execute(connection, transaction, "INSERT INTO inspection_items(id,inspection_id,product_id,batch_id,production_date_snapshot,expiry_date_snapshot,stage_snapshot,arrival_qty_snapshot,checked_qty,updated_at_utc) SELECT id,id,((id-1)%100000)+1,((id-1)%100000)+1,'2026-01-01','2026-09-04','expired',10,9,'2026-09-01T00:00:00.0000000Z' FROM inspections;");
        Execute(connection, transaction, "INSERT INTO inspection_item_revisions(id,inspection_item_id,previous_checked_qty,new_checked_qty,changed_at_utc) VALUES(1,1,8,9,'2026-09-02T00:00:00.0000000Z');");
        Execute(connection, transaction, "INSERT INTO drafts(id,task_id,inspector_name,check_date,is_invalid,invalid_reason,invalidated_at_utc,created_at_utc,updated_at_utc) VALUES(1,1,'S8-T01','2026-09-03',0,NULL,NULL,'2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z'),(2,2,'S8-T01','2026-09-03',1,'S8-T01 invalid sample','2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z','2026-09-03T00:00:00.0000000Z');");
        Execute(connection, transaction, "INSERT INTO draft_items(id,draft_id,task_item_id,task_id,checked_qty,confirmed_attention_version) VALUES(1,1,1,1,9,1),(2,2,2,2,9,1);");
        transaction.Commit();
        using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM tasks t JOIN products p ON p.id=t.product_id WHERE p.expiry_management_status='excluded'";
        Assert.Equal(0L, (long)verify.ExecuteScalar()!);
    }

    private static void MeasurePath(List<Measure> target, string path, string name, Func<StoreDbContext, object> action)
    {
        try
        {
            using (var warm = Open(path)) _ = action(warm);
            var samples = new List<double>(); var commands = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var interceptor = new Capture();
                var options = new DbContextOptionsBuilder<StoreDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString()).AddInterceptors(interceptor).Options;
                using var context = new StoreDbContext(options);
                var watch = Stopwatch.StartNew(); _ = action(context); watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds); commands.AddRange(interceptor.Commands);
            }
            var ordered = samples.Order().ToArray();
            target.Add(new(name, samples, ordered[ordered.Length / 2], ordered[^1], commands.Count / 3, commands.Distinct().Take(8).ToArray(), "warm=1; measured=3; cold=process-start not measured", null, Environment.WorkingSet, GC.GetTotalAllocatedBytes()));
        }
        catch (Exception exception) { target.Add(new(name, [], 0, 0, 0, [], $"blocker={exception.GetType().Name}: {exception.Message}", null, Environment.WorkingSet, GC.GetTotalAllocatedBytes())); }
    }

    private static Counts ReadCounts(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True"); connection.Open();
        return new(Scalar(connection, "batches"), Scalar(connection, "inspections"), Scalar(connection, "inspection_items"));
    }
    private static long Scalar(SqliteConnection c, string table) { using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT COUNT(*) FROM {table}"; return (long)cmd.ExecuteScalar()!; }
    private static void AssertIntegrity(string path) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = "PRAGMA integrity_check;"; Assert.Equal("ok", (string)x.ExecuteScalar()!); x.CommandText = "PRAGMA foreign_key_check;"; using var r = x.ExecuteReader(); Assert.False(r.Read()); r.Close(); x.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory"; Assert.Equal(9L, (long)x.ExecuteScalar()!); }
    private static string[] Indexes(string path) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); using var x = c.CreateCommand(); x.CommandText = "SELECT name FROM sqlite_master WHERE type='index' ORDER BY name"; using var r = x.ExecuteReader(); var rows = new List<string>(); while (r.Read()) rows.Add(r.GetString(0)); return rows.ToArray(); }
    private static string[] Plans(string path) { using var c = new SqliteConnection($"Data Source={path};Foreign Keys=True"); c.Open(); var sql = new[] { "SELECT t.id FROM tasks t JOIN products p ON t.product_id=p.id WHERE t.status='open'", "SELECT i.id FROM inspections i JOIN tasks t ON i.task_id=t.id WHERE t.status='completed' ORDER BY i.submitted_at_utc DESC", "SELECT b.id FROM batches b JOIN products p ON b.product_id=p.id WHERE b.tracking_status='active' AND p.effective_stock_qty>0" }; return sql.SelectMany(statement => { using var x = c.CreateCommand(); x.CommandText = "EXPLAIN QUERY PLAN " + statement; using var r = x.ExecuteReader(); var rows = new List<string>(); while (r.Read()) rows.Add($"{statement} => {r.GetString(3)}"); return rows; }).ToArray(); }
    private static void Execute(SqliteConnection c, SqliteTransaction t, string sql) { using var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private sealed class Capture : DbCommandInterceptor { public List<string> Commands { get; } = []; public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { Commands.Add(command.CommandText); return result; } }
    private sealed record Counts(long Batches, long Inspections, long InspectionItems);
    private sealed record Measure(string Name, IReadOnlyList<double> SamplesMs, double MedianMs, double MaxMs, int CommandCount, IReadOnlyList<string> Sql, string Conditions, string? Artifact, long WorkingSetBytes, long ManagedAllocatedBytes);
    private sealed record Evidence(string Root, string DatabasePath, long DatabaseBytes, Counts Before, Counts After, IReadOnlyList<Measure> Measures, IReadOnlyList<string> ExistingIndexes, IReadOnlyList<string> QueryPlans, DateTime CreatedUtc, string DotNet, int LogicalProcessors, long TotalAvailableMemoryBytes, string? SnapshotPath, string? SnapshotSha256);
}
