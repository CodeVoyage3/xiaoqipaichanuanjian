using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class InspectionHistoryQueryTests
{
    private static readonly DateTime OlderSubmittedAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewerSubmittedAtUtc = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmptyHistoryReturnsEmptyList()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var result = new InspectionHistoryQuery().List(context);

        Assert.Empty(result);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void ListReturnsEachFormalRecordOnceWithSnapshotIdentityAndItemCount()
    {
        using var database = SqliteTestDatabase.Create();
        FormalScenario scenario;
        using (var seed = database.Open())
        {
            scenario = AddFormalInspection(seed, "HISTORY-LIST", NewerSubmittedAtUtc, new[] { 0, 3 });
        }

        using var context = database.Open();
        var result = new InspectionHistoryQuery().List(context);

        var item = Assert.Single(result);
        Assert.Equal(scenario.InspectionId, item.InspectionId);
        Assert.Equal(scenario.TaskId, item.TaskId);
        Assert.Equal(scenario.ProductId, item.ProductId);
        Assert.Equal("HISTORY-LIST", item.ProductCode);
        Assert.Equal("商品-HISTORY-LIST", item.ProductName);
        Assert.Equal("BAR-HISTORY-LIST", item.ProductBarcode);
        Assert.Equal(NewerSubmittedAtUtc, item.SubmittedAtUtc);
        Assert.Equal(2, item.ItemCount);
        Assert.Equal(result.Count, result.Select(row => row.InspectionId).Distinct().Count());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void ListOrdersBySubmittedAtDescendingThenInspectionIdDescending()
    {
        using var database = SqliteTestDatabase.Create();
        FormalScenario first;
        FormalScenario second;
        FormalScenario older;
        using (var seed = database.Open())
        {
            first = AddFormalInspection(seed, "HISTORY-FIRST", NewerSubmittedAtUtc, new[] { 1 });
            second = AddFormalInspection(seed, "HISTORY-SECOND", NewerSubmittedAtUtc, new[] { 2 });
            older = AddFormalInspection(seed, "HISTORY-OLDER", OlderSubmittedAtUtc, new[] { 3 });
        }

        using var context = database.Open();
        var result = new InspectionHistoryQuery().List(context);

        Assert.Equal(
            new[] { second.InspectionId, first.InspectionId, older.InspectionId },
            result.Select(item => item.InspectionId));
        Assert.Equal(3, result.Count);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void ListPageCountsOrdersAndKeepsZeroItemInspection()
    {
        using var database = SqliteTestDatabase.Create();
        FormalScenario first;
        FormalScenario second;
        using (var seed = database.Open())
        {
            first = AddFormalInspection(seed, "PAGE-FIRST", NewerSubmittedAtUtc, []);
            second = AddFormalInspection(seed, "PAGE-SECOND", NewerSubmittedAtUtc, new[] { 3 });
        }
        using var context = database.Open();
        var page = new InspectionHistoryQuery().ListPage(context, new(1, 1));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.InspectionId, Assert.Single(page.Items).InspectionId);
        Assert.Equal(1, page.Items[0].ItemCount);
        var last = new InspectionHistoryQuery().ListPage(context, new(2, 1));
        Assert.Equal(first.InspectionId, Assert.Single(last.Items).InspectionId);
        Assert.Equal(0, last.Items[0].ItemCount);
        Assert.Empty(new InspectionHistoryQuery().ListPage(context, new(3, 1)).Items);
    }

    [Fact]
    public void DraftAndUnfinishedTasksAreExcludedWithoutAnInspection()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddUnfinishedTask(seed, "DRAFT-ONLY", withDraft: true);
            AddUnfinishedTask(seed, "OPEN-ONLY", withDraft: false);
            AddTaskWithoutInspection(seed, "COMPLETED-WITHOUT-HISTORY", "completed");
        }

        using var context = database.Open();
        var result = new InspectionHistoryQuery().List(context);

        Assert.Empty(result);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void NonCompletedTaskInspectionRowsAreExcludedFromListAndDetail()
    {
        using var database = SqliteTestDatabase.Create();
        FormalScenario openTaskInspection;
        FormalScenario systemClosedTaskInspection;
        using (var seed = database.Open())
        {
            openTaskInspection = AddFormalInspection(seed, "HISTORY-OPEN", NewerSubmittedAtUtc, new[] { 1 });
            systemClosedTaskInspection = AddFormalInspection(seed, "HISTORY-SYSTEM-CLOSED", NewerSubmittedAtUtc, new[] { 2 });
            var openTask = seed.Tasks.Single(task => task.Id == openTaskInspection.TaskId);
            openTask.Status = "open";
            openTask.ClosedAtUtc = null;
            var systemClosedTask = seed.Tasks.Single(task => task.Id == systemClosedTaskInspection.TaskId);
            systemClosedTask.Status = "system_closed";
            systemClosedTask.CloseReason = "product_stock_zero";
            seed.SaveChanges();
        }

        using var context = database.Open();
        var query = new InspectionHistoryQuery();

        Assert.Empty(query.List(context));
        Assert.Equal("not_found", query.GetDetail(context, openTaskInspection.InspectionId).Status);
        Assert.Equal("not_found", query.GetDetail(context, systemClosedTaskInspection.InspectionId).Status);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void DetailReturnsEveryRawInspectionItemSnapshotWithoutCurrentStateOrDraft()
    {
        using var database = SqliteTestDatabase.Create();
        FormalScenario scenario;
        using (var seed = database.Open())
        {
            scenario = AddFormalInspection(seed, "HISTORY-DETAIL", NewerSubmittedAtUtc, new[] { 0, 4 });
            var product = seed.Products.Single(product => product.Id == scenario.ProductId);
            product.EffectiveStockQty = 999;
            product.CurrentName = "当前商品名";
            var batches = seed.Batches
                .Where(batch => scenario.BatchIds.Contains(batch.Id))
                .OrderBy(batch => batch.Id)
                .ToArray();
            batches[0].CurrentStage = ExpiryStageCalculator.Expired;
            batches[0].CurrentArrivalQty = 88;
            batches[0].ExpiryDate = new DateOnly(2030, 1, 1);
            var draft = new InspectionDraft
            {
                TaskId = scenario.TaskId,
                InspectorName = "当前草稿检查员",
                CheckDate = new DateOnly(2030, 1, 1)
            };
            seed.Drafts.Add(draft);
            seed.SaveChanges();
            seed.DraftItems.Add(new InspectionDraftItem
            {
                DraftId = draft.Id,
                TaskItemId = seed.TaskItems.First(item => item.TaskId == scenario.TaskId).Id,
                TaskId = scenario.TaskId,
                CheckedQty = 999,
                ConfirmedAttentionVersion = 0
            });
            seed.SaveChanges();
        }

        using var context = database.Open();
        var result = new InspectionHistoryQuery().GetDetail(context, scenario.InspectionId);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.Detail);
        var detail = result.Detail!;
        Assert.Equal(scenario.InspectionId, detail.InspectionId);
        Assert.Equal(scenario.TaskId, detail.TaskId);
        Assert.Equal(scenario.ProductId, detail.ProductId);
        Assert.Equal("HISTORY-DETAIL", detail.ProductCodeSnapshot);
        Assert.Equal("商品-HISTORY-DETAIL", detail.ProductNameSnapshot);
        Assert.Equal("BAR-HISTORY-DETAIL", detail.BarcodeSnapshot);
        Assert.Equal(ExpiryStageCalculator.Withdraw, detail.StageSnapshot);
        Assert.Equal(7, detail.StockQtySnapshot);
        Assert.Equal("检查员-HISTORY-DETAIL", detail.InspectorName);
        Assert.Equal(new DateOnly(2026, 8, 27), detail.CheckDate);
        Assert.Equal(NewerSubmittedAtUtc, detail.SubmittedAtUtc);
        Assert.Equal(2, detail.Items.Count);
        Assert.Equal(new[] { 0, 4 }, detail.Items.Select(item => item.CheckedQty));
        Assert.Equal(
            new[] { ExpiryStageCalculator.Discount20, ExpiryStageCalculator.Withdraw },
            detail.Items.Select(item => item.StageSnapshot));
        Assert.Equal(new[] { 11, 12 }, detail.Items.Select(item => item.ArrivalQtySnapshot));
        Assert.Equal(new[] { scenario.BatchIds[0], scenario.BatchIds[1] }, detail.Items.Select(item => item.BatchId));
        Assert.All(detail.Items, item => Assert.Equal(scenario.InspectionId, item.InspectionId));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void MissingHistoryUsesNotFoundResult()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var result = new InspectionHistoryQuery().GetDetail(context, 999_999);

        Assert.Equal(999_999, result.InspectionId);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Detail);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void HistoryQueriesDoNotChangeDatabaseRowsOrTrackEntities()
    {
        using var database = SqliteTestDatabase.Create();
        FormalScenario scenario;
        using (var seed = database.Open())
        {
            scenario = AddFormalInspection(seed, "HISTORY-READONLY", NewerSubmittedAtUtc, new[] { 1, 2 });
        }

        using var context = database.Open();
        var before = CaptureHistoryRows(context);
        var query = new InspectionHistoryQuery();
        _ = query.List(context);
        _ = query.GetDetail(context, scenario.InspectionId);
        var after = CaptureHistoryRows(context);

        Assert.Equal(before, after);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void InvalidInspectionIdMatchesExistingArgumentValidationStyle()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        Assert.Throws<ArgumentOutOfRangeException>(() => new InspectionHistoryQuery().GetDetail(context, 0));
    }

    private static FormalScenario AddFormalInspection(
        StoreDbContext context,
        string productCode,
        DateTime submittedAtUtc,
        IReadOnlyList<int> checkedQuantities)
    {
        var product = new Product
        {
            ProductCode = productCode,
            CurrentName = $"商品-{productCode}",
            CurrentBarcode = $"BAR-{productCode}",
            ExcelStockQty = 7,
            EffectiveStockQty = 7,
            EffectiveStockSource = "excel"
        };
        context.Products.Add(product);
        context.SaveChanges();

        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = "completed",
            HighestStage = ExpiryStageCalculator.Withdraw,
            ClosedAtUtc = submittedAtUtc,
            CreatedAtUtc = submittedAtUtc.AddHours(-1),
            UpdatedAtUtc = submittedAtUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();

        var batches = new List<Batch>();
        var taskItems = new List<ProductTaskItem>();
        for (var index = 0; index < checkedQuantities.Count; index++)
        {
            var batch = new Batch
            {
                ProductId = product.Id,
                ProductionDate = new DateOnly(2026, 8, 1).AddDays(index),
                ExpiryDate = new DateOnly(2026, 9, 1).AddDays(index),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 11 + index,
                MaxArrivalQty = 11 + index,
                CurrentStage = ExpiryStageCalculator.Discount50,
                TrackingStatus = "active"
            };
            context.Batches.Add(batch);
            context.SaveChanges();
            batches.Add(batch);

            var taskItem = new ProductTaskItem
            {
                TaskId = task.Id,
                BatchId = batch.Id,
                ProductId = product.Id,
                Stage = index == 0 ? ExpiryStageCalculator.Discount20 : ExpiryStageCalculator.Withdraw,
                AttentionVersion = 0
            };
            context.TaskItems.Add(taskItem);
            context.SaveChanges();
            taskItems.Add(taskItem);
        }

        var inspection = new Inspection
        {
            TaskId = task.Id,
            ProductId = product.Id,
            ProductCodeSnapshot = product.ProductCode,
            ProductNameSnapshot = product.CurrentName,
            BarcodeSnapshot = product.CurrentBarcode,
            StageSnapshot = ExpiryStageCalculator.Withdraw,
            StockQtySnapshot = product.EffectiveStockQty,
            InspectorName = $"检查员-{productCode}",
            CheckDate = new DateOnly(2026, 8, 27),
            SubmittedAtUtc = submittedAtUtc
        };
        context.Inspections.Add(inspection);
        context.SaveChanges();

        for (var index = 0; index < checkedQuantities.Count; index++)
        {
            context.InspectionItems.Add(new InspectionItem
            {
                InspectionId = inspection.Id,
                ProductId = product.Id,
                BatchId = batches[index].Id,
                ProductionDateSnapshot = batches[index].ProductionDate,
                ExpiryDateSnapshot = batches[index].ExpiryDate,
                StageSnapshot = taskItems[index].Stage,
                ArrivalQtySnapshot = batches[index].CurrentArrivalQty,
                CheckedQty = checkedQuantities[index],
                UpdatedAtUtc = submittedAtUtc
            });
        }

        context.SaveChanges();
        return new(inspection.Id, task.Id, product.Id, batches.Select(batch => batch.Id).ToArray());
    }

    private static void AddUnfinishedTask(StoreDbContext context, string productCode, bool withDraft)
    {
        var scenario = AddTaskWithoutInspection(context, productCode, "open");
        if (!withDraft)
        {
            return;
        }

        var draft = new InspectionDraft
        {
            TaskId = scenario.TaskId,
            InspectorName = "草稿检查员",
            CheckDate = new DateOnly(2026, 8, 27)
        };
        context.Drafts.Add(draft);
        context.SaveChanges();
    }

    private static FormalScenario AddTaskWithoutInspection(
        StoreDbContext context,
        string productCode,
        string status)
    {
        var product = new Product { ProductCode = productCode };
        context.Products.Add(product);
        context.SaveChanges();
        var task = new ProductTask
        {
            ProductId = product.Id,
            Status = status,
            HighestStage = ExpiryStageCalculator.Discount50,
            ClosedAtUtc = status == "open" ? null : OlderSubmittedAtUtc
        };
        context.Tasks.Add(task);
        context.SaveChanges();
        return new(0, task.Id, product.Id, Array.Empty<long>());
    }

    private static string CaptureHistoryRows(StoreDbContext context) => JsonSerializer.Serialize(new
    {
        Inspections = context.Inspections
            .AsNoTracking()
            .OrderBy(inspection => inspection.Id)
            .Select(inspection => new
            {
                inspection.Id,
                inspection.TaskId,
                inspection.ProductId,
                inspection.ProductCodeSnapshot,
                inspection.ProductNameSnapshot,
                inspection.BarcodeSnapshot,
                inspection.StageSnapshot,
                inspection.StockQtySnapshot,
                inspection.InspectorName,
                inspection.CheckDate,
                inspection.SubmittedAtUtc
            })
            .ToArray(),
        Items = context.InspectionItems
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.InspectionId,
                item.ProductId,
                item.BatchId,
                item.ProductionDateSnapshot,
                item.ExpiryDateSnapshot,
                item.StageSnapshot,
                item.ArrivalQtySnapshot,
                item.CheckedQty,
                item.UpdatedAtUtc
            })
            .ToArray()
    });

    private sealed record FormalScenario(
        long InspectionId,
        long TaskId,
        long ProductId,
        long[] BatchIds);
}
