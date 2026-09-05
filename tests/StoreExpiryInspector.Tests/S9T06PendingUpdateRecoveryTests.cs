using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T06PendingUpdateRecoveryTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(15)]
    public void AcknowledgedOrTerminalJournalDoesNotLaunchRecovery(int phase) => WithRoot(root =>
    {
        Journal(root, phase);
        Assert.False(PendingUpdateRecovery.TryResume(root));
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
        File.WriteAllText(Path.Combine(directory, "journal.json"), JsonSerializer.Serialize(new { Phase = phase }));
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try { Directory.CreateDirectory(root); action(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
