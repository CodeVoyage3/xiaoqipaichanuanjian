using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;

var root = Environment.GetEnvironmentVariable("S9_T06_CHECK_ROOT")!;
if (!Guid.TryParse(Path.GetRelativePath(Path.GetTempPath(), root), out _)) throw new Exception("TEMP/GUID only");
var results = new List<object>();
foreach (var scenario in new[] { "dns-cdn", "timeout", "caller", "redirect-reject" })
{
    var run = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(run);
    var log = Path.Combine(run, "s9-t06-network-diagnostic.jsonl");
    using (var diagnostics = UpdateNetworkDiagnostics.OpenIfRequested(["--data-root", run, "--s9-t06-network-diagnostic", log, "--s9-t06-prepare-only", "--s9-t06-simulated-source", "1.0.0"], run))
    {
        using var cts = new CancellationTokenSource();
        if (scenario == "caller") cts.CancelAfter(100);
        using var handler = new Handler(scenario);
        var downloader = new SignedUpdatePackageDownloader(handler, new UpdatePackageOptions(MetadataTimeout: TimeSpan.FromMilliseconds(scenario == "timeout" ? 100 : 10000)), diagnostics);
        var read = typeof(SignedUpdatePackageDownloader).GetMethod("ReadSmallAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<byte[]>)read.Invoke(downloader, ["Signature", new Uri("https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.1/update-manifest.sig"), 1024, UpdatePackageOutcome.SignatureMissing, cts.Token])!;
        try { await task; throw new Exception("Expected rejection"); } catch (Exception error) when (error.Message != "Expected rejection") { }
    }
    var text = File.ReadAllText(log);
    if (text.Contains("CANARY_SECRET", StringComparison.Ordinal) || text.Contains("?token=", StringComparison.Ordinal)) throw new Exception("Unsafe log");
    var rows = File.ReadLines(log).Select(line => JsonDocument.Parse(line)).ToArray();
    bool found = scenario switch
    {
        "dns-cdn" => rows.Any(row => row.RootElement.GetProperty("kind").GetString() == "request-error" && row.RootElement.GetProperty("detail").GetProperty("redirectHop").GetInt32() == 1 && row.RootElement.GetProperty("detail").GetProperty("error").GetProperty("classification").GetString() == "DNS"),
        "timeout" or "caller" => rows.Any(row => row.RootElement.GetProperty("kind").GetString() == "request-cancelled" && row.RootElement.GetProperty("detail").GetProperty("source").GetString() == scenario),
        _ => rows.Any(row => row.RootElement.GetProperty("kind").GetString() == "redirect-rejected")
    };
    if (!found) throw new Exception("Missing event: " + scenario);
    results.Add(new { scenario, passed = true, redacted = true });
    File.Copy(log, Path.Combine(root, scenario + ".jsonl"));
    foreach (var row in rows) row.Dispose();
}
var linkedRoot = Environment.GetEnvironmentVariable("S9_T06_LINK_ROOT")!;
try
{
    using var log = UpdateNetworkDiagnostics.OpenIfRequested(["--data-root", linkedRoot, "--s9-t06-network-diagnostic", Path.Combine(linkedRoot, "s9-t06-network-diagnostic.jsonl"), "--s9-t06-prepare-only", "--s9-t06-simulated-source", "1.0.0"], linkedRoot);
    throw new Exception("Reparse root accepted");
}
catch (ArgumentException) { results.Add(new { scenario = "reparse-root-rejected", passed = true }); }
File.WriteAllText(Path.Combine(root, "network-check-result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(results));

sealed class Handler(string scenario) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (scenario is "timeout" or "caller") await Task.Delay(Timeout.Infinite, token);
        if (request.RequestUri!.Host == "github.com")
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(scenario == "redirect-reject" ? "https://evil.invalid/?token=CANARY_SECRET" : "https://release-assets.githubusercontent.com/github-production-release-asset/123/11111111-1111-1111-1111-111111111111?token=CANARY_SECRET");
            return response;
        }
        throw new HttpRequestException(HttpRequestError.NameResolutionError, "CANARY_SECRET", new SocketException((int)SocketError.HostNotFound));
    }
}
