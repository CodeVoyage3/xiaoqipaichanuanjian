using System.Net.Http;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T06NetworkDiagnosticsTests
{
    [Fact]
    public void ExplicitTempGuidPrepareOnlyDiagnosticRedactsRawExceptionText()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "s9-t06-network-diagnostic.jsonl");
            var diagnostics = UpdateNetworkDiagnostics.TryCreate(["--data-root", root, "--s9-t06-network-diagnostic", file, "--s9-t06-prepare-only", "--s9-t06-simulated-source", "1.0.0"], isolated: true);
            Assert.NotNull(diagnostics);
            diagnostics!.Add("request-error", new { error = diagnostics.SafeError(new HttpRequestException(HttpRequestError.NameResolutionError, "https://example.invalid/?token=CANARY_SECRET")) });
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("CANARY_SECRET", text, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(text);
            Assert.Equal("DNS", document.RootElement.GetProperty("detail").GetProperty("error").GetProperty("classification").GetString());
        }
        finally { Directory.Delete(root, true); }
    }
}
