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
    NetworkUnavailable, RateLimited, InvalidRemoteMetadata, Cancelled = 7,
    SecurityFailure = 8, NoLegalUpgradePath = 9
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, Version CurrentVersion, Version? LatestVersion = null, string? ReleaseNotes = null, CheckedRelease? Release = null, bool TrustedHigherLatest = false)
{
    public static UpdateCheckResult From(UpdateCheckOutcome outcome, Version current) => new(outcome, current);
}
public sealed record StableReleaseListResult(UpdateCheckOutcome Outcome, IReadOnlyList<CheckedRelease> Releases);

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
            _diagnostics?.Add("request-cancelled", new { stage = "CheckLatest", host = LatestReleaseUri.IdnHost, pathCategory = "release-metadata", redirectHop = 0, source = "caller" });
            return UpdateCheckResult.From(UpdateCheckOutcome.Cancelled, currentVersion);
        }
        catch (OperationCanceledException) { _diagnostics?.Add("request-cancelled", new { stage = "CheckLatest", host = LatestReleaseUri.IdnHost, pathCategory = "release-metadata", redirectHop = 0, source = "timeout" }); return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion); }
        catch (HttpRequestException error) { _diagnostics?.Add("request-error", new { stage = "CheckLatest", host = LatestReleaseUri.IdnHost, pathCategory = "release-metadata", redirectHop = 0, error = _diagnostics.SafeError(error) }); return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion); }
        catch (IOException) { return UpdateCheckResult.From(UpdateCheckOutcome.NetworkUnavailable, currentVersion); }
        catch (JsonException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
        catch (InvalidOperationException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
    }

    public async Task<StableReleaseListResult> ListStableReleasesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            var uri = new Uri("https://api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases?per_page=100");
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429) return new(UpdateCheckOutcome.RateLimited, []);
            if (!response.IsSuccessStatusCode) return new(UpdateCheckOutcome.NetworkUnavailable, []);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var data = new MemoryStream(); var buffer = new byte[8192];
            for (int read; (read = await stream.ReadAsync(buffer, timeout.Token)) > 0;)
            {
                if (data.Length + read > 1024 * 1024) return new(UpdateCheckOutcome.InvalidRemoteMetadata, []);
                data.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(data.ToArray());
            if (json.RootElement.ValueKind != JsonValueKind.Array) return new(UpdateCheckOutcome.InvalidRemoteMetadata, []);
            var releases = new List<CheckedRelease>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !IsFalse(item, "draft") || !IsFalse(item, "prerelease")) continue;
                if (!item.TryGetProperty("id", out var id) || !id.TryGetInt64(out var releaseId) || releaseId <= 0 || !item.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String || !TryParseTag(tag.GetString(), out var version) || !item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                var names = new List<string>();
                foreach (var asset in assets.EnumerateArray())
                    if (asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(name.GetString())) names.Add(name.GetString()!); else { names.Clear(); break; }
                if (names.Count == assets.GetArrayLength() && names.Distinct(StringComparer.Ordinal).Count() == names.Count) releases.Add(new(version, releaseId, tag.GetString()!, names));
            }
            return releases.GroupBy(item => item.Version).Any(group => group.Count() != 1) ? new(UpdateCheckOutcome.InvalidRemoteMetadata, []) : new(UpdateCheckOutcome.UpToDate, releases);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(UpdateCheckOutcome.Cancelled, []); }
        catch (OperationCanceledException) { return new(UpdateCheckOutcome.NetworkUnavailable, []); }
        catch (HttpRequestException) { return new(UpdateCheckOutcome.NetworkUnavailable, []); }
        catch (IOException) { return new(UpdateCheckOutcome.NetworkUnavailable, []); }
        catch (JsonException) { return new(UpdateCheckOutcome.InvalidRemoteMetadata, []); }
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
