using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InspectionTaskQueryTests
{
    private static readonly DateTime SubmittedAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmptyDatabaseDashboardIsZero()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var result = new InspectionTaskQuery().Dashboard(context);

        Assert.Equal(0, result.OpenTaskCount);
        Assert.Equal(0, result.ExpiredCount);
        Assert.Equal(0, result.WithdrawCount);
        Assert.Equal(0, result.Discount20Count);
        Assert.Equal(0, result.Discount50Count);
        Assert.Empty(result.UrgentTasks);
    }

    [Fact]
    public void DashboardCountsEachOpenTaskOnceAndUsesCanonicalPriorityForStableUrgency()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddOpenTask(seed, "P-EXPIRED", ExpiryStageCalculator.Expired, new DateOnly(2026, 8, 28));
            AddOpenTask(seed, "P-WITHDRAW", ExpiryStageCalculator.Withdraw, new DateOnly(2026, 8, 29));
            AddOpenTask(seed, "P-20", ExpiryStageCalculator.Discount20, new DateOnly(2026, 8, 30));
            AddOpenTask(seed, "P-50", ExpiryStageCalculator.Discount50, new DateOnly(2026, 8, 31));
            var (product, task, firstBatch) = AddOpenTask(
                seed,
                "P-MULTI",
                ExpiryStageCalculator.Expired,
                new DateOnly(2026, 9, 20));
            AddTaskItem(seed, task, product, AddBatch(seed, product.Id, new DateOnly(2026, 8, 27), null, 10), ExpiryStageCalculator.Expired, 0, false);
            firstBatch.CurrentArrivalQty = 2;
            seed.SaveChanges();
        }

        using var context = database.Open();
        var result = new InspectionTaskQuery().Dashboard(context);

        Assert.Equal(5, result.OpenTaskCount);
        Assert.Equal(2, result.ExpiredCount);
        Assert.Equal(1, result.WithdrawCount);
        Assert.Equal(1, result.Discount20Count);
        Assert.Equal(1, result.Discount50Count);
        Assert.Equal(
            new[] { "P-MULTI", "P-EXPIRED", "P-WITHDRAW", "P-20", "P-50" },
            result.UrgentTasks.Select(item => item.ProductCode));
        Assert.All(result.UrgentTasks, item => Assert.True(item.PendingBatchCount >= 1));
        Assert.Equal(
            new[]
            {
                ExpiryStageCalculator.GetStagePriority(ExpiryStageCalculator.Expired),
                ExpiryStageCalculator.GetStagePriority(ExpiryStageCalculator.Withdraw),
                ExpiryStageCalculator.GetStagePriority(ExpiryStageCalculator.Discount20),
                ExpiryStageCalculator.GetStagePriority(ExpiryStageCalculator.Discount50)
            },
            result.UrgentTasks
                .Where(item => item.ProductCode != "P-MULTI")
                .Select(item => ExpiryStageCalculator.GetStagePriority(item.HighestStage)));
    }

    [Fact]
    public void DashboardUrgentTasksAreCappedAtTwenty()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            for (var index = 0; index < 21; index++)
            {
                AddOpenTask(
                    seed,
                    $"P-{index:00}",
                    ExpiryStageCalculator.Discount50,
                    new DateOnly(2026, 9, 1).AddDays(index));
            }
        }

        using var context = database.Open();
        var result = new InspectionTaskQuery().Dashboard(context);

        Assert.Equal(21, result.OpenTaskCount);
        Assert.Equal(20, result.UrgentTasks.Count);
        Assert.Equal("P-00", result.UrgentTasks[0].ProductCode);
        Assert.Equal("P-19", result.UrgentTasks[^1].ProductCode);
    }

    [Fact]
    public void SearchSupportsFieldsStagesAndDefaultFiftyRowPaging()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddOpenTask(seed, "CODE-APPLE", ExpiryStageCalculator.Expired, new DateOnly(2026, 8, 28), "红富士苹果", "BAR-APPLE");
            AddOpenTask(seed, "CODE-BANANA", ExpiryStageCalculator.Withdraw, new DateOnly(2026, 8, 29), "香蕉", "BAR-BANANA");
            AddOpenTask(seed, "CODE-CARROT", ExpiryStageCalculator.Discount20, new DateOnly(2026, 8, 30), "胡萝卜", "BAR-CARROT");
            AddOpenTask(seed, "CODE-DATE", ExpiryStageCalculator.Discount50, new DateOnly(2026, 8, 31), "红枣", "BAR-DATE");
            for (var index = 0; index < 51; index++)
            {
                AddOpenTask(
                    seed,
                    $"PAGE-{index:00}",
                    ExpiryStageCalculator.Discount50,
                    new DateOnly(2026, 10, 1).AddDays(index),
                    $"分页商品{index}",
                    $"PAGE-BAR-{index:00}");
            }
        }

        using var context = database.Open();
        var query = new InspectionTaskQuery();
        Assert.Equal("CODE-APPLE", Assert.Single(query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest("富士")).Items).ProductCode);
        Assert.Equal("CODE-BANANA", Assert.Single(query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest("CODE-BANANA")).Items).ProductCode);
        Assert.Equal("CODE-CARROT", Assert.Single(query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest("BAR-CARROT")).Items).ProductCode);
        Assert.Equal("CODE-APPLE", Assert.Single(query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Stage: ExpiryStageCalculator.Expired)).Items).ProductCode);
        Assert.Equal("CODE-BANANA", Assert.Single(query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Stage: ExpiryStageCalculator.Withdraw)).Items).ProductCode);
        Assert.Equal("CODE-CARROT", Assert.Single(query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Stage: ExpiryStageCalculator.Discount20)).Items).ProductCode);
        var discount50 = query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Stage: ExpiryStageCalculator.Discount50));
        Assert.Equal(52, discount50.TotalCount);
        Assert.All(discount50.Items, item => Assert.Equal(ExpiryStageCalculator.Discount50, item.HighestStage));
        Assert.Equal(55, query.SearchOpenTasks(context, new InspectionTaskSearchRequest()).TotalCount);
        var firstPage = query.SearchOpenTasks(context, new InspectionTaskSearchRequest(Page: 1));
        var secondPage = query.SearchOpenTasks(context, new InspectionTaskSearchRequest(Page: 2));
        Assert.Equal(50, firstPage.Items.Count);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(item => item.TaskId).Intersect(secondPage.Items.Select(item => item.TaskId)));
        Assert.Equal(
            firstPage.Items.Select(item => item.TaskId).Concat(secondPage.Items.Select(item => item.TaskId)),
            query.SearchOpenTasks(context, new InspectionTaskSearchRequest(PageSize: 100)).Items.Select(item => item.TaskId));
        Assert.Throws<ArgumentException>(() => query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Stage: "none")));
        Assert.Throws<ArgumentException>(() => query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Stage: "not-a-stage")));
        Assert.Throws<ArgumentOutOfRangeException>(() => query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(Page: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => query.SearchOpenTasks(
            context,
            new InspectionTaskSearchRequest(PageSize: 0)));
    }

    [Fact]
    public void SearchTreatsBlankTextAsNoFilterAndKeepsStableTieBreak()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddOpenTask(seed, "TIE-A", ExpiryStageCalculator.Discount50, new DateOnly(2026, 9, 1));
            AddOpenTask(seed, "TIE-B", ExpiryStageCalculator.Discount50, new DateOnly(2026, 9, 1));
            AddOpenTask(seed, "TIE-C", ExpiryStageCalculator.Discount50, new DateOnly(2026, 9, 1));
        }

        using var context = database.Open();
        var query = new InspectionTaskQuery();
        var first = query.SearchOpenTasks(context, new InspectionTaskSearchRequest("   "));
        var second = query.SearchOpenTasks(context, new InspectionTaskSearchRequest("   "));

        Assert.Equal(new[] { "TIE-A", "TIE-B", "TIE-C" }, first.Items.Select(item => item.ProductCode));
        Assert.Equal(first.Items.Select(item => item.TaskId), second.Items.Select(item => item.TaskId));
    }

    [Fact]
    public void DetailReturnsCurrentItemsNormalBatchesValidDraftAndLatestFormalResults()
    {
        using var database = SqliteTestDatabase.Create();
        long taskId;
        long firstTaskItemId;
        long secondTaskItemId;
        long firstBatchId;
        long secondBatchId;
        long normalBatchId;
        using (var seed = database.Open())
        {
            var product = NewProduct("DETAIL-001", "详情商品", "DETAIL-BAR", 23);
            seed.Products.Add(product);
            seed.SaveChanges();
            var firstBatch = AddBatch(seed, product.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), 11);
            var secondBatch = AddBatch(seed, product.Id, new DateOnly(2026, 9, 2), null, 12);
            var normalBatch = AddBatch(seed, product.Id, new DateOnly(2026, 9, 3), null, 13);
            normalBatch.CurrentStage = ExpiryStageCalculator.Discount20;
            var stoppedBatch = AddBatch(seed, product.Id, new DateOnly(2026, 9, 4), null, 14);
            stoppedBatch.TrackingStatus = "stopped";
            var task = AddTask(seed, product.Id, ExpiryStageCalculator.Withdraw);
            var firstItem = AddTaskItem(seed, task, product, firstBatch, ExpiryStageCalculator.Discount50, 4, true);
            var secondItem = AddTaskItem(seed, task, product, secondBatch, ExpiryStageCalculator.Withdraw, 7, false);
            taskId = task.Id;
            firstTaskItemId = firstItem.Id;
            secondTaskItemId = secondItem.Id;
            firstBatchId = firstBatch.Id;
            secondBatchId = secondBatch.Id;
            normalBatchId = normalBatch.Id;

            var seededDraft = new InspectionDraft
            {
                TaskId = task.Id,
                InspectorName = "检查员",
                CheckDate = new DateOnly(2026, 8, 27)
            };
            seed.Drafts.Add(seededDraft);
            seed.SaveChanges();
            seed.DraftItems.AddRange(
                new InspectionDraftItem
                {
                    DraftId = seededDraft.Id,
                    TaskItemId = firstItem.Id,
                    TaskId = task.Id,
                    CheckedQty = 0,
                    ConfirmedAttentionVersion = firstItem.AttentionVersion
                },
                new InspectionDraftItem
                {
                    DraftId = seededDraft.Id,
                    TaskItemId = secondItem.Id,
                    TaskId = task.Id,
                    CheckedQty = null,
                    ConfirmedAttentionVersion = secondItem.AttentionVersion
                });
            seed.SaveChanges();

            var oldTask = AddTask(seed, product.Id, ExpiryStageCalculator.Discount50, "completed");
            var newestTask = AddTask(seed, product.Id, ExpiryStageCalculator.Withdraw, "completed");
            var secondBatchTask = AddTask(seed, product.Id, ExpiryStageCalculator.Withdraw, "completed");
            var tieTask = AddTask(seed, product.Id, ExpiryStageCalculator.Withdraw, "completed");
            var normalBatchTask = AddTask(seed, product.Id, ExpiryStageCalculator.Discount20, "completed");
            AddInspection(seed, oldTask, product, firstBatch, 2, SubmittedAtUtc.AddHours(-1));
            AddInspection(seed, newestTask, product, firstBatch, 9, SubmittedAtUtc);
            AddInspection(seed, secondBatchTask, product, secondBatch, 3, SubmittedAtUtc.AddHours(-1));
            AddInspection(seed, tieTask, product, firstBatch, 11, SubmittedAtUtc);
            AddInspection(seed, normalBatchTask, product, normalBatch, 8, SubmittedAtUtc);
        }

        using var context = database.Open();
        var result = new InspectionTaskQuery().GetDetail(context, taskId);

        Assert.Equal("open", result.Status);
        Assert.NotNull(result.Detail);
        var detail = result.Detail!;
        Assert.Equal("DETAIL-001", detail.ProductCode);
        Assert.Equal(23, detail.EffectiveStockQty);
        Assert.Equal(ExpiryStageCalculator.Withdraw, detail.HighestStage);
        Assert.Equal(new[] { firstBatchId, secondBatchId }, detail.TaskItems.Select(item => item.BatchId));
        Assert.Equal(
            new[] { ExpiryStageCalculator.Discount50, ExpiryStageCalculator.Withdraw },
            detail.TaskItems.Select(item => item.Stage));
        Assert.Equal(new DateOnly(2026, 8, 1), detail.TaskItems[0].ProductionDate);
        Assert.Null(detail.TaskItems[1].ProductionDate);
        Assert.Equal(0, detail.TaskItems[0].CheckedQty);
        Assert.Null(detail.TaskItems[1].CheckedQty);
        Assert.Equal(11, detail.TaskItems[0].LastInspection?.CheckedQty);
        Assert.Equal(3, detail.TaskItems[1].LastInspection?.CheckedQty);
        Assert.Equal(new[] { normalBatchId }, detail.NormalBatches.Select(batch => batch.BatchId));
        Assert.Equal(ExpiryStageCalculator.Discount20, detail.NormalBatches[0].CurrentStage);
        Assert.Equal(13, detail.NormalBatches[0].CurrentArrivalQty);
        Assert.Equal(8, detail.NormalBatches[0].LastInspection?.CheckedQty);
        Assert.NotNull(detail.Draft);
        var draftResult = detail.Draft!;
        Assert.Equal("检查员", draftResult.InspectorName);
        Assert.Equal(new DateOnly(2026, 8, 27), draftResult.CheckDate);
        Assert.Equal(
            new[] { firstTaskItemId, secondTaskItemId },
            draftResult.Items.Select(item => item.TaskItemId));
        Assert.Equal(new int?[] { 0, null }, draftResult.Items.Select(item => item.CheckedQty));
        Assert.True(draftResult.AnyRequiresReconfirmation);
        Assert.Contains(detail.TaskItems, item => item.RequiresReconfirmation);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void InvalidDraftIsNotReturnedAsRecoverableAndQueryDoesNotWrite()
    {
        using var database = SqliteTestDatabase.Create();
        long taskId;
        using (var seed = database.Open())
        {
            var (product, task, _) = AddOpenTask(seed, "INVALID-DRAFT", ExpiryStageCalculator.Withdraw, new DateOnly(2026, 9, 1));
            taskId = task.Id;
            var draft = new InspectionDraft
            {
                TaskId = task.Id,
                IsInvalid = true,
                InvalidReason = "stale",
                InvalidatedAtUtc = SubmittedAtUtc
            };
            seed.Drafts.Add(draft);
            seed.SaveChanges();
            _ = product;
        }

        using var context = database.Open();
        var before = context.Drafts.AsNoTracking().Single();
        var result = new InspectionTaskQuery().GetDetail(context, taskId);

        Assert.NotNull(result.Detail);
        Assert.Null(result.Detail!.Draft);
        Assert.Equal(before.IsInvalid, context.Drafts.AsNoTracking().Single().IsInvalid);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void DetailDistinguishesMissingCompletedAndSystemClosedTasks()
    {
        using var database = SqliteTestDatabase.Create();
        long completedId;
        long systemClosedId;
        using (var seed = database.Open())
        {
            var product = NewProduct("STATUS-001", "状态商品", null, 1);
            seed.Products.Add(product);
            seed.SaveChanges();
            completedId = AddTask(seed, product.Id, ExpiryStageCalculator.Discount20, "completed").Id;
            systemClosedId = AddTask(seed, product.Id, ExpiryStageCalculator.Withdraw, "system_closed", "库存归零").Id;
        }

        using var context = database.Open();
        var query = new InspectionTaskQuery();
        var missing = query.GetDetail(context, 999_999);
        var completed = query.GetDetail(context, completedId);
        var systemClosed = query.GetDetail(context, systemClosedId);

        Assert.Equal("not_found", missing.Status);
        Assert.Null(missing.ProductId);
        Assert.Null(missing.Detail);
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.ClosedAtUtc);
        Assert.Null(completed.Detail);
        Assert.Equal("system_closed", systemClosed.Status);
        Assert.Equal("库存归零", systemClosed.CloseReason);
        Assert.NotNull(systemClosed.ClosedAtUtc);
        Assert.Null(systemClosed.Detail);
    }

    [Fact]
    public void QueryFailureIsPropagatedInsteadOfBeingReturnedAsEmpty()
    {
        using var database = SqliteTestDatabase.Create();
        var context = database.Open();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => new InspectionTaskQuery().Dashboard(context));
    }

    private static (Product Product, ProductTask Task, Batch Batch) AddOpenTask(
        StoreDbContext context,
        string productCode,
        string stage,
        DateOnly expiryDate,
        string? name = null,
        string? barcode = null)
    {
        var product = NewProduct(productCode, name, barcode, 10);
        context.Products.Add(product);
        context.SaveChanges();
        var batch = AddBatch(context, product.Id, expiryDate, null, 10);
        var task = AddTask(context, product.Id, stage);
        AddTaskItem(context, task, product, batch, stage, 0, false);
        return (product, task, batch);
    }

    private static Product NewProduct(string code, string? name, string? barcode, int stock) => new()
    {
        ProductCode = code,
        CurrentName = name,
        CurrentBarcode = barcode,
        ExcelStockQty = stock,
        EffectiveStockQty = stock,
        EffectiveStockSource = "excel"
    };

    private static Batch AddBatch(
        StoreDbContext context,
        long productId,
        DateOnly expiryDate,
        DateOnly? productionDate,
        int currentArrivalQty)
    {
        var batch = new Batch
        {
            ProductId = productId,
            ProductionDate = productionDate,
            ExpiryDate = expiryDate,
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = currentArrivalQty,
            MaxArrivalQty = currentArrivalQty,
            CurrentStage = ExpiryStageCalculator.Discount50
        };
        context.Batches.Add(batch);
        context.SaveChanges();
        return batch;
    }

    private static ProductTask AddTask(
        StoreDbContext context,
        long productId,
        string highestStage,
        string status = "open",
        string? closeReason = null)
    {
        var task = new ProductTask
        {
            ProductId = productId,
            HighestStage = highestStage,
            Status = status,
            ClosedAtUtc = status == "open" ? null : SubmittedAtUtc,
            CloseReason = closeReason
        };
        context.Tasks.Add(task);
        context.SaveChanges();
        return task;
    }

    private static ProductTaskItem AddTaskItem(
        StoreDbContext context,
        ProductTask task,
        Product product,
        Batch batch,
        string stage,
        int attentionVersion,
        bool requiresReconfirmation)
    {
        var item = new ProductTaskItem
        {
            TaskId = task.Id,
            BatchId = batch.Id,
            ProductId = product.Id,
            Stage = stage,
            AttentionVersion = attentionVersion,
            RequiresReconfirmation = requiresReconfirmation
        };
        context.TaskItems.Add(item);
        context.SaveChanges();
        return item;
    }

    private static Inspection AddInspection(
        StoreDbContext context,
        ProductTask task,
        Product product,
        Batch batch,
        int checkedQty,
        DateTime submittedAtUtc)
    {
        var inspection = new Inspection
        {
            TaskId = task.Id,
            ProductId = product.Id,
            ProductCodeSnapshot = product.ProductCode,
            ProductNameSnapshot = product.CurrentName,
            BarcodeSnapshot = product.CurrentBarcode,
            StageSnapshot = task.HighestStage,
            StockQtySnapshot = product.EffectiveStockQty,
            InspectorName = "历史检查员",
            CheckDate = DateOnly.FromDateTime(submittedAtUtc),
            SubmittedAtUtc = submittedAtUtc
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();
        context.InspectionItems.Add(new InspectionItem
        {
            InspectionId = inspection.Id,
            ProductId = product.Id,
            BatchId = batch.Id,
            ProductionDateSnapshot = batch.ProductionDate,
            ExpiryDateSnapshot = batch.ExpiryDate,
            StageSnapshot = task.HighestStage,
            ArrivalQtySnapshot = batch.CurrentArrivalQty,
            CheckedQty = checkedQty
        });
        context.SaveChanges();
        return inspection;
    }
}
