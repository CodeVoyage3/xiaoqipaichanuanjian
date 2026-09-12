using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S19T01ContractTests
{
    [Fact]
    public void ExportAndReaderKeepOnlyBusinessColumns()
    {
        var root = FindRoot();
        var export = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "Application", "Tasks", "TodayInspectionPlanExportUseCase.cs"));
        var reader = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "Application", "Tasks", "InspectionPlanResultReader.cs"));
        Assert.Contains("\"本次排查数量\"", export);
        Assert.DoesNotContain("Hidden = true", export, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskId\", \"TaskItemId", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void ColdStartUsesOnePercentWithOneToSevenDayBounds()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "Application", "ColdStartScopeBaselineUseCase.cs"));
        Assert.Contains("Math.Clamp((int)((batch.ExpiryDate.DayNumber - production.DayNumber + 99L) / 100L), 1, 7)", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
