using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S25T03TodayEntryTests
{
    [Fact]
    public void XamlKeepsNavigationAndHomeEntryHierarchy()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreExpiryInspector.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(root!.FullName, "src", "StoreExpiryInspector", "UI", "MainWindow.xaml"));
        var elements = document.Descendants().ToList();
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        int Named(string name) => elements.FindIndex(e => (string?)e.Attribute(x + "Name") == name);
        Assert.True(Named("NavigationTodayInspectionButton") < Named("NavigationTasksButton"));
        Assert.True(Named("NavigationTasksButton") < Named("NavigationHistoryButton"));
        var entry = elements.Single(e => (string?)e.Attribute("Content") == "进入今日排查");
        Assert.Equal("{Binding OpenTodayTasksCommand}", (string?)entry.Attribute("Command"));
        Assert.Equal("{StaticResource PrimaryButtonStyle}", (string?)entry.Attribute("Style"));
        var today = elements.Single(e => (string?)e.Attribute("Text") == "{Binding Dashboard.TodayWorkText}");
        var state = elements.Single(e => (string?)e.Attribute("Text") == "{Binding Dashboard.LastImportText}");
        var priority = elements.Single(e => (string?)e.Attribute("Text") == "优先处理");
        Assert.True(elements.IndexOf(state) < elements.IndexOf(today));
        Assert.True(elements.IndexOf(entry) < elements.IndexOf(priority));
        var pending = elements.Single(e => (string?)e.Attribute("Command") == "{Binding Dashboard.ViewAllTasksCommand}");
        Assert.Equal("{StaticResource LinkButtonStyle}", (string?)pending.Attribute("Style"));
    }

    [Fact]
    public async Task WorkCountsDistinguishLoadingFailureSearchAndValidZero()
    {
        using var release = new ManualResetEventSlim();
        var fail = false;
        var vm = new DashboardViewModel(() =>
        {
            release.Wait();
            if (fail) throw new InvalidOperationException();
            return new InspectionDashboardResult(0, 0, 0, 0, 0, [], DateTime.UtcNow);
        }, searchTasks: request => new InspectionTaskSearchResult([], 0, request.Page, request.PageSize));
        Assert.DoesNotContain("0 项", vm.TodayWorkText);
        var loading = vm.LoadAsync();
        Assert.Contains("加载中", vm.TodayWorkText);
        Assert.Empty(vm.TomorrowWorkText);
        release.Set(); await loading;
        Assert.Equal("今日需排查 0 项", vm.TodayWorkText);
        Assert.Equal("明天需排查 0 项", vm.TomorrowWorkText);
        fail = true; await vm.LoadAsync();
        Assert.Equal("今日任务加载失败", vm.TodayWorkText);
        Assert.Empty(vm.TomorrowWorkText);
        await vm.SearchAsync("商品");
        Assert.DoesNotContain("0 项", vm.TodayWorkText);
        Assert.Empty(vm.TomorrowWorkText);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HomeEntryReturnsToTodayWhileOrdinaryNavigationPreservesDate(int offset)
    {
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(3, 0, 0, 0, 3, [], DateTime.UtcNow),
            taskLoader: request => new InspectionTaskSearchResult([], 0, request.Page, request.PageSize));
        await shell.StartupLoadTask;
        Assert.Equal("今日需排查 3 项", shell.Dashboard.TodayWorkText);
        await shell.TodayInspection.LoadAsync();
        await shell.TodayInspection.SelectDayAsync(offset);
        await shell.NavigateToAsync(ShellPage.Dashboard);
        await shell.NavigateToAsync(ShellPage.TodayInspection);
        Assert.Equal(offset, shell.TodayInspection.SelectedDayOffset);
        await shell.NavigateToAsync(ShellPage.Dashboard);
        await shell.OpenTodayTasksAsync();
        Assert.Equal(ShellPage.TodayInspection, shell.CurrentPage);
        Assert.True(shell.TodayInspection.IsTodaySelected);
    }

    [Fact]
    public async Task HomeEntryDisabledDuringTaskLoadingAndNotifiesWhenReady()
    {
        using var release = new ManualResetEventSlim(true);
        var shell = new ShellViewModel(
            dashboardLoader: () => new InspectionDashboardResult(0, 0, 0, 0, 0, []),
            taskLoader: request => { release.Wait(); return new InspectionTaskSearchResult([], 0, request.Page, request.PageSize); });
        await shell.StartupLoadTask;
        await shell.TodayInspection.SelectDayAsync(1);
        var changes = 0;
        shell.OpenTodayTasksCommand.CanExecuteChanged += (_, _) => changes++;
        release.Reset();
        var loading = shell.TodayInspection.LoadAsync();
        Assert.False(shell.OpenTodayTasksCommand.CanExecute(null));
        await shell.OpenTodayTasksAsync();
        Assert.Equal(1, shell.TodayInspection.SelectedDayOffset);
        release.Set(); await loading;
        Assert.True(shell.OpenTodayTasksCommand.CanExecute(null));
        Assert.True(changes >= 2);
    }
}
