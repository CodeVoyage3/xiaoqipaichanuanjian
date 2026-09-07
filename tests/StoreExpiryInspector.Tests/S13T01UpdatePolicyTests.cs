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
    public void TrustedHigherThenListNetworkFailurePersistsForcedStateAcrossRestart()
    {
        var now = DateTime.UnixEpoch.AddDays(10);
        var current = new Version(1, 0, 5);
        var result = new UpdateCheckResult(UpdateCheckOutcome.NetworkUnavailable, current, new Version(1, 0, 6), TrustedHigherLatest: true);
        var blocked = UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), result, now);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, blocked.Decision); Assert.True(blocked.State.ForcedUpdateRequired); Assert.Equal("1.0.6", blocked.State.RequiredVersion);
        var restarted = UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, current), now.AddMinutes(1));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, restarted.Decision); Assert.True(restarted.State.ForcedUpdateRequired); Assert.Equal("1.0.6", restarted.State.RequiredVersion);
    }

    [Fact]
    public void TrustedHigherWithoutLegalPathPersistsForcedState()
    {
        var current = new Version(1, 0, 5);
        var result = new UpdateCheckResult(UpdateCheckOutcome.NoLegalUpgradePath, current, new Version(1, 0, 6), TrustedHigherLatest: true);
        var blocked = UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), result, DateTime.UnixEpoch.AddDays(10));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, blocked.Decision); Assert.True(blocked.State.ForcedUpdateRequired); Assert.Equal("1.0.6", blocked.State.RequiredVersion);
    }

    [Fact]
    public void UntrustedNetworkFailureWithAClaimedVersionKeepsFreshGrace()
    {
        var current = new Version(1, 0, 5);
        var result = new UpdateCheckResult(UpdateCheckOutcome.NetworkUnavailable, current, new Version(1, 0, 6));
        var allowed = UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), result, DateTime.UnixEpoch.AddDays(10));
        Assert.Equal(UpdatePolicyDecision.AllowBusiness, allowed.Decision); Assert.False(allowed.State.ForcedUpdateRequired);
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
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now.AddMinutes(1)).Decision);
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
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now).Decision);
    }

    [Fact]
    public void DoublePolicyFileDeletionOnExistingDataRootNeverBecomesFreshGrace()
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        _ = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 5)), now);
        Directory.CreateDirectory(Path.Combine(_root, "data")); File.WriteAllText(Path.Combine(_root, "data", "app.db"), "existing-data-presence-only");
        File.Delete(Path.Combine(_root, "updates", "update-policy-state.json")); File.Delete(Path.Combine(_root, "updates", "update-policy-anchor.json"));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now.AddMinutes(1)).Decision);
    }

    [Fact]
    public void RemoteOlderCannotClearPersistedForcedState()
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        _ = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6)), now);
        var result = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.RemoteOlder, new Version(1, 0, 5)), now.AddMinutes(1));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, result.Decision); Assert.True(result.State.ForcedUpdateRequired);
    }

    [Fact]
    public void RequiredVersionOnlyMovesForwardAndClearsAtThePersistedTarget()
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        var forced = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 9)), now);
        var lower = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8)), now.AddMinutes(1));
        Assert.Equal("1.0.9", lower.State.RequiredVersion); Assert.True(lower.State.ForcedUpdateRequired);
        var staleLatest = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 8)), now.AddMinutes(2));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, staleLatest.Decision); Assert.Equal("1.0.9", staleLatest.State.RequiredVersion);
        var final = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 9)), now.AddMinutes(3));
        Assert.Equal(UpdatePolicyDecision.AllowBusiness, final.Decision); Assert.False(final.State.ForcedUpdateRequired); Assert.Null(final.State.RequiredVersion);
    }

    [Fact]
    public void TrustedHigherLatestRaisesTheRequiredVersion()
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        _ = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8)), now);
        var raised = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 9)), now.AddMinutes(1));
        Assert.Equal("1.0.9", raised.State.RequiredVersion); Assert.True(raised.State.ForcedUpdateRequired);
    }

    [Fact]
    public void PersistedHigherRequiredVersionRejectsLowerTrustedLatestWithoutOfferingIt()
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        _ = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 9)), now);
        var result = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 7), new Version(1, 0, 8)), now.AddMinutes(1));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, result.Decision); Assert.Equal("1.0.9", result.State.RequiredVersion); Assert.True(result.State.ForcedUpdateRequired);
    }

    [Fact]
    public void AutoContinueSurvivesReloadAndOnlyTrustedLatestConsumesIt()
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        var forced = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6)), now);
        _ = UpdatePolicyGate.EnableAutoContinue(store, forced.State);
        Assert.True(store.LoadOrCreate(now.AddMinutes(1)).AutoContinue);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now.AddMinutes(2)).Decision);
        var final = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, new Version(1, 0, 6)), now.AddMinutes(3));
        Assert.False(final.State.AutoContinue); Assert.False(final.State.ForcedUpdateRequired);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.NetworkUnavailable, UpdatePolicyDecision.AllowBusiness)]
    [InlineData(UpdateCheckOutcome.RateLimited, UpdatePolicyDecision.AllowBusiness)]
    [InlineData(UpdateCheckOutcome.Cancelled, UpdatePolicyDecision.RecheckRequired)]
    [InlineData(UpdateCheckOutcome.InvalidRemoteMetadata, UpdatePolicyDecision.RecheckRequired)]
    [InlineData(UpdateCheckOutcome.NoPublishedRelease, UpdatePolicyDecision.RecheckRequired)]
    [InlineData(UpdateCheckOutcome.RemoteOlder, UpdatePolicyDecision.RecheckRequired)]
    [InlineData(UpdateCheckOutcome.SecurityFailure, UpdatePolicyDecision.RecheckRequired)]
    [InlineData(UpdateCheckOutcome.NoLegalUpgradePath, UpdatePolicyDecision.RecheckRequired)]
    public void FreshPolicyClassifiesOnlyTemporaryFailuresAsGrace(UpdateCheckOutcome outcome, UpdatePolicyDecision expected)
    {
        var result = UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), UpdateCheckResult.From(outcome, new Version(1, 0, 5)), DateTime.UnixEpoch.AddDays(10));
        Assert.Equal(expected, result.Decision);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.NetworkUnavailable)]
    [InlineData(UpdateCheckOutcome.RateLimited)]
    [InlineData(UpdateCheckOutcome.InvalidRemoteMetadata)]
    [InlineData(UpdateCheckOutcome.SecurityFailure)]
    [InlineData(UpdateCheckOutcome.NoLegalUpgradePath)]
    [InlineData(UpdateCheckOutcome.RemoteOlder)]
    public void PersistedForcedStateBlocksEveryNonTrustedFollowup(UpdateCheckOutcome outcome)
    {
        var now = DateTime.UnixEpoch.AddDays(10); var store = new UpdatePolicyStore(_root);
        _ = UpdatePolicyGate.Evaluate(store, new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 5), new Version(1, 0, 6)), now);
        var result = UpdatePolicyGate.Evaluate(store, UpdateCheckResult.From(outcome, new Version(1, 0, 5)), now.AddMinutes(1));
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, result.Decision); Assert.True(result.State.ForcedUpdateRequired);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.InvalidRemoteMetadata)]
    [InlineData(UpdateCheckOutcome.SecurityFailure)]
    [InlineData(UpdateCheckOutcome.NoLegalUpgradePath)]
    [InlineData(UpdateCheckOutcome.NoPublishedRelease)]
    [InlineData(UpdateCheckOutcome.RemoteOlder)]
    [InlineData(UpdateCheckOutcome.Cancelled)]
    public void SecurityAndPathFailuresNeverUseOfflineGrace(UpdateCheckOutcome outcome)
    {
        var now = DateTime.UnixEpoch.AddDays(10);
        var decision = UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), UpdateCheckResult.From(outcome, new Version(1, 0, 5)), now);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, decision.Decision);
        Assert.Equal(UpdatePolicyDecision.RecheckRequired, UpdatePolicyGate.Evaluate(new UpdatePolicyStore(_root), UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, new Version(1, 0, 5)), now.AddMinutes(1)).Decision);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
