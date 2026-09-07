using System.IO;
using System.Text.Json;
namespace StoreExpiryInspector.Application.Updates;

public enum UpdatePolicyDecision { AllowBusiness, ForceUpdate, RecheckRequired }

public sealed record UpdatePolicyState(
    int SchemaVersion,
    string ProductId,
    string StateId,
    DateTime FirstObservedUtc,
    DateTime LastObservedUtc,
    DateTime? LastSuccessfulCheckUtc,
    string? RequiredVersion,
    bool ForcedUpdateRequired,
    bool AutoContinue,
    string? LastBlockingReason);

// Small, independent policy store.  Stage9 journals remain authoritative for
// transaction recovery; this only decides whether ordinary business may open.
public sealed class UpdatePolicyStore
{
    private const int Schema = 1;
    private const string Product = "StoreExpiryInspector";
    private readonly string _statePath;
    private readonly string _anchorPath;

    public UpdatePolicyStore(string root)
    {
        var updates = Path.Combine(root, "updates");
        _statePath = Path.Combine(updates, "update-policy-state.json");
        _anchorPath = Path.Combine(updates, "update-policy-anchor.json");
    }

    public UpdatePolicyState LoadOrCreate(DateTime utcNow)
    {
        var hasState = File.Exists(_statePath); var hasAnchor = File.Exists(_anchorPath);
        if (!hasState && !hasAnchor)
        {
            var state = new UpdatePolicyState(Schema, Product, Guid.NewGuid().ToString("N"), utcNow, utcNow, null, null, false, false, null);
            Save(state); return state;
        }
        if (!hasState || !hasAnchor) throw new InvalidDataException("更新策略状态不完整，必须联网重新验证。");
        try
        {
            var state = JsonSerializer.Deserialize<UpdatePolicyState>(File.ReadAllBytes(_statePath)) ?? throw new InvalidDataException();
            var anchor = JsonSerializer.Deserialize<Anchor>(File.ReadAllBytes(_anchorPath)) ?? throw new InvalidDataException();
            if (state.SchemaVersion != Schema || state.ProductId != Product || !Guid.TryParseExact(state.StateId, "N", out _) ||
                anchor.SchemaVersion != Schema || anchor.ProductId != Product || anchor.StateId != state.StateId ||
                state.FirstObservedUtc.Kind != DateTimeKind.Utc || state.LastObservedUtc.Kind != DateTimeKind.Utc || state.LastSuccessfulCheckUtc?.Kind != DateTimeKind.Utc ||
                state.FirstObservedUtc > state.LastObservedUtc || state.LastSuccessfulCheckUtc > state.LastObservedUtc ||
                (state.ForcedUpdateRequired != !string.IsNullOrWhiteSpace(state.RequiredVersion)) ||
                (state.AutoContinue && !state.ForcedUpdateRequired) ||
                !ValidRequiredVersion(state.RequiredVersion)) throw new InvalidDataException();
            return state;
        }
        catch (JsonException exception) { throw new InvalidDataException("更新策略状态损坏，必须联网重新验证。", exception); }
    }

    public void Save(UpdatePolicyState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(state);
        DurableFile.Replace(_statePath, json);
        DurableFile.Replace(_anchorPath, JsonSerializer.SerializeToUtf8Bytes(new Anchor(Schema, Product, state.StateId)));
    }

    private sealed record Anchor(int SchemaVersion, string ProductId, string StateId);

    private static bool ValidRequiredVersion(string? value) => string.IsNullOrWhiteSpace(value) ||
        Version.TryParse(value, out var version) && version is not null && version.Build >= 0 && version.Revision < 0;
}

public static class UpdatePolicyGate
{
    private static readonly TimeSpan Grace = TimeSpan.FromHours(24);

    public static (UpdatePolicyDecision Decision, UpdatePolicyState State) Evaluate(UpdatePolicyStore store, UpdateCheckResult check, DateTime utcNow)
    {
        UpdatePolicyState state;
        try { state = store.LoadOrCreate(utcNow); }
        catch (InvalidDataException)
        {
            if (IsTemporary(check.Outcome)) throw;
            // A verified result may repair a damaged policy pair, but no local or
            // temporary-failure path gets to turn corruption into a new install.
            state = new UpdatePolicyState(1, "StoreExpiryInspector", Guid.NewGuid().ToString("N"), utcNow, utcNow, null, null, false, false, "STATE_INVALID");
        }
        if (utcNow < state.LastObservedUtc)
        {
            state = state with { LastBlockingReason = "CLOCK_ROLLBACK_RECHECK_REQUIRED" };
            store.Save(state); return (UpdatePolicyDecision.RecheckRequired, state);
        }
        state = state with { LastObservedUtc = utcNow };
        if (check.Outcome == UpdateCheckOutcome.UpToDate)
        {
            state = state with { LastSuccessfulCheckUtc = utcNow, RequiredVersion = null, ForcedUpdateRequired = false, AutoContinue = false, LastBlockingReason = null };
            store.Save(state); return (UpdatePolicyDecision.AllowBusiness, state);
        }
        if (check.Outcome == UpdateCheckOutcome.UpdateAvailable && check.LatestVersion is not null && check.LatestVersion > check.CurrentVersion)
        {
            state = state with { LastSuccessfulCheckUtc = utcNow, RequiredVersion = check.LatestVersion.ToString(3), ForcedUpdateRequired = true, LastBlockingReason = "FORCED_UPDATE_REQUIRED" };
            store.Save(state); return (UpdatePolicyDecision.ForceUpdate, state);
        }
        if (state.ForcedUpdateRequired || !IsTemporary(check.Outcome))
        {
            state = state with { LastBlockingReason = state.ForcedUpdateRequired ? state.LastBlockingReason ?? "FORCED_UPDATE_REQUIRED" : "SECURITY_OR_PATH_RECHECK_REQUIRED" };
            store.Save(state); return (UpdatePolicyDecision.RecheckRequired, state);
        }
        if (state.LastSuccessfulCheckUtc is { } successful && utcNow - successful <= Grace || state.LastSuccessfulCheckUtc is null && utcNow - state.FirstObservedUtc <= Grace)
        {
            store.Save(state); return (UpdatePolicyDecision.AllowBusiness, state);
        }
        state = state with { LastBlockingReason = "VERSION_VERIFICATION_EXPIRED" };
        store.Save(state); return (UpdatePolicyDecision.RecheckRequired, state);
    }

    public static UpdatePolicyState EnableAutoContinue(UpdatePolicyStore store, UpdatePolicyState state)
    {
        state = state with { AutoContinue = true }; store.Save(state); return state;
    }

    private static bool IsTemporary(UpdateCheckOutcome outcome) => outcome is UpdateCheckOutcome.NetworkUnavailable or UpdateCheckOutcome.RateLimited;
}
