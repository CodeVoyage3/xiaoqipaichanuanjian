using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

public enum NormalLaunchRole { Candidate, Old }
public enum NormalLaunchState { Pending, Identified, Loaded }
public sealed record NormalLaunchIntent(string OperationId, string LaunchToken, NormalLaunchRole Role, string ExpectedTreeHash, int ExpectedOuterPhase, int ExpectedSchemaPhase, NormalLaunchState State, int Pid, DateTimeOffset? StartedUtc, DateTimeOffset UpdatedUtc);

public static class NormalLaunchHandshake
{
    private static readonly string[] Fields = ["operationId", "launchToken", "role", "expectedTreeHash", "expectedOuterPhase", "expectedSchemaPhase", "state", "pid", "startedUtc", "updatedUtc"];
    public static string PathFor(string root, string operationId) => Path.Combine(root, "updates", operationId, "normal-launch.json");
    public static string TreeHash(string root)
    {
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(path =>
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("普通启动程序树不安全。");
            using var stream = File.OpenRead(path); return $"{Path.GetRelativePath(root, path)}|{new FileInfo(path).Length}|{Convert.ToHexString(SHA256.HashData(stream))}";
        });
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries))));
    }
    public static NormalLaunchIntent Read(string root, string operationId)
    {
        var path = PathFor(root, operationId); var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text);
        UpdateProtocolJson.RequireObject(document.RootElement, Fields);
        _ = UpdateProtocolJson.ReadEnum<NormalLaunchRole>(document.RootElement.GetProperty("role")); _ = UpdateProtocolJson.ReadEnum<NormalLaunchState>(document.RootElement.GetProperty("state"));
        var result = JsonSerializer.Deserialize<NormalLaunchIntent>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("普通启动协议无效。");
        Validate(result, operationId); return result;
    }
    public static void Write(string root, NormalLaunchIntent intent) { Validate(intent, intent.OperationId); DurableFile.Replace(PathFor(root, intent.OperationId), JsonSerializer.Serialize(intent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })); }
    public static NormalLaunchIntent Identify(string root, string operationId, string token, string executableRoot, Action<SchemaUpdateJournal>? validateSchema = null)
    {
        var intent = Read(root, operationId);
        var schema = ValidateJournalIntent(root, intent, executableRoot);
        if (schema is not null) validateSchema?.Invoke(schema);
        if (intent.LaunchToken != token || intent.State != NormalLaunchState.Pending || !string.Equals(intent.ExpectedTreeHash, TreeHash(executableRoot), StringComparison.Ordinal)) throw new InvalidDataException("普通启动授权无效。");
        var process = Process.GetCurrentProcess(); var identified = intent with { State = NormalLaunchState.Identified, Pid = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), UpdatedUtc = DateTimeOffset.UtcNow };
        Write(root, identified); return identified;
    }
    public static void Loaded(string root, string operationId, string token)
    {
        var intent = Read(root, operationId); var process = Process.GetCurrentProcess();
        if (intent.LaunchToken != token || intent.State != NormalLaunchState.Identified || intent.Pid != process.Id || intent.StartedUtc is null || Math.Abs((intent.StartedUtc.Value - process.StartTime.ToUniversalTime()).TotalSeconds) > 1) throw new InvalidDataException("普通启动加载确认无效。");
        Write(root, intent with { State = NormalLaunchState.Loaded, UpdatedUtc = DateTimeOffset.UtcNow });
    }
    public static bool IsLive(NormalLaunchIntent intent, string executablePath)
    {
        if (intent.Pid <= 0 || intent.StartedUtc is null) return false;
        try { using var process = Process.GetProcessById(intent.Pid); return Math.Abs((process.StartTime.ToUniversalTime() - intent.StartedUtc.Value.UtcDateTime).TotalSeconds) <= 1 && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static SchemaUpdateJournal? ValidateJournalIntent(string root, NormalLaunchIntent intent, string executableRoot)
    {
        var path = Path.Combine(root, "updates", intent.OperationId, "journal.json"); var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text); var journal = document.RootElement;
        var fields = new[] { "OperationId", "ProductId", "InstallRoot", "DataRoot", "AppPath", "StagingPath", "OldPath", "PackageSha256", "SourceVersion", "TargetVersion", "ParentPid", "ParentStartedUtc", "Phase", "OldTree", "CandidateTree", "CreatedUtc", "UpdatedUtc", "CandidatePid", "CandidateStartedUtc", "LastError" };
        var hasSchema = journal.TryGetProperty("Schema", out var schema) && schema.ValueKind != JsonValueKind.Null;
        UpdateProtocolJson.RequireObject(journal, journal.TryGetProperty("Schema", out _) ? [.. fields, "Schema"] : fields); UpdateProtocolJson.RequireObject(journal.GetProperty("OldTree"), "Files", "Hash"); UpdateProtocolJson.RequireObject(journal.GetProperty("CandidateTree"), "Files", "Hash");
        if (!hasSchema)
        {
            RequireExactPhase(journal.GetProperty("Phase"), 9, "Committed");
            if (intent.Role != NormalLaunchRole.Candidate || intent.ExpectedOuterPhase != 9 || intent.ExpectedSchemaPhase != -1 || journal.GetProperty("OperationId").GetString() != intent.OperationId || !string.Equals(NormalizePath(journal.GetProperty("DataRoot").GetString()!), NormalizePath(root), StringComparison.OrdinalIgnoreCase) || !string.Equals(NormalizePath(journal.GetProperty("AppPath").GetString()!), NormalizePath(executableRoot), StringComparison.OrdinalIgnoreCase) || journal.GetProperty("CandidateTree").GetProperty("Hash").GetString() != intent.ExpectedTreeHash) throw new InvalidDataException("普通启动事务状态无效。");
            return null;
        }
        var (outer, outerName, schemaPhase, schemaName, tree) = intent.Role == NormalLaunchRole.Candidate ? (9, "Committed", 8, "CandidateCommitted", "CandidateTree") : (13, "OldAppRestored", 15, "OldCandidateHealthVerified", "OldTree");
        RequireExactPhase(journal.GetProperty("Phase"), outer, outerName); UpdateProtocolJson.RequireObject(schema, "Phase", "Snapshot", "SourceMigrations", "TargetMigrations", "LaunchToken", "CandidatePid", "CandidateStartedUtc", "LastError"); RequireExactPhase(schema.GetProperty("Phase"), schemaPhase, schemaName);
        if (intent.ExpectedOuterPhase != outer || intent.ExpectedSchemaPhase != schemaPhase || journal.GetProperty("OperationId").GetString() != intent.OperationId || !string.Equals(NormalizePath(journal.GetProperty("DataRoot").GetString()!), NormalizePath(root), StringComparison.OrdinalIgnoreCase) || !string.Equals(NormalizePath(journal.GetProperty("AppPath").GetString()!), NormalizePath(executableRoot), StringComparison.OrdinalIgnoreCase) || schema.GetProperty("LaunchToken").GetString() != intent.LaunchToken || journal.GetProperty(tree).GetProperty("Hash").GetString() != intent.ExpectedTreeHash) throw new InvalidDataException("普通启动事务状态无效。");
        var wire = JsonSerializer.Deserialize<SchemaUpdateJournal>(schema.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? throw new InvalidDataException("普通启动事务状态无效。");
        SchemaUpdateJournal.Validate(wire, intent.OperationId, journal.GetProperty("SourceVersion").GetString()!, journal.GetProperty("TargetVersion").GetString()!);
        return wire;
    }
    private static void RequireExactPhase(JsonElement element, int number, string name)
    {
        if (!((element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value == number) || (element.ValueKind == JsonValueKind.String && element.GetString() == name))) throw new InvalidDataException("普通启动事务状态无效。");
    }
    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static void Validate(NormalLaunchIntent intent, string operationId)
    {
        if (!Guid.TryParse(operationId, out _) || intent.OperationId != operationId || !Guid.TryParse(intent.LaunchToken, out _) || !Enum.IsDefined(intent.Role) || !Enum.IsDefined(intent.State) || !System.Text.RegularExpressions.Regex.IsMatch(intent.ExpectedTreeHash, "\\A[0-9A-F]{64}\\z") || intent.UpdatedUtc == default || (intent.State == NormalLaunchState.Pending ? intent.Pid != 0 || intent.StartedUtc is not null : intent.Pid <= 0 || intent.StartedUtc is null)) throw new InvalidDataException("普通启动协议无效。");
    }
}
