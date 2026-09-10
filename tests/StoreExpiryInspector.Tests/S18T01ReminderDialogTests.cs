using StoreExpiryInspector.Application.Reminders;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S18T01ReminderDialogTests
{
    [Fact]
    public void ReminderDialogKeepsFourOrderedLowSaturationStageAccents()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "UI", "WindowsMessageBoxReminderChannel.cs"));

        Assert.Contains("\"即将5折\"", source, StringComparison.Ordinal);
        Assert.Contains("\"即将2折\"", source, StringComparison.Ordinal);
        Assert.Contains("\"即将收仓\"", source, StringComparison.Ordinal);
        Assert.Contains("\"即将过期\"", source, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionBrush", source, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(161, 98, 7)", source, StringComparison.Ordinal);
        Assert.Contains("WarningTextBrush", source, StringComparison.Ordinal);
        Assert.Contains("DangerBrush", source, StringComparison.Ordinal);
        Assert.Contains("Title = \"门店效期提醒\"", source, StringComparison.Ordinal);
        Assert.Contains("Button(\"知道了\"", source, StringComparison.Ordinal);
        Assert.Contains("Button(\"查看待排查任务\"", source, StringComparison.Ordinal);
        Assert.Contains("dismiss.Click += (_, _) => dialog.Close();", source, StringComparison.Ordinal);
        Assert.Contains("viewTasks.Click += (_, _) => { dialog.Close(); _openPendingTasks(); };", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReminderTextUsesExistingProductCountsAndNeutralPreReminderState()
    {
        var message = WindowsMessageBoxReminderChannel.FormatMessage(new ReminderNotification(
            ItemCount: 9,
            HighestStage: ExpiryStageCalculator.Expired,
            FormalTaskItemCount: 2,
            UpcomingDiscount50Count: 1,
            UpcomingDiscount20Count: 2,
            UpcomingWithdrawCount: 3,
            UpcomingExpiredCount: 4));

        Assert.Contains("今日待排查：2 个商品", message, StringComparison.Ordinal);
        Assert.Contains("最高紧急阶段：过期", message, StringComparison.Ordinal);
        Assert.Contains("即将5折 1 个商品", message, StringComparison.Ordinal);
        Assert.Contains("即将2折 2 个商品", message, StringComparison.Ordinal);
        Assert.Contains("即将收仓 3 个商品", message, StringComparison.Ordinal);
        Assert.Contains("即将过期 4 个商品", message, StringComparison.Ordinal);
        Assert.Contains("涉及商品总数：9 个", message, StringComparison.Ordinal);
        Assert.Contains("待排查任务", message, StringComparison.Ordinal);

        var preReminderOnly = WindowsMessageBoxReminderChannel.FormatMessage(new ReminderNotification(1, ExpiryStageCalculator.None, 0, 1));
        Assert.Contains("今日待排查：0 个商品", preReminderOnly, StringComparison.Ordinal);
        Assert.Contains("最高紧急阶段：无今日任务", preReminderOnly, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
