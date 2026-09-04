using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;

namespace StoreExpiryInspector.Tests;

public sealed class S9T04SignedUpdatePackageTests
{
    [Fact]
    public void NotificationStateCancelsAndResetsForRetry()
    {
        var cancelled = 0;
        var result = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0), new Version(1, 0, 1));
        var model = new UpdateNotificationViewModel(result, () => { }, () => { });
        model.CancelRequested += () => cancelled++;
        model.Begin(); model.Report(new("正在下载更新包", 40, 100));
        Assert.True(model.IsBusy); Assert.Equal("40 / 100 字节（40%）", model.ProgressText); Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null); model.Complete(new(UpdatePackageOutcome.Cancelled, "已取消更新包准备。")); model.Begin();
        Assert.Equal(1, cancelled); Assert.True(model.IsBusy); Assert.Equal(0, model.ReceivedBytes); Assert.False(model.CancelCommand.CanExecute(null) == false);
    }
    [Fact]
    public async Task UnconfiguredProductionKeyFailsBeforeAnyRequest()
    {
        var handler = new Routes();
        var result = await new SignedUpdatePackageDownloader(handler).PrepareAsync(new(new Version(1, 0, 1), 7, "v1.0.0", []), new Version(0, 9, 9), null, CancellationToken.None);
        Assert.Equal(UpdatePackageOutcome.SigningNotConfigured, result.Outcome);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task SignedPackageCompletesThroughRealHttpMessageFlow()
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorT04", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "package.zip");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "StoreExpiryInspector.exe"), "StoreExpiryInspector.exe");
                zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "StoreExpiryInspector.dll"), "StoreExpiryInspector.dll");
            }
            var packageBytes = await File.ReadAllBytesAsync(package); var hash = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
            var manifest = Encoding.UTF8.GetBytes($"{{\"schemaVersion\":1,\"version\":\"1.0.0\",\"releaseTag\":\"v1.0.0\",\"repository\":\"CodeVoyage3/xiaoqipaichanuanjian\",\"channel\":\"stable\",\"rid\":\"win-x64\",\"minimumProtocolVersion\":1,\"package\":{{\"fileName\":\"StoreExpiryInspector-1.0.0-win-x64.zip\",\"bytes\":{packageBytes.Length},\"sha256\":\"{hash}\"}},\"targetMigrations\":[\"20260826123739_InitialCreate\",\"20260826130822_AddTasksAndDrafts\",\"20260826135612_AddInspectionHistory\",\"20260826142429_AddInventoryAdjustments\",\"20260826152131_AddImportPersistence\",\"20260826155455_AddBackupMetadata\",\"20260826162033_AddSettingsAndAppState\",\"20260826170403_AddLifecycleEvents\",\"20260901155124_AddPolicyAndBaselineFoundation\"],\"source\":{{\"minVersion\":\"0.9.9\",\"maxVersion\":\"1.0.0\",\"minMigration\":\"20260826123739_InitialCreate\",\"maxMigration\":\"20260901155124_AddPolicyAndBaselineFoundation\"}}}}");
            using var rsa = RSA.Create(2048); var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            var handler = new Routes(manifest, signature, packageBytes);
            var options = new UpdatePackageOptions(rsa.ExportParameters(false), CacheRoot: root);
            var release = new CheckedRelease(new Version(1, 0, 0), 7, "v1.0.0", ["update-manifest.json", "update-manifest.sig", "StoreExpiryInspector-1.0.0-win-x64.zip"]);
            var result = await new SignedUpdatePackageDownloader(handler, options).PrepareAsync(release, new Version(0, 9, 9), null, CancellationToken.None);
            Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome);
            Assert.NotNull(result.Package); Assert.True(File.Exists(result.Package!.PackagePath));
            Directory.Delete(result.Package.CacheDirectory, true);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Routes(byte[]? manifest = null, byte[]? signature = null, byte[]? package = null) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "api.github.com") return Task.FromResult(Bytes(HttpStatusCode.OK, Encoding.UTF8.GetBytes("{\"id\":7,\"tag_name\":\"v1.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"update-manifest.json\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.0/update-manifest.json\"},{\"name\":\"update-manifest.sig\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.0/update-manifest.sig\"},{\"name\":\"StoreExpiryInspector-1.0.0-win-x64.zip\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.0/StoreExpiryInspector-1.0.0-win-x64.zip\"}]}")));
            return Task.FromResult(Bytes(HttpStatusCode.OK, path.EndsWith(".sig") ? signature! : path.EndsWith(".zip") ? package! : manifest!));
        }
        private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] bytes) => new(status) { Content = new ByteArrayContent(bytes) };
    }
}

