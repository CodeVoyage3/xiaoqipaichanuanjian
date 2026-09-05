using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace StoreExpiryInspector.Application.Updates;

public enum UpdateCheckOutcome
{
    UpdateAvailable, UpToDate, NoPublishedRelease, RemoteOlder,
    NetworkUnavailable, RateLimited, InvalidRemoteMetadata, Cancelled
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, Version CurrentVersion, Version? LatestVersion = null, string? ReleaseNotes = null, CheckedRelease? Release = null)
{
    public static UpdateCheckResult From(UpdateCheckOutcome outcome, Version current) => new(outcome, current);
}

public sealed class GitHubReleaseUpdateChecker
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/latest");
    private readonly HttpClient _client;
    private readonly HttpMessageHandler _handler;
    private readonly UpdateNetworkDiagnostics? _diagnostics;

    public GitHubReleaseUpdateChecker(HttpMessageHandler? handler = null, TimeSpan? timeout = null, UpdateNetworkDiagnostics? diagnostics = null)
    {
        _handler = handler ?? new HttpClientHandler { AllowAutoRedirect = false };
        _client = new HttpClient(_handler, disposeHandler: handler is null);
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("StoreExpiryInspector/1.0");
        _diagnostics = diagnostics;
        _diagnostics?.Add("checker-handler", new { handlerType = _handler.GetType().FullName, handlerId = RuntimeHelpers.GetHashCode(_handler), clientId = RuntimeHelpers.GetHashCode(_client), createdThreadId = Environment.CurrentManagedThreadId, automaticRedirects = false, timeout = _timeout.TotalSeconds, defaultProxy = true, tls = "system-default" });
    }

    private readonly TimeSpan _timeout;

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return UpdateCheckResult.From(UpdateCheckOutcome.Cancelled, currentVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            _diagnostics?.Add("request", new { stage = "CheckLatest", host = LatestReleaseUri.IdnHost, pathCategory = "release-metadata", redirectHop = 0 });
            using var response = await _client.GetAsync(LatestReleaseUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            _diagnostics?.Add("response", new { stage = "CheckLatest", host = LatestReleaseUri.IdnHost, pathCategory = "release-metadata", redirectHop = 0, status = (int)response.StatusCode });
            if (response.StatusCode == HttpStatusCode.NotFound) return UpdateCheckResult.From(UpdateCheckOutcome.NoPublishedRelease, currentVersion);
            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429) return UpdateCheckResult.From(UpdateCheckOutcome.RateLimited, currentVersion);
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion);

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var limited = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (limited.Length + read > 256 * 1024) return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion);
                limited.Write(buffer, 0, read);
            }

            using var json = JsonDocument.Parse(limited.ToArray());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String || !TryParseTag(tag.GetString(), out var latest) ||
                !IsFalse(root, "draft") || !IsFalse(root, "prerelease"))
                return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion);

            var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                ? SanitizeNotes(body.GetString()) : null;
            var outcome = latest > currentVersion ? UpdateCheckOutcome.UpdateAvailable : latest == currentVersion ? UpdateCheckOutcome.UpToDate : UpdateCheckOutcome.RemoteOlder;
            CheckedRelease? release = null;
            if (root.TryGetProperty("id", out var id) && id.TryGetInt64(out var releaseId) && releaseId > 0 && root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(name.GetString())) { names.Clear(); break; }
                    names.Add(name.GetString()!);
                }
                if (names.Count == assets.GetArrayLength()) release = new CheckedRelease(latest, releaseId, tag.GetString()!, names);
            }
            return new(outcome, currentVersion, latest, notes, release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.From(UpdateCheckOutcome.Cancelled, currentVersion);
        }
        catch (OperationCanceledException) { return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion); }
        catch (HttpRequestException error) { _diagnostics?.Add("request-error", new { stage = "CheckLatest", error = _diagnostics.SafeError(error) }); return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion); }
        catch (IOException) { return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion); }
        catch (JsonException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
        catch (InvalidOperationException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
    }

    public static bool TryGetCurrentVersion(out Version version)
    {
        var parsed = Assembly.GetEntryAssembly()?.GetName().Version;
        version = new Version();
        if (parsed is null || parsed.Build < 0) return false;
        version = new Version(parsed.Major, parsed.Minor, parsed.Build);
        return true;
    }

    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version();
        if (tag is null || !System.Text.RegularExpressions.Regex.IsMatch(tag, "\\Av(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z")) return false;
        if (!Version.TryParse(tag[1..], out var parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }

    private static bool IsFalse(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.False;

    private static string? SanitizeNotes(string? value) => string.IsNullOrEmpty(value) ? null : new string(value.Where(character => !char.IsControl(character) || character is '\n' or '\r' or '\t').Take(1000).ToArray());
}
