using Xunit;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Application.Updates;

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

    [Fact]
    public void Ordinary_update_requires_the_complete_current_migration_history()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var database = Path.Combine(root, "data", "app.db");
        try
        {
            DatabaseInitializer.Initialize(database);
            var migrations = UpgradeHealthAck.VerifyDatabase(database, includeWal: true);

            UpdateTransaction.RequireCurrentMigrations(migrations);
            Assert.Equal(10, migrations.Count);
            Assert.Equal("20260912083448_AdjustCatchupWindowConstraint", migrations[^1]);
            Assert.Throws<InvalidDataException>(() => { UpdateTransaction.RequireCurrentMigrations(migrations.Take(9).ToArray()); });
            Assert.Throws<InvalidDataException>(() => { UpdateTransaction.RequireCurrentMigrations(migrations.Reverse().ToArray()); });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Ordinary_ack_requires_migration10_identity()
    {
        Assert.True(UpdateTransaction.IsCurrentMigrationAck(10, "20260912083448_AdjustCatchupWindowConstraint"));
        Assert.False(UpdateTransaction.IsCurrentMigrationAck(9, "20260901155124_AddPolicyAndBaselineFoundation"));
        Assert.False(UpdateTransaction.IsCurrentMigrationAck(10, "20260901155124_AddPolicyAndBaselineFoundation"));
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
