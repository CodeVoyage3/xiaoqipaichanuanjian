using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UpdateSafety;
using System.Text.Json;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ReleaseCandidateBuilderTests
{
    [Fact]
    public void ContractAndReceiptSchemaStayNarrow()
    {
        var root = RepositoryRoot();
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "release", "release-contract.json")));
        var raw = contract.RootElement.GetRawText();
        Assert.DoesNotContain("targetMigrations", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256", raw, StringComparison.OrdinalIgnoreCase);

        var builder = File.ReadAllText(Path.Combine(root, "tools", "release", "Build-ReleaseCandidate.ps1"));
        Assert.DoesNotContain("-p:Version", builder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git push", builder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gh release", builder, StringComparison.OrdinalIgnoreCase);

        var releases = contract.RootElement.GetProperty("releases").EnumerateArray().ToArray();
        Assert.Equal(releases.Length, releases.Select(item => item.GetProperty("targetVersion").GetString()).Distinct(StringComparer.Ordinal).Count());

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "release", "release-receipt.schema.json")));
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("builderSha", required);
        Assert.Contains("builderSourceClean", required);
        Assert.Contains("mode", required);
        Assert.Contains("publishAuthorized", required);
    }

    [Fact]
    public void CandidateIdentityProbeUsesProductionSchemaAuthority()
    {
        var output = Environment.GetEnvironmentVariable("S20_RELEASE_IDENTITY_PROBE");
        if (string.IsNullOrWhiteSpace(output)) return;

        using var context = new StoreDbContextFactory().CreateDbContext([]);
        var ef = context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.True(ef.SequenceEqual(CurrentSchemaIdentity.Migrations, StringComparer.Ordinal));
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            currentSchemaIdentity = CurrentSchemaIdentity.Migrations,
            migrationCount = CurrentSchemaIdentity.Count,
            latestMigration = CurrentSchemaIdentity.LastMigration
        }));
    }

    [Fact]
    public void ProductionTrustAnchorRevalidatesReleaseCandidate()
    {
        var assets = Environment.GetEnvironmentVariable("S20_RELEASE_ASSET_DIR");
        if (string.IsNullOrWhiteSpace(assets)) return;

        var version = Environment.GetEnvironmentVariable("S20_RELEASE_VERSION")!;
        var sourceVersion = Environment.GetEnvironmentVariable("S20_RELEASE_SOURCE_VERSION")!;
        var sourceMigration = Environment.GetEnvironmentVariable("S20_RELEASE_SOURCE_MIGRATION")!;
        var resultPath = Environment.GetEnvironmentVariable("S20_RELEASE_REVALIDATION_RESULT")!;
        var result = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options).PrepareEmbedded(
            Path.Combine(assets, $"StoreExpiryInspector-{version}-win-x64.zip"),
            Path.Combine(assets, "update-manifest.json"),
            Path.Combine(assets, "update-manifest.sig"),
            Version.Parse(sourceVersion), sourceMigration, CancellationToken.None);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(new { outcome = result.Outcome.ToString() }));
        Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome);
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "StoreExpiryInspector.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("repository root not found");
    }
}
