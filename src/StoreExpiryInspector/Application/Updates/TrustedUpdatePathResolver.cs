namespace StoreExpiryInspector.Application.Updates;

// The release list is only an index.  A node exists only after the signed
// manifest has independently bound it to this repository, tag and package.
public sealed class TrustedUpdatePathResolver
{
    private readonly GitHubReleaseUpdateChecker _releases;
    private readonly SignedUpdatePackageDownloader _packages;

    public TrustedUpdatePathResolver(GitHubReleaseUpdateChecker releases, SignedUpdatePackageDownloader packages)
    {
        _releases = releases;
        _packages = packages;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, IReadOnlyList<string> currentMigrations, CancellationToken cancellationToken)
    {
        if (currentMigrations.Count == 0) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        var latest = await _releases.CheckAsync(currentVersion, cancellationToken);
        if (latest.Outcome is UpdateCheckOutcome.NetworkUnavailable or UpdateCheckOutcome.RateLimited or UpdateCheckOutcome.Cancelled) return UpdateCheckResult.From(latest.Outcome, currentVersion);
        if (latest.Outcome == UpdateCheckOutcome.RemoteOlder) return UpdateCheckResult.From(UpdateCheckOutcome.RemoteOlder, currentVersion);
        if (latest.Outcome is not (UpdateCheckOutcome.UpToDate or UpdateCheckOutcome.UpdateAvailable) || latest.LatestVersion is null || latest.Release is null)
            return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);

        var authoritative = await _packages.VerifyReleaseMetadataAsync(latest.Release, cancellationToken);
        if (authoritative.Outcome is UpdatePackageOutcome.NetworkUnavailable or UpdatePackageOutcome.RateLimited or UpdatePackageOutcome.Cancelled)
            return UpdateCheckResult.From(authoritative.Outcome == UpdatePackageOutcome.RateLimited ? UpdateCheckOutcome.RateLimited : authoritative.Outcome == UpdatePackageOutcome.Cancelled ? UpdateCheckOutcome.Cancelled : UpdateCheckOutcome.NetworkUnavailable, currentVersion);
        // The release named by releases/latest is the authority.  Never silently
        // fall back to an older signed node if its manifest cannot be trusted.
        if (authoritative.Outcome != UpdatePackageOutcome.Verified || authoritative.Metadata is null)
            return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        var listed = await _releases.ListStableReleasesAsync(cancellationToken);
        if (listed.Outcome is UpdateCheckOutcome.NetworkUnavailable or UpdateCheckOutcome.RateLimited or UpdateCheckOutcome.Cancelled) return UpdateCheckResult.From(listed.Outcome, currentVersion);
        if (listed.Outcome != UpdateCheckOutcome.UpToDate) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        var trusted = new List<VerifiedReleaseMetadata> { authoritative.Metadata };
        foreach (var candidate in listed.Releases.OrderBy(item => item.Version))
        {
            if (candidate.Version == latest.LatestVersion) continue;
            var verified = await _packages.VerifyReleaseMetadataAsync(candidate, cancellationToken);
            if (verified.Outcome is UpdatePackageOutcome.NetworkUnavailable or UpdatePackageOutcome.RateLimited or UpdatePackageOutcome.Cancelled)
                return UpdateCheckResult.From(verified.Outcome == UpdatePackageOutcome.RateLimited ? UpdateCheckOutcome.RateLimited : UpdateCheckOutcome.NetworkUnavailable, currentVersion);
            if (verified.Outcome != UpdatePackageOutcome.Verified || verified.Metadata is null) continue; // Bad release is never a graph node.
            if (verified.Outcome == UpdatePackageOutcome.Verified && verified.Metadata is not null) trusted.Add(verified.Metadata);
        }
        return ResolveVerifiedPath(currentVersion, currentMigrations, latest.LatestVersion, trusted);
    }

    public static UpdateCheckResult ResolveVerifiedPath(Version currentVersion, IReadOnlyList<string> currentMigrations, IEnumerable<VerifiedReleaseMetadata> releases)
        => ResolveVerifiedPath(currentVersion, currentMigrations, releases.Select(item => item.Release.Version).Aggregate(currentVersion, (latest, version) => version > latest ? version : latest), releases);

    public static UpdateCheckResult ResolveVerifiedPath(Version currentVersion, IReadOnlyList<string> currentMigrations, Version authoritativeLatest, IEnumerable<VerifiedReleaseMetadata> releases)
    {
        if (currentMigrations.Count == 0) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        if (authoritativeLatest < currentVersion) return UpdateCheckResult.From(UpdateCheckOutcome.RemoteOlder, currentVersion);
        var trusted = releases.Where(item => item.MinimumProtocolVersion is >= 1 and <= 2).Select(item => (Node: new LegalUpgradeRelease(item.Release.Version, item.SourceMinVersion, item.SourceMaxVersion, item.SourceMinMigration, item.SourceMaxMigration, item.TargetMigrations, true), Release: item.Release)).ToArray();
        if (trusted.Length == 0) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        if (!trusted.Any(item => item.Node.Version == authoritativeLatest)) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        if (authoritativeLatest == currentVersion) return UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, currentVersion);
        var path = LegalUpgradePath.Find(currentVersion, currentMigrations[^1], authoritativeLatest, trusted.Select(item => item.Node));
        if (path is null || path.Count == 0) return new(UpdateCheckOutcome.NoLegalUpgradePath, currentVersion, authoritativeLatest);
        var next = path[0];
        var nextRelease = trusted.Single(item => item.Node.Version == next.Version).Release;
        return new(UpdateCheckOutcome.UpdateAvailable, currentVersion, authoritativeLatest, Release: nextRelease);
    }
}
