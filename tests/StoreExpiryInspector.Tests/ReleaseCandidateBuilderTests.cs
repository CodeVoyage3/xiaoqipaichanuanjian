using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UpdateSafety;
using System.Runtime.Loader;
using System.Security.Cryptography;
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
        Assert.DoesNotContain("Join-Path $source 'tests\\StoreExpiryInspector.Tests", builder, StringComparison.Ordinal);
        Assert.Contains("S20_RELEASE_CANDIDATE_SCHEMA_ASSEMBLY", builder, StringComparison.Ordinal);

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
    public void CandidateIdentityProbeReadsCandidateAssembly()
    {
        var output = Environment.GetEnvironmentVariable("S20_RELEASE_IDENTITY_PROBE");
        if (string.IsNullOrWhiteSpace(output)) return;

        var assemblyPath = Path.GetFullPath(Environment.GetEnvironmentVariable("S20_RELEASE_CANDIDATE_SCHEMA_ASSEMBLY")!);
        Assert.True(File.Exists(assemblyPath));
        var loadContext = new AssemblyLoadContext("S20 candidate schema probe", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            Assert.Equal(assemblyPath, assembly.Location, ignoreCase: true);
            var type = assembly.GetType("StoreExpiryInspector.UpdateSafety.CurrentSchemaIdentity", throwOnError: true)!;
            var migrations = ((IEnumerable<string>)type.GetProperty("Migrations")!.GetValue(null)!).ToArray();
            var count = (int)type.GetProperty("Count")!.GetValue(null)!;
            var latestMigration = (string)type.GetProperty("LastMigration")!.GetValue(null)!;
            Assert.Equal(migrations.Length, count);
            Assert.Equal(migrations[^1], latestMigration);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                candidateAssemblyPath = assembly.Location,
                candidateAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant(),
                currentSchemaIdentity = migrations,
                migrationCount = count,
                latestMigration
            }));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ProductionEfMigrationsMatchCurrentSchemaIdentity()
    {
        using var context = new StoreDbContextFactory().CreateDbContext([]);
        var ef = context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.True(ef.SequenceEqual(CurrentSchemaIdentity.Migrations, StringComparer.Ordinal));
    }

    [Fact]
    public void ProductionTrustAnchorRevalidatesReleaseCandidate()
    {
        var assets = Environment.GetEnvironmentVariable("S20_RELEASE_ASSET_DIR");
        if (string.IsNullOrWhiteSpace(assets)) return;

        var versionText = Environment.GetEnvironmentVariable("S20_RELEASE_VERSION")!;
        var resultPath = Environment.GetEnvironmentVariable("S20_RELEASE_REVALIDATION_RESULT")!;
        var packagePath = Path.Combine(assets, $"StoreExpiryInspector-{versionText}-win-x64.zip");
        var manifest = File.ReadAllBytes(Path.Combine(assets, "update-manifest.json"));
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var source = root.GetProperty("source");
        var version = Version.Parse(root.GetProperty("version").GetString()!);
        var migrations = root.GetProperty("targetMigrations").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var package = new VerifiedUpdatePackage(assets, packagePath, version,
            root.GetProperty("package").GetProperty("sha256").GetString()!, migrations,
            manifest, File.ReadAllBytes(Path.Combine(assets, "update-manifest.sig")),
            new CheckedRelease(version, 1, root.GetProperty("releaseTag").GetString()!,
                ["update-manifest.json", "update-manifest.sig", Path.GetFileName(packagePath)]),
            root.GetProperty("minimumProtocolVersion").GetInt32(),
            Version.Parse(source.GetProperty("minVersion").GetString()!), Version.Parse(source.GetProperty("maxVersion").GetString()!),
            source.GetProperty("minMigration").GetString(), source.GetProperty("maxMigration").GetString());
        var result = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options).RevalidateForInstall(package, CancellationToken.None);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(new { outcome = result.Outcome.ToString(), result.Message }));
        Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome);
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "StoreExpiryInspector.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("repository root not found");
    }
}
