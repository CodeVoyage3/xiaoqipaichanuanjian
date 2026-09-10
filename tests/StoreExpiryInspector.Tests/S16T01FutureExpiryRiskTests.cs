using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S16T01FutureExpiryRiskTests
{
    [Fact]
    public void Synthetic100kOverviewAndFirstPageAreBounded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"s16-perf-{Guid.NewGuid():N}.db");
        try
        {
            DatabaseInitializer.Initialize(path);
            using var context = DatabaseInitializer.CreateContext(path);
            var import = new ImportRecord { SourceFileName = "s16-perf.xlsx", SourceFileSha256 = new string('b', 64), Status = "succeeded" };
            context.Imports.Add(import); context.SaveChanges();
            context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
            context.Products.AddRange(Enumerable.Range(0, 1_000).Select(i => Product($"P{i:D4}", 1))); context.SaveChanges();
            using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                context.Database.OpenConnection();
                command.CommandText = """
                    WITH RECURSIVE n(value) AS (SELECT 0 UNION ALL SELECT value + 1 FROM n WHERE value < 99999)
                    INSERT INTO batches (product_id, production_date, expiry_date, shelf_life_value, shelf_life_unit, current_arrival_qty, max_arrival_qty, lifecycle_generation, tracking_status, current_stage, attention_version, handled_attention_version)
                    SELECT (value % 1000) + 1, date('1900-01-01', '+' || value || ' days'), '2026-02-07', 270, 'D', 1, 1, 0, 'active', 'none', 0, 0 FROM n;
                    """;
                command.ExecuteNonQuery(); context.Database.CloseConnection();
            }
            context.ChangeTracker.Clear();
            var query = new FutureExpiryRiskQuery(); var watch = Stopwatch.StartNew(); var overview = query.Overview(context, new DateOnly(2026, 1, 1)); var overviewMs = watch.ElapsedMilliseconds;
            watch.Restart(); var page = query.Search(context, new DateOnly(2026, 1, 1), new(7, ExpiryStageCalculator.Discount50)); var pageMs = watch.ElapsedMilliseconds;
            Console.WriteLine($"S16_100K overview_ms={overviewMs} first_page_ms={pageMs} candidates=100000 results={page.TotalCount}");
            Assert.Equal(100_000, context.Batches.Count()); Assert.Equal(1_000, overview.Cells.Single(x => x.Days == 7 && x.Stage == ExpiryStageCalculator.Discount50).Count); Assert.Equal(50, page.Items.Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void WindowsDeduplicateRepresentativesSortPageAndRemainReadOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"s16-{Guid.NewGuid():N}.db");
        try
        {
            DatabaseInitializer.Initialize(path);
            using (var context = DatabaseInitializer.CreateContext(path))
            {
                var import = new ImportRecord { SourceFileName = "s16.xlsx", SourceFileSha256 = new string('a', 64), Status = "succeeded" };
                context.Imports.Add(import); context.SaveChanges();
                context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
                var a = Product("A", 10); var b = Product("B", 10); var excluded = Product("X", 0);
                context.Products.AddRange(a, b, excluded);
                // Food/270 has 5折节点 expiry-30: exactly day 7 is included; day 8 is not.
                context.Batches.AddRange(Batch(a, new DateOnly(2026, 2, 7)), Batch(a, new DateOnly(2026, 2, 6)), Batch(b, new DateOnly(2026, 2, 8)), Batch(excluded, new DateOnly(2026, 2, 7)));
                context.SaveChanges();
                var writes = context.ChangeTracker.Entries().Count();
                var query = new FutureExpiryRiskQuery();
                var overview = query.Overview(context, new DateOnly(2026, 1, 1));
                var page = query.Search(context, new DateOnly(2026, 1, 1), new(7, ExpiryStageCalculator.Discount50));
                Assert.Equal(1, overview.Cells.Single(x => x.Days == 7 && x.Stage == ExpiryStageCalculator.Discount50).Count);
                Assert.Single(page.Items); Assert.Equal("A", page.Items[0].ProductCode); Assert.Equal(6, page.Items[0].DaysUntil);
                Assert.Equal(writes, context.ChangeTracker.Entries().Count());
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task DetailUsesFiftyRowsAndClickRouteReturnsHome()
    {
        var page = new FutureExpiryRiskPage(Enumerable.Range(1, 50).Select(i => new FutureExpiryRiskItem(i, i, "p", "b", i.ToString("D3"), 1, "none", new DateOnly(2026, 1, 2), 1)).ToArray(), 51, 1, 50);
        var vm = new StoreExpiryInspector.UI.FutureExpiryRiskViewModel(_ => page); await vm.OpenAsync(7, ExpiryStageCalculator.Discount50);
        Assert.Equal(50, vm.Items.Count); Assert.Equal(2, vm.TotalPages);
        var shell = new StoreExpiryInspector.UI.ShellViewModel(dashboardLoader: () => new(0, 0, 0, 0, 0, []));
        shell.OpenFutureRisk(7, ExpiryStageCalculator.Discount50); Assert.Equal(StoreExpiryInspector.UI.ShellPage.FutureExpiryRisk, shell.CurrentPage);
        shell.ReturnFromFutureRiskCommand.Execute(null); Assert.Equal(StoreExpiryInspector.UI.ShellPage.Dashboard, shell.CurrentPage);
    }

    [Fact]
    public async Task FailedLoadClearsPriorRowsAndStaticPrototypeContractRemainsPresent()
    {
        var row = new FutureExpiryRiskItem(1, 1, "p", "b", "p", 1, "none", new DateOnly(2026, 1, 2), 1);
        var calls = 0;
        var vm = new StoreExpiryInspector.UI.FutureExpiryRiskViewModel(_ => ++calls == 1 ? new([row], 1, 1, 50) : throw new InvalidOperationException("test"));
        await vm.OpenAsync(7, ExpiryStageCalculator.Discount50); Assert.Single(vm.Items);
        Assert.Equal(ExpiryStageCalculator.Discount50, vm.TargetStageBadge.HighestStage);
        await vm.OpenAsync(14, ExpiryStageCalculator.Withdraw); Assert.Empty(vm.Items); Assert.Equal(0, vm.TotalCount); Assert.True(vm.HasError);
        Assert.Equal(ExpiryStageCalculator.Withdraw, vm.TargetStageBadge.HighestStage);
        var root = FindRepositoryRoot(); var xaml = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        Assert.True(xaml.IndexOf("优先处理", StringComparison.Ordinal) < xaml.IndexOf("未来效期风险", StringComparison.Ordinal));
        foreach (var parameter in new[] { "7|discount_50", "7|discount_20", "7|withdraw", "7|expired", "14|discount_50", "14|discount_20", "14|withdraw", "14|expired", "30|discount_50", "30|discount_20", "30|withdraw", "30|expired" }) Assert.Contains(parameter, xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#EDF6FF\"", xaml, StringComparison.Ordinal); Assert.Contains("时间范围", xaml, StringComparison.Ordinal); Assert.Contains("5折", xaml, StringComparison.Ordinal); Assert.Contains("2折", xaml, StringComparison.Ordinal); Assert.Contains("收仓", xaml, StringComparison.Ordinal); Assert.Contains("过期", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1,1,1,1\"", xaml, StringComparison.Ordinal); Assert.Contains("后续阶段", xaml, StringComparison.Ordinal); Assert.Contains("预计进入后续阶段日期", xaml, StringComparison.Ordinal); Assert.Contains("所选时间范围内进入指定未来后续阶段", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigationHomeButton", xaml, StringComparison.Ordinal); Assert.Contains("IsHomeSectionVisible", xaml, StringComparison.Ordinal); Assert.Contains("返回首页", xaml, StringComparison.Ordinal); Assert.Contains("未来效期风险明细列表", xaml, StringComparison.Ordinal); Assert.Contains("Height=\"420\"", xaml, StringComparison.Ordinal); Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal); Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal); Assert.Contains("PreviewMouseWheel=\"FutureRiskDataGrid_PreviewMouseWheel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationFutureRisk", xaml, StringComparison.Ordinal); Assert.DoesNotContain("Chart", xaml, StringComparison.Ordinal); Assert.DoesNotContain("处理风险", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExpiryPolicies.Food, 270, "D")]
    [InlineData(ExpiryPolicies.Pet, 12, "M")]
    [InlineData(ExpiryPolicies.GeneralLong, 1, "Y")]
    public void ApprovedPoliciesSupplyAllFourDates(string policy, int value, string unit)
    {
        var days = unit switch { "D" => value, "M" => value * 30, _ => value * 365 };
        var dates = ExpiryPolicyCalculator.CalculateStageDates(policy, 1, new DateOnly(2027, 1, 1), days);
        Assert.NotNull(dates); Assert.True(dates!.Discount50 < dates.Discount20 && dates.Discount20 < dates.Withdraw && dates.Withdraw < dates.Expired);
    }

    [Fact]
    public void QueryHonorsAllWindowsEligibilityRepresentationSortingAndReadOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"s16-matrix-{Guid.NewGuid():N}.db"); var today = new DateOnly(2026, 1, 1);
        try
        {
            DatabaseInitializer.Initialize(path); using var db = DatabaseInitializer.CreateContext(path);
            var import = new ImportRecord { SourceFileName = "matrix.xlsx", SourceFileSha256 = new string('c', 64), Status = "succeeded" }; db.Imports.Add(import); db.SaveChanges();
            foreach (var policy in new[] { ExpiryPolicies.Food, ExpiryPolicies.Pet, ExpiryPolicies.GeneralLong }) db.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = policy, PolicyCode = policy, PolicyVersion = 1, CreatedImportId = import.Id, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
            var query = new FutureExpiryRiskQuery(); var serial = 0;
            foreach (var days in new[] { 7, 14, 30 }) foreach (var stage in Stages())
            {
                foreach (var offset in new[] { 0, days, days + 1 })
                {
                    var product = Product($"P{serial++ :D3}", 1, ExpiryPolicies.Food, ExpiryPolicies.Food); db.Products.Add(product);
                    db.Batches.Add(BatchForNode(product, ExpiryPolicies.Food, 270, "D", today, stage, today.AddDays(offset)));
                }
            }
            foreach (var valid in new[] { (ExpiryPolicies.Food, 270, "D"), (ExpiryPolicies.Pet, 12, "M"), (ExpiryPolicies.GeneralLong, 1, "Y") })
            { var product = Product($"V{serial++ :D3}", 1, valid.Item1, valid.Item1); product.CategoryCode = valid.Item1; db.Products.Add(product); db.Batches.Add(BatchForNode(product, valid.Item1, valid.Item2, valid.Item3, today, ExpiryStageCalculator.Withdraw, today.AddDays(7))); }
            var stopped = Product("stopped", 1, ExpiryPolicies.Food, ExpiryPolicies.Food); var zero = Product("zero", 0, ExpiryPolicies.Food, ExpiryPolicies.Food); var excluded = Product("excluded", 1, ExpiryPolicies.Food, ExpiryPolicies.Food); excluded.ExpiryManagementStatus = ExpiryManagementStatus.Excluded; excluded.PolicyCode = null; excluded.PolicyVersion = null; var unresolved = Product("unresolved", 1, ExpiryPolicies.Food, ExpiryPolicies.Food); unresolved.ExpiryManagementStatus = ExpiryManagementStatus.Unresolved; unresolved.PolicyCode = null; unresolved.PolicyVersion = null; var noBaseline = Product("nobase", 1, ExpiryPolicies.Food, "other"); noBaseline.CategoryCode = "other";
            db.Products.AddRange(stopped, zero, excluded, unresolved, noBaseline); foreach (var item in new[] { stopped, zero, excluded, unresolved, noBaseline }) db.Batches.Add(BatchForNode(item, ExpiryPolicies.Food, 270, "D", today, ExpiryStageCalculator.Withdraw, today.AddDays(7), item == stopped ? "stopped" : "active"));
            db.SaveChanges();
            var before = (db.Products.Count(), db.Batches.Count(), db.Tasks.Count(), db.TaskItems.Count(), db.Batches.OrderBy(x => x.Id).AsEnumerable().Select(x => (x.CurrentStage, x.NextTriggerDate)).ToArray());
            foreach (var days in new[] { 7, 14, 30 }) foreach (var stage in Stages())
            { var result = query.Search(db, today, new(days, stage)); Assert.DoesNotContain(result.Items, x => x.ProductCode.StartsWith("stopped") || x.ProductCode.StartsWith("zero") || x.ProductCode.StartsWith("excluded") || x.ProductCode.StartsWith("unresolved") || x.ProductCode.StartsWith("nobase")); Assert.Equal(query.Overview(db, today).Cells.Single(x => x.Days == days && x.Stage == stage).Count, result.TotalCount); }
            foreach (var stage in Stages()) foreach (var days in new[] { 7, 14, 30 }) { Assert.DoesNotContain(query.Search(db, today, new(days, stage)).Items, x => x.EffectiveDate == today); Assert.Contains(query.Search(db, today, new(days, stage)).Items, x => x.EffectiveDate == today.AddDays(days)); Assert.DoesNotContain(query.Search(db, today, new(days, stage)).Items, x => x.EffectiveDate == today.AddDays(days + 1)); }
            var after = (db.Products.Count(), db.Batches.Count(), db.Tasks.Count(), db.TaskItems.Count(), db.Batches.OrderBy(x => x.Id).AsEnumerable().Select(x => (x.CurrentStage, x.NextTriggerDate)).ToArray()); Assert.Equal(before.Item1, after.Item1); Assert.Equal(before.Item2, after.Item2); Assert.Equal(before.Item3, after.Item3); Assert.Equal(before.Item4, after.Item4); Assert.Equal(before.Item5, after.Item5);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SameDayRepresentativeSortsAndPaginatesFiftyThenOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"s16-page-{Guid.NewGuid():N}.db"); var today = new DateOnly(2026, 1, 1);
        try
        {
            DatabaseInitializer.Initialize(path); using var db = DatabaseInitializer.CreateContext(path);
            var import = new ImportRecord { SourceFileName = "page.xlsx", SourceFileSha256 = new string('d', 64), Status = "succeeded" }; db.Imports.Add(import); db.SaveChanges();
            db.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = ExpiryPolicies.Food, PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = import.Id, IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
            Product? duplicate = null;
            for (var i = 50; i >= 0; i--) { var product = Product($"C{i:D3}", 1, ExpiryPolicies.Food, ExpiryPolicies.Food); db.Products.Add(product); db.Batches.Add(BatchForNode(product, ExpiryPolicies.Food, 270, "D", today, ExpiryStageCalculator.Withdraw, today.AddDays(7))); if (i == 25) duplicate = product; }
            db.SaveChanges(); var extra = BatchForNode(duplicate!, ExpiryPolicies.Food, 270, "D", today, ExpiryStageCalculator.Withdraw, today.AddDays(7)); extra.ProductionDate = new DateOnly(2025, 1, 2); db.Batches.Single(x => x.ProductId == duplicate!.Id).ProductionDate = new DateOnly(2025, 1, 1); db.Batches.Add(extra); db.SaveChanges();
            var allBatches = db.Batches.Where(x => x.ProductId == duplicate!.Id).OrderBy(x => x.Id).Select(x => x.Id).ToArray(); var query = new FutureExpiryRiskQuery(); var first = query.Search(db, today, new(7, ExpiryStageCalculator.Withdraw)); var second = query.Search(db, today, new(7, ExpiryStageCalculator.Withdraw, 2)); var combined = first.Items.Concat(second.Items).ToArray();
            Assert.Equal(51, first.TotalCount); Assert.Equal(50, first.Items.Count); Assert.Single(second.Items); Assert.Equal(allBatches[0], combined.Single(x => x.ProductId == duplicate!.Id).RepresentativeBatchId); Assert.Equal(combined.OrderBy(x => x.EffectiveDate).ThenBy(x => x.ProductCode, StringComparer.Ordinal).ThenBy(x => x.ProductId).Select(x => x.ProductId), combined.Select(x => x.ProductId));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static IEnumerable<string> Stages() => [ExpiryStageCalculator.Discount50, ExpiryStageCalculator.Discount20, ExpiryStageCalculator.Withdraw, ExpiryStageCalculator.Expired];
    private static Batch BatchForNode(Product product, string policy, int shelfLife, string unit, DateOnly today, string stage, DateOnly target, string tracking = "active")
    {
        for (var expiry = today.AddDays(-200); expiry <= today.AddDays(600); expiry = expiry.AddDays(1)) { var dates = ExpiryPolicyCalculator.CalculateStageDates(policy, 1, expiry, unit == "D" ? shelfLife : unit == "M" ? shelfLife * 30 : shelfLife * 365); if (dates is not null && (stage switch { "discount_50" => dates.Discount50, "discount_20" => dates.Discount20, "withdraw" => dates.Withdraw, _ => dates.Expired }) == target) return new Batch { Product = product, ExpiryDate = expiry, ShelfLifeValue = shelfLife, ShelfLifeUnit = unit, CurrentArrivalQty = 1, MaxArrivalQty = 1, TrackingStatus = tracking }; }
        throw new InvalidOperationException("node not found");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("repository root");
    }

    private static Product Product(string code, int stock, string policy = ExpiryPolicies.Food, string category = "food") => new() { ProductCode = code, CurrentName = code, CurrentBarcode = code, CategoryCode = category, PolicyCode = policy, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = stock };
    private static Batch Batch(Product product, DateOnly expiry) => new() { Product = product, ExpiryDate = expiry, ShelfLifeValue = 270, ShelfLifeUnit = "D", CurrentArrivalQty = 1, MaxArrivalQty = 1, TrackingStatus = "active" };
}
