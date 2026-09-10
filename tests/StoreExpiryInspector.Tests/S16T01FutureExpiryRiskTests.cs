using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
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
            for (var i = 0; i < 100_000; i++) { var product = Product($"P{i:D6}", 1); context.Products.Add(product); context.Batches.Add(Batch(product, new DateOnly(2026, 2, 7))); }
            context.SaveChanges(); context.ChangeTracker.Clear();
            var query = new FutureExpiryRiskQuery(); var watch = Stopwatch.StartNew(); var overview = query.Overview(context, new DateOnly(2026, 1, 1)); var overviewMs = watch.ElapsedMilliseconds;
            watch.Restart(); var page = query.Search(context, new DateOnly(2026, 1, 1), new(7, ExpiryStageCalculator.Discount50)); var pageMs = watch.ElapsedMilliseconds;
            Console.WriteLine($"S16_100K overview_ms={overviewMs} first_page_ms={pageMs} candidates=100000 results={page.TotalCount}");
            Assert.Equal(100_000, overview.Cells.Single(x => x.Days == 7 && x.Stage == ExpiryStageCalculator.Discount50).Count); Assert.Equal(50, page.Items.Count);
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
        await Task.Delay(50); Assert.Equal(50, vm.Items.Count); Assert.Equal(2, vm.TotalPages);
        var shell = new StoreExpiryInspector.UI.ShellViewModel(dashboardLoader: () => new(0, 0, 0, 0, 0, []));
        shell.OpenFutureRisk(7, ExpiryStageCalculator.Discount50); Assert.Equal(StoreExpiryInspector.UI.ShellPage.FutureExpiryRisk, shell.CurrentPage);
        shell.ReturnFromFutureRiskCommand.Execute(null); Assert.Equal(StoreExpiryInspector.UI.ShellPage.Dashboard, shell.CurrentPage);
    }

    private static Product Product(string code, int stock) => new() { ProductCode = code, CurrentName = code, CurrentBarcode = code, CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = stock };
    private static Batch Batch(Product product, DateOnly expiry) => new() { Product = product, ExpiryDate = expiry, ShelfLifeValue = 270, ShelfLifeUnit = "D", CurrentArrivalQty = 1, MaxArrivalQty = 1, TrackingStatus = "active" };
}
