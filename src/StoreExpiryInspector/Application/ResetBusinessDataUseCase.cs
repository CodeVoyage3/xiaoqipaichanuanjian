using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public static class ResetBusinessDataCodes
{
    public const string Success = "reset";
    public const string NoData = "no_business_data";
    public const string BackupFailed = "backup_failed";
    public const string ClearFailed = "clear_failed";
    public const string DatabaseBusy = "database_busy";
}

public sealed class ResetBusinessDataUseCase
{
    public ResetBusinessDataResult Execute(
        string? databasePath = null,
        string? backupDirectory = null)
    {
        var sourcePath = databasePath ?? DatabaseInitializer.GetDefaultDatabasePath();
        using var context = DatabaseInitializer.CreateContext(sourcePath);
        return Execute(context, sourcePath, backupDirectory ?? DatabaseInitializer.GetDefaultBackupDirectory());
    }

    internal ResetBusinessDataResult Execute(
        StoreDbContext context,
        string databasePath,
        string backupDirectory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        if (context.Database.CurrentTransaction is not null)
        {
            return ResetBusinessDataResult.Failure(
                ResetBusinessDataCodes.DatabaseBusy,
                "数据库已有事务，重置未开始。");
        }

        if (!HasBusinessData(context))
        {
            return ResetBusinessDataResult.NoData();
        }

        var backup = new LocalDatabaseBackupUseCase().Create(databasePath, backupDirectory);
        if (!backup.Succeeded)
        {
            return ResetBusinessDataResult.Failure(
                ResetBusinessDataCodes.BackupFailed,
                $"保护备份失败，业务数据未清除：{backup.SafeSummary}");
        }

        using var transaction = context.Database.BeginTransaction();
        try
        {
            context.Database.ExecuteSqlRaw("""
                DELETE FROM inspection_item_revisions;
                DELETE FROM draft_items;
                DELETE FROM batch_baselines;
                DELETE FROM lifecycle_events;
                DELETE FROM inspection_items;
                DELETE FROM drafts;
                DELETE FROM inspections;
                DELETE FROM task_items;
                DELETE FROM tasks;
                DELETE FROM inventory_adjustments;
                DELETE FROM import_issues;
                DELETE FROM import_workbooks;
                DELETE FROM scope_baselines;
                DELETE FROM batches;
                DELETE FROM products;
                DELETE FROM imports;
                UPDATE app_state SET last_reminder_date = NULL, last_normal_run_date = NULL WHERE id = 1;
                """);
            transaction.Commit();
            context.ChangeTracker.Clear();
            return ResetBusinessDataResult.Success(backup);
        }
        catch
        {
            transaction.Rollback();
            context.ChangeTracker.Clear();
            return ResetBusinessDataResult.Failure(
                ResetBusinessDataCodes.ClearFailed,
                "业务数据清除失败，事务未提交；保护备份已保留。");
        }
    }

    private static bool HasBusinessData(StoreDbContext context) =>
        context.Products.Any() ||
        context.Batches.Any() ||
        context.Tasks.Any() ||
        context.TaskItems.Any() ||
        context.Drafts.Any() ||
        context.DraftItems.Any() ||
        context.Inspections.Any() ||
        context.InspectionItems.Any() ||
        context.InspectionItemRevisions.Any() ||
        context.InventoryAdjustments.Any() ||
        context.Imports.Any() ||
        context.ImportWorkbooks.Any() ||
        context.ImportIssues.Any() ||
        context.LifecycleEvents.Any() ||
        context.ScopeBaselines.Any() ||
        context.BatchBaselines.Any();
}

public sealed record ResetBusinessDataResult(
    bool Succeeded,
    string Code,
    string Message,
    string? BackupId,
    string? BackupPath)
{
    internal static ResetBusinessDataResult Success(LocalDatabaseBackupResult backup) => new(
        true,
        ResetBusinessDataCodes.Success,
        "业务数据已重置，保护备份已创建并验证。",
        backup.BackupId,
        backup.BackupPath);

    internal static ResetBusinessDataResult NoData() => new(
        true,
        ResetBusinessDataCodes.NoData,
        "当前没有可重置的业务数据，未创建备份。",
        null,
        null);

    internal static ResetBusinessDataResult Failure(string code, string message) => new(
        false,
        code,
        message,
        null,
        null);
}
