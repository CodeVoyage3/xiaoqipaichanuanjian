using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UpdateSafety;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V110ReleaseAssetTests
{
    [Fact]
    public void ProductionTrustAnchorRevalidatesFrozenReleaseAssets()
    {
        var assets = Environment.GetEnvironmentVariable("V110_RELEASE_ASSET_DIR");
        Assert.False(string.IsNullOrWhiteSpace(assets));
        var manifestPath = Path.Combine(assets!, "update-manifest.json");
        var packagePath = Path.Combine(assets, "StoreExpiryInspector-1.1.0-win-x64.zip");
        var manifest = File.ReadAllBytes(manifestPath);
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var package = new VerifiedUpdatePackage(assets, packagePath, new Version(1, 1, 0),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))), CurrentSchemaIdentity.Migrations,
            manifest, File.ReadAllBytes(Path.Combine(assets, "update-manifest.sig")),
            new CheckedRelease(new Version(1, 1, 0), 1, "v1.1.0", ["update-manifest.json", "update-manifest.sig", "StoreExpiryInspector-1.1.0-win-x64.zip"]),
            2, new Version(1, 0, 9), new Version(1, 0, 9),
            root.GetProperty("source").GetProperty("minMigration").GetString(),
            root.GetProperty("source").GetProperty("maxMigration").GetString());
        var result = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options).RevalidateForInstall(package, CancellationToken.None);
        Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome);
    }
}
