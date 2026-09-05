using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

// This is deliberately opt-in and only accepted with an isolated TEMP/GUID data root.
public sealed class UpdateNetworkDiagnostics
{
    private const string FileArgument = "--s9-t06-network-diagnostic";
    private const string PrepareOnlyArgument = "--s9-t06-prepare-only";
    private const string SimulatedSourceArgument = "--s9-t06-simulated-source";
    private static readonly object Sync = new();
    private readonly string _path;

    private UpdateNetworkDiagnostics(string path, Version simulatedSourceVersion)
    {
        _path = path;
        SimulatedSourceVersion = simulatedSourceVersion;
    }

    public Version SimulatedSourceVersion { get; }
    public string Banner => $"诊断候选：实际版本 v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "未知"}；模拟 source v{SimulatedSourceVersion:0.0.0}；只准备，不安装。";

    public static UpdateNetworkDiagnostics? TryCreate(string[] args, bool isolated)
    {
        string? path = null; string? source = null; var prepareOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == FileArgument) { if (++i >= args.Length || path is not null) return null; path = args[i]; }
            else if (args[i] == SimulatedSourceArgument) { if (++i >= args.Length || source is not null) return null; source = args[i]; }
            else if (args[i] == PrepareOnlyArgument) prepareOnly = true;
        }
        if (!isolated || !prepareOnly || !Version.TryParse(source, out var version) || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        if (version.ToString(3) != "1.0.0") return null;
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Path.GetTempPath(), full);
        var parentRelative = Path.GetRelativePath(Path.GetTempPath(), Path.GetDirectoryName(full)!);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) || !Guid.TryParse(parentRelative, out _) || !string.Equals(Path.GetFileName(full), "s9-t06-network-diagnostic.jsonl", StringComparison.Ordinal) || File.Exists(full)) return null;
        return new(full, version);
    }

    public void Add(string kind, object? detail = null)
    {
        try { lock (Sync)
        {
            File.AppendAllText(_path, JsonSerializer.Serialize(new { kind, utc = DateTime.UtcNow, detail }) + Environment.NewLine);
        } } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public object SafeError(Exception error) => new
    {
        type = error.GetType().FullName,
        httpRequestError = (error as HttpRequestException)?.HttpRequestError.ToString(),
        status = (int?)(error as HttpRequestException)?.StatusCode,
        classification = Category(error),
        socketError = (error as SocketException)?.SocketErrorCode.ToString(),
        nativeErrorCode = (error as Win32Exception)?.NativeErrorCode,
        safeNativeMessage = error is Win32Exception win32 ? new Win32Exception(win32.NativeErrorCode).Message : null,
        rawMessageRecorded = false,
        inner = error.InnerException is null ? null : SafeError(error.InnerException)
    };

    private static string Category(Exception error) => error switch
    {
        OperationCanceledException or TimeoutException => "TimeoutOrCancellation",
        AuthenticationException => "TLS",
        SocketException socket when socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "DNS",
        SocketException => "Connection",
        HttpRequestException http when http.HttpRequestError == HttpRequestError.NameResolutionError => "DNS",
        HttpRequestException http when http.HttpRequestError == HttpRequestError.SecureConnectionError => "TLS",
        HttpRequestException http when http.HttpRequestError == HttpRequestError.ConnectionError => "Connection",
        HttpRequestException http when http.HttpRequestError == HttpRequestError.ProxyTunnelError => "ProxyTunnel",
        _ => error.InnerException is null ? "Other" : Category(error.InnerException)
    };
}
