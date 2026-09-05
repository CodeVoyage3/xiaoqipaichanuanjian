using System.Security.Cryptography;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T06ProductionTrustAnchorTests
{
    [Fact]
    public void ProductionAnchorIsPinnedToTheDocumentedSpkiFingerprint()
    {
        var key = ProductionUpdateTrustAnchor.CreatePublicKey();
        using var rsa = RSA.Create();
        rsa.ImportParameters(key);
        Assert.Equal(3072, rsa.KeySize);
        Assert.Equal(ProductionUpdateTrustAnchor.SpkiSha256, Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())));
        Assert.NotNull(ProductionUpdateTrustAnchor.Options.CreateVerifier());
    }
}
