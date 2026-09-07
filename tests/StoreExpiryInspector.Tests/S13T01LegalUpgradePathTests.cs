using System.Net;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void AuthoritativeLatestMustBeTheVerifiedNodeBeforeCurrentCanBeUpToDate()
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        var current = Version.Parse("1.0.5");
        var trusted = new VerifiedReleaseMetadata(new CheckedRelease(current, 105, "v1.0.5", []), current, current, migration, migration, [migration], 1);
        Assert.Equal(UpdateCheckOutcome.UpToDate, TrustedUpdatePathResolver.ResolveVerifiedPath(current, [migration], current, [trusted]).Outcome);
        Assert.Equal(UpdateCheckOutcome.SecurityFailure, TrustedUpdatePathResolver.ResolveVerifiedPath(current, [migration], Version.Parse("1.0.6"), [trusted]).Outcome);
    }

    [Fact]
    public void AuthoritativeRemoteOlderIsNotUpToDate()
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        Assert.Equal(UpdateCheckOutcome.RemoteOlder, TrustedUpdatePathResolver.ResolveVerifiedPath(Version.Parse("1.0.6"), [migration], Version.Parse("1.0.5"), []).Outcome);
    }

    [Theory]
    [InlineData(1, UpdateCheckOutcome.UpdateAvailable)]
    [InlineData(2, UpdateCheckOutcome.UpdateAvailable)]
    [InlineData(0, UpdateCheckOutcome.SecurityFailure)]
    public void ResolverOnlyAcceptsTheImplementedProtocol(int protocol, UpdateCheckOutcome expected)
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        var release = new CheckedRelease(Version.Parse("1.0.6"), 106, "v1.0.6", []);
        var metadata = new VerifiedReleaseMetadata(release, Version.Parse("1.0.5"), Version.Parse("1.0.5"), migration, migration, [migration], protocol);
        Assert.Equal(expected, TrustedUpdatePathResolver.ResolveVerifiedPath(Version.Parse("1.0.5"), [migration], release.Version, [metadata]).Outcome);
    }

    [Fact]
    public void IntermediateBadProtocolIsExcludedButSignedAuthoritativeLatestRemainsReachable()
    {
        var migration = "20260901155124_AddPolicyAndBaselineFoundation";
        VerifiedReleaseMetadata Node(string version, string min, string max, int protocol) => new(new CheckedRelease(Version.Parse(version), long.Parse(version.Replace(".", "")), "v" + version, []), Version.Parse(min), Version.Parse(max), migration, migration, [migration], protocol);
        var result = TrustedUpdatePathResolver.ResolveVerifiedPath(Version.Parse("1.0.5"), [migration], Version.Parse("1.0.7"), [Node("1.0.6", "1.0.5", "1.0.5", 3), Node("1.0.7", "1.0.5", "1.0.5", 1)]);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome); Assert.Equal("1.0.7", result.Release!.Version.ToString(3));
    }

    [Fact]
    public async Task TrustedHigherLatestSurvivesAStableListNetworkFailure()
    {
        const string migration = "20260901155124_AddPolicyAndBaselineFoundation";
        var manifest = Manifest(migration, "1.0.5");
        using var key = RSA.Create(2048);
        var signature = key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var result = await new TrustedUpdatePathResolver(
            new GitHubReleaseUpdateChecker(new LatestAndFailedList(manifest, signature)),
            new SignedUpdatePackageDownloader(new LatestAndFailedList(manifest, signature), new UpdatePackageOptions(key.ExportParameters(false))))
            .CheckAsync(new Version(1, 0, 5), [migration], CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.NetworkUnavailable, result.Outcome); Assert.Equal("1.0.6", result.LatestVersion!.ToString(3)); Assert.True(result.TrustedHigherLatest); Assert.Null(result.Release);
    }

    [Fact]
    public async Task TrustedHigherLatestMarksNoLegalPath()
    {
        const string migration = "20260901155124_AddPolicyAndBaselineFoundation";
        var manifest = Manifest(migration, "1.0.4");
        using var key = RSA.Create(2048);
        var signature = key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var result = await new TrustedUpdatePathResolver(
            new GitHubReleaseUpdateChecker(new LatestAndFailedList(manifest, signature, false)),
            new SignedUpdatePackageDownloader(new LatestAndFailedList(manifest, signature, false), new UpdatePackageOptions(key.ExportParameters(false))))
            .CheckAsync(new Version(1, 0, 5), [migration], CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.NoLegalUpgradePath, result.Outcome); Assert.Equal("1.0.6", result.LatestVersion!.ToString(3)); Assert.True(result.TrustedHigherLatest); Assert.Null(result.Release);
    }

    private static byte[] Manifest(string migration, string sourceVersion) => Encoding.UTF8.GetBytes($"{{\"schemaVersion\":1,\"version\":\"1.0.6\",\"releaseTag\":\"v1.0.6\",\"repository\":\"CodeVoyage3/xiaoqipaichanuanjian\",\"channel\":\"stable\",\"rid\":\"win-x64\",\"minimumProtocolVersion\":1,\"package\":{{\"fileName\":\"StoreExpiryInspector-1.0.6-win-x64.zip\",\"bytes\":1,\"sha256\":\"{new string('0', 64)}\"}},\"targetMigrations\":[\"{migration}\"],\"source\":{{\"minVersion\":\"{sourceVersion}\",\"maxVersion\":\"{sourceVersion}\",\"minMigration\":\"{migration}\",\"maxMigration\":\"{migration}\"}}}}");

    private sealed class LatestAndFailedList(byte[] manifest, byte[] signature, bool listFails = true) : HttpMessageHandler
    {
        private const string Assets = "[{\"name\":\"update-manifest.json\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.6/update-manifest.json\"},{\"name\":\"update-manifest.sig\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.6/update-manifest.sig\"},{\"name\":\"StoreExpiryInspector-1.0.6-win-x64.zip\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.6/StoreExpiryInspector-1.0.6-win-x64.zip\"}]";
        private static readonly byte[] Release = Encoding.UTF8.GetBytes($"{{\"id\":6,\"tag_name\":\"v1.0.6\",\"draft\":false,\"prerelease\":false,\"assets\":{Assets}}}");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "api.github.com" && uri.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)) return Task.FromResult(Response(HttpStatusCode.OK, Release));
            if (uri.Host == "api.github.com" && uri.Query.Contains("per_page=100", StringComparison.Ordinal)) return Task.FromResult(listFails ? Response(HttpStatusCode.InternalServerError, []) : Response(HttpStatusCode.OK, Encoding.UTF8.GetBytes("[" + Encoding.UTF8.GetString(Release) + "]")));
            if (uri.Host == "api.github.com") return Task.FromResult(Response(HttpStatusCode.OK, Release));
            return Task.FromResult(Response(HttpStatusCode.OK, uri.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal) ? signature : manifest));
        }

        private static HttpResponseMessage Response(HttpStatusCode status, byte[] body) => new(status) { Content = new ByteArrayContent(body) };
    }
}
