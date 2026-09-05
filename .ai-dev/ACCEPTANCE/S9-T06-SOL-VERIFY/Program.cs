using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;

// Evidence harness only. Never constructs App, RuntimeDataRoot or a production database.
var runRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
var relative = Path.GetRelativePath(Path.GetTempPath(), runRoot);
if (Path.IsPathRooted(relative) || !Guid.TryParse(relative, out _)) throw new InvalidOperationException("Run from TEMP/GUID only");
var results = new List<object>();
var failures = 0;
void Check(string name, bool pass, object? details = null)
{
    results.Add(new { name, pass, details });
    Console.WriteLine(name + ": " + (pass ? "PASS" : "FAIL"));
    if (!pass) failures++;
}
using var publicKey = RSA.Create();
publicKey.ImportParameters(ProductionUpdateTrustAnchor.CreatePublicKey());
var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo()));
Check("embedded-production-public-key", fingerprint == "565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC", fingerprint);
var service = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options);
if (args[0] == "local")
{
    var assets = Path.GetFullPath(args[1]);
    var manifest = File.ReadAllBytes(Path.Combine(assets, "update-manifest.json"));
    var signature = File.ReadAllBytes(Path.Combine(assets, "update-manifest.sig"));
    using var document = JsonDocument.Parse(manifest);
    var root = document.RootElement;
    var version = Version.Parse(root.GetProperty("version").GetString()!);
    var package = root.GetProperty("package");
    var packageName = package.GetProperty("fileName").GetString()!;
    if (packageName != $"StoreExpiryInspector-{version.ToString(3)}-win-x64.zip") throw new InvalidDataException();
    var packagePath = Path.Combine(assets, packageName);
    var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath)));
    Check("raw-production-manifest-signature", publicKey.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    Check("package-size-sha", new FileInfo(packagePath).Length == package.GetProperty("bytes").GetInt64() && sha.Equals(package.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), new { bytes = new FileInfo(packagePath).Length, sha });
    var migrations = root.GetProperty("targetMigrations").EnumerateArray().Select(x => x.GetString()!).ToArray();
    var scratch = Path.Combine(runRoot, Guid.NewGuid().ToString());
    Directory.CreateDirectory(scratch);
    var verified = new VerifiedUpdatePackage(scratch, packagePath, version, sha, migrations, manifest, signature,
        new CheckedRelease(version, 1, "v" + version.ToString(3), [packageName, "update-manifest.json", "update-manifest.sig"]));
    var accepted = service.RevalidateForInstall(verified, CancellationToken.None);
    Check("production-client-full-archive-revalidation", accepted.Outcome == UpdatePackageOutcome.Verified, accepted.Outcome.ToString());
    var wrongSignature = signature.ToArray(); wrongSignature[0] ^= 1;
    var wrong = service.RevalidateForInstall(verified with { ManifestSignature = wrongSignature }, CancellationToken.None);
    Check("wrong-production-signature-rejected", wrong.Outcome == UpdatePackageOutcome.InvalidManifestSignature, wrong.Outcome.ToString());
    using var testKey = RSA.Create(3072);
    var testSigned = testKey.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    var rejected = service.RevalidateForInstall(verified with { ManifestSignature = testSigned }, CancellationToken.None);
    Check("test-key-rejected-by-production-client", rejected.Outcome == UpdatePackageOutcome.InvalidManifestSignature, rejected.Outcome.ToString());
}
else if (args[0] == "github")
{
    var current = new Version(1, 0, 0);
    var check = await new GitHubReleaseUpdateChecker().CheckAsync(current, CancellationToken.None);
    Check("real-anonymous-github-check", check.Outcome == UpdateCheckOutcome.UpdateAvailable && check.LatestVersion == new Version(1, 0, 1), check.Outcome.ToString());
    if (check.Release is not null)
    {
        var downloaded = await service.PrepareAsync(check.Release, current, null, CancellationToken.None);
        Check("real-github-production-download-verify", downloaded.Outcome == UpdatePackageOutcome.Verified,
            new { outcome = downloaded.Outcome.ToString(), downloaded.Package?.Version, downloaded.Package?.Sha256, downloaded.Package?.PackagePath });
        if (downloaded.Package is not null)
            Check("real-download-consumer-revalidation", service.RevalidateForInstall(downloaded.Package, CancellationToken.None).Outcome == UpdatePackageOutcome.Verified);
    }
}
else if (args[0] == "network-negative")
{
    var release = new CheckedRelease(new Version(1, 0, 1), 1, "v1.0.1", ["update-manifest.json", "update-manifest.sig", "StoreExpiryInspector-1.0.1-win-x64.zip"]);
    foreach (var mode in new[] { "offline", "timeout", "cancel" })
    {
        var cache = Path.Combine(runRoot, Guid.NewGuid().ToString());
        using var handler = new FailureTransport(mode);
        var client = new SignedUpdatePackageDownloader(handler, ProductionUpdateTrustAnchor.Options with { MetadataTimeout = TimeSpan.FromMilliseconds(80), CacheRoot = cache });
        using var cancel = new CancellationTokenSource();
        if (mode == "cancel") cancel.Cancel();
        var result = await client.PrepareAsync(release, new Version(1, 0, 0), null, cancel.Token);
        Check("isolated-network-" + mode, result.Package is null && result.Outcome is UpdatePackageOutcome.NetworkUnavailable or UpdatePackageOutcome.Cancelled,
            new { outcome = result.Outcome.ToString(), updaterStarted = false, noVerifiedPackage = result.Package is null });
    }
}
else throw new ArgumentException("Unknown verification mode");
File.WriteAllText(Path.Combine(runRoot, "result-" + args[0] + ".json"), JsonSerializer.Serialize(new { mode = args[0], failures, results }, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

sealed class FailureTransport(string mode) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Headers.Authorization is not null) throw new InvalidOperationException("Unexpected client credential");
        if (mode == "offline") throw new HttpRequestException("Synthetic offline transport");
        await Task.Delay(Timeout.Infinite, token);
        return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
    }
}
