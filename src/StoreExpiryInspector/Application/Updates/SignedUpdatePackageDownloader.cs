using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StoreExpiryInspector.Application.Updates;

public enum UpdatePackageOutcome
{
    Verified, Cancelled, NetworkUnavailable, RateLimited, ManifestMissing, SignatureMissing,
    InvalidManifest, InvalidManifestSignature, SigningNotConfigured, UnsupportedProtocol,
    UnsupportedPlatform, VersionMismatch, AssetMissing, AssetTooLarge, SizeMismatch,
    HashMismatch, UnsafeArchive, PackageVersionMismatch, SourceNotSupported, IoFailure
}

public sealed record UpdatePackageProgress(string Stage, long BytesReceived, long TotalBytes);
public sealed record VerifiedUpdatePackage(string CacheDirectory, string PackagePath, Version Version, string Sha256, IReadOnlyList<string> TargetMigrations, byte[]? SignedManifest = null, byte[]? ManifestSignature = null, CheckedRelease? Release = null);
public sealed record UpdatePackageResult(UpdatePackageOutcome Outcome, string Message, VerifiedUpdatePackage? Package = null);
public sealed record CheckedRelease(Version Version, long ReleaseId, string Tag, IReadOnlyList<string> AssetNames);

public sealed record UpdatePackageOptions(RSAParameters? TrustedPublicKey = null, TimeSpan? MetadataTimeout = null, TimeSpan? PackageTimeout = null, string? CacheRoot = null)
{
    internal RSA? CreateVerifier()
    {
        if (TrustedPublicKey is not { } key || key.Modulus is null || key.Exponent is null) return null;
        RSA? rsa = null;
        try { rsa = RSA.Create(); rsa.ImportParameters(new RSAParameters { Modulus = key.Modulus, Exponent = key.Exponent }); if (rsa.KeySize >= 2048) return rsa; rsa.Dispose(); return null; }
        catch (CryptographicException) { rsa?.Dispose(); return null; }
    }
}

public sealed class SignedUpdatePackageDownloader
{
    private const string Owner = "CodeVoyage3";
    private const string Repo = "xiaoqipaichanuanjian";
    private const long PackageLimit = 256L * 1024 * 1024;
    private const long ExpandedLimit = 512L * 1024 * 1024;
    private const int EntryLimit = 4096;
    private static readonly Regex VersionPattern = new("\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z", RegexOptions.CultureInvariant);
    private static readonly Regex MigrationPattern = new("\\A[0-9]{14}_[^\\s/\\\\]+\\z", RegexOptions.CultureInvariant);
    private static readonly string[] CurrentMigrations = ["20260826123739_InitialCreate", "20260826130822_AddTasksAndDrafts", "20260826135612_AddInspectionHistory", "20260826142429_AddInventoryAdjustments", "20260826152131_AddImportPersistence", "20260826155455_AddBackupMetadata", "20260826162033_AddSettingsAndAppState", "20260826170403_AddLifecycleEvents", "20260901155124_AddPolicyAndBaselineFoundation"];
    private readonly HttpClient _client;
    private readonly UpdatePackageOptions _options;
    private readonly ConcurrentDictionary<string, Lazy<Task<UpdatePackageResult>>> _flights = new();

    public SignedUpdatePackageDownloader(HttpMessageHandler? handler = null, UpdatePackageOptions? options = null)
    {
        _client = handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler, false);
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("StoreExpiryInspector/1.0");
        _options = options ?? new();
    }

    public Task<UpdatePackageResult> PrepareAsync(CheckedRelease release, Version currentVersion, Action<UpdatePackageProgress>? progress, CancellationToken cancellationToken)
    {
        using var anchor = _options.CreateVerifier();
        if (anchor is null) return Task.FromResult(Fail(UpdatePackageOutcome.SigningNotConfigured, "发行验签未配置，已安全拒绝下载。"));
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 || RuntimeInformation.ProcessArchitecture != Architecture.X64) return Task.FromResult(Fail(UpdatePackageOutcome.UnsupportedPlatform, "当前运行环境不支持 win-x64 更新包。"));
        if (!IsVersion(release.Version) || release.Tag != "v" + release.Version.ToString(3) || release.ReleaseId <= 0 || !CurrentMigrations.All(MigrationPattern.IsMatch)) return Task.FromResult(Fail(UpdatePackageOutcome.InvalidManifest, "更新发布身份无效。"));
        var key = release.Version.ToString(3);
        var lazy = _flights.GetOrAdd(key, _ => new Lazy<Task<UpdatePackageResult>>(() => PrepareCoreAsync(release, currentVersion, progress, cancellationToken)));
        return AwaitAndForget(key, lazy);
    }

    private async Task<UpdatePackageResult> AwaitAndForget(string key, Lazy<Task<UpdatePackageResult>> lazy)
    {
        try { return await lazy.Value; }
        finally { _flights.TryRemove(new KeyValuePair<string, Lazy<Task<UpdatePackageResult>>>(key, lazy)); }
    }

    private async Task<UpdatePackageResult> PrepareCoreAsync(CheckedRelease release, Version currentVersion, Action<UpdatePackageProgress>? progress, CancellationToken cancellationToken)
    {
        try { return await PrepareCoreInnerAsync(release, currentVersion, progress, cancellationToken); }
        catch (IOException) { return Fail(UpdatePackageOutcome.IoFailure, "更新缓存清理失败。"); }
    }

    private async Task<UpdatePackageResult> PrepareCoreInnerAsync(CheckedRelease release, Version currentVersion, Action<UpdatePackageProgress>? progress, CancellationToken cancellationToken)
    {
        string? directory = null;
        var verified = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refreshed = await RefreshReleaseAsync(release, cancellationToken);
            if (refreshed.Result is not null) return refreshed.Result;
            release = refreshed.Release!;
            directory = CreateCacheDirectory();
            var manifestUri = AssetUri(release, "update-manifest.json");
            var signatureUri = AssetUri(release, "update-manifest.sig");
            if (!release.AssetNames.Contains("update-manifest.json", StringComparer.Ordinal)) return Fail(UpdatePackageOutcome.ManifestMissing, "发行中缺少更新清单。");
            if (!release.AssetNames.Contains("update-manifest.sig", StringComparer.Ordinal)) return Fail(UpdatePackageOutcome.SignatureMissing, "发行中缺少清单签名。");
            var rawManifest = await ReadSmallAsync(manifestUri, 64 * 1024, UpdatePackageOutcome.ManifestMissing, cancellationToken);
            var signature = await ReadSmallAsync(signatureUri, 1024, UpdatePackageOutcome.SignatureMissing, cancellationToken);
            using var verifier = _options.CreateVerifier()!;
            if (!verifier.VerifyData(rawManifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) return Fail(UpdatePackageOutcome.InvalidManifestSignature, "更新清单签名无效。");
            try { using var schema = JsonDocument.Parse(rawManifest); if (schema.RootElement.ValueKind != JsonValueKind.Object || !schema.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.ValueKind != JsonValueKind.Number || !schemaVersion.TryGetInt32(out var value)) return Fail(UpdatePackageOutcome.InvalidManifest, "更新清单格式无效。"); if (value != 1) return Fail(UpdatePackageOutcome.UnsupportedProtocol, "更新协议不受支持。"); } catch (JsonException) { return Fail(UpdatePackageOutcome.InvalidManifest, "更新清单格式无效。"); }
            if (!TryParseManifest(rawManifest, out var manifest)) return Fail(UpdatePackageOutcome.InvalidManifest, "更新清单格式无效。");
            if (manifest.MinimumProtocolVersion != 1) return Fail(UpdatePackageOutcome.UnsupportedProtocol, "更新协议不受支持。");
            if (manifest.Version != release.Version || manifest.ReleaseTag != release.Tag || manifest.Repository != Owner + "/" + Repo) return Fail(UpdatePackageOutcome.VersionMismatch, "更新清单与发行版本不一致。");
            if (manifest.Rid != "win-x64") return Fail(UpdatePackageOutcome.UnsupportedPlatform, "更新包平台不受支持。");
            if (manifest.Channel != "stable") return Fail(UpdatePackageOutcome.InvalidManifest, "更新通道无效。");
            if (manifest.Version <= currentVersion) return Fail(UpdatePackageOutcome.VersionMismatch, "候选版本必须高于当前版本。");
            if (!manifest.SourceAllows(currentVersion, CurrentMigrations[^1])) return Fail(UpdatePackageOutcome.SourceNotSupported, "当前版本不在该更新包支持范围内。");
            var packageName = $"StoreExpiryInspector-{manifest.Version:0.0.0}-win-x64.zip";
            if (manifest.PackageName != packageName || !release.AssetNames.Count(name => name == packageName).Equals(1)) return Fail(UpdatePackageOutcome.AssetMissing, "发行中没有唯一匹配的更新包。");
            if (manifest.PackageBytes > PackageLimit) return Fail(UpdatePackageOutcome.AssetTooLarge, "更新包超过安全大小限制。");
            var expectedHash = Convert.FromHexString(manifest.PackageHash);
            var packagePath = Path.Combine(directory, "package.download");
            progress?.Invoke(new("正在下载更新包", 0, manifest.PackageBytes));
            var download = await DownloadAsync(AssetUri(release, packageName), packagePath, manifest.PackageBytes, progress, cancellationToken);
            if (download.Outcome != UpdatePackageOutcome.Verified) return Fail(download.Outcome, download.Outcome == UpdatePackageOutcome.Cancelled ? "已取消更新包准备。" : "更新包下载校验失败。");
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, download.Hash!)) return Fail(UpdatePackageOutcome.HashMismatch, "更新包摘要不匹配。");
            using var packageLock = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var lockedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var lockedBuffer = new byte[81920];
            for (int read; (read = packageLock.Read(lockedBuffer, 0, lockedBuffer.Length)) > 0;) { cancellationToken.ThrowIfCancellationRequested(); lockedHash.AppendData(lockedBuffer, 0, read); }
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, lockedHash.GetHashAndReset())) return Fail(UpdatePackageOutcome.HashMismatch, "更新包摘要不匹配。");
            progress?.Invoke(new("正在校验更新包", manifest.PackageBytes, manifest.PackageBytes));
            cancellationToken.ThrowIfCancellationRequested();
            var audit = await Task.Run(() => AuditArchive(packagePath, directory, manifest, cancellationToken), cancellationToken);
            if (audit != UpdatePackageOutcome.Verified) return Fail(audit, "更新包内容不符合安全要求。");
            progress?.Invoke(new("更新包已准备完成，正在进入维护状态。", manifest.PackageBytes, manifest.PackageBytes));
            cancellationToken.ThrowIfCancellationRequested(); verified = true;
            return new(UpdatePackageOutcome.Verified, "更新包已准备完成，可在维护窗口中安装。", new(directory, packagePath, manifest.Version, manifest.PackageHash, manifest.TargetMigrations, rawManifest.ToArray(), signature.ToArray(), release));
        }
        catch (OperationCanceledException) { return Fail(UpdatePackageOutcome.Cancelled, "已取消更新包准备。"); }
        catch (HttpRequestException) { return Fail(UpdatePackageOutcome.NetworkUnavailable, "无法连接更新服务器。"); }
        catch (IOException) { return Fail(UpdatePackageOutcome.IoFailure, "更新缓存读写失败。"); }
        catch (CryptographicException) { return Fail(UpdatePackageOutcome.InvalidManifestSignature, "更新签名数据无效。"); }
        catch (UpdatePackageException error) { return Fail(error.Outcome, "更新发布内容不可用。"); }
        catch (Exception) { return Fail(UpdatePackageOutcome.UnsafeArchive, "更新包无法安全验证。"); }
        finally
        {
            if (!verified && directory is not null && !TryDelete(directory)) throw new IOException("update cache cleanup failed");
        }
    }

    public UpdatePackageResult RevalidateForInstall(VerifiedUpdatePackage package, CancellationToken cancellationToken)
    {
        try
        {
            using var verifier = _options.CreateVerifier();
            if (verifier is null || package.SignedManifest is null || package.ManifestSignature is null || package.Release is null) return Fail(UpdatePackageOutcome.SigningNotConfigured, "更新包缺少可重验的发行身份。");
            if (!verifier.VerifyData(package.SignedManifest, package.ManifestSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss) || !TryParseManifest(package.SignedManifest, out var manifest)) return Fail(UpdatePackageOutcome.InvalidManifestSignature, "更新清单签名或格式无效。");
            if (manifest.Version != package.Version || manifest.ReleaseTag != package.Release.Tag || manifest.Repository != Owner + "/" + Repo || manifest.Rid != "win-x64" || manifest.TargetMigrations.Count != package.TargetMigrations.Count || !manifest.TargetMigrations.SequenceEqual(package.TargetMigrations, StringComparer.Ordinal)) return Fail(UpdatePackageOutcome.VersionMismatch, "更新包身份在安装前发生变化。");
            if (!File.Exists(package.PackagePath) || !string.Equals(manifest.PackageHash, package.Sha256, StringComparison.OrdinalIgnoreCase)) return Fail(UpdatePackageOutcome.HashMismatch, "更新包摘要不匹配。");
            using var packageStream = File.OpenRead(package.PackagePath);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(packageStream)), package.Sha256, StringComparison.OrdinalIgnoreCase)) return Fail(UpdatePackageOutcome.HashMismatch, "更新包摘要不匹配。");
            if (AuditArchive(package.PackagePath, package.CacheDirectory, manifest, cancellationToken) != UpdatePackageOutcome.Verified) return Fail(UpdatePackageOutcome.UnsafeArchive, "更新包内容不符合安全要求。");
            return new(UpdatePackageOutcome.Verified, "更新包安装前重验通过。", package);
        }
        catch (OperationCanceledException) { return Fail(UpdatePackageOutcome.Cancelled, "已取消更新包重验。"); }
        catch (IOException) { return Fail(UpdatePackageOutcome.IoFailure, "更新包重验读取失败。"); }
        catch (Exception) { return Fail(UpdatePackageOutcome.UnsafeArchive, "更新包无法安全重验。"); }
    }

    internal void DiscardVerifiedCache(VerifiedUpdatePackage package)
    {
        var root = Path.GetFullPath(_options.CacheRoot ?? Path.Combine(Path.GetTempPath(), "StoreExpiryInspector", "updates"));
        var directory = Path.GetFullPath(package.CacheDirectory);
        if (Guid.TryParse(Path.GetFileName(directory), out _) && string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase) && IsOrdinaryDirectory(directory)) TryDelete(directory);
    }

    private async Task<(CheckedRelease? Release, UpdatePackageResult? Result)> RefreshReleaseAsync(CheckedRelease expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.MetadataTimeout ?? TimeSpan.FromSeconds(30));
        try
        {
            var uri = new Uri($"https://api.github.com/repos/{Owner}/{Repo}/releases/tags/{expected.Tag}");
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429) return (null, Fail(UpdatePackageOutcome.RateLimited, "更新服务器请求受限。"));
            if (response.StatusCode == HttpStatusCode.NotFound) return (null, Fail(UpdatePackageOutcome.AssetMissing, "指定发行不存在。"));
            if (!response.IsSuccessStatusCode) return (null, Fail(UpdatePackageOutcome.NetworkUnavailable, "无法读取指定发行。"));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var data = new MemoryStream(); var buffer = new byte[8192];
            for (int read; (read = await stream.ReadAsync(buffer, timeout.Token)) > 0;) { if (data.Length + read > 1024 * 1024) return (null, Fail(UpdatePackageOutcome.InvalidManifest, "发行元数据超过限制。")); data.Write(buffer, 0, read); }
            using var json = JsonDocument.Parse(data.ToArray()); var root = json.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var actualId) || actualId != expected.ReleaseId || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() != expected.Tag || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return (null, Fail(UpdatePackageOutcome.AssetMissing, "指定发行身份不匹配。"));
            var names = new List<string>();
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || !asset.TryGetProperty("state", out var state) || state.GetString() != "uploaded" || !asset.TryGetProperty("browser_download_url", out var url) || url.ValueKind != JsonValueKind.String) return (null, Fail(UpdatePackageOutcome.AssetMissing, "发行资产无效。"));
                var value = name.GetString()!;
                if (url.GetString() != AssetUri(expected, value).ToString()) return (null, Fail(UpdatePackageOutcome.AssetMissing, "发行资产来源无效。"));
                names.Add(value);
            }
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) return (null, Fail(UpdatePackageOutcome.AssetMissing, "发行资产名称重复。"));
            return (expected with { AssetNames = names }, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return (null, Fail(UpdatePackageOutcome.Cancelled, "已取消更新包准备。")); }
        catch (OperationCanceledException) { return (null, Fail(UpdatePackageOutcome.NetworkUnavailable, "读取发行超时。")); }
        catch (HttpRequestException) { return (null, Fail(UpdatePackageOutcome.NetworkUnavailable, "无法读取指定发行。")); }
        catch (JsonException) { return (null, Fail(UpdatePackageOutcome.AssetMissing, "发行元数据无效。")); }
    }

    private async Task<(UpdatePackageOutcome Outcome, byte[]? Hash)> DownloadAsync(Uri uri, string target, long expected, Action<UpdatePackageProgress>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(_options.PackageTimeout ?? TimeSpan.FromMinutes(10));
        try
        {
        using var response = await SendFollowingRedirects(uri, timeout.Token);
        if (response.StatusCode is HttpStatusCode.NotFound) return (UpdatePackageOutcome.AssetMissing, null);
        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429) return (UpdatePackageOutcome.RateLimited, null);
        if (!response.IsSuccessStatusCode) return (UpdatePackageOutcome.NetworkUnavailable, null);
        if (response.Content.Headers.ContentLength is > PackageLimit) return (UpdatePackageOutcome.AssetTooLarge, null);
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long total = 0; var last = Environment.TickCount64;
        for (int read; (read = await input.ReadAsync(buffer, timeout.Token)) > 0;)
        {
            total += read; if (total > PackageLimit || total > expected) return (total > PackageLimit ? UpdatePackageOutcome.AssetTooLarge : UpdatePackageOutcome.SizeMismatch, null);
            hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            if (Environment.TickCount64 - last >= 100) { progress?.Invoke(new("正在下载更新包", total, expected)); last = Environment.TickCount64; }
        }
        progress?.Invoke(new("正在下载更新包", total, expected));
        return total == expected ? (UpdatePackageOutcome.Verified, hash.GetHashAndReset()) : (UpdatePackageOutcome.SizeMismatch, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return (UpdatePackageOutcome.Cancelled, null); }
        catch (OperationCanceledException) { return (UpdatePackageOutcome.NetworkUnavailable, null); }
    }

    private async Task<byte[]> ReadSmallAsync(Uri uri, int limit, UpdatePackageOutcome missing, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(_options.MetadataTimeout ?? TimeSpan.FromSeconds(30));
        try
        {
        using var response = await SendFollowingRedirects(uri, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new UpdatePackageException(missing);
        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429) throw new UpdatePackageException(UpdatePackageOutcome.RateLimited);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException();
        if (response.Content.Headers.ContentLength > limit) throw new UpdatePackageException(UpdatePackageOutcome.InvalidManifest);
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        for (int read; (read = await input.ReadAsync(buffer, timeout.Token)) > 0;) { if (output.Length + read > limit) throw new UpdatePackageException(UpdatePackageOutcome.InvalidManifest); output.Write(buffer, 0, read); }
        return output.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new UpdatePackageException(UpdatePackageOutcome.NetworkUnavailable); }
    }

    private async Task<HttpResponseMessage> SendFollowingRedirects(Uri initial, CancellationToken cancellationToken)
    {
        var uri = initial;
        for (var hop = 0; hop <= 3; hop++)
        {
            if (!(hop == 0 ? IsInitialUri(uri) : IsCdnUri(uri))) throw new HttpRequestException("unsafe update host");
            var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode)) return response;
            if (hop == 3 || response.Headers.Location is null) { response.Dispose(); throw new HttpRequestException("bad redirect"); }
            uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
            response.Dispose();
        }
        throw new HttpRequestException("redirect limit");
    }

    private static UpdatePackageOutcome AuditArchive(string packagePath, string scratch, Manifest manifest, CancellationToken cancellationToken)
    {
        if (!TryReadZipDirectory(packagePath, out var expectedCrc)) return UpdatePackageOutcome.UnsafeArchive;
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count is 0 or > EntryLimit) return UpdatePackageOutcome.UnsafeArchive;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0; string? exe = null; string? dll = null;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SafeEntry(entry, paths) || entry.Length > PackageLimit || entry.CompressedLength < 0 || (entry.Length > 0 && (entry.CompressedLength == 0 || entry.Length / Math.Max(1, entry.CompressedLength) > 200))) return UpdatePackageOutcome.UnsafeArchive;
            expanded += entry.Length; if (expanded > ExpandedLimit) return UpdatePackageOutcome.UnsafeArchive;
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            if (!Allowed(entry.FullName)) return UpdatePackageOutcome.UnsafeArchive;
            var temp = Path.Combine(scratch, "audit-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var input = entry.Open()) using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; long actual = 0; var crc = new Crc32();
                    for (int read; (read = input.Read(buffer, 0, buffer.Length)) > 0;) { cancellationToken.ThrowIfCancellationRequested(); actual += read; if (actual > entry.Length || actual > PackageLimit) return UpdatePackageOutcome.UnsafeArchive; crc.Append(buffer.AsSpan(0, read)); output.Write(buffer, 0, read); }
                    if (actual != entry.Length || !expectedCrc.TryGetValue(entry.FullName, out var expected) || crc.Value != expected) return UpdatePackageOutcome.UnsafeArchive;
                }
                if (ContainsSecret(temp)) return UpdatePackageOutcome.UnsafeArchive;
                if (entry.FullName == "StoreExpiryInspector.exe") exe = temp; else if (entry.FullName == "StoreExpiryInspector.dll") dll = temp; else File.Delete(temp);
            }
            catch (OperationCanceledException) { throw; }
            catch { return UpdatePackageOutcome.UnsafeArchive; }
        }
        if (exe is null || dll is null) return UpdatePackageOutcome.PackageVersionMismatch;
        try
        {
            if (!IsAmd64Executable(exe)) return UpdatePackageOutcome.PackageVersionMismatch;
            var fileVersion = FileVersionInfo.GetVersionInfo(exe);
            if (fileVersion.FileMajorPart != manifest.Version.Major || fileVersion.FileMinorPart != manifest.Version.Minor || fileVersion.FileBuildPart != manifest.Version.Build || fileVersion.FilePrivatePart != 0) return UpdatePackageOutcome.PackageVersionMismatch;
            if (!ReadAssembly(dll, manifest.Version, out var migrations) || !migrations.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(manifest.TargetMigrations.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal)) return UpdatePackageOutcome.PackageVersionMismatch;
            return UpdatePackageOutcome.Verified;
        }
        finally { TryDelete(exe); TryDelete(dll); }
    }

    private static bool ReadAssembly(string path, Version version, out List<string> migrations)
    {
        migrations = [];
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        var asm = reader.GetAssemblyDefinition();
        if (!Version.TryParse(asm.Version.ToString(), out var product) || product.Major != version.Major || product.Minor != version.Minor || product.Build != version.Build || product.Revision != 0) return false;
        foreach (var type in reader.TypeDefinitions)
            foreach (var attributeHandle in reader.GetTypeDefinition(type).GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (AttributeName(reader, attribute.Constructor) != "Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute") continue;
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) return false;
                var id = blob.ReadSerializedString(); if (id is null || !MigrationPattern.IsMatch(id)) return false; migrations.Add(id);
            }
        return migrations.Count > 0 && migrations.Distinct(StringComparer.Ordinal).Count() == migrations.Count;
    }

    private static bool IsAmd64Executable(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        return pe.PEHeaders.CoffHeader.Machine == System.Reflection.PortableExecutable.Machine.Amd64;
    }

    private static string? AttributeName(MetadataReader reader, EntityHandle handle)
    {
        EntityHandle parent = handle.Kind == HandleKind.MemberReference ? reader.GetMemberReference((MemberReferenceHandle)handle).Parent : reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType();
        return parent.Kind == HandleKind.TypeReference ? TypeName(reader, reader.GetTypeReference((TypeReferenceHandle)parent).Namespace, reader.GetTypeReference((TypeReferenceHandle)parent).Name) : parent.Kind == HandleKind.TypeDefinition ? TypeName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)parent).Namespace, reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name) : null;
    }
    private static string TypeName(MetadataReader reader, StringHandle @namespace, StringHandle name) => reader.GetString(@namespace) + "." + reader.GetString(name);

    private static bool SafeEntry(ZipArchiveEntry entry, HashSet<string> paths)
    {
        if (entry.FullName.Contains('\\')) return false;
        var name = entry.FullName;
        if (name.Length is 0 or > 1024 || name.StartsWith('/') || name.StartsWith("//") || name.Contains(':') || name.Any(character => char.IsControl(character) || character is '<' or '>' or '"' or '|' or '?' or '*') || name.Split('/').Any(part => part.Length > 255 || part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || IsReserved(part))) return false;
        if (!paths.Add(name) || paths.Any(existing => existing.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase) || name.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase))) return false;
        var unixType = ((uint)entry.ExternalAttributes >> 16) & 0xF000;
        return (unixType is 0 or 0x8000 or 0x4000) && (((uint)entry.ExternalAttributes & 0x400) == 0) && !HasLinkExtra(entry);
    }

    private static bool HasLinkExtra(ZipArchiveEntry entry) => (((uint)entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 or 0x6000;
    private static bool IsReserved(string part) => new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(part.Split('.')[0], StringComparer.OrdinalIgnoreCase);
    private static bool Allowed(string name) => !name.Split('/').Any(part => part.Equals("data", StringComparison.OrdinalIgnoreCase) || part.Equals("logs", StringComparison.OrdinalIgnoreCase) || part.StartsWith("backup", StringComparison.OrdinalIgnoreCase)) && (name is "StoreExpiryInspector.exe" or "createdump.exe" or "StoreExpiryInspector.dll" or "Updater/StoreExpiryInspector.Updater.exe" or "Updater/createdump.exe" || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase));
    private static bool ContainsSecret(string path)
    {
        using var stream = File.OpenRead(path); var buffer = new byte[8192]; var tail = string.Empty;
        for (int read; (read = stream.Read(buffer, 0, buffer.Length)) > 0;)
        {
            var text = tail + Encoding.ASCII.GetString(buffer, 0, read);
            if (text.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) || text.Contains("-----BEGIN RSA PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) || text.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) || text.Contains("github_pat_", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(text, "ghp_[A-Za-z0-9]{20,}")) return true;
            tail = text.Length > 64 ? text[^64..] : text;
        }
        return false;
    }
    private static bool TryReadZipDirectory(string path, out Dictionary<string, uint> crcs)
    {
        crcs = new(StringComparer.Ordinal);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 22) return false;
            var tailStart = Math.Max(0, stream.Length - 65557); var tail = ReadAt(stream, tailStart, checked((int)(stream.Length - tailStart))); var end = tail.Length - 22;
            while (end >= 0 && Read32(tail, end) != 0x06054b50) end--;
            if (end < 0 || tailStart + end + 22 != stream.Length || Read16(tail, end + 20) != 0 || Read16(tail, end + 4) != 0 || Read16(tail, end + 6) != 0 || Read16(tail, end + 8) != Read16(tail, end + 10)) return false;
            var count = Read16(tail, end + 10); var size = Read32(tail, end + 12); var offset = Read32(tail, end + 16);
            if (count > EntryLimit || (long)offset + size != tailStart + end || offset > stream.Length) return false;
            var cursor = (long)offset; long nextLocal = 0;
            for (var index = 0; index < count; index++)
            {
                var header = ReadAt(stream, cursor, 46); if (Read32(header, 0) != 0x02014b50) return false;
                var flags = Read16(header, 8); var method = Read16(header, 10); var crc = Read32(header, 16); var compressed = Read32(header, 20); var uncompressed = Read32(header, 24); var nameLength = Read16(header, 28); var extraLength = Read16(header, 30); var commentLength = Read16(header, 32); var attrs = Read32(header, 38); var localOffset = Read32(header, 42);
                if (Read16(header, 34) != 0 || (flags & ~0x800) != 0 || method is not 0 and not 8 || extraLength != 0 || commentLength != 0 || compressed == uint.MaxValue || uncompressed == uint.MaxValue || localOffset != nextLocal || (long)localOffset + 30 > stream.Length) return false;
                var nameBytes = ReadAt(stream, cursor + 46, nameLength); var local = ReadAt(stream, localOffset, 30); if (Read32(local, 0) != 0x04034b50) return false;
                var name = Encoding.UTF8.GetString(nameBytes);
                if (name.Any(character => character > 127) || Read16(local, 6) != flags || Read16(local, 8) != method || Read32(local, 14) != crc || Read32(local, 18) != compressed || Read32(local, 22) != uncompressed || Read16(local, 26) != nameLength || Read16(local, 28) != 0 || !nameBytes.AsSpan().SequenceEqual(ReadAt(stream, (long)localOffset + 30, nameLength)) || (((attrs >> 16) & 0xF000) is 0xA000 or 0x6000) || (attrs & 0x400) != 0 || !crcs.TryAdd(name, crc)) return false;
                var payload = (long)localOffset + 30 + nameLength; if (payload + compressed > offset || payload + compressed > stream.Length) return false;
                nextLocal = payload + compressed;
                cursor += 46 + nameLength + extraLength + commentLength;
            }
            return cursor == tailStart + end && nextLocal == offset;
        }
        catch { return false; }
    }
    private static byte[] ReadAt(FileStream stream, long position, int count) { var buffer = new byte[count]; stream.Position = position; for (var read = 0; read < count;) { var got = stream.Read(buffer, read, count - read); if (got == 0) throw new EndOfStreamException(); read += got; } return buffer; }
    private static ushort Read16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);
    private static uint Read32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private sealed class Crc32 { private uint _value = uint.MaxValue; public void Append(ReadOnlySpan<byte> bytes) { foreach (var b in bytes) { _value ^= b; for (var i = 0; i < 8; i++) _value = (_value >> 1) ^ ((_value & 1) == 1 ? 0xEDB88320u : 0); } } public uint Value => ~_value; }
    private static bool IsRedirect(HttpStatusCode code) => code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    private static bool IsInitialUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && uri.Host == "github.com" && Regex.IsMatch(uri.AbsolutePath, $"\\A/{Owner}/{Repo}/releases/download/v[0-9]+\\.[0-9]+\\.[0-9]+/[^/]+\\z");
    private static bool IsCdnUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && uri.Host == "release-assets.githubusercontent.com" && Regex.IsMatch(uri.AbsolutePath, "\\A/github-production-release-asset/[0-9]+/[0-9a-fA-F-]{36}\\z");
    private static Uri AssetUri(CheckedRelease release, string name) => new($"https://github.com/{Owner}/{Repo}/releases/download/{release.Tag}/{name}");
    private static bool IsVersion(Version version) => version.Major >= 0 && version.Minor >= 0 && version.Build >= 0 && version.Revision < 0;
    private static bool TryDelete(string? path) { try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); else if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path, true); return true; } catch { return false; } }
    private string CreateCacheDirectory()
    {
        var temp = Path.GetFullPath(Path.GetTempPath()); var root = Path.GetFullPath(_options.CacheRoot ?? Path.Combine(temp, "StoreExpiryInspector", "updates"));
        if (root.StartsWith("\\\\", StringComparison.Ordinal) || !root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !IsOrdinaryDirectory(root)) throw new IOException();
        var path = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); if (!IsOrdinaryDirectory(path)) throw new IOException(); return path;
    }
    private static bool IsOrdinaryDirectory(string path) { for (var item = new DirectoryInfo(path); item is not null; item = item.Parent) { if (!item.Exists) continue; if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return false; } return true; }
    private static UpdatePackageResult Fail(UpdatePackageOutcome outcome, string message) => new(outcome, message);
    private sealed class UpdatePackageException(UpdatePackageOutcome outcome) : Exception { public UpdatePackageOutcome Outcome { get; } = outcome; }

    private sealed record Manifest(Version Version, string ReleaseTag, string Repository, string Channel, string Rid, int MinimumProtocolVersion, string PackageName, long PackageBytes, string PackageHash, List<string> TargetMigrations, Version MinVersion, Version MaxVersion, string MinMigration, string MaxMigration)
    { public bool SourceAllows(Version version, string migration) => version >= MinVersion && version <= MaxVersion && string.CompareOrdinal(migration, MinMigration) >= 0 && string.CompareOrdinal(migration, MaxMigration) <= 0; }

    private static bool TryParseManifest(byte[] bytes, out Manifest manifest)
    {
        manifest = null!;
        try
        {
            using var document = JsonDocument.Parse(bytes); if (!NoDuplicateProperties(document.RootElement) || document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = document.RootElement;
            if (Int(root, "schemaVersion") < 1 || String(root, "channel") != "stable" || string.IsNullOrEmpty(String(root, "rid")) || Int(root, "minimumProtocolVersion") < 1 || !VersionValue(String(root, "version"), out var version)) return false;
            var package = Object(root, "package"); var source = Object(root, "source"); var migrations = Array(root, "targetMigrations").EnumerateArray().Select((JsonElement item) => item.GetString()).ToList();
            if (migrations.Any(id => id is null || !MigrationPattern.IsMatch(id)) || migrations.Distinct(StringComparer.Ordinal).Count() != migrations.Count || !migrations.SequenceEqual(migrations.OrderBy(id => id, StringComparer.Ordinal)) || !VersionValue(String(source, "minVersion"), out var min) || !VersionValue(String(source, "maxVersion"), out var max) || min > max || !MigrationPattern.IsMatch(String(source, "minMigration")) || !MigrationPattern.IsMatch(String(source, "maxMigration")) || string.CompareOrdinal(String(source, "minMigration"), String(source, "maxMigration")) > 0) return false;
            var hash = String(package, "sha256"); if (!Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z") || Long(package, "bytes") <= 0) return false;
            manifest = new(version, String(root, "releaseTag"), String(root, "repository"), String(root, "channel"), String(root, "rid"), Int(root, "minimumProtocolVersion"), String(package, "fileName"), Long(package, "bytes"), hash, migrations!, min, max, String(source, "minMigration"), String(source, "maxMigration")); return true;
        }
        catch { return false; }
    }
    private static bool NoDuplicateProperties(JsonElement element) => element.ValueKind switch { JsonValueKind.Object => element.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).All(group => group.Count() == 1 && NoDuplicateProperties(group.First().Value)), JsonValueKind.Array => element.EnumerateArray().All(NoDuplicateProperties), _ => true };
    private static JsonElement Object(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new JsonException();
    private static JsonElement Array(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new JsonException();
    private static string String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) ? value.GetString()! : throw new JsonException();
    private static int Int(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : throw new JsonException();
    private static long Long(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : throw new JsonException();
    private static bool VersionValue(string value, out Version version)
    {
        version = new();
        if (!VersionPattern.IsMatch(value) || !Version.TryParse(value, out var parsed) || parsed is null || !IsVersion(parsed)) return false;
        version = parsed; return true;
    }
}
