using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Tasks;

public sealed record InspectionTaskSearchRequest(
    string? SearchText = null,
    string? Stage = null,
    int Page = 1,
    int PageSize = 50,
    string? CategoryName = null)
{
    public string? Search => SearchText;
}

public sealed record InspectionTaskListItem(
    long TaskId,
    long ProductId,
    string? ProductName,
    string ProductCode,
    string? ProductBarcode,
    string HighestStage,
    int PendingBatchCount,
    int EffectiveStockQty,
    DateOnly? NearestExpiryDate,
    bool HasValidDraft,
    string CategoryName = "");

public sealed record InspectionDashboardResult(
    int OpenTaskCount,
    int ExpiredCount,
    int WithdrawCount,
    int Discount20Count,
    int Discount50Count,
    IReadOnlyList<InspectionTaskListItem> UrgentTasks,
    DateTime? LastSuccessfulImportAtUtc = null,
    int ProductCount = 0,
    int BatchCount = 0);

public sealed record InspectionTaskSearchResult(
    IReadOnlyList<InspectionTaskListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ReminderCandidate(
    long ProductId,
    string? ProductName,
    string? ProductBarcode,
    string ProductCode,
    string HighestStage);

public sealed record InspectionLatestInspectionResult(
    long InspectionId,
    long InspectionItemId,
    DateTime SubmittedAtUtc,
    int CheckedQty);

public sealed record InspectionTaskItemResult(
    long TaskItemId,
    long BatchId,
    DateOnly? ProductionDate,
    DateOnly ExpiryDate,
    string Stage,
    int CurrentArrivalQty,
    int AttentionVersion,
    bool RequiresReconfirmation,
    int? CheckedQty,
    InspectionLatestInspectionResult? LastInspection);

public sealed record InspectionNormalBatchResult(
    long BatchId,
    DateOnly? ProductionDate,
    DateOnly ExpiryDate,
    string CurrentStage,
    int CurrentArrivalQty,
    InspectionLatestInspectionResult? LastInspection);

public sealed record InspectionDraftItemResult(
    long TaskItemId,
    int? CheckedQty);

public sealed record InspectionDraftResult(
    long DraftId,
    string? InspectorName,
    DateOnly? CheckDate,
    IReadOnlyList<InspectionDraftItemResult> Items,
    bool AnyRequiresReconfirmation)
{
    public bool RequiresReconfirmation => AnyRequiresReconfirmation;
}

public sealed record InspectionTaskDetail(
    long TaskId,
    long ProductId,
    string? ProductName,
    string ProductCode,
    string? ProductBarcode,
    int EffectiveStockQty,
    string HighestStage,
    IReadOnlyList<InspectionTaskItemResult> TaskItems,
    IReadOnlyList<InspectionNormalBatchResult> NormalBatches,
    InspectionDraftResult? Draft);

public sealed record InspectionTaskDetailResult(
    long TaskId,
    string Status,
    long? ProductId,
    DateTime? ClosedAtUtc,
    string? CloseReason,
    InspectionTaskDetail? Detail);

public sealed class InspectionTaskQuery
{
    public IReadOnlyList<string> GetOpenTaskCategoryNames(StoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Array.AsReadOnly(context.Tasks
            .AsNoTracking()
            .Where(task => task.Status == "open")
            .Select(task => task.Product.CategoryCode)
            .Distinct()
            .AsEnumerable()
            .Select(ProductCategoryScopes.DisplayNameForCategoryCode)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray());
    }

    public IReadOnlyList<long> GetOpenTaskIds(StoreDbContext context, string? categoryName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Array.AsReadOnly(BuildTasks(context, categoryName: categoryName)
            .OrderBy(task => task.Id)
            .Select(task => task.Id)
            .ToArray());
    }

    public IReadOnlyList<long> GetOpenTaskIds(StoreDbContext context, IReadOnlyCollection<long> taskIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (taskIds.Count == 0) return Array.Empty<long>();

        var openIds = new List<long>(taskIds.Count);
        foreach (var ids in taskIds.Distinct().Chunk(900))
        {
            openIds.AddRange(BuildTasks(context).Where(task => ids.Contains(task.Id)).Select(task => task.Id));
        }
        return Array.AsReadOnly(openIds.ToArray());
    }

    public IReadOnlyList<ReminderCandidate> GetReminderCandidates(StoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Array.AsReadOnly(BuildTasks(context, eligibleForReminder: true)
            .Select(task => new
            {
                TaskId = task.Id,
                task.ProductId,
                task.Product.CurrentName,
                task.Product.CurrentBarcode,
                task.Product.ProductCode,
                task.HighestStage,
                NearestExpiryDate = task.Items.Select(item => (DateOnly?)item.Batch.ExpiryDate).Min()
            })
            .OrderByDescending(task => task.HighestStage == ExpiryStageCalculator.Expired ? 4 : task.HighestStage == ExpiryStageCalculator.Withdraw ? 3 : task.HighestStage == ExpiryStageCalculator.Discount20 ? 2 : task.HighestStage == ExpiryStageCalculator.Discount50 ? 1 : 0)
            .ThenBy(task => task.NearestExpiryDate == null)
            .ThenBy(task => task.NearestExpiryDate)
            .ThenBy(task => task.TaskId)
            .AsEnumerable()
            .Select(task => new ReminderCandidate(task.ProductId, task.CurrentName, task.CurrentBarcode, task.ProductCode, task.HighestStage))
            .ToArray());
    }

    public InspectionDashboardResult Dashboard(StoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tasks = BuildTasks(context);
        var lastSuccessfulImportAtUtc = context.Imports
            .AsNoTracking()
            .Where(import => import.Status == ImportStatuses.Succeeded
                && !import.IsUndone
                && import.ConfirmedAtUtc.HasValue)
            .OrderByDescending(import => import.ConfirmedAtUtc)
            .ThenByDescending(import => import.Id)
            .Select(import => import.ConfirmedAtUtc)
            .FirstOrDefault();
        var productCount = context.Products.AsNoTracking().Count();
        var batchCount = context.Batches.AsNoTracking().Count();
        return new(
            tasks.Count(),
            tasks.Count(task => task.HighestStage == ExpiryStageCalculator.Expired),
            tasks.Count(task => task.HighestStage == ExpiryStageCalculator.Withdraw),
            tasks.Count(task => task.HighestStage == ExpiryStageCalculator.Discount20),
            tasks.Count(task => task.HighestStage == ExpiryStageCalculator.Discount50),
            Array.AsReadOnly(ReadTaskRows(tasks, 1, 20).ToArray()),
            lastSuccessfulImportAtUtc,
            productCount,
            batchCount);
    }

    public InspectionTaskSearchResult SearchOpenTasks(
        StoreDbContext context,
        InspectionTaskSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateSearchRequest(request);

        var search = string.IsNullOrWhiteSpace(request.SearchText)
            ? null
            : request.SearchText.Trim();
        var tasks = BuildTasks(context, search, request.Stage, request.CategoryName);
        var totalCount = tasks.Count();
        var page = ReadTaskRows(tasks, request.Page, request.PageSize);
        return new(Array.AsReadOnly(page), totalCount, request.Page, request.PageSize);
    }

    public InspectionTaskDetailResult GetDetail(StoreDbContext context, long taskId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (taskId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskId));
        }

        var task = context.Tasks
            .AsNoTracking()
            .Where(candidate => candidate.Id == taskId)
            .Select(candidate => new TaskHeader(
                candidate.Id,
                candidate.Status,
                candidate.ProductId,
                candidate.Product.CurrentName,
                candidate.Product.ProductCode,
                candidate.Product.CurrentBarcode,
                candidate.Product.EffectiveStockQty,
                candidate.HighestStage,
                candidate.ClosedAtUtc,
                candidate.CloseReason))
            .SingleOrDefault();
        if (task is null)
        {
            return new(taskId, "not_found", null, null, null, null);
        }

        if (!string.Equals(task.Status, "open", StringComparison.Ordinal))
        {
            return new(task.Id, task.Status, task.ProductId, task.ClosedAtUtc, task.CloseReason, null);
        }

        var taskItems = context.TaskItems
            .AsNoTracking()
            .Where(item => item.TaskId == task.Id)
            .Select(item => new TaskItemRow(
                item.Id,
                item.BatchId,
                item.Batch.ProductionDate,
                item.Batch.ExpiryDate,
                item.Stage,
                item.Batch.CurrentArrivalQty,
                item.AttentionVersion,
                item.RequiresReconfirmation))
            .ToArray()
            .OrderBy(item => item.TaskItemId)
            .ToArray();

        var taskBatchIds = taskItems
            .Select(item => item.BatchId)
            .ToArray();
        var normalBatches = context.Batches
            .AsNoTracking()
            .Where(batch =>
                batch.ProductId == task.ProductId &&
                batch.TrackingStatus == "active" &&
                !taskBatchIds.Contains(batch.Id))
            .Select(batch => new NormalBatchRow(
                batch.Id,
                batch.ProductionDate,
                batch.ExpiryDate,
                batch.CurrentStage,
                batch.CurrentArrivalQty))
            .ToArray()
            .OrderBy(batch => batch.BatchId)
            .ToArray();

        var draft = context.Drafts
            .AsNoTracking()
            .Where(candidate => candidate.TaskId == task.Id && !candidate.IsInvalid)
            .Select(candidate => new DraftHeader(
                candidate.Id,
                candidate.InspectorName,
                candidate.CheckDate))
            .SingleOrDefault();
        var draftItems = draft is null
            ? Array.Empty<DraftItemRow>()
            : context.DraftItems
                .AsNoTracking()
                .Where(item => item.DraftId == draft.DraftId && item.TaskId == task.Id)
                .Select(item => new DraftItemRow(item.TaskItemId, item.CheckedQty))
                .ToArray();
        var checkedByTaskItem = draftItems.ToDictionary(item => item.TaskItemId, item => item.CheckedQty);

        var allBatchIds = taskBatchIds
            .Concat(normalBatches.Select(batch => batch.BatchId))
            .Distinct()
            .ToArray();
        var inspections = allBatchIds.Length == 0
            ? Array.Empty<InspectionRow>()
            : (
                from item in context.InspectionItems.AsNoTracking()
                join inspection in context.Inspections.AsNoTracking()
                    on new { item.InspectionId, item.ProductId }
                    equals new { InspectionId = inspection.Id, inspection.ProductId }
                where item.ProductId == task.ProductId && allBatchIds.Contains(item.BatchId)
                select new InspectionRow(
                    item.BatchId,
                    inspection.Id,
                    item.Id,
                    inspection.SubmittedAtUtc,
                    item.CheckedQty)
            ).ToArray();
        var latestInspections = inspections
            .GroupBy(item => item.BatchId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.SubmittedAtUtc)
                    .ThenByDescending(item => item.InspectionId)
                    .ThenByDescending(item => item.InspectionItemId)
                    .Select(item => new InspectionLatestInspectionResult(
                        item.InspectionId,
                        item.InspectionItemId,
                        item.SubmittedAtUtc,
                        item.CheckedQty))
                    .First());

        var taskItemResults = taskItems
            .Select(item => new InspectionTaskItemResult(
                item.TaskItemId,
                item.BatchId,
                item.ProductionDate,
                item.ExpiryDate,
                item.Stage,
                item.CurrentArrivalQty,
                item.AttentionVersion,
                item.RequiresReconfirmation,
                checkedByTaskItem.TryGetValue(item.TaskItemId, out var checkedQty)
                    ? checkedQty
                    : null,
                latestInspections.GetValueOrDefault(item.BatchId)))
            .ToArray();
        var normalBatchResults = normalBatches
            .Select(batch => new InspectionNormalBatchResult(
                batch.BatchId,
                batch.ProductionDate,
                batch.ExpiryDate,
                batch.CurrentStage,
                batch.CurrentArrivalQty,
                latestInspections.GetValueOrDefault(batch.BatchId)))
            .ToArray();
        var draftResult = draft is null
            ? null
            : new InspectionDraftResult(
                draft.DraftId,
                draft.InspectorName,
                draft.CheckDate,
                Array.AsReadOnly(taskItemResults
                    .Select(item => new InspectionDraftItemResult(item.TaskItemId, item.CheckedQty))
                    .ToArray()),
                taskItemResults.Any(item => item.RequiresReconfirmation));
        var detail = new InspectionTaskDetail(
            task.Id,
            task.ProductId,
            task.ProductName,
            task.ProductCode,
            task.ProductBarcode,
            task.EffectiveStockQty,
            task.HighestStage,
            Array.AsReadOnly(taskItemResults),
            Array.AsReadOnly(normalBatchResults),
            draftResult);
        return new(task.Id, task.Status, task.ProductId, task.ClosedAtUtc, task.CloseReason, detail);
    }

    private static IQueryable<ProductTask> BuildTasks(
        StoreDbContext context,
        string? search = null,
        string? stage = null,
        string? categoryName = null,
        bool eligibleForReminder = false)
    {
        var query = context.Tasks
            .AsNoTracking()
            .Where(task => task.Status == "open");
        if (eligibleForReminder)
        {
            query = query.Where(task =>
                task.Product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
                task.Product.PolicyVersion == ExpiryPolicies.Version1 &&
                (task.Product.PolicyCode == ExpiryPolicies.Food ||
                 task.Product.PolicyCode == ExpiryPolicies.Pet ||
                 task.Product.PolicyCode == ExpiryPolicies.GeneralLong) &&
                context.ScopeBaselines.Any(baseline =>
                    baseline.IsCompleted &&
                    baseline.ScopeKey == task.Product.CategoryCode &&
                    baseline.PolicyCode == task.Product.PolicyCode &&
                    baseline.PolicyVersion == task.Product.PolicyVersion));
        }
        if (!string.IsNullOrEmpty(stage))
        {
            query = query.Where(task => task.HighestStage == stage);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(task =>
                (task.Product.CurrentName != null && task.Product.CurrentName.Contains(search)) ||
                task.Product.ProductCode.Contains(search) ||
                (task.Product.CurrentBarcode != null && task.Product.CurrentBarcode.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            query = query.Where(task => task.Product.CategoryCode == CategoryCodeForDisplayName(categoryName));
        }
        return query;
    }

    private static InspectionTaskListItem[] ReadTaskRows(IQueryable<ProductTask> tasks, int page, int pageSize)
    {
        var offset = checked((long)(page - 1) * pageSize);
        if (offset > int.MaxValue)
        {
            return [];
        }

        return tasks
            .Select(task => new
            {
                task.Id, task.ProductId, task.Product.CurrentName, task.Product.ProductCode, task.Product.CurrentBarcode,
                task.Product.CategoryCode, task.HighestStage, task.Product.EffectiveStockQty,
                PendingBatchCount = task.Items.Count(),
                NearestExpiryDate = task.Items.Select(item => (DateOnly?)item.Batch.ExpiryDate).Min(),
                HasValidDraft = task.Draft != null && !task.Draft.IsInvalid
            })
            .OrderByDescending(task => task.HighestStage == ExpiryStageCalculator.Expired ? 4 : task.HighestStage == ExpiryStageCalculator.Withdraw ? 3 : task.HighestStage == ExpiryStageCalculator.Discount20 ? 2 : task.HighestStage == ExpiryStageCalculator.Discount50 ? 1 : 0)
            .ThenBy(task => task.NearestExpiryDate == null)
            .ThenBy(task => task.NearestExpiryDate)
            .ThenBy(task => task.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .AsEnumerable()
            .Select(task => new InspectionTaskListItem(task.Id, task.ProductId, task.CurrentName, task.ProductCode, task.CurrentBarcode, task.HighestStage, task.PendingBatchCount, task.EffectiveStockQty, task.NearestExpiryDate, task.HasValidDraft, ProductCategoryScopes.DisplayNameForCategoryCode(task.CategoryCode)))
            .ToArray();
    }

    private static string CategoryCodeForDisplayName(string categoryName) => categoryName.Trim() switch
    {
        "食品" => "food", "宠物" => "pet", "日用" => "daily_use", "美妆" => "beauty", "家居" => "home", "香氛香水" => "fragrance", "文具" => "stationery", "潮流玩具" => "trendy_toys", "应季搭配" => "seasonal_assortment", "赠品小样" => "gift_sample", _ => throw new ArgumentException("Unknown category.", nameof(categoryName))
    };

    private static void ValidateSearchRequest(InspectionTaskSearchRequest request)
    {
        if (request.Page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Page));
        }

        if (request.PageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PageSize));
        }

        if (string.IsNullOrEmpty(request.Stage))
        {
            return;
        }

        if (ExpiryStageCalculator.GetStagePriority(request.Stage) <= 0)
        {
            throw new ArgumentException(
                "Stage filter must be one of the trackable expiry stages.",
                nameof(request.Stage));
        }
    }

    private sealed record TaskHeader(
        long Id,
        string Status,
        long ProductId,
        string? ProductName,
        string ProductCode,
        string? ProductBarcode,
        int EffectiveStockQty,
        string HighestStage,
        DateTime? ClosedAtUtc,
        string? CloseReason);

    private sealed record TaskListHeader(
        long TaskId,
        long ProductId,
        string? ProductName,
        string ProductCode,
        string? ProductBarcode,
        string CategoryCode,
        string HighestStage,
        int EffectiveStockQty);

    private sealed record TaskListItemRow(long TaskId, DateOnly ExpiryDate);

    private sealed record TaskItemRow(
        long TaskItemId,
        long BatchId,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate,
        string Stage,
        int CurrentArrivalQty,
        int AttentionVersion,
        bool RequiresReconfirmation);

    private sealed record NormalBatchRow(
        long BatchId,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate,
        string CurrentStage,
        int CurrentArrivalQty);

    private sealed record DraftHeader(
        long DraftId,
        string? InspectorName,
        DateOnly? CheckDate);

    private sealed record DraftItemRow(long TaskItemId, int? CheckedQty);

    private sealed record InspectionRow(
        long BatchId,
        long InspectionId,
        long InspectionItemId,
        DateTime SubmittedAtUtc,
        int CheckedQty);
}
