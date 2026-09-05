using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

public sealed class UpdateNetworkDiagnostics : IDisposable
{
    private const string FileArgument = "--s9-t06-network-diagnostic", PrepareOnlyArgument = "--s9-t06-prepare-only", SimulatedSourceArgument = "--s9-t06-simulated-source", DataRootArgument = "--data-root";
    private readonly StreamWriter _writer; private readonly object _sync = new();
    private UpdateNetworkDiagnostics(StreamWriter writer, Version source) { _writer = writer; SimulatedSourceVersion = source; }
    public Version SimulatedSourceVersion { get; }
    public string Banner => $"诊断候选：实际版本 v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "未知"}；模拟 source v{SimulatedSourceVersion:0.0.0}；只准备，不安装。";
    public static bool IsRequested(string[] args) => args.Any(arg => arg is FileArgument or PrepareOnlyArgument or SimulatedSourceArgument);

    public static UpdateNetworkDiagnostics? OpenIfRequested(string[] args, string isolatedRoot)
    {
        if (!IsRequested(args)) return null;
        string? dataRoot = null, file = null, source = null; var prepareOnly = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == DataRootArgument) { if (++i >= args.Length || dataRoot is not null) throw new ArgumentException("网络诊断必须指定唯一隔离数据目录。"); dataRoot = args[i]; }
            else if (args[i] == FileArgument) { if (++i >= args.Length || file is not null) throw new ArgumentException("网络诊断日志参数无效。"); file = args[i]; }
            else if (args[i] == SimulatedSourceArgument) { if (++i >= args.Length || source is not null) throw new ArgumentException("网络诊断模拟版本参数无效。"); source = args[i]; }
            else if (args[i] == PrepareOnlyArgument) prepareOnly++;
        }
        if (prepareOnly != 1 || !Version.TryParse(source, out var version) || version.ToString(3) != "1.0.0" || string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(file)) throw new ArgumentException("网络诊断必须显式指定 source 1.0.0 和只准备模式。");
        var root = Path.GetFullPath(isolatedRoot); var requestedRoot = Path.GetFullPath(dataRoot); var logPath = Path.GetFullPath(file);
        if (!string.Equals(root, requestedRoot, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetDirectoryName(logPath), root, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(logPath), "s9-t06-network-diagnostic.jsonl", StringComparison.Ordinal)) throw new ArgumentException("网络诊断日志必须位于已验证 TEMP/GUID 隔离目录。");
        for (var d = new DirectoryInfo(root); d is not null; d = d.Parent) if ((d.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("网络诊断目录不能包含链接。");
        var stream = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        return new(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true }, version);
    }
    public void Add(string kind, object? detail = null) { try { lock (_sync) _writer.WriteLine(JsonSerializer.Serialize(new { kind, utc = DateTime.UtcNow, detail })); } catch (IOException) { } catch (ObjectDisposedException) { } }
    public void Dispose() { lock (_sync) _writer.Dispose(); }
    public object SafeError(Exception error) => new { type = error.GetType().FullName, httpRequestError = (error as HttpRequestException)?.HttpRequestError.ToString(), status = (int?)(error as HttpRequestException)?.StatusCode, classification = Category(error), socketError = (error as SocketException)?.SocketErrorCode.ToString(), nativeErrorCode = (error as Win32Exception)?.NativeErrorCode, safeNativeMessage = error is Win32Exception win32 ? new Win32Exception(win32.NativeErrorCode).Message : null, rawMessageRecorded = false, inner = error.InnerException is null ? null : SafeError(error.InnerException) };
    private static string Category(Exception error) => error switch { OperationCanceledException or TimeoutException => "TimeoutOrCancellation", AuthenticationException => "TLS", SocketException socket when socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "DNS", SocketException => "Connection", HttpRequestException http when http.HttpRequestError == HttpRequestError.NameResolutionError => "DNS", HttpRequestException http when http.HttpRequestError == HttpRequestError.SecureConnectionError => "TLS", HttpRequestException http when http.HttpRequestError == HttpRequestError.ConnectionError => "Connection", HttpRequestException http when http.HttpRequestError == HttpRequestError.ProxyTunnelError => "ProxyTunnel", _ => error.InnerException is null ? "Other" : Category(error.InnerException) };
}
