using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;

// Acceptance-side probe. Loads frozen release DLL; never constructs App or opens any database.
var run = Path.GetFullPath(Environment.CurrentDirectory);
if (!Guid.TryParse(Path.GetRelativePath(Path.GetTempPath(), run), out _)) throw new InvalidOperationException("Run from TEMP/GUID only");
foreach (var folder in new[] { run, AppContext.BaseDirectory })
    for (var d = new DirectoryInfo(folder); d is not null; d = d.Parent)
        if ((d.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked run path");
TraceLog.Path = System.IO.Path.Combine(run, "network-diagnostic.jsonl");
if (File.Exists(TraceLog.Path)) throw new InvalidOperationException("Do not overwrite evidence");
var original = typeof(SignedUpdatePackageDownloader).Assembly;
TraceLog.Add(new { kind = "environment", os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
    httpAssembly = typeof(HttpClient).Assembly.GetName().Version?.ToString(), architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    sourceProductVersion = FileVersionInfo.GetVersionInfo(original.Location).ProductVersion,
    sourceAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original.Location))),
    ipv4Supported = Socket.OSSupportsIPv4, ipv6Supported = Socket.OSSupportsIPv6,
    proxyEnvironmentPresent = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" }.ToDictionary(x => x, x => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(x))),
    automaticRedirects = false, tls = "System default; certificate validation unchanged", noDatabaseAccess = true, noUpdaterLaunch = true });
if (args.Contains("--self-test"))
{
    using var test = new TraceHandler(new ProbeFaultHandler());
    using var http = new HttpClient(test);
    using var redirect = await http.GetAsync("https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.1/update-manifest.json");
    if (test.LastClassification != "RedirectRejected") throw new Exception("Redirect classification test failed");
    try { await http.GetAsync("https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.1/update-manifest.sig"); }
    catch (HttpRequestException) { }
    if (test.LastClassification != "TLS") throw new Exception("TLS classification test failed");
    if (TraceLog.Category(new HttpRequestException(HttpRequestError.NameResolutionError, "CANARY_SECRET_QUERY")) != "DNS" || TraceLog.Category(new TaskCanceledException()) != "TimeoutOrCancellation") throw new Exception("Classification failed");
    var safe = File.ReadAllText(TraceLog.Path);
    if (safe.Contains("CANARY_SECRET_QUERY") || safe.Contains("CANARY_PASSWORD")) throw new Exception("Secret redaction failed");
    TraceLog.Add(new { kind = "self-test", passed = true, redirectReject = true, dnsTlsTimeoutClassification = true, nestedExceptionRedaction = true });
    Console.WriteLine("Self-test PASS");
    return;
}
foreach (var host in new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com" })
{
    try
    {
        var addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(10));
        TraceLog.Add(new { kind = "dns", host, ipv4 = addresses.Count(x => x.AddressFamily == AddressFamily.InterNetwork), ipv6 = addresses.Count(x => x.AddressFamily == AddressFamily.InterNetworkV6) });
    }
    catch (Exception ex) { TraceLog.Add(new { kind = "dns", host, error = TraceLog.Error(ex) }); }
    try { TraceLog.Add(new { kind = "proxy", host, defaultProxyBypassed = HttpClient.DefaultProxy.IsBypassed(new Uri("https://" + host)) }); }
    catch (Exception ex) { TraceLog.Add(new { kind = "proxy", host, error = TraceLog.Error(ex) }); }
}
using var handler = new TraceHandler(new HttpClientHandler { AllowAutoRedirect = false });
var check = await new GitHubReleaseUpdateChecker(handler).CheckAsync(new Version(1, 0, 0), CancellationToken.None);
TraceLog.Add(new { kind = "check-result", outcome = check.Outcome.ToString(), latest = check.LatestVersion?.ToString() });
if (check.Release is not null && check.Release.Tag == "v1.0.1")
{
    var downloader = new SignedUpdatePackageDownloader(handler, ProductionUpdateTrustAnchor.Options with { CacheRoot = System.IO.Path.Combine(run, "cache") });
    var result = await downloader.PrepareAsync(check.Release, new Version(1, 0, 0), p => TraceLog.Add(new { kind = "progress", p.Stage, p.BytesReceived, p.TotalBytes }), CancellationToken.None);
    TraceLog.Add(new { kind = "prepare-result", outcome = result.Outcome.ToString(), safeProductMessage = result.Message, handler.LastStage, handler.LastHop,
        classification = handler.LastClassification, updaterStarted = false });
}
else TraceLog.Add(new { kind = "prepare-skipped", reason = "Expected checked v1.0.1; do not probe other versions" });
Console.WriteLine("Completed. Return network-diagnostic.jsonl only; no cache/package files.");

static class TraceLog
{
    public static string Path = "";
    public static void Add(object value) { lock (typeof(TraceLog)) File.AppendAllText(Path, JsonSerializer.Serialize(value) + "\n"); }
    public static string Category(Exception ex) => ex switch
    {
        OperationCanceledException or TimeoutException => "TimeoutOrCancellation",
        AuthenticationException => "TLS",
        SocketException s when s.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "DNS",
        SocketException => "Connection",
        HttpRequestException h when h.HttpRequestError == HttpRequestError.NameResolutionError => "DNS",
        HttpRequestException h when h.HttpRequestError == HttpRequestError.SecureConnectionError => "TLS",
        HttpRequestException h when h.HttpRequestError == HttpRequestError.ConnectionError => "Connection",
        HttpRequestException h when h.HttpRequestError == HttpRequestError.ProxyTunnelError => "ProxyTunnel",
        _ => ex.InnerException is not null ? Category(ex.InnerException) : "Other"
    };
    public static object Error(Exception ex) => new { type = ex.GetType().FullName, classification = Category(ex), hresult = ex.HResult,
        httpRequestError = (ex as HttpRequestException)?.HttpRequestError.ToString(), status = (int?)(ex as HttpRequestException)?.StatusCode,
        socketError = (ex as SocketException)?.SocketErrorCode.ToString(),
        nativeErrorCode = (ex as System.ComponentModel.Win32Exception)?.NativeErrorCode,
        safeNativeMessage = ex is System.ComponentModel.Win32Exception win32 ? new System.ComponentModel.Win32Exception(win32.NativeErrorCode).Message : null,
        message = "Raw text removed to exclude URL query, credentials and machine paths; use classification and native error codes.",
        inner = ex.InnerException is null ? null : Error(ex.InnerException) };
}

sealed class TraceHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public string LastStage = "Unknown", LastClassification = "None";
    public int LastHop;
    private static readonly MethodInfo CdnRule = typeof(SignedUpdatePackageDownloader).GetMethod("IsCdnUri", BindingFlags.Static | BindingFlags.NonPublic)!;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        if (uri.Host == "api.github.com") { LastStage = uri.AbsolutePath.EndsWith("/latest") ? "CheckLatest" : "RefreshRelease"; LastHop = 0; }
        else if (uri.Host == "github.com") { LastStage = uri.AbsolutePath.EndsWith("update-manifest.json") ? "Manifest" : uri.AbsolutePath.EndsWith("update-manifest.sig") ? "Signature" : "Package"; LastHop = 0; }
        else LastHop++;
        var stage = LastStage; var hop = LastHop;
        var category = uri.Host == "api.github.com" ? "release-metadata" : uri.Host == "github.com" ? "release-download" : "release-cdn-asset";
        var watch = Stopwatch.StartNew();
        TraceLog.Add(new { kind = "request", stage, host = uri.IdnHost, pathCategory = category, redirectHop = hop });
        try
        {
            var response = await base.SendAsync(request, token);
            TraceLog.Add(new { kind = "response", stage, host = uri.IdnHost, pathCategory = category, redirectHop = hop, status = (int)response.StatusCode, elapsedMs = watch.ElapsedMilliseconds, httpVersion = response.Version.ToString() });
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                var target = location is null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
                var allowed = target is not null && (bool)CdnRule.Invoke(null, [target])! && hop < 3;
                var pathMatches = target is not null && System.Text.RegularExpressions.Regex.IsMatch(target.AbsolutePath, "\\A/github-production-release-asset/[0-9]+/[0-9a-fA-F-]{36}\\z");
                if (!allowed) LastClassification = "RedirectRejected";
                TraceLog.Add(new { kind = "redirect", stage = "Redirect", sourceStage = stage, host = uri.IdnHost, redirectHop = hop,
                    targetHost = target?.IdnHost, targetPathCategory = pathMatches ? "github-production-release-asset/repoId/GUID" : "other-path",
                    https = target?.Scheme == "https", port443 = target?.Port == 443, userInfoPresent = !string.IsNullOrEmpty(target?.UserInfo), fragmentPresent = !string.IsNullOrEmpty(target?.Fragment),
                    frozenProductionRuleAccepted = allowed, queryRecorded = false, status = (int)response.StatusCode });
            }
            else if (!response.IsSuccessStatusCode) LastClassification = "HttpStatus";
            response.Content = new TraceContent(response.Content, stage, hop, uri.IdnHost);
            return response;
        }
        catch (Exception ex)
        {
            LastClassification = TraceLog.Category(ex);
            TraceLog.Add(new { kind = "request-error", stage, host = uri.IdnHost, pathCategory = category, redirectHop = hop, elapsedMs = watch.ElapsedMilliseconds, error = TraceLog.Error(ex) });
            throw;
        }
    }
}

sealed class TraceContent : HttpContent
{
    readonly HttpContent source; readonly string stage, host; readonly int hop;
    public TraceContent(HttpContent content, string s, int h, string target) { source = content; stage = s; hop = h; host = target; foreach (var header in content.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value); }
    protected override bool TryComputeLength(out long length) { length = source.Headers.ContentLength ?? 0; return source.Headers.ContentLength.HasValue; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => source.CopyToAsync(stream);
    protected override async Task<Stream> CreateContentReadStreamAsync() => new TraceStream(await source.ReadAsStreamAsync(), stage, hop, host);
    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => new TraceStream(await source.ReadAsStreamAsync(token), stage, hop, host);
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
}
sealed class TraceStream(Stream source, string stage, int hop, string host) : Stream
{
    long total;
    void Failed(Exception ex) => TraceLog.Add(new { kind = "body-error", stage, host, redirectHop = hop, bytesRead = total, error = TraceLog.Error(ex) });
    int Count(int value) { total += value; if (value == 0) TraceLog.Add(new { kind = "body-complete", stage, host, redirectHop = hop, bytesRead = total }); return value; }
    public override int Read(byte[] b, int o, int c) { try { return Count(source.Read(b, o, c)); } catch (Exception ex) { Failed(ex); throw; } }
    public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken t = default) { try { return Count(await source.ReadAsync(b, t)); } catch (Exception ex) { Failed(ex); throw; } }
    public override async Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken t) { try { return Count(await source.ReadAsync(b, o, c, t)); } catch (Exception ex) { Failed(ex); throw; } }
    public override bool CanRead => source.CanRead; public override bool CanSeek => source.CanSeek; public override bool CanWrite => false;
    public override long Length => source.Length; public override long Position { get => source.Position; set => source.Position = value; }
    public override long Seek(long o, SeekOrigin s) => source.Seek(o, s); public override void Flush() => source.Flush();
    public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
}
sealed class ProbeFaultHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json"))
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://example.invalid/private?token=CANARY_SECRET_QUERY");
            return Task.FromResult(response);
        }
        throw new HttpRequestException(HttpRequestError.SecureConnectionError, "https://example.invalid/?token=CANARY_SECRET_QUERY", new AuthenticationException("password=CANARY_PASSWORD"));
    }
}
