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
            Add(context, "P", 5, Day.AddDays(70), Day.AddDays(-290)); // 50%.
            Add(context, "P", 5, Day.AddDays(20), Day.AddDays(-340)); // 20%.
            Add(context, "P", 5, Day.AddDays(7), Day.AddDays(-353)); // withdraw.
            Add(context, "P", 5, Day, Day.AddDays(-360)); // expiry today.
            Add(context, "P", 5, Day.AddDays(-4), Day.AddDays(-105)); // 101 days => ceil(3.03)=4, inclusive catchup.
            Add(context, "P", 5, Day.AddDays(-4), Day.AddDays(-100)); // historical.
            Add(context, "ZERO", 0, Day.AddDays(-1), Day.AddDays(-100));
            context.SaveChanges();
            context.Batches.OrderByDescending(batch => batch.Id).First().TrackingStatus = "stopped";
            context.SaveChanges();
            var result = Execute(context, import.Id);
            Assert.True(result.Started);
        }
        using var verify = database.Open();
        var baselines = verify.BatchBaselines.AsNoTracking().OrderBy(item => item.Id).ToArray();
        Assert.Equal(7, baselines.Length);
        Assert.Equal(3, baselines.Count(item => item.SourceTaskId.HasValue));
        Assert.Single(verify.Tasks.AsNoTracking());
        Assert.Equal(ExpiryStageCalculator.Expired, verify.Tasks.Single().HighestStage);
        Assert.Contains(baselines, item => item.ColdStartDisposition == ColdStartDispositions.ExpiredCatchupTask && item.CatchupWindowDays == 4 && item.CatchupSource == "historical_window");
        Assert.Contains(baselines, item => item.ColdStartDisposition == ColdStartDispositions.StockZeroBaseline && item.SourceTaskId is null);
        Assert.Contains(baselines, item => item.ColdStartDisposition == ColdStartDispositions.Discount50Baseline && item.SourceTaskId is null);
        Assert.Contains(baselines, item => item.ColdStartDisposition == ColdStartDispositions.Discount20Baseline && item.SourceTaskId is null);
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
    public void ExistingTaskHistoryIsPreservedAndOpenTaskIsMerged()
    {
        using var database = SqliteTestDatabase.Create();
        using (var context = database.Open())
        {
            var import = AddImport(context);
            Add(context, "P", 5, Day.AddDays(-1), Day.AddDays(-100));
            context.SaveChanges();
            var product = context.Products.Single();
            context.Batches.Single().HandledAttentionVersion = 7;
            context.Tasks.Add(new ProductTask { ProductId = product.Id, Status = "completed", HighestStage = ExpiryStageCalculator.Withdraw, CreatedAtUtc = Utc, UpdatedAtUtc = Utc, ClosedAtUtc = Utc });
            context.Tasks.Add(new ProductTask { ProductId = product.Id, Status = "open", HighestStage = ExpiryStageCalculator.Withdraw, CreatedAtUtc = Utc, UpdatedAtUtc = Utc });
            context.SaveChanges();
            var completed = context.Tasks.Single(task => task.Status == "completed");
            var batch = context.Batches.Single();
            var inspection = new Inspection { TaskId = completed.Id, ProductId = product.Id, ProductCodeSnapshot = "P", StageSnapshot = ExpiryStageCalculator.Withdraw, InspectorName = "history", CheckDate = Day, SubmittedAtUtc = Utc };
            context.Inspections.Add(inspection); context.SaveChanges();
            var item = new InspectionItem { InspectionId = inspection.Id, ProductId = product.Id, BatchId = batch.Id, ExpiryDateSnapshot = batch.ExpiryDate, StageSnapshot = ExpiryStageCalculator.Withdraw, UpdatedAtUtc = Utc };
            context.InspectionItems.Add(item); context.SaveChanges();
            context.InspectionItemRevisions.Add(new InspectionItemRevision { InspectionItemId = item.Id, PreviousCheckedQty = 1, NewCheckedQty = 2, ChangedAtUtc = Utc }); context.SaveChanges();
            Assert.True(Execute(context, import.Id).Started);
        }
        using var verify = database.Open();
        Assert.Single(verify.Tasks.Where(task => task.Status == "completed"));
        Assert.Single(verify.Tasks.Where(task => task.Status == "open"));
        Assert.Single(verify.TaskItems);
        Assert.Equal(7, verify.Batches.Single().HandledAttentionVersion);
        Assert.Equal("history", verify.Inspections.Single().InspectorName);
        Assert.Equal(2, verify.InspectionItemRevisions.Single().NewCheckedQty);
    }

    [Fact]
    public void IncompleteBaselineRejectsDifferentImportOrBusinessDateWithoutFacts()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var first = AddImport(context);
        var second = AddImport(context);
        Add(context, "P", 5, Day.AddDays(-1), Day.AddDays(-100));
        context.SaveChanges();
        context.ScopeBaselines.Add(new ScopeBaseline { ScopeKey = "food", PolicyCode = ExpiryPolicies.Food, PolicyVersion = 1, CreatedImportId = first.Id, BusinessDate = Day, CreatedAtUtc = Utc });
        context.SaveChanges();
        Assert.Throws<InvalidOperationException>(() => new ColdStartScopeBaselineUseCase().Execute(context, new("food", ExpiryPolicies.Food, 1, second.Id, Day.AddDays(1), Utc)));
        Assert.Single(context.ScopeBaselines);
        Assert.Empty(context.BatchBaselines);
        Assert.Empty(context.Tasks);
        Assert.Empty(context.ImportIssues);
    }

    [Fact]
    public void ScopeGateAndNonV1RejectionLeaveEveryBusinessFactUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = AddImport(context);
        var excluded = new Product { ProductCode = "X", CategoryCode = "seasonal_assortment", PolicyCode = null, PolicyVersion = null, ExpiryManagementStatus = ExpiryManagementStatus.Excluded, EffectiveStockQty = 5 };
        context.Products.Add(excluded); context.SaveChanges();
        context.Batches.Add(new Batch { ProductId = excluded.Id, ExpiryDate = Day.AddDays(-1), ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 1, MaxArrivalQty = 1 }); context.SaveChanges();
        var batch = context.Batches.Single();
        var before = (context.ScopeBaselines.Count(), context.BatchBaselines.Count(), context.Tasks.Count(), context.TaskItems.Count(), batch.TrackingStatus, batch.CurrentStage, batch.HandledAttentionVersion, context.ImportIssues.Count(), context.Inspections.Count(), context.InspectionItemRevisions.Count(), context.LifecycleEvents.Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColdStartScopeBaselineUseCase().Execute(context, new("food", ExpiryPolicies.Food, 2, import.Id, Day, Utc)));
        Assert.Throws<InvalidOperationException>(() => new ColdStartScopeBaselineUseCase().Execute(context, new("seasonal_assortment", ExpiryPolicies.Food, 1, import.Id, Day, Utc)));
        var after = (context.ScopeBaselines.Count(), context.BatchBaselines.Count(), context.Tasks.Count(), context.TaskItems.Count(), batch.TrackingStatus, batch.CurrentStage, batch.HandledAttentionVersion, context.ImportIssues.Count(), context.Inspections.Count(), context.InspectionItemRevisions.Count(), context.LifecycleEvents.Count());
        Assert.Equal(before, after);
    }

    [Fact]
    public void CatchupClampAndScopeIsolationAreIndependent()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = AddImport(context);
        Add(context, "F", 5, Day.AddDays(-3), Day.AddDays(-4)); // lower clamp 3 inclusive.
        Add(context, "F", 5, Day.AddDays(-4), Day.AddDays(-5)); // lower clamp outside.
        Add(context, "F", 5, Day.AddDays(-30), Day.AddDays(-1030)); // upper clamp 30 inclusive.
        Add(context, "F", 5, Day.AddDays(-31), Day.AddDays(-1031)); // upper clamp outside.
        context.Products.Add(new Product { ProductCode = "PET", CategoryCode = "pet", PolicyCode = ExpiryPolicies.Pet, PolicyVersion = 1, ExpiryManagementStatus = ExpiryManagementStatus.Managed, EffectiveStockQty = 5 }); context.SaveChanges();
        context.Batches.Add(new Batch { ProductId = context.Products.Single(p => p.ProductCode == "PET").Id, ProductionDate = Day.AddDays(-100), ExpiryDate = Day.AddDays(-1), ShelfLifeValue = 12, ShelfLifeUnit = "M", CurrentArrivalQty = 1, MaxArrivalQty = 1 }); context.SaveChanges();
        Assert.True(Execute(context, import.Id).Started);
        Assert.True(new ColdStartScopeBaselineUseCase().Execute(context, new("pet", ExpiryPolicies.Pet, 1, import.Id, Day, Utc)).Started);
        Assert.Equal(2, context.ScopeBaselines.Count());
        var facts = context.BatchBaselines.OrderBy(item => item.BatchId).ToArray();
        Assert.Contains(facts, item => item.ColdStartDisposition == ColdStartDispositions.ExpiredCatchupTask && item.CatchupWindowDays == 3);
        Assert.Contains(facts, item => item.ColdStartDisposition == ColdStartDispositions.ExpiredCatchupTask && item.CatchupWindowDays == 30);
        Assert.Equal(2, facts.Count(item => item.ColdStartDisposition == ColdStartDispositions.ExpiredHistoricalBaseline));
    }

    [Fact]
    public void ShelfLifeOverflowRollsBackWithoutBaselineFacts()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var import = AddImport(context);
        Add(context, "P", 5, Day.AddDays(-1), Day.AddDays(-100)); context.SaveChanges();
        context.Batches.Single().ShelfLifeValue = int.MaxValue; context.Batches.Single().ShelfLifeUnit = "Y"; context.SaveChanges();
        Assert.Throws<OverflowException>(() => Execute(context, import.Id));
        context.ChangeTracker.Clear();
        Assert.Empty(context.ScopeBaselines); Assert.Empty(context.BatchBaselines); Assert.Empty(context.Tasks); Assert.Empty(context.ImportIssues);
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
            Assert.Empty(verify.ImportIssues);
            var batch = verify.Batches.Single();
            Assert.Equal(ExpiryStageCalculator.None, batch.CurrentStage);
            Assert.Null(batch.NextTriggerDate);
            Assert.Equal("active", batch.TrackingStatus);
            Assert.Equal(0, batch.HandledAttentionVersion);
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
        var baselineBatches = verify.BatchBaselines.AsNoTracking().ToArray();
        Assert.Equal(baselineBatches.Length, baselineBatches.Select(item => item.BatchId).Distinct().Count());
        Assert.All(baselineBatches.Where(item => item.ColdStartDisposition is ColdStartDispositions.WithdrawTask or ColdStartDispositions.ExpiredTodayTask or ColdStartDispositions.ExpiredCatchupTask), item => Assert.True(item.SourceTaskId.HasValue));
        Assert.All(baselineBatches.Where(item => item.ColdStartDisposition == ColdStartDispositions.ExpiredCatchupTask), item => Assert.InRange(item.CatchupWindowDays!.Value, 3, 30));
        Assert.DoesNotContain(baselineBatches.Join(verify.Batches.Include(batch => batch.Product), item => item.BatchId, batch => batch.Id, (item, batch) => batch), batch => batch.Product.ExpiryManagementStatus != ExpiryManagementStatus.Managed);
        Assert.Empty(verify.Inspections);
        Assert.Empty(verify.InspectionItemRevisions);
        Assert.Empty(verify.LifecycleEvents);
        Assert.Equal(8, verify.ScopeBaselines.Count(scope => scope.IsCompleted));
        var nonManagedTaskProducts = verify.Tasks.Join(verify.Products, task => task.ProductId, product => product.Id, (task, product) => product).ToArray();
        Assert.DoesNotContain(nonManagedTaskProducts, product => product.ExpiryManagementStatus == ExpiryManagementStatus.Excluded || product.ExpiryManagementStatus == ExpiryManagementStatus.Unresolved);
        Assert.Equal(baselineBatches.Length, baselineBatches.GroupBy(item => item.ColdStartDisposition).Sum(group => group.Count()));
        Assert.Equal(2522641, new FileInfo(source).Length);
        Assert.Equal("BBD91AE4E40E5381D749F8DB8F4CC0A600FB88D8C1CF6EA160C7C33EC1A3F0F6", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
    }
}
