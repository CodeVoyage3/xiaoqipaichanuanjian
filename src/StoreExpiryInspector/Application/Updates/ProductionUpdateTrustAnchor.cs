using System.Security.Cryptography;

namespace StoreExpiryInspector.Application.Updates;

public static class ProductionUpdateTrustAnchor
{
    public const string SpkiSha256 = "565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC";

    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAs737Mux0RbZEyMdRBMHB
        bcUF36TEZmBQVS+eu5qmQ4j/dgfu/e1oQjI4MYDcOrfCJK02nx2CwWv0h1EeqOOV
        mMZHuW335/ljnBZuBaOIqHxdzpS5xFIL6uoKkwXPKtHwHlxiCm7i5Z7Fmgwwb25R
        9UrKU/CJJZnSboc8zpJOenJWQDFqdJFVh+7Kqjbh4HAV3XObjSzV74TkYxsgx/Xe
        ws3cAgiOw8EjWnudUVNreSPerxdsvtbeDhaqddnYgTc+yZHRD1AOPHFEmLAVrq0A
        iW9/pYgVtIZvMuyzXl0WovCRgOoIZlFHQ8VpH50Jinrb0zFkpMgxNU1gp3WH8hEN
        w6A3OiD56JHuqjgU8RvhniuKkN7t2lEKREmzJzQViAhVG3TRPpn4MXFb8/4A7go8
        d/MBIrEII9Np82x+VXFgWE9hRMQsXkakJXaXdhfd+Mn9bRKmqlrRg7MkJqPtmMol
        sBNg9if7RAVwkv3SR0MXvzf6usQQ+0sH0nw5wGwX+q25AgMBAAE=
        -----END PUBLIC KEY-----
        """;

    public static UpdatePackageOptions Options { get; } = new(CreatePublicKey());

    public static RSAParameters CreatePublicKey()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PublicKeyPem);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())), SpkiSha256, StringComparison.Ordinal))
            throw new CryptographicException("Production update trust anchor fingerprint mismatch.");
        return rsa.ExportParameters(false);
    }
}
