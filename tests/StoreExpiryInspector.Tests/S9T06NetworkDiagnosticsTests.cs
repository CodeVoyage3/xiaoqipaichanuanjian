using System.Net.Http;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T06NetworkDiagnosticsTests
{
    [Fact]
    public void NoDiagnosticFlagsKeepsDiagnosticsOff()
    {
        Assert.False(UpdateNetworkDiagnostics.IsRequested([]));
        Assert.Null(UpdateNetworkDiagnostics.OpenIfRequested([], Path.GetTempPath()));
    }

    [Fact]
    public void ExplicitTempGuidPrepareOnlyDiagnosticRedactsRawExceptionText()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "s9-t06-network-diagnostic.jsonl");
            var diagnostics = UpdateNetworkDiagnostics.OpenIfRequested(["--data-root", root, "--s9-t06-network-diagnostic", file, "--s9-t06-prepare-only", "--s9-t06-simulated-source", "1.0.0"], root);
            Assert.NotNull(diagnostics);
            diagnostics!.Add("request-error", new { error = diagnostics.SafeError(new HttpRequestException(HttpRequestError.NameResolutionError, "https://example.invalid/?token=CANARY_SECRET")) });
            diagnostics.Dispose();
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("CANARY_SECRET", text, StringComparison.Ordinal);
            using (var document = JsonDocument.Parse(text))
            {
            Assert.Equal("DNS", document.RootElement.GetProperty("detail").GetProperty("error").GetProperty("classification").GetString());
            }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("--s9-t06-prepare-only")]
    [InlineData("--s9-t06-network-diagnostic")]
    public void RequestedDiagnosticWithoutCompleteIsolatedArgumentsFailsClosed(string flag)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try { Assert.Throws<ArgumentException>(() => UpdateNetworkDiagnostics.OpenIfRequested(["--data-root", root, flag], root)); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExistingLogAndDuplicateDiagnosticFlagsAreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "s9-t06-network-diagnostic.jsonl"); File.WriteAllText(file, "prior");
            var valid = new[] { "--data-root", root, "--s9-t06-network-diagnostic", file, "--s9-t06-prepare-only", "--s9-t06-simulated-source", "1.0.0" };
            Assert.Throws<IOException>(() => UpdateNetworkDiagnostics.OpenIfRequested(valid, root));
            Assert.Throws<ArgumentException>(() => UpdateNetworkDiagnostics.OpenIfRequested([.. valid, "--s9-t06-prepare-only"], root));
        }
        finally { Directory.Delete(root, true); }
    }

}
