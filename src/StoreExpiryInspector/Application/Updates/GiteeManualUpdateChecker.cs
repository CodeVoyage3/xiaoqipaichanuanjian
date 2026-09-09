using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace StoreExpiryInspector.Application.Updates;

public sealed class GiteeManualUpdateChecker
{
    private static readonly Uri LatestUri = new("https://gitee.com/CodeVoyage3/xiaoqipaichanuanjian-update/raw/master/latest.json");
    private static readonly Regex VersionPattern = new("\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z", RegexOptions.CultureInvariant);
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public GiteeManualUpdateChecker(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: handler is null) { Timeout = Timeout.InfiniteTimeSpan };
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return UpdateCheckResult.From(UpdateCheckOutcome.Cancelled, currentVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var response = await _client.GetAsync(LatestUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion);
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
            if (root.ValueKind != JsonValueKind.Object) return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion);
            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 3 || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 3 ||
                properties.Any(property => property.Name is not ("version" or "releaseNotes" or "manualDownloadUrl")))
                return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion);
            if (!root.TryGetProperty("version", out var versionElement) || versionElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("releaseNotes", out var notesElement) || notesElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("manualDownloadUrl", out var urlElement) || urlElement.ValueKind != JsonValueKind.String ||
                !TryParseVersion(versionElement.GetString(), out var latest) || !TryParseManualUrl(urlElement.GetString(), out var url))
                return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion);

            return latest > currentVersion
                ? new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, currentVersion, latest, SanitizeNotes(notesElement.GetString()), ManualDownloadUrl: url)
                : new UpdateCheckResult(latest == currentVersion ? UpdateCheckOutcome.UpToDate : UpdateCheckOutcome.RemoteOlder, currentVersion, latest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return UpdateCheckResult.From(UpdateCheckOutcome.Cancelled, currentVersion); }
        catch (OperationCanceledException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
        catch (HttpRequestException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
        catch (IOException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
        catch (JsonException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
        catch (InvalidOperationException) { return UpdateCheckResult.From(UpdateCheckOutcome.InvalidRemoteMetadata, currentVersion); }
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (value is null || !VersionPattern.IsMatch(value) || !Version.TryParse(value, out var parsed)) return false;
        version = parsed;
        return true;
    }

    private static bool TryParseManualUrl(string? value, out Uri? url)
    {
        url = null;
        return Uri.TryCreate(value, UriKind.Absolute, out url) && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort &&
            string.IsNullOrEmpty(url.UserInfo) && string.Equals(url.IdnHost, "pan.quark.cn", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SanitizeNotes(string? value) => string.IsNullOrEmpty(value) ? null : new string(value.Where(character => !char.IsControl(character) || character is '\n' or '\r' or '\t').ToArray());
}

public sealed class GiteeFallbackUpdateChecker
{
    private readonly Func<Version, CancellationToken, Task<UpdateCheckResult>> _github;
    private readonly Func<Version, CancellationToken, Task<UpdateCheckResult>> _gitee;

    public GiteeFallbackUpdateChecker(Func<Version, CancellationToken, Task<UpdateCheckResult>> github, Func<Version, CancellationToken, Task<UpdateCheckResult>> gitee)
    {
        _github = github;
        _gitee = gitee;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        var result = await _github(currentVersion, cancellationToken);
        return result.Outcome is UpdateCheckOutcome.NetworkUnavailable or UpdateCheckOutcome.RateLimited
            ? await _gitee(currentVersion, cancellationToken)
            : result;
    }
}
