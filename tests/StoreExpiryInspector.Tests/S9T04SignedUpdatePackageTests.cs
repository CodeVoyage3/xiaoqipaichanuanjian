using System.IO.Compression;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;
using StoreExpiryInspector.Infrastructure;
using Microsoft.Data.Sqlite;

namespace StoreExpiryInspector.Tests;

public sealed class S9T04SignedUpdatePackageTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":null}")]
    public async Task SignedMalformedManifestIsInvalidAndCleansCache(string manifestText)
    {
        var root = Path.Combine(Path.GetTempPath(), "StoreExpiryInspectorT04", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var manifest = Encoding.UTF8.GetBytes(manifestText); using var rsa = RSA.Create(2048);
            var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            var release = new CheckedRelease(new Version(1, 0, 0), 7, "v1.0.0", ["update-manifest.json", "update-manifest.sig", "StoreExpiryInspector-1.0.0-win-x64.zip"]);
            var result = await new SignedUpdatePackageDownloader(new Routes(manifest, signature, []), new UpdatePackageOptions(rsa.ExportParameters(false), CacheRoot: root)).PrepareAsync(release, new Version(0, 9, 9), null, CancellationToken.None);
            Assert.Equal(UpdatePackageOutcome.InvalidManifest, result.Outcome); Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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
            var (downloader, package) = await CreateVerifiedPackageAsync(root);
            Assert.True(File.Exists(package.PackagePath));
            Assert.Equal(UpdatePackageOutcome.Verified, downloader.RevalidateForInstall(package, CancellationToken.None).Outcome);
            await File.AppendAllTextAsync(package.PackagePath, "tampered");
            Assert.Equal(UpdatePackageOutcome.HashMismatch, downloader.RevalidateForInstall(package, CancellationToken.None).Outcome);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InstallationPreparationCopiesIndependentUpdaterAndWritesJournalAfterRevalidation()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var updater = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root); Directory.CreateDirectory(install); Directory.CreateDirectory(data); Directory.CreateDirectory(updater);
        try
        {
            CreateClosedMigration9Database(data);
            Directory.CreateDirectory(Path.Combine(install, "app")); File.WriteAllText(Path.Combine(install, "app", "old.dll"), "old");
            File.WriteAllText(Path.Combine(updater, "StoreExpiryInspector.Updater.exe"), "independent");
            var (downloader, package) = await CreateVerifiedPackageAsync(root);
            var prepared = new UpdateInstallationPreparer(downloader).PrepareForTest(package, Process.GetCurrentProcess(), install, data, updater, CancellationToken.None);
            Assert.True(File.Exists(prepared.JournalPath)); Assert.True(File.Exists(prepared.UpdaterPath));
            var journal = System.Text.Json.JsonSerializer.Deserialize<InstallationJournal>(File.ReadAllText(prepared.JournalPath), new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
            Assert.Equal(InstallationUpdatePhase.Prepared, journal.Phase); Assert.Equal(journal.CandidateTree.Hash, InstallationTreeFingerprint.Create(journal.StagingPath).Hash);
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var directory in new[] { root, install, data, updater }) if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InstallRevalidationRejectsTamperedSignedSourceFields()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try
        {
            var (downloader, package) = await CreateVerifiedPackageAsync(root);
            var replacements = new VerifiedUpdatePackage[]
            {
                package with { SourceMinVersion = new Version(0, 9, 8) },
                package with { SourceMaxVersion = new Version(9, 9, 9) },
                package with { SourceMinMigration = "20260826130822_AddTasksAndDrafts" },
                package with { SourceMaxMigration = "20260826170403_AddLifecycleEvents" }
            };
            foreach (var tampered in replacements)
                Assert.Equal(UpdatePackageOutcome.VersionMismatch, downloader.RevalidateForInstall(tampered, CancellationToken.None).Outcome);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InstallationPreparationRejectsActualSourceOutsideSignedRange()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var updater = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root); Directory.CreateDirectory(install); Directory.CreateDirectory(data); Directory.CreateDirectory(updater);
        try
        {
            CreateClosedMigration9Database(data); Directory.CreateDirectory(Path.Combine(install, "app")); File.WriteAllText(Path.Combine(install, "app", "old.dll"), "old"); File.WriteAllText(Path.Combine(updater, "StoreExpiryInspector.Updater.exe"), "independent");
            var (downloader, package) = await CreateVerifiedPackageAsync(root, "0.9.9");
            Assert.Throws<InvalidDataException>(() => new UpdateInstallationPreparer(downloader).PrepareForTest(package, Process.GetCurrentProcess(), install, data, updater, CancellationToken.None));
            Assert.Empty(Directory.EnumerateDirectories(install, "app.staging-*"));
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var directory in new[] { root, install, data, updater }) if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void CreateClosedMigration9Database(string dataRoot)
    {
        var destination = Path.Combine(dataRoot, "data", "app.db"); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var template = Environment.GetEnvironmentVariable("S9_T05_PREPARER_DATABASE_TEMPLATE");
        if (string.IsNullOrWhiteSpace(template))
        {
            DatabaseInitializer.Initialize(destination); // The fixture deliberately creates a WAL database.
            SqliteConnection.ClearAllPools(); // Its own pooled connection must close and checkpoint before a main-file copy is trusted.
        }
        else File.Copy(template, destination);
        Assert.False(File.Exists(destination + "-wal")); Assert.False(File.Exists(destination + "-shm")); Assert.False(File.Exists(destination + "-journal"));
        using var connection = new SqliteConnection($"Data Source={destination};Mode=ReadOnly;Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId COLLATE BINARY;";
        using var reader = command.ExecuteReader(); var count = 0; while (reader.Read()) count++;
        Assert.Equal(9, count);
    }

    private static async Task<(SignedUpdatePackageDownloader Downloader, VerifiedUpdatePackage Package)> CreateVerifiedPackageAsync(string root, string? sourceMaxVersion = null)
    {
        var package = Path.Combine(root, "package.zip");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var app = Path.Combine(AppContext.BaseDirectory, "StoreExpiryInspector.exe");
            var production = Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "bin", "Release", "net10.0-windows");
            // S13 changes the controlled assembly identity to v1.0.5.  Keep the
            // fixture bound to the explicit release target, not stale v1.0.3 bytes.
            Assert.Equal("1.0.5", Version.Parse(FileVersionInfo.GetVersionInfo(app).FileVersion!).ToString(3));
            Assert.Equal("1.0.5", AssemblyName.GetAssemblyName(Path.Combine(AppContext.BaseDirectory, "StoreExpiryInspector.dll")).Version!.ToString(3));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(production, "StoreExpiryInspector.exe")))), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(app))));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(production, "StoreExpiryInspector.dll")))), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "StoreExpiryInspector.dll")))));
            zip.CreateEntryFromFile(app, "StoreExpiryInspector.exe");
            zip.CreateEntryFromFile(Path.ChangeExtension(app, ".dll"), "StoreExpiryInspector.dll");
        }
        var version = Version.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "StoreExpiryInspector.exe")).FileVersion!).ToString(3);
        var packageBytes = await File.ReadAllBytesAsync(package); var hash = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var maxVersion = sourceMaxVersion ?? UpdateInstallationPreparer.NormalizeSourceVersion(Process.GetCurrentProcess().MainModule?.FileVersionInfo.ProductVersion?.Split('+')[0]);
        var manifest = Encoding.UTF8.GetBytes($"{{\"schemaVersion\":1,\"version\":\"{version}\",\"releaseTag\":\"v{version}\",\"repository\":\"CodeVoyage3/xiaoqipaichanuanjian\",\"channel\":\"stable\",\"rid\":\"win-x64\",\"minimumProtocolVersion\":1,\"package\":{{\"fileName\":\"StoreExpiryInspector-{version}-win-x64.zip\",\"bytes\":{packageBytes.Length},\"sha256\":\"{hash}\"}},\"targetMigrations\":[\"20260826123739_InitialCreate\",\"20260826130822_AddTasksAndDrafts\",\"20260826135612_AddInspectionHistory\",\"20260826142429_AddInventoryAdjustments\",\"20260826152131_AddImportPersistence\",\"20260826155455_AddBackupMetadata\",\"20260826162033_AddSettingsAndAppState\",\"20260826170403_AddLifecycleEvents\",\"20260901155124_AddPolicyAndBaselineFoundation\"],\"source\":{{\"minVersion\":\"0.9.9\",\"maxVersion\":\"{maxVersion}\",\"minMigration\":\"20260826123739_InitialCreate\",\"maxMigration\":\"20260901155124_AddPolicyAndBaselineFoundation\"}}}}");
        using var rsa = RSA.Create(2048); var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var downloader = new SignedUpdatePackageDownloader(new Routes(manifest, signature, packageBytes, version), new UpdatePackageOptions(rsa.ExportParameters(false), CacheRoot: root));
        var result = await downloader.PrepareAsync(new(Version.Parse(version), 7, "v" + version, ["update-manifest.json", "update-manifest.sig", $"StoreExpiryInspector-{version}-win-x64.zip"]), new Version(0, 9, 9), null, CancellationToken.None);
        Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome); return (downloader, result.Package!);
    }

    private static string FindRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "StoreExpiryInspector.slnx"))) return path.FullName;
        throw new DirectoryNotFoundException("repository root not found");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1.0")]
    [InlineData("1.0.0.1")]
    public void InstallationPreparationRejectsAmbiguousSourceVersion(string? value) =>
        Assert.Throws<InvalidDataException>(() => UpdateInstallationPreparer.NormalizeSourceVersion(value));


    private sealed class Routes(byte[]? manifest = null, byte[]? signature = null, byte[]? package = null, string? releaseVersion = null) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "api.github.com") { var version = releaseVersion ?? "1.0.0"; return Task.FromResult(Bytes(HttpStatusCode.OK, Encoding.UTF8.GetBytes($"{{\"id\":7,\"tag_name\":\"v{version}\",\"draft\":false,\"prerelease\":false,\"assets\":[{{\"name\":\"update-manifest.json\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v{version}/update-manifest.json\"}},{{\"name\":\"update-manifest.sig\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v{version}/update-manifest.sig\"}},{{\"name\":\"StoreExpiryInspector-{version}-win-x64.zip\",\"state\":\"uploaded\",\"browser_download_url\":\"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v{version}/StoreExpiryInspector-{version}-win-x64.zip\"}}]}}"))); }
            return Task.FromResult(Bytes(HttpStatusCode.OK, path.EndsWith(".sig") ? signature! : path.EndsWith(".zip") ? package! : manifest!));
        }
        private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] bytes) => new(status) { Content = new ByteArrayContent(bytes) };
    }
}

