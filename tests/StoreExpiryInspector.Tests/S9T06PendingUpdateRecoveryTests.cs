using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T06PendingUpdateRecoveryTests
{
    [Fact]
    public void UpdaterLaunchUsesTheCopiedUpdaterDirectory()
    {
        var updater = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "updater", "StoreExpiryInspector.Updater.exe");
        var journal = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "journal.json");

        var start = UpdaterLaunch.Create(updater, journal);

        Assert.False(start.UseShellExecute);
        Assert.Equal(Path.GetDirectoryName(updater), start.WorkingDirectory);
        Assert.Equal(["--journal", journal], start.ArgumentList);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(14, false)]
    [InlineData(15, false)]
    public void SameSchemaJournalRecoveryMatchesPhase(int phase, bool pending) => WithRoot(root =>
    {
        Journal(root, phase);
        if (pending) Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root)); else Assert.False(PendingUpdateRecovery.TryResume(root));
    });

    [Fact]
    public void JournallessPreparationResidueIsPreservedAndIgnored() => WithRoot(root =>
    {
        Directory.CreateDirectory(Path.Combine(root, "updates", Guid.NewGuid().ToString()));
        Assert.False(PendingUpdateRecovery.TryResume(root));
    });

    [Theory]
    [InlineData("{")]
    [InlineData("{\"Phase\":99}")]
    public void InvalidJournalBlocksNormalStartup(string text) => WithRoot(root =>
    {
        var directory = Path.Combine(root, "updates", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "journal.json"), text);
        Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
    });

    [Fact]
    public void FailedManualRecoveryBlocksNormalStartup() => WithRoot(root =>
    {
        Journal(root, 16);
        Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
    });

    [Fact]
    public void MultiplePendingJournalsBlockNormalStartup() => WithRoot(root =>
    {
        Journal(root, 0); Journal(root, 1);
        Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
    });

    private static void Journal(string root, int phase)
    {
        var directory = Path.Combine(root, "updates", Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "journal.json"), JsonSerializer.Serialize(new
        {
            OperationId = Path.GetFileName(directory), ProductId = "StoreExpiryInspector", InstallRoot = "C:\\temp\\install", DataRoot = root, AppPath = "C:\\temp\\install\\app", StagingPath = "C:\\temp\\install\\stage", OldPath = "C:\\temp\\install\\old", PackageSha256 = new string('A', 64), SourceVersion = "1.0.0", TargetVersion = "1.0.2", ParentPid = 0, ParentStartedUtc = "2026-09-05T00:00:00Z", Phase = phase,
            OldTree = new { Files = Array.Empty<string>(), Hash = new string('A', 64) }, CandidateTree = new { Files = Array.Empty<string>(), Hash = new string('A', 64) }, CreatedUtc = "2026-09-05T00:00:00Z", UpdatedUtc = "2026-09-05T00:00:00Z", CandidatePid = 0, CandidateStartedUtc = (string?)null, LastError = (string?)null
        }));
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try { Directory.CreateDirectory(root); action(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
