#if S9T07_TEST
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

internal sealed class PreReleaseTransportHandler : HttpMessageHandler
{
    private const string ReleasePath = "/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.3/";
    private static readonly string[] Assets = ["update-manifest.json", "update-manifest.sig", "StoreExpiryInspector-1.0.3-win-x64.zip"];
    private readonly List<string> _requests = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { Scheme: "https", Port: 443 } uri) throw new HttpRequestException("unexpected pre-release request");
        _requests.Add(uri.IdnHost + uri.AbsolutePath);
        PersistRequests();
        if (uri.IdnHost == "api.github.com" && uri.AbsolutePath is "/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/latest" or "/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/tags/v1.0.3")
            return Task.FromResult(Bytes(Metadata()));
        if (uri.IdnHost == "github.com" && uri.AbsolutePath.StartsWith(ReleasePath, StringComparison.Ordinal))
        {
            var index = Array.IndexOf(Assets, uri.AbsolutePath[ReleasePath.Length..]);
            if (index < 0) throw new HttpRequestException("unexpected pre-release asset");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri($"https://release-assets.githubusercontent.com/github-production-release-asset/1/00000000-0000-0000-0000-00000000000{index + 1}") } });
        }
        const string cdn = "/github-production-release-asset/1/00000000-0000-0000-0000-00000000000";
        if (uri.IdnHost == "release-assets.githubusercontent.com" && uri.AbsolutePath.StartsWith(cdn, StringComparison.Ordinal) && int.TryParse(uri.AbsolutePath[cdn.Length..], out var number) && number is >= 1 and <= 3)
            return Task.FromResult(Bytes(File.ReadAllBytes(RequiredPath(number == 1 ? "S11_PRE_RELEASE_MANIFEST" : number == 2 ? "S11_PRE_RELEASE_SIGNATURE" : "S11_PRE_RELEASE_PACKAGE"))));
        throw new HttpRequestException("unexpected pre-release host or path");
    }

    internal void RequireCompleteSequence()
    {
        var expected = new[]
        {
            "api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/latest",
            "api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/tags/v1.0.3",
            "github.com" + ReleasePath + Assets[0], "release-assets.githubusercontent.com/github-production-release-asset/1/00000000-0000-0000-0000-000000000001",
            "github.com" + ReleasePath + Assets[1], "release-assets.githubusercontent.com/github-production-release-asset/1/00000000-0000-0000-0000-000000000002",
            "github.com" + ReleasePath + Assets[2], "release-assets.githubusercontent.com/github-production-release-asset/1/00000000-0000-0000-0000-000000000003"
        };
        if (!_requests.SequenceEqual(expected, StringComparer.Ordinal)) throw new InvalidDataException("pre-release request sequence mismatch");
    }

    private static byte[] Metadata() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        id = 110103,
        tag_name = "v1.0.3",
        draft = false,
        prerelease = false,
        body = "PRE_RELEASE_PRODUCTION_EQUIVALENT",
        assets = Assets.Select(name => new { name, state = "uploaded", browser_download_url = "https://github.com" + ReleasePath + name })
    }));

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private void PersistRequests() => File.WriteAllText(RequiredPath("S11_PRE_RELEASE_REQUESTS"), JsonSerializer.Serialize(_requests));

    private static string RequiredPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidDataException("missing pre-release path");
        path = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), path);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) || relative.Split(Path.DirectorySeparatorChar).All(part => !Guid.TryParse(part, out _))) throw new InvalidDataException("unsafe pre-release path");
        return path;
    }
}
#endif
