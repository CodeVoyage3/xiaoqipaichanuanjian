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
        var listed = await _releases.ListStableReleasesAsync(cancellationToken);
        if (listed.Outcome is UpdateCheckOutcome.NetworkUnavailable or UpdateCheckOutcome.RateLimited or UpdateCheckOutcome.Cancelled) return UpdateCheckResult.From(listed.Outcome, currentVersion);
        if (listed.Outcome != UpdateCheckOutcome.UpToDate) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        var trusted = new List<(LegalUpgradeRelease Node, CheckedRelease Release)>();
        foreach (var candidate in listed.Releases.OrderBy(item => item.Version))
        {
            var verified = await _packages.VerifyReleaseMetadataAsync(candidate, cancellationToken);
            if (verified.Outcome is UpdatePackageOutcome.NetworkUnavailable or UpdatePackageOutcome.RateLimited or UpdatePackageOutcome.Cancelled)
                return UpdateCheckResult.From(verified.Outcome == UpdatePackageOutcome.RateLimited ? UpdateCheckOutcome.RateLimited : UpdateCheckOutcome.NetworkUnavailable, currentVersion);
            if (verified.Outcome != UpdatePackageOutcome.Verified || verified.Metadata is null) continue; // Bad release is never a graph node.
            var item = verified.Metadata;
            trusted.Add((new(item.Release.Version, item.SourceMinVersion, item.SourceMaxVersion, item.SourceMinMigration, item.SourceMaxMigration, item.TargetMigrations, true), item.Release));
        }
        return ResolveVerifiedPath(currentVersion, currentMigrations, trusted.Select(item => new VerifiedReleaseMetadata(item.Release, item.Node.SourceMinVersion, item.Node.SourceMaxVersion, item.Node.SourceMinMigration, item.Node.SourceMaxMigration, item.Node.TargetMigrations, 1)));
    }

    public static UpdateCheckResult ResolveVerifiedPath(Version currentVersion, IReadOnlyList<string> currentMigrations, IEnumerable<VerifiedReleaseMetadata> releases)
    {
        if (currentMigrations.Count == 0) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        var trusted = releases.Select(item => (Node: new LegalUpgradeRelease(item.Release.Version, item.SourceMinVersion, item.SourceMaxVersion, item.SourceMinMigration, item.SourceMaxMigration, item.TargetMigrations, true), Release: item.Release)).ToArray();
        if (trusted.Length == 0) return UpdateCheckResult.From(UpdateCheckOutcome.SecurityFailure, currentVersion);
        var latest = trusted.Select(item => item.Node.Version).Max()!;
        if (latest <= currentVersion) return UpdateCheckResult.From(UpdateCheckOutcome.UpToDate, currentVersion);
        var path = LegalUpgradePath.Find(currentVersion, currentMigrations[^1], latest, trusted.Select(item => item.Node));
        if (path is null || path.Count == 0) return new(UpdateCheckOutcome.NoLegalUpgradePath, currentVersion, latest);
        var next = path[0];
        var nextRelease = trusted.Single(item => item.Node.Version == next.Version).Release;
        return new(UpdateCheckOutcome.UpdateAvailable, currentVersion, latest, Release: nextRelease);
    }
}
