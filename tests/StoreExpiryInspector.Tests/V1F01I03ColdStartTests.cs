using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V1F01I03ColdStartTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTime Utc = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ColdStartUsesSchemeCAndAggregatesOnlyTaskStages()
    {
        using var database = SqliteTestDatabase.Create();
        using (var context = database.Open())
        {
            var import = AddImport(context);
            Add(context, "P", 5, Day.AddDays(20), Day.AddDays(-340)); // 20%.
            Add(context, "P", 5, Day.AddDays(7), Day.AddDays(-353)); // withdraw.
            Add(context, "P", 5, Day, Day.AddDays(-360)); // expiry today.
            Add(context, "P", 5, Day.AddDays(-3), Day.AddDays(-100)); // catchup.
            Add(context, "P", 5, Day.AddDays(-4), Day.AddDays(-100)); // historical.
            Add(context, "ZERO", 0, Day.AddDays(-1), Day.AddDays(-100));
            context.SaveChanges();
            var result = Execute(context, import.Id);
            Assert.True(result.Started);
        }
        using var verify = database.Open();
        var baselines = verify.BatchBaselines.AsNoTracking().OrderBy(item => item.Id).ToArray();
        Assert.Equal(6, baselines.Length);
        Assert.Equal(3, baselines.Count(item => item.SourceTaskId.HasValue));
        Assert.Single(verify.Tasks.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Expired, verify.Tasks.Single().HighestStage);
        Assert.Contains(baselines, item => item.ColdStartDisposition == ColdStartDispositions.ExpiredCatchupTask && item.CatchupWindowDays == 3 && item.CatchupSource == "historical_window");
        Assert.Contains(baselines, item => item.ColdStartDisposition == ColdStartDispositions.StockZeroBaseline && item.SourceTaskId is null);
        Assert.Empty(verify.Inspections);
        Assert.Empty(verify.InspectionItemRevisions);
        Assert.Empty(verify.LifecycleEvents);
    }

    [Fact]
    public void InvalidActualShelfLifeIsHistoricalOnlyAndAuditable()
    {
        using var database = SqliteTestDatabase.Create();
        using (var context = database.Open())
        {
            var import = AddImport(context);
            Add(context, "P", 5, Day.AddDays(-1), null);
            Add(context, "P", 5, Day.AddDays(-1), Day);
            context.SaveChanges();
            Execute(context, import.Id);
        }
        using var verify = database.Open();
        Assert.Equal(2, verify.BatchBaselines.Count(item => item.ColdStartDisposition == ColdStartDispositions.ExpiredHistoricalBaseline));
        Assert.Equal(2, verify.ImportIssues.Count(item => item.IssueType == "cold_start_actual_shelf_life_unavailable"));
        Assert.Empty(verify.Tasks);
    }

    [Fact]
    public void ReplayAndNonV1RequestLeaveCompletedBaselineUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        using (var context = database.Open())
        {
            importId = AddImport(context).Id;
            Add(context, "P", 5, Day.AddDays(-1), Day.AddDays(-100));
            context.SaveChanges();
            var first = Execute(context, importId);
            var completedAt = context.ScopeBaselines.Single().CompletedAtUtc;
            Assert.True(Execute(context, importId).AlreadyCompleted);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColdStartScopeBaselineUseCase().Execute(context,
                new("food", ExpiryPolicies.Food, 2, importId, Day, Utc)));
            Assert.Equal(completedAt, context.ScopeBaselines.Single().CompletedAtUtc);
        }
        using var verify = database.Open();
        Assert.Single(verify.ScopeBaselines);
        Assert.Single(verify.BatchBaselines);
        Assert.Single(verify.Tasks);
    }

    [Fact]
    public void TaskInsertFailureRollsBackAndRetryCompletes()
    {
        using var database = SqliteTestDatabase.Create();
        long importId;
        using (var context = database.Open())
        {
            importId = AddImport(context).Id;
            Add(context, "P", 5, Day.AddDays(-1), Day.AddDays(-100));
            context.SaveChanges();
            context.Database.ExecuteSqlRaw("CREATE TRIGGER fail_i03_task BEFORE INSERT ON tasks BEGIN SELECT RAISE(ABORT, 'i03 test'); END;");
            Assert.ThrowsAny<Exception>(() => Execute(context, importId));
        }
        using (var verify = database.Open())
        {
            Assert.Empty(verify.ScopeBaselines);
            Assert.Empty(verify.BatchBaselines);
            Assert.Empty(verify.Tasks);
            verify.Database.ExecuteSqlRaw("DROP TRIGGER fail_i03_task;");
            Assert.True(Execute(verify, importId).Started);
        }
        using var completed = database.Open();
        Assert.Single(completed.ScopeBaselines);
        Assert.Single(completed.Tasks);
    }

    private static ColdStartScopeBaselineResult Execute(StoreDbContext context, long importId) =>
        new ColdStartScopeBaselineUseCase().Execute(context, new("food", ExpiryPolicies.Food, 1, importId, Day, Utc));

    private static ImportRecord AddImport(StoreDbContext context)
    {
        var import = new ImportRecord { SourceFileName = "i03.xlsx", SourceFileSha256 = Guid.NewGuid().ToString("N").PadRight(64, '0'), ParsedAtUtc = Utc, ConfirmedAtUtc = Utc, Status = ImportStatuses.Succeeded };
        context.Imports.Add(import); context.SaveChanges(); return import;
    }

    private static void Add(StoreDbContext context, string code, int stock, DateOnly expiry, DateOnly? production)
    {
        var product = context.Products.SingleOrDefault(item => item.ProductCode == code);
        if (product is null)
        {
            product = new Product { ProductCode = code, CategoryCode = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = stock };
            context.Products.Add(product); context.SaveChanges();
        }
        context.Batches.Add(new Batch { ProductId = product.Id, ProductionDate = production, ExpiryDate = expiry, ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 1, MaxArrivalQty = 1, TrackingStatus = "active" });
    }
}

public sealed class V1F01I03RealSampleTests
{
    [Fact]
    public void RealSampleMatchesApprovedColdStartReconciliationWhenExplicitlyEnabled()
    {
        var source = Environment.GetEnvironmentVariable("V1_F01_I03_REAL_EXCEL");
        if (string.IsNullOrWhiteSpace(source)) return;
        var file = new FileInfo(source);
        Assert.Equal(2522641, file.Length);
        Assert.Equal("BBD91AE4E40E5381D749F8DB8F4CC0A600FB88D8C1CF6EA160C7C33EC1A3F0F6", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var database = SqliteTestDatabase.Create();
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(source);
            var plan = new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(new ImportConfirmationGuard().BindPreview(source, workbook, plan)).Contract);
        }
        using (var context = database.Open())
        {
            var result = new ConfirmedImportLifecycleOrchestrator().Execute(context, new(contract, Path.Combine(database.Directory, "snapshots"), new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 9, 1), new DateTime(2026, 9, 1, 8, 1, 0, DateTimeKind.Utc)));
            Assert.True(result.Succeeded, result.Code);
        }
        using var verify = database.Open();
        var open = verify.Tasks.Where(task => task.Status == "open").ToArray();
        Assert.Equal(583, open.Length);
        Assert.Equal(210, open.Count(task => task.HighestStage == ExpiryStageCalculator.Withdraw));
        Assert.Equal(373, open.Count(task => task.HighestStage == ExpiryStageCalculator.Expired));
        Assert.DoesNotContain(open, task => task.HighestStage is ExpiryStageCalculator.Discount50 or ExpiryStageCalculator.Discount20);
    }
}
