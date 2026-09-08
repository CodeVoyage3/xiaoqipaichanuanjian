using System.Text.Json;
using System.Diagnostics;
using StoreExpiryInspector.Application.Updates;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T07ProtocolContractTests
{
    [Theory]
    [InlineData("\"3\"")]
    [InlineData("\" SnapshotVerified\"")]
    [InlineData("\"SnapshotVerified \"")]
    [InlineData("\"snapshotverified\"")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("3.0")]
    public void EnumReaderRejectsNonCanonicalSchemaPhase(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => UpdateProtocolJson.ReadEnum<SchemaPhase>(document.RootElement));
    }

    [Theory]
    [InlineData(6, 8)]
    [InlineData(10, 3)]
    public void PendingRejectsUnsafeOuterSchemaPair(int outer, int schema) => WithJournal(CreateJournal(outer, schema), root =>
        Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root)));

    [Fact]
    public void PendingRejectsDuplicatePhaseBeforeTerminalClassification()
    {
        var json = CreateJournal(10, 8).Replace("\"Phase\":10", "\"Phase\":8,\"Phase\":10", StringComparison.Ordinal);
        WithJournal(json, root => Assert.ThrowsAny<Exception>(() => PendingUpdateRecovery.TryResume(root)));
    }

    [Theory]
    [InlineData("null", 10)]
    [InlineData("absent", 10)]
    [InlineData("null", 15)]
    [InlineData("absent", 15)]
    public void LegacySchemaAbsentOrNullWithLegacyAckKeepsTerminalCompatibility(string schema, int phase)
    {
        var json = CreateLegacyJournal(phase, schema == "null");
        WithJournal(json, root =>
        {
            var operation = Directory.EnumerateDirectories(Path.Combine(root, "updates")).Single();
            File.WriteAllText(Path.Combine(operation, "health-ack.json"), "{\"operationId\":\"legacy\",\"version\":\"1.0.2\",\"pid\":1,\"startedUtc\":\"2026-09-05T00:00:00Z\",\"migrationCount\":9,\"lastMigration\":\"20260901155124_AddPolicyAndBaselineFoundation\",\"integrity\":\"ok\",\"foreignKeys\":\"ok\",\"coreRead\":true,\"uiLoaded\":true}");
            Assert.False(PendingUpdateRecovery.TryResume(root));
        });
    }

    [Theory]
    [InlineData("schema-source.db")]
    public void PendingRejectsSchemaEvidenceWhenSchemaWasRemoved(string evidence)
    {
        WithJournal(CreateLegacyJournal(10, false), root =>
        {
            var operation = Directory.EnumerateDirectories(Path.Combine(root, "updates")).Single();
            File.WriteAllText(Path.Combine(operation, evidence), "evidence");
            Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
        });
    }

    [Fact]
    public void PendingRejectsSchemaAckWhenSchemaWasRemoved()
    {
        WithJournal(CreateLegacyJournal(10, false), root =>
        {
            var operation = Directory.EnumerateDirectories(Path.Combine(root, "updates")).Single();
            File.WriteAllText(Path.Combine(operation, "health-ack.json"), "{\"operationId\":\"schema\",\"launchToken\":\"x\",\"version\":\"1.0.2\",\"pid\":1,\"startedUtc\":\"2026-09-05T00:00:00Z\",\"migrations\":[],\"migrationCount\":0,\"lastMigration\":\"x\",\"integrity\":\"ok\",\"foreignKeys\":\"ok\",\"coreRead\":true,\"uiLoaded\":true}");
            Assert.Throws<InvalidOperationException>(() => PendingUpdateRecovery.TryResume(root));
        });
    }

    [Theory]
    [InlineData("\"LaunchToken\":\"[0-9a-f-]+\"", "\"LaunchToken\":null")]
    [InlineData("\"CandidatePid\":0", "\"CandidatePid\":1")]
    public void PendingTerminalRejectsSemanticSchemaTampering(string find, string replace)
    {
        var json = CreateJournal(10, 8);
        if (find.Contains("LaunchToken", StringComparison.Ordinal)) json = new System.Text.RegularExpressions.Regex(find).Replace(json, replace, 1);
        else json = json.Replace(find, replace, StringComparison.Ordinal);
        WithJournal(json, root => Assert.ThrowsAny<Exception>(() => PendingUpdateRecovery.TryResume(root)));
    }

    [Fact]
    public void OldHealthWithoutPidIsRejectedBeforeNormalLaunch()
    {
        var source = new[] { "20260901155124_Test" }; var schema = new SchemaUpdateJournal(SchemaPhase.OldCandidateHealthVerified, new SchemaUpgradeSnapshot(Guid.NewGuid().ToString(), "1.0.0", "id", "snapshot", new string('A', 64), new string('A', 64), new string('A', 64), source, DateTimeOffset.UtcNow), source, [.. source, "20260905120000_Test"], Guid.NewGuid().ToString());
        Assert.Throws<InvalidDataException>(() => SchemaUpdateJournal.Validate(schema, schema.Snapshot!.OperationId, "1.0.0", "1.0.2"));
    }

    [Fact]
    public void AuthorizationRejectsDuplicateSourceShaBeforeDatabaseOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); var token = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(root, "updates", operation));
        try
        {
            var path = Path.Combine(root, "updates", operation, "candidate-authorization.json");
            File.WriteAllText(path, $"{{\"operationId\":\"{operation}\",\"launchToken\":\"{token}\",\"pid\":1,\"startedUtc\":\"2026-09-05T00:00:00Z\",\"migrations\":[\"20260901155124_Test\"],\"sourceSha256\":\"{new string('A', 64)}\",\"sourceSha256\":\"{new string('A', 64)}\",\"sourceMigrations\":[\"20260901155124_Test\"]}}");
            Assert.Throws<InvalidDataException>(() => UpgradeHealthAck.WaitForSchemaAuthorization(root, operation, token, ["20260901155124_Test"], TimeSpan.FromSeconds(1)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [MemberData(nameof(InvalidAuthorizationMigrationRelations))]
    public void AuthorizationRejectsNonPrefixReorderedOrForkedMigrationsBeforeSqliteOpen(string[] source, string[] identityMigrations)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); var token = Guid.NewGuid().ToString(); var directory = Path.Combine(root, "updates", operation); Directory.CreateDirectory(directory);
        try
        {
            var process = Process.GetCurrentProcess(); var authorization = new SchemaCandidateAuthorization(operation, token, process.Id, process.StartTime.ToUniversalTime(), identityMigrations, new string('A', 64), source);
            File.WriteAllText(Path.Combine(directory, "candidate-authorization.json"), JsonSerializer.Serialize(authorization, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Assert.Throws<InvalidDataException>(() => UpgradeHealthAck.WaitForSchemaAuthorization(root, operation, token, identityMigrations, TimeSpan.FromSeconds(1)));
            Assert.False(Directory.Exists(Path.Combine(root, "data")));
            Assert.False(File.Exists(Path.Combine(directory, "migration-applied.json")));
            Assert.False(File.Exists(Path.Combine(directory, "health-ack.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [MemberData(nameof(ValidAuthorizationMigrationRelations))]
    public void AuthorizationAllowsOldEqualOrCandidateStrictPrefix(string[] source, string[] identityMigrations)
    {
        var operation = Guid.NewGuid().ToString(); var token = Guid.NewGuid().ToString(); var identity = new SchemaCandidateIdentity(operation, token, 1, DateTimeOffset.UtcNow, identityMigrations);
        SchemaCandidateHandshake.Validate(new SchemaCandidateAuthorization(operation, token, 1, identity.StartedUtc, identityMigrations, new string('A', 64), source), identity);
    }

    public static IEnumerable<object[]> InvalidAuthorizationMigrationRelations()
    {
        yield return new object[] { new[] { "20260901155124_A", "20260901155124_C" }, new[] { "20260901155124_A", "20260901155124_B" } };
        yield return new object[] { new[] { "20260901155124_B", "20260901155124_A" }, new[] { "20260901155124_A", "20260901155124_B" } };
        yield return new object[] { new[] { "20260901155124_A", "20260901155124_B" }, new[] { "20260901155124_A", "20260901155124_C" } };
    }

    public static IEnumerable<object[]> ValidAuthorizationMigrationRelations()
    {
        yield return new object[] { new[] { "20260901155124_A" }, new[] { "20260901155124_A" } };
        yield return new object[] { new[] { "20260901155124_A" }, new[] { "20260901155124_A", "20260901155124_B" } };
    }

    private static string CreateJournal(int outer, int schema) => JsonSerializer.Serialize(new
    {
        OperationId = Guid.NewGuid().ToString(), ProductId = "StoreExpiryInspector", InstallRoot = "C:\\temp\\install", DataRoot = "C:\\temp\\data", AppPath = "C:\\temp\\install\\app", StagingPath = "C:\\temp\\install\\stage", OldPath = "C:\\temp\\install\\old", PackageSha256 = new string('A', 64), SourceVersion = "1.0.0", TargetVersion = "1.0.2", ParentPid = 0, ParentStartedUtc = "2026-09-05T00:00:00Z", Phase = outer, OldTree = new { Files = Array.Empty<string>(), Hash = new string('A', 64) }, CandidateTree = new { Files = Array.Empty<string>(), Hash = new string('A', 64) }, CreatedUtc = "2026-09-05T00:00:00Z", UpdatedUtc = "2026-09-05T00:00:00Z", CandidatePid = 0, CandidateStartedUtc = (string?)null, LastError = (string?)null,
        Schema = new { Phase = schema, Snapshot = new { OperationId = Guid.NewGuid().ToString(), SourceVersion = "1.0.0", DataRootIdentity = "id", SnapshotPath = "C:\\temp\\snapshot", SourceSha256 = new string('A', 64), SnapshotSha256 = new string('A', 64), LogicalFingerprint = new string('A', 64), SourceMigrations = new[] { "20260901155124_Test" }, CreatedUtc = "2026-09-05T00:00:00Z" }, SourceMigrations = new[] { "20260901155124_Test" }, TargetMigrations = new[] { "20260901155124_Test", "20260905120000_Test" }, LaunchToken = Guid.NewGuid().ToString(), CandidatePid = 0, CandidateStartedUtc = (string?)null, LastError = (string?)null }
    });

    private static string CreateLegacyJournal(int phase, bool schema) => JsonSerializer.Serialize(new
    {
        OperationId = Guid.NewGuid().ToString(), ProductId = "StoreExpiryInspector", InstallRoot = "C:\\temp\\install", DataRoot = "C:\\temp\\data", AppPath = "C:\\temp\\install\\app", StagingPath = "C:\\temp\\install\\stage", OldPath = "C:\\temp\\install\\old", PackageSha256 = new string('A', 64), SourceVersion = "1.0.0", TargetVersion = "1.0.2", ParentPid = 0, ParentStartedUtc = "2026-09-05T00:00:00Z", Phase = phase, OldTree = new { Files = Array.Empty<string>(), Hash = new string('A', 64) }, CandidateTree = new { Files = Array.Empty<string>(), Hash = new string('A', 64) }, CreatedUtc = "2026-09-05T00:00:00Z", UpdatedUtc = "2026-09-05T00:00:00Z", CandidatePid = 0, CandidateStartedUtc = (string?)null, LastError = (string?)null, Schema = (object?)null
    }).Replace(",\"Schema\":null", schema ? ",\"Schema\":null" : string.Empty, StringComparison.Ordinal);

    private static void WithJournal(string json, Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString(); var directory = Path.Combine(root, "updates", operation);
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "journal.json"), json);
        try { action(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
