using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UpdateSafety;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class V110ReleaseAssetTests
{
    [Fact]
    public void ProductionTrustAnchorRevalidatesFrozenReleaseAssets()
    {
        var assets = Environment.GetEnvironmentVariable("V110_RELEASE_ASSET_DIR");
        Assert.False(string.IsNullOrWhiteSpace(assets));
        var result = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options).PrepareEmbedded(
            Path.Combine(assets!, "StoreExpiryInspector-1.1.0-win-x64.zip"),
            Path.Combine(assets, "update-manifest.json"),
            Path.Combine(assets, "update-manifest.sig"),
            new Version(1, 0, 9), CurrentSchemaIdentity.Migrations[8], CancellationToken.None);
        Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome);
    }
}
