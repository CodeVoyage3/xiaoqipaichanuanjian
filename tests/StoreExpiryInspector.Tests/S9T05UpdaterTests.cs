using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T05UpdaterTests
{
    [Fact]
    public void TestModeUsesSeparateUpdaterBuildPaths()
    {
        var root = FindRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector.Updater", "StoreExpiryInspector.Updater.csproj"));
        Assert.Contains("S9T05TestMode", project, StringComparison.Ordinal);
        Assert.Contains("s9t05test", project, StringComparison.Ordinal);
        Assert.Contains("IntermediateOutputPath", project, StringComparison.Ordinal);
        Assert.Contains("OutputPath", project, StringComparison.Ordinal);
    }
    [Fact]
    public void TreeFingerprintChangesForAnyFileChange()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "a.dll"), "old");
            var before = TreeFingerprint.Create(root);
            File.WriteAllText(Path.Combine(root, "a.dll"), "new");
            Assert.NotEqual(before.Hash, TreeFingerprint.Create(root).Hash);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
