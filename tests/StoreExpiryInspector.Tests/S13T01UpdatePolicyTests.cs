using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S13T01UpdatePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    [Fact]
    public void FreshTemporaryFailureGetsOneDayButNeverRefreshesIt()
    {
        var store = new UpdatePolicyStore(_root);
        var start = DateTime.UnixEpoch.AddDays(10);
        var offline = UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5));
        Assert.Equal(UpdatePolicyDecision.AllowBusiness, UpdatePolicyGate.Evaluate(store, offline, start).Decision);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(store, offline, start.AddHours(25)).Decision);
    }

    [Fact]
    public void ForcedStateSurvivesOfflineAndOnlyTrustedLatestClearsIt()
    {
        var store = new UpdatePolicyStore(_root);
        var now = DateTime.UnixEpoch.AddDays(10);
        var available = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6));
        var forced = UpdatePolicyGate.Evaluate(store, available, now);
        Assert.Equal(UpdatePolicyDecision.ForceUpdate, forced.Decision);
        Assert.True(UpdatePolicyGate.EnableAutoContinue(store, forced.State).AutoContinue);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now.AddMinutes(1)).Decision);
        var clear = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 6)), now.AddMinutes(2));
        Assert.Equal(UpdatePolicyDecision.AllowBusiness, clear.Decision);
        Assert.False(clear.State.ForcedUpdateRequired);
    }

    [Fact]
    public void ClockRollbackAndSingleFileLossFailClosed()
    {
        var store = new UpdatePolicyStore(_root);
        var now = DateTime.UnixEpoch.AddDays(10);
        _ = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 5)), now);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 5)), now.AddMinutes(-1)).Decision);
        File.Delete(Path.Combine(_root, "updates", "update-policy-anchor.json"));
        Assert.Throws<InvalidDataException>(() => UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now.AddMinutes(1)));
    }

    [Fact]
    public void ContradictoryForcedAutoContinueAndVersionStateFailsClosed()
    {
        var now = DateTime.UnixEpoch.AddDays(10);
        var updates = Path.Combine(_root, "updates");
        Directory.CreateDirectory(updates);
        var state = new UpdatePolicyState(1, "StoreExpiryInspector", Guid.NewGuid().ToString("N"), now, now, null, null, true, false, null);
        File.WriteAllText(Path.Combine(updates, "update-policy-state.json"), System.Text.Json.JsonSerializer.Serialize(state));
        File.WriteAllText(Path.Combine(updates, "update-policy-anchor.json"), "{\"SchemaVersion\":1,\"ProductId\":\"StoreExpiryInspector\",\"StateId\":\"" + state.StateId + "\"}");
        Assert.Throws<InvalidDataException>(() => UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
