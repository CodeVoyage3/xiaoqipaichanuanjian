using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoreExpiryInspector.Application.Updates;

public static class PendingUpdateRecovery
{
    public static bool TryResume(string dataRoot)
    {
        var updates = Path.Combine(dataRoot, "updates");
        if (!Directory.Exists(updates)) return false;
        EnsureOrdinaryDirectory(dataRoot);
        EnsureOrdinaryDirectory(updates);
        (string Directory, string JournalPath, bool Pending)[] pending;
        try { pending = Directory.EnumerateDirectories(updates).Select(Read).Where(item => item.Pending).ToArray(); }
        catch (InvalidDataException exception) { throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。", exception); }
        if (pending.Length == 0) return false;
        if (pending.Length != 1) throw new InvalidOperationException("检测到多个未完成更新，已停止启动以保护程序和数据。");
        var item = pending[0];
        var updaterDirectory = Path.Combine(item.Directory, "updater");
        var updater = Path.Combine(updaterDirectory, "StoreExpiryInspector.Updater.exe");
        EnsureOrdinaryDirectory(updaterDirectory);
        if (!File.Exists(updater) || (File.GetAttributes(updater) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("未完成更新缺少安全的独立 Updater，已停止启动以保护程序和数据。");
        _ = Process.Start(UpdaterLaunch.Create(updater, item.JournalPath)) ?? throw new InvalidOperationException("未完成更新无法恢复，已停止启动以保护程序和数据。");
        return true;
    }

    private static (string Directory, string JournalPath, bool Pending) Read(string directory)
    {
        EnsureOrdinaryDirectory(directory);
        if (!Guid.TryParse(Path.GetFileName(directory), out _)) throw new InvalidOperationException("更新目录身份无效，已停止启动以保护程序和数据。");
        var journal = Path.Combine(directory, "journal.json");
        if (!File.Exists(journal)) return (directory, journal, false); // Preparation has not yet atomically created a journal, so the active app was never switched.
        if ((File.GetAttributes(journal) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        JsonDocument document;
        try { document = JsonDocument.Parse(File.ReadAllText(journal)); }
        catch (JsonException) { throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。"); }
        using (document)
        {
        var legacy = new[] { "OperationId", "ProductId", "InstallRoot", "DataRoot", "AppPath", "StagingPath", "OldPath", "PackageSha256", "SourceVersion", "TargetVersion", "ParentPid", "ParentStartedUtc", "Phase", "OldTree", "CandidateTree", "CreatedUtc", "UpdatedUtc", "CandidatePid", "CandidateStartedUtc", "LastError" };
        var hasSchema = document.RootElement.TryGetProperty("Schema", out var schemaElement);
        UpdateProtocolJson.RequireObject(document.RootElement, hasSchema ? [.. legacy, "Schema"] : legacy);
        if (!document.RootElement.TryGetProperty("Phase", out var phase))
            throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        var value = (int)UpdateProtocolJson.ReadEnum<PendingPhase>(phase);
        if (value == 16) throw new InvalidOperationException("上次更新需要人工恢复，已停止启动以保护程序和数据。");
        if (value is < 0 or > 16) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        // S9-T07's CandidateCommitted uses the legacy Committed value while the updater still
        // must perform its durable post-commit completion.  Never let that journal bypass recovery.
        if (hasSchema && schemaElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        var schema = hasSchema && schemaElement.ValueKind == JsonValueKind.Object;
        if (!schema && HasSchemaEvidence(directory)) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        var sameSchemaNormalLaunch = false;
        var normalLaunch = Path.Combine(directory, "normal-launch.json");
        if (!schema && File.Exists(normalLaunch))
        {
            try { sameSchemaNormalLaunch = NormalLaunchHandshake.IsBoundSameSchemaIntent(Path.GetDirectoryName(Path.GetDirectoryName(directory)!)!, Path.GetFileName(directory), document.RootElement.GetProperty("AppPath").GetString()!); }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。", exception); }
        }
        if (schema)
        {
            UpdateProtocolJson.RequireObject(schemaElement, "Phase", "Snapshot", "SourceMigrations", "TargetMigrations", "LaunchToken", "CandidatePid", "CandidateStartedUtc", "LastError");
            var schemaPhase = UpdateProtocolJson.ReadEnum<SchemaPhase>(schemaElement.GetProperty("Phase"));
            UpdateProtocolJson.RequireObject(schemaElement.GetProperty("Snapshot"), "OperationId", "SourceVersion", "DataRootIdentity", "SnapshotPath", "SourceSha256", "SnapshotSha256", "LogicalFingerprint", "SourceMigrations", "CreatedUtc");
            var operation = document.RootElement.GetProperty("OperationId").GetString();
            var root = Path.GetDirectoryName(Path.GetDirectoryName(directory)!)!;
            if (operation != Path.GetFileName(directory) || document.RootElement.GetProperty("DataRoot").GetString() != root) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
            var wire = JsonSerializer.Deserialize<SchemaUpdateJournal>(schemaElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }) ?? throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
            SchemaUpdateJournal.Validate(wire, operation, document.RootElement.GetProperty("SourceVersion").GetString()!, document.RootElement.GetProperty("TargetVersion").GetString()!);
            SchemaUpgradeSnapshots.ValidateMetadata(root, wire.Snapshot!);
            if (!IsValidPhasePair((PendingPhase)value, schemaPhase)) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        }
        return (directory, journal, schema ? value is not (10 or 15) : sameSchemaNormalLaunch ? value != 10 : value is not (9 or 10 or 14 or 15));
        }
    }

    private static bool IsValidPhasePair(PendingPhase outer, SchemaPhase schema) => outer switch
    {
        PendingPhase.Prepared or PendingPhase.MainExitRequested or PendingPhase.MainExited or PendingPhase.CandidateStaged or PendingPhase.OldAppPreserved or PendingPhase.SwitchStarted => schema == SchemaPhase.SnapshotVerified,
        PendingPhase.CandidateActivated => schema is SchemaPhase.SnapshotVerified or SchemaPhase.MigrationAuthorized,
        PendingPhase.CandidateStarted => schema == SchemaPhase.MigrationStarted,
        PendingPhase.WaitingForHealthAck => schema is SchemaPhase.MigrationApplied or SchemaPhase.SchemaHealthVerified,
        PendingPhase.Committed or PendingPhase.Completed => schema == SchemaPhase.CandidateCommitted,
        PendingPhase.RollbackRequired => schema == SchemaPhase.RollbackRequired,
        PendingPhase.RollbackStarted => schema == SchemaPhase.CandidateStopped,
        PendingPhase.OldAppRestored => schema is SchemaPhase.OldAppRestored or SchemaPhase.SnapshotRestoreStarted or SchemaPhase.SnapshotRestored or SchemaPhase.OldSchemaVerified or SchemaPhase.OldCandidateHealthVerified,
        PendingPhase.RolledBack => schema == SchemaPhase.RolledBack,
        PendingPhase.FailedNeedsManualRecovery => schema == SchemaPhase.FailedNeedsManualRecovery,
        _ => false
    };

    private enum PendingPhase { Prepared, MainExitRequested, MainExited, CandidateStaged, OldAppPreserved, SwitchStarted, CandidateActivated, CandidateStarted, WaitingForHealthAck, Committed, Completed, RollbackRequired, RollbackStarted, OldAppRestored, RollbackVerified, RolledBack, FailedNeedsManualRecovery }

    private static bool HasSchemaEvidence(string directory) => new[] { "schema-source.db", "schema-restore.json", "candidate-identity.json", "candidate-authorization.json" }.Any(file => File.Exists(Path.Combine(directory, file))) || (File.Exists(Path.Combine(directory, "health-ack.json")) && !UpdateProtocolJson.IsLegacyHealthAck(Path.Combine(directory, "health-ack.json")));

    private static void EnsureOrdinaryDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("更新目录不安全，已停止启动以保护程序和数据。");
    }
}
