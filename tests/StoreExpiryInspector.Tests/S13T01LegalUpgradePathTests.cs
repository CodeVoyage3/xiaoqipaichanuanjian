using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S13T01LegalUpgradePathTests
{
    [Fact]
    public void UsesFewestHopsAndHigherVersionForTies()
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        LegalUpgradeRelease Node(string version, string min, string max) => new(Version.Parse(version), Version.Parse(min), Version.Parse(max), migration, migration, [migration], true);
        var path = LegalUpgradePath.Find(Version.Parse("1.0.5"), migration, Version.Parse("1.0.9"), [Node("1.0.6", "1.0.5", "1.0.5"), Node("1.0.7", "1.0.5", "1.0.5"), Node("1.0.9", "1.0.6", "1.0.7")]);
        Assert.Equal(["1.0.7", "1.0.9"], path!.Select(item => item.Version.ToString(3)));
    }

    [Fact]
    public void NeverUsesUntrustedOrMigrationMismatchNodes()
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        var result = LegalUpgradePath.Find(Version.Parse("1.0.5"), migration, Version.Parse("1.0.6"), [new LegalUpgradeRelease(Version.Parse("1.0.6"), Version.Parse("1.0.5"), Version.Parse("1.0.5"), "20260901155125_Other", "20260901155125_Other", [migration], true)]);
        Assert.Null(result);
    }

    [Fact]
    public void ResolverChoosesOnlyNextSignedHopAndFailsClosedWithoutOne()
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        VerifiedReleaseMetadata Node(string version, string min, string max) => new(new CheckedRelease(Version.Parse(version), long.Parse(version.Replace(".", "")), "v" + version, ["update-manifest.json", "update-manifest.sig", $"StoreExpiryInspector-{version}-win-x64.zip"]), Version.Parse(min), Version.Parse(max), migration, migration, [migration], 1);
        var resolved = TrustedUpdatePathResolver.ResolveVerifiedPath(Version.Parse("1.0.5"), [migration], [Node("1.0.7", "1.0.5", "1.0.5"), Node("1.0.9", "1.0.7", "1.0.7")]);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, resolved.Outcome); Assert.Equal("1.0.9", resolved.LatestVersion!.ToString(3)); Assert.Equal("1.0.7", resolved.Release!.Version.ToString(3));
        Assert.Equal(UpdateCheckOutcome.NoLegalUpgradePath, TrustedUpdatePathResolver.ResolveVerifiedPath(Version.Parse("1.0.5"), [migration], [Node("1.0.9", "1.0.7", "1.0.7")]).Outcome);
    }
}
