namespace StoreExpiryInspector.Application.Updates;

// Kept separate from UpdatePhase: its numeric values are part of the S9-T05 journal format.
public enum SchemaPhase
{
    SourceVerified = 1,
    SnapshotPreparing = 2,
    SnapshotVerified = 3,
    MigrationAuthorized = 4,
    MigrationStarted = 5,
    MigrationApplied = 6,
    SchemaHealthVerified = 7,
    CandidateCommitted = 8,
    RollbackRequired = 9,
    CandidateStopped = 10,
    OldAppRestored = 11,
    SnapshotRestoreStarted = 12,
    SnapshotRestored = 13,
    OldSchemaVerified = 14,
    OldCandidateHealthVerified = 15,
    RolledBack = 16,
    FailedNeedsManualRecovery = 17
}

public sealed record SchemaUpdateJournal(
    SchemaPhase Phase,
    SchemaUpgradeSnapshot? Snapshot,
    IReadOnlyList<string> SourceMigrations,
    IReadOnlyList<string> TargetMigrations,
    string LaunchToken,
    int CandidatePid = 0,
    DateTimeOffset? CandidateStartedUtc = null,
    string? LastError = null)
{
    public static bool IsStrictPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> migrations) =>
        prefix.Count < migrations.Count && prefix.SequenceEqual(migrations.Take(prefix.Count), StringComparer.Ordinal);

    public static bool IsValidMigrationList(IReadOnlyList<string>? migrations) =>
        migrations is { Count: > 0 } &&
        migrations.All(id => !string.IsNullOrWhiteSpace(id) && System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[0-9]{14}_[^\\s/\\\\]+\\z")) &&
        migrations.Distinct(StringComparer.Ordinal).Count() == migrations.Count &&
        migrations.SequenceEqual(migrations.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal);

    public static void Validate(SchemaUpdateJournal schema, string operationId, string sourceVersion, string targetVersion)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!Enum.IsDefined(schema.Phase) || !Guid.TryParse(operationId, out _) || !IsValidMigrationList(schema.SourceMigrations) || !IsValidMigrationList(schema.TargetMigrations) ||
            !IsStrictPrefix(schema.SourceMigrations, schema.TargetMigrations) ||
            !Guid.TryParse(schema.LaunchToken, out _)) throw new InvalidDataException("跨 Schema 升级日志无效。");
        if (schema.Snapshot is not null && (schema.Snapshot.OperationId != operationId || schema.Snapshot.SourceVersion != sourceVersion ||
            !schema.Snapshot.SourceMigrations.SequenceEqual(schema.SourceMigrations, StringComparer.Ordinal))) throw new InvalidDataException("跨 Schema 快照身份无效。");
        if (schema.Phase >= SchemaPhase.SnapshotVerified && schema.Snapshot is null) throw new InvalidDataException("跨 Schema 升级缺少已验证快照。");
        if ((schema.Phase is SchemaPhase.MigrationStarted or SchemaPhase.MigrationApplied or SchemaPhase.SchemaHealthVerified or SchemaPhase.CandidateCommitted or SchemaPhase.OldCandidateHealthVerified or SchemaPhase.RolledBack) && (schema.CandidatePid <= 0 || schema.CandidateStartedUtc is null)) throw new InvalidDataException("跨 Schema 候选身份无效。");
        _ = targetVersion; // Versions are validated by the legacy journal; migration identity is validated here.
    }
}

// The candidate writes its identity before opening SQLite, then waits for the updater's
// matching authorization.  This closes the Process.Start-to-journal persistence window.
public sealed record SchemaCandidateIdentity(string OperationId, string LaunchToken, int Pid, DateTimeOffset StartedUtc, IReadOnlyList<string> Migrations);
public sealed record SchemaCandidateAuthorization(string OperationId, string LaunchToken, int Pid, DateTimeOffset StartedUtc, IReadOnlyList<string> Migrations, string SourceSha256, IReadOnlyList<string> SourceMigrations);

public static class SchemaCandidateHandshake
{
    public static void Validate(SchemaCandidateIdentity identity, string operationId, string launchToken)
    {
        if (identity.OperationId != operationId || identity.LaunchToken != launchToken || identity.Pid <= 0 ||
            !Guid.TryParse(launchToken, out _) || identity.StartedUtc == default || !SchemaUpdateJournal.IsValidMigrationList(identity.Migrations)) throw new InvalidDataException("跨 Schema 候选进程身份无效。");
    }

    public static void Validate(SchemaCandidateAuthorization authorization, SchemaCandidateIdentity identity)
    {
        if (authorization.OperationId != identity.OperationId || authorization.LaunchToken != identity.LaunchToken ||
            authorization.Pid != identity.Pid || Math.Abs((authorization.StartedUtc - identity.StartedUtc).TotalSeconds) > 1 ||
            !authorization.Migrations.SequenceEqual(identity.Migrations, StringComparer.Ordinal) || !SchemaUpdateJournal.IsValidMigrationList(authorization.SourceMigrations) ||
            !(authorization.SourceMigrations.SequenceEqual(identity.Migrations, StringComparer.Ordinal) || SchemaUpdateJournal.IsStrictPrefix(authorization.SourceMigrations, identity.Migrations)) || !System.Text.RegularExpressions.Regex.IsMatch(authorization.SourceSha256, "\\A[0-9A-F]{64}\\z"))
            throw new InvalidDataException("跨 Schema 候选进程授权无效。");
    }
}
