using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using System.Diagnostics;
using StoreExpiryInspector.Application.Updates;

try { Directory.SetCurrentDirectory(AppContext.BaseDirectory); }
catch (Exception) { return 1; }
if (args.Length != 2 || args[0] != "--journal") return 2;
return await UpdateTransaction.ResumeAsync(args[1]);

internal enum UpdatePhase { Prepared, MainExitRequested, MainExited, CandidateStaged, OldAppPreserved, SwitchStarted, CandidateActivated, CandidateStarted, WaitingForHealthAck, Committed, Completed, RollbackRequired, RollbackStarted, OldAppRestored, RollbackVerified, RolledBack, FailedNeedsManualRecovery }
internal sealed record TreeFingerprint(IReadOnlyList<string> Files, string Hash)
{
    internal static TreeFingerprint Create(string root)
    {
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(file =>
        {
            var relative = Path.GetRelativePath(root, file); if (relative.Contains(':') || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
            using var stream = File.OpenRead(file);
            return $"{relative}|{new FileInfo(file).Length}|{Convert.ToHexString(SHA256.HashData(stream))}";
        }).ToArray();
        return new(entries, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries)))));
    }
}
internal sealed record UpdateJournal(string OperationId, string ProductId, string InstallRoot, string DataRoot, string AppPath, string StagingPath, string OldPath, string PackageSha256, string SourceVersion, string TargetVersion, int ParentPid, DateTimeOffset ParentStartedUtc, UpdatePhase Phase, TreeFingerprint OldTree, TreeFingerprint CandidateTree, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, int CandidatePid = 0, DateTimeOffset? CandidateStartedUtc = null, string? LastError = null, SchemaUpdateJournal? Schema = null);

internal static class UpdateTransaction
{
    internal static async Task<int> ResumeAsync(string journalPath)
    {
        try { ValidateJournalLocation(journalPath); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { return 1; }
        using var operationMutex = new Mutex(false, "Local\\StoreExpiryInspector.Updater." + Path.GetFileName(Path.GetDirectoryName(journalPath)!));
        var ownsMutex = false;
        try { ownsMutex = operationMutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) return 1;
        Mutex? dataRootMutex = null; var ownsDataRootMutex = false;
        try
        {
            // This is deliberately data-root scoped, not operation scoped: two packages must never migrate one DB concurrently.
            string dataRoot;
            try { dataRoot = Read(journalPath).DataRoot; }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return 1; }
            dataRootMutex = new Mutex(false, "Local\\StoreExpiryInspector.Updater.Data." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataRoot).ToUpperInvariant()))));
            try { ownsDataRootMutex = dataRootMutex.WaitOne(0); } catch (AbandonedMutexException) { ownsDataRootMutex = true; }
            if (!ownsDataRootMutex) return 1;
            return ResumeCoreAsync(journalPath).GetAwaiter().GetResult();
        }
        finally { if (ownsDataRootMutex) dataRootMutex?.ReleaseMutex(); dataRootMutex?.Dispose(); if (ownsMutex) operationMutex.ReleaseMutex(); }
    }
    private static async Task<int> ResumeCoreAsync(string journalPath)
    {
        UpdateJournal? journal = null;
        var validated = false;
        try
        {
            journal = Read(journalPath);
            Validate(journalPath, journal);
            validated = true;
            if (journal.Schema is { Phase: SchemaPhase.SnapshotVerified } schemaBeforeSwitch && journal.Phase is UpdatePhase.MainExited or UpdatePhase.CandidateStaged or UpdatePhase.OldAppPreserved or UpdatePhase.SwitchStarted)
                SchemaUpgradeSnapshots.Verify(journal.DataRoot, schemaBeforeSwitch.Snapshot!);
            if (journal.Schema is not null && journal.Phase >= UpdatePhase.CandidateActivated && journal.Phase is not UpdatePhase.Completed and not UpdatePhase.RolledBack)
                return await ResumeSchema(journalPath, journal, journal.Schema);
            switch (journal.Phase)
            {
                case UpdatePhase.Prepared: return await Advance(journalPath, journal with { Phase = UpdatePhase.MainExitRequested });
                case UpdatePhase.MainExitRequested: return await Advance(journalPath, journal with { Phase = UpdatePhase.MainExited });
                case UpdatePhase.MainExited:
                    if (!WaitForParentExit(journal, ParentExitTimeout()))
                    {
                        MarkFailedNeedsManualRecovery(journalPath, journal, new TimeoutException("Main process did not exit in time."));
                        return 1;
                    }
                    TestFault(UpdatePhase.MainExited);
                    Require(TreeFingerprint.Create(journal.StagingPath), journal.CandidateTree);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.CandidateStaged });
                case UpdatePhase.CandidateStaged:
                    TestFault(UpdatePhase.CandidateStaged);
                    if (Matches(journal.OldPath, journal.OldTree)) return await Advance(journalPath, journal with { Phase = UpdatePhase.OldAppPreserved });
                    Require(TreeFingerprint.Create(journal.AppPath), journal.OldTree);
                    Directory.Move(journal.AppPath, journal.OldPath);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.OldAppPreserved });
                case UpdatePhase.OldAppPreserved:
                    Require(TreeFingerprint.Create(journal.OldPath), journal.OldTree);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.SwitchStarted });
                case UpdatePhase.SwitchStarted:
                    TestFault(UpdatePhase.SwitchStarted);
                    if (Matches(journal.AppPath, journal.CandidateTree)) return await Advance(journalPath, journal with { Phase = UpdatePhase.CandidateActivated });
                    Directory.Move(journal.StagingPath, journal.AppPath);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.CandidateActivated });
                case UpdatePhase.CandidateActivated:
                    TestFault(UpdatePhase.CandidateActivated);
                    Require(TreeFingerprint.Create(journal.AppPath), journal.CandidateTree);
                    var candidate = Process.Start(new ProcessStartInfo(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe"), VerificationArguments(journal)) { UseShellExecute = false }) ?? throw new InvalidDataException();
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.CandidateStarted, CandidatePid = candidate.Id, CandidateStartedUtc = candidate.StartTime.ToUniversalTime() });
                case UpdatePhase.CandidateStarted: return await Advance(journalPath, journal with { Phase = UpdatePhase.WaitingForHealthAck });
                case UpdatePhase.WaitingForHealthAck:
                    if (WaitForAck(journal, TimeSpan.FromSeconds(30)) && WaitForCandidateExit(journal, TimeSpan.FromSeconds(5))) return await Advance(journalPath, journal with { Phase = UpdatePhase.Committed });
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.RollbackRequired });
                case UpdatePhase.Committed:
                    TryDeleteOperationPackage(journal);
                    StartNormalApplication(journal);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.Completed });
                case UpdatePhase.RollbackRequired: return await Advance(journalPath, journal with { Phase = UpdatePhase.RollbackStarted });
                case UpdatePhase.RollbackStarted:
                    StopCandidate(journal);
                    if (Matches(journal.AppPath, journal.OldTree)) return await Advance(journalPath, journal with { Phase = UpdatePhase.OldAppRestored });
                    if (Directory.Exists(journal.AppPath)) Directory.Move(journal.AppPath, journal.StagingPath);
                    Require(TreeFingerprint.Create(journal.OldPath), journal.OldTree);
                    Directory.Move(journal.OldPath, journal.AppPath);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.OldAppRestored });
                case UpdatePhase.OldAppRestored:
                    Require(TreeFingerprint.Create(journal.AppPath), journal.OldTree);
                    var oldInfo = new ProcessStartInfo(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe"), VerificationArguments(journal)) { UseShellExecute = false }; oldInfo.Environment["S9_T05_ACK_VERSION"] = journal.SourceVersion;
                    var old = Process.Start(oldInfo) ?? throw new InvalidDataException();
                    return WaitForAck(journal with { TargetVersion = journal.SourceVersion, CandidatePid = old.Id, CandidateStartedUtc = old.StartTime.ToUniversalTime() }, TimeSpan.FromSeconds(30)) && WaitForCandidateExit(journal with { CandidatePid = old.Id, CandidateStartedUtc = old.StartTime.ToUniversalTime() }, TimeSpan.FromSeconds(5))
                        ? await Advance(journalPath, journal with { Phase = UpdatePhase.RollbackVerified })
                        : throw new InvalidDataException("Old application did not produce a valid health acknowledgement.");
                case UpdatePhase.RollbackVerified:
                    StartNormalApplication(journal);
                    return await Advance(journalPath, journal with { Phase = UpdatePhase.RolledBack });
                case UpdatePhase.Completed or UpdatePhase.RolledBack: return 0;
                default: return 1;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (validated && journal?.Schema is { Phase: SchemaPhase.RollbackRequired })
            {
                // No identity means no process can be proved safe to stop.  Preserve evidence once; never recurse rollback-required.
                MarkFailedNeedsManualRecovery(journalPath, journal, exception);
            }
            else if (validated && journal?.Schema is { } schema && schema.Phase < SchemaPhase.CandidateCommitted)
            {
                try { return await Advance(journalPath, journal with { Phase = UpdatePhase.RollbackRequired, Schema = schema with { Phase = SchemaPhase.RollbackRequired, LastError = exception.Message } }); }
                catch (Exception rollbackException) { MarkFailedNeedsManualRecovery(journalPath, journal, rollbackException); }
            }
            else if (validated && journal is not null && journal.Phase is UpdatePhase.MainExited or UpdatePhase.CandidateStaged or UpdatePhase.OldAppPreserved or UpdatePhase.SwitchStarted or UpdatePhase.CandidateActivated or UpdatePhase.CandidateStarted or UpdatePhase.WaitingForHealthAck)
            {
                try { return await Advance(journalPath, journal with { Phase = UpdatePhase.RollbackRequired, LastError = exception.Message }); }
                catch (Exception rollbackException) { MarkFailedNeedsManualRecovery(journalPath, journal, rollbackException); }
            }
            else if (validated && journal is not null) MarkFailedNeedsManualRecovery(journalPath, journal, exception);
            return 1;
        }
    }

    private static async Task<int> ResumeSchema(string path, UpdateJournal journal, SchemaUpdateJournal schema)
    {
        SchemaUpdateJournal.Validate(schema, journal.OperationId, journal.SourceVersion, journal.TargetVersion);
        if (!IsValidPhasePair(journal.Phase, schema.Phase)) throw new InvalidDataException("Invalid outer/schema update phase pair.");
        if (schema.Phase < SchemaPhase.MigrationAuthorized)
            return await Advance(path, journal with { Schema = schema with { Phase = SchemaPhase.MigrationAuthorized } });

        if (schema.Phase == SchemaPhase.MigrationAuthorized)
        {
            SchemaUpgradeSnapshots.Verify(journal.DataRoot, schema.Snapshot!);
            SchemaCandidateIdentity identity;
            try { identity = ReadIdentity(journal, schema); }
            catch (FileNotFoundException)
            {
                _ = Process.Start(new ProcessStartInfo(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe"), SchemaVerificationArguments(journal, schema, true)) { UseShellExecute = false }) ?? throw new InvalidDataException("Candidate did not start.");
                identity = WaitForIdentity(journal, schema, TimeSpan.FromSeconds(30));
            }
            using (RequireProcessIdentity(journal, identity)) { }
            return await Advance(path, journal with
            {
                Phase = UpdatePhase.CandidateStarted,
                CandidatePid = identity.Pid,
                CandidateStartedUtc = identity.StartedUtc,
                Schema = schema with { Phase = SchemaPhase.MigrationStarted, CandidatePid = identity.Pid, CandidateStartedUtc = identity.StartedUtc }
            });
        }

        if (schema.Phase == SchemaPhase.MigrationStarted)
        {
            var identity = ReadIdentity(journal, schema);
            using (RequireProcessIdentity(journal, identity)) { }
            HardKillMarker(journal, "SchemaMigrationStarted", "candidate", identity.Pid, identity.StartedUtc.UtcDateTime, schema.TargetMigrations, schema.Snapshot?.SourceSha256, pause: false);
            if (WaitForHardKilledCandidate(identity))
                return await Advance(path, journal with { Phase = UpdatePhase.RollbackRequired, Schema = schema with { Phase = SchemaPhase.RollbackRequired, LastError = "Candidate was hard-killed before migration authorization." } });
            if (!identity.Migrations.SequenceEqual(schema.TargetMigrations, StringComparer.Ordinal)) throw new InvalidDataException("Candidate static migrations do not match the signed target.");
            SchemaUpgradeSnapshots.Verify(journal.DataRoot, schema.Snapshot!);
            SchemaUpgradeSnapshots.VerifyFrozenSource(journal.DataRoot, schema.Snapshot!);
            WriteAuthorization(journal, schema, identity);
            if (WaitForMigrationApplied(journal, schema, identity, TimeSpan.FromSeconds(30)))
                return await Advance(path, journal with { Phase = UpdatePhase.WaitingForHealthAck, Schema = schema with { Phase = SchemaPhase.MigrationApplied } });
            return await Advance(path, journal with { Phase = UpdatePhase.RollbackRequired, Schema = schema with { Phase = SchemaPhase.RollbackRequired, LastError = "Candidate migration completion was not verified." } });
        }

        if (schema.Phase == SchemaPhase.MigrationApplied)
        {
            HardKillMarker(journal, "SchemaMigrationAppliedBeforeAck", "updater", Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime(), schema.TargetMigrations, schema.Snapshot?.SourceSha256, pause: true);
            // A target ACK plus confirmed candidate exit is the first evidence that migration
            // completed; persist it before recording the broader schema health verdict.
            if (!WaitForAck(journal, TimeSpan.FromSeconds(30)) || !WaitForCandidateExit(journal, TimeSpan.FromSeconds(5)))
                return await Advance(path, journal with { Phase = UpdatePhase.RollbackRequired, Schema = schema with { Phase = SchemaPhase.RollbackRequired, LastError = "Applied migration health acknowledgement was not verified." } });
            HardKillMarker(journal, "AckPersistedBeforeCandidateCommitted", "updater", Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime(), schema.TargetMigrations, schema.Snapshot?.SourceSha256, pause: true);
            return await Advance(path, journal with { Schema = schema with { Phase = SchemaPhase.SchemaHealthVerified } });
        }

        if (schema.Phase == SchemaPhase.SchemaHealthVerified)
            return await Advance(path, journal with { Phase = UpdatePhase.Committed, Schema = schema with { Phase = SchemaPhase.CandidateCommitted } });

        if (schema.Phase == SchemaPhase.CandidateCommitted)
        {
            return await CompleteNormalLaunch(path, journal, schema, NormalLaunchRole.Candidate, UpdatePhase.Completed, SchemaPhase.CandidateCommitted);
        }

        if (schema.Phase == SchemaPhase.RollbackRequired)
        {
            StopSchemaCandidate(journal, schema);
            return await Advance(path, journal with { Phase = UpdatePhase.RollbackStarted, Schema = schema with { Phase = SchemaPhase.CandidateStopped } });
        }

        if (schema.Phase == SchemaPhase.CandidateStopped)
        {
            RestoreOldTree(journal);
            return await Advance(path, journal with { Phase = UpdatePhase.OldAppRestored, Schema = schema with { Phase = SchemaPhase.OldAppRestored } });
        }

        if (schema.Phase == SchemaPhase.OldAppRestored)
            return await Advance(path, journal with { Schema = schema with { Phase = SchemaPhase.SnapshotRestoreStarted } });

        if (schema.Phase == SchemaPhase.SnapshotRestoreStarted)
        {
            SchemaUpgradeSnapshots.Restore(journal.DataRoot, schema.Snapshot!);
            return await Advance(path, journal with { Schema = schema with { Phase = SchemaPhase.SnapshotRestored } });
        }

        if (schema.Phase == SchemaPhase.SnapshotRestored)
        {
            SchemaUpgradeSnapshots.VerifyCurrentSource(journal.DataRoot, schema.Snapshot!);
#if S9T05_TEST
            var sourceVerifiedMarker = Environment.GetEnvironmentVariable("S9_T07_SOURCE_VERIFIED_MARKER");
            if (!string.IsNullOrWhiteSpace(sourceVerifiedMarker)) File.WriteAllText(sourceVerifiedMarker, schema.Snapshot!.LogicalFingerprint);
#endif
            foreach (var file in new[] { "candidate-identity.json", "candidate-authorization.json", "health-ack.json" })
            {
                var transient = SchemaOperationFile(journal, file);
                if (File.Exists(transient)) File.Delete(transient);
            }
            return await Advance(path, journal with { CandidatePid = 0, CandidateStartedUtc = null, Schema = schema with { Phase = SchemaPhase.OldSchemaVerified, CandidatePid = 0, CandidateStartedUtc = null } });
        }

        if (schema.Phase == SchemaPhase.OldSchemaVerified)
        {
            // The old verification process is launched only after snapshot restore returned successfully.
            // It uses the same no-SQLite-before-authorization handshake, but its ACK must prove the source list.
            var oldSchema = schema.CandidatePid > 0 ? schema : schema with { LaunchToken = Guid.NewGuid().ToString() };
            SchemaCandidateIdentity identity;
            try { identity = ReadIdentity(journal, oldSchema); using (RequireProcessIdentity(journal, identity)) { } }
            catch (FileNotFoundException)
            {
                _ = Process.Start(new ProcessStartInfo(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe"), SchemaVerificationArguments(journal, oldSchema, false)) { UseShellExecute = false }) ?? throw new InvalidDataException("Old application did not start.");
                identity = WaitForIdentity(journal, oldSchema, TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                foreach (var file in new[] { "candidate-identity.json", "candidate-authorization.json", "health-ack.json" })
                {
                    var transient = SchemaOperationFile(journal, file);
                    if (File.Exists(transient)) File.Delete(transient);
                }
                return await Advance(path, journal with { CandidatePid = 0, CandidateStartedUtc = null, Schema = oldSchema with { CandidatePid = 0, CandidateStartedUtc = null } });
            }
            using (RequireProcessIdentity(journal, identity)) { }
            if (schema.CandidatePid == 0)
                return await Advance(path, journal with { CandidatePid = identity.Pid, CandidateStartedUtc = identity.StartedUtc, Schema = oldSchema with { CandidatePid = identity.Pid, CandidateStartedUtc = identity.StartedUtc } });
            if (!identity.Migrations.SequenceEqual(oldSchema.SourceMigrations, StringComparer.Ordinal)) throw new InvalidDataException("Old static migrations do not match the source.");
            HardKillMarker(journal, "OldAppRestoredBeforeOldHealthAck", "old-app", identity.Pid, identity.StartedUtc.UtcDateTime, oldSchema.SourceMigrations, oldSchema.Snapshot?.SourceSha256, pause: false);
            WriteAuthorization(journal, oldSchema with { TargetMigrations = oldSchema.SourceMigrations }, identity);
            var oldJournal = journal with { CandidatePid = identity.Pid, CandidateStartedUtc = identity.StartedUtc, TargetVersion = journal.SourceVersion, Schema = oldSchema with { TargetMigrations = oldSchema.SourceMigrations } };
            var oldAck = WaitForAck(oldJournal, TimeSpan.FromSeconds(30)); var oldExit = oldAck && WaitForCandidateExit(oldJournal, TimeSpan.FromSeconds(5));
            if (!oldAck || !oldExit)
            {
                if (!OldVerifierExited(journal, identity)) throw new InvalidDataException("Old application did not produce a valid health acknowledgement.");
                foreach (var file in new[] { "candidate-identity.json", "candidate-authorization.json", "health-ack.json" })
                {
                    var transient = SchemaOperationFile(journal, file);
                    if (File.Exists(transient)) File.Delete(transient);
                }
                var resumable = journal with { Phase = UpdatePhase.OldAppRestored, CandidatePid = 0, CandidateStartedUtc = null, Schema = oldSchema with { Phase = SchemaPhase.OldSchemaVerified, CandidatePid = 0, CandidateStartedUtc = null } };
                DurableFile.Replace(path, JsonSerializer.Serialize(resumable with { UpdatedUtc = DateTimeOffset.UtcNow }));
                return 1;
            }
            return await Advance(path, journal with { Schema = oldSchema with { Phase = SchemaPhase.OldCandidateHealthVerified } });
        }

        if (schema.Phase == SchemaPhase.OldCandidateHealthVerified)
        {
            return await CompleteNormalLaunch(path, journal, schema, NormalLaunchRole.Old, UpdatePhase.RolledBack, SchemaPhase.RolledBack);
        }

        throw new InvalidDataException("Unsupported cross-schema update state.");
    }

    private static void RestoreOldTree(UpdateJournal journal)
    {
        if (Matches(journal.AppPath, journal.OldTree)) return;
        if (Directory.Exists(journal.AppPath)) Directory.Move(journal.AppPath, journal.StagingPath);
        Require(TreeFingerprint.Create(journal.OldPath), journal.OldTree);
        Directory.Move(journal.OldPath, journal.AppPath);
        Require(TreeFingerprint.Create(journal.AppPath), journal.OldTree);
    }

    private static async Task<int> CompleteNormalLaunch(string path, UpdateJournal journal, SchemaUpdateJournal schema, NormalLaunchRole role, UpdatePhase terminal, SchemaPhase terminalSchema)
    {
        var expectedTree = role == NormalLaunchRole.Candidate ? journal.CandidateTree : journal.OldTree;
        var acknowledgement = role == NormalLaunchRole.Old
            ? journal with { TargetVersion = journal.SourceVersion, Schema = schema with { TargetMigrations = schema.SourceMigrations } }
            : journal;
        var executable = Path.Combine(journal.AppPath, "StoreExpiryInspector.exe");
        NormalLaunchIntent? intent = null; Process? normal = null; var intentValidated = false;
        try
        {
            var intentPath = NormalLaunchHandshake.PathFor(journal.DataRoot, journal.OperationId);
            var intentCreated = !File.Exists(intentPath);
            if (intentCreated)
            {
                intent = new NormalLaunchIntent(journal.OperationId, schema.LaunchToken, role, expectedTree.Hash, (int)journal.Phase, (int)schema.Phase, NormalLaunchState.Pending, 0, null, DateTimeOffset.UtcNow);
                NormalLaunchHandshake.Write(journal.DataRoot, intent);
            }
            else intent = NormalLaunchHandshake.Read(journal.DataRoot, journal.OperationId);
            if (intent.LaunchToken != schema.LaunchToken || intent.Role != role || intent.ExpectedTreeHash != expectedTree.Hash || intent.ExpectedOuterPhase != (int)journal.Phase || intent.ExpectedSchemaPhase != (int)schema.Phase) throw new InvalidDataException("Normal launch intent does not match the durable transaction.");
            intentValidated = true;
            Require(TreeFingerprint.Create(journal.AppPath), expectedTree);
            if (!HasValidAck(acknowledgement) || !WaitForCandidateExit(journal, TimeSpan.FromMilliseconds(1))) throw new InvalidDataException("Health acknowledgement was not revalidated before normal launch.");
            var hadAuthorizedIdentity = intent.State is NormalLaunchState.Identified or NormalLaunchState.Loaded;
            for (var started = false; ; )
            {
                hadAuthorizedIdentity |= intent.State is NormalLaunchState.Identified or NormalLaunchState.Loaded;
                if (intent.State == NormalLaunchState.Loaded && NormalLaunchHandshake.IsLive(intent, executable))
                {
                    var migrations = UpgradeHealthAck.VerifyDatabase(Path.Combine(journal.DataRoot, "data", "app.db"), includeWal: true);
                    if (!migrations.SequenceEqual(role == NormalLaunchRole.Candidate ? schema.TargetMigrations : schema.SourceMigrations, StringComparer.Ordinal)) throw new InvalidDataException("Normal application database state is invalid.");
                    return await Advance(path, journal with { Phase = terminal, Schema = schema with { Phase = terminalSchema } });
                }
                if (intent.State == NormalLaunchState.Identified && NormalLaunchHandshake.IsLive(intent, executable))
                {
#if S9T05_TEST
                    var observedIdentity = Environment.GetEnvironmentVariable("S9_T07_UPDATER_OBSERVED_IDENTITY_MARKER"); if (!string.IsNullOrWhiteSpace(observedIdentity)) File.WriteAllText(observedIdentity, $"{intent.Pid}|{intent.StartedUtc:O}");
#endif
                    for (var until = DateTime.UtcNow + NormalLaunchTimeout(); DateTime.UtcNow < until; Thread.Sleep(100))
                    {
                        intent = NormalLaunchHandshake.Read(journal.DataRoot, journal.OperationId); hadAuthorizedIdentity |= intent.State is NormalLaunchState.Identified or NormalLaunchState.Loaded;
                        if (intent.State == NormalLaunchState.Loaded || !NormalLaunchHandshake.IsLive(intent, executable)) break;
                    }
                    if (intent.State == NormalLaunchState.Loaded) continue;
                    if (!NormalLaunchHandshake.IsLive(intent, executable)) continue;
                    throw new TimeoutException("Normal application did not report loaded.");
                }
                if (started) throw new TimeoutException("Normal application identity is not live.");
                if (!intentCreated && intent.State == NormalLaunchState.Pending)
                {
                    for (var until = DateTime.UtcNow + NormalLaunchTimeout(); DateTime.UtcNow < until; Thread.Sleep(100))
                    {
                        intent = NormalLaunchHandshake.Read(journal.DataRoot, journal.OperationId); hadAuthorizedIdentity |= intent.State is NormalLaunchState.Identified or NormalLaunchState.Loaded;
                        if (intent.State != NormalLaunchState.Pending) break;
                    }
                    if (intent.State != NormalLaunchState.Pending) continue;
                }
                if (intent.State != NormalLaunchState.Pending) NormalLaunchHandshake.Write(journal.DataRoot, intent = intent with { State = NormalLaunchState.Pending, Pid = 0, StartedUtc = null, UpdatedUtc = DateTimeOffset.UtcNow });
                var preLaunchMigrations = UpgradeHealthAck.VerifyDatabase(Path.Combine(journal.DataRoot, "data", "app.db"), includeWal: true);
                if (!preLaunchMigrations.SequenceEqual(role == NormalLaunchRole.Candidate ? schema.TargetMigrations : schema.SourceMigrations, StringComparer.Ordinal)) throw new InvalidDataException("Normal application database state is invalid.");
                if (role == NormalLaunchRole.Old && !hadAuthorizedIdentity) SchemaUpgradeSnapshots.VerifyCurrentSource(journal.DataRoot, schema.Snapshot!);
                normal = StartNormalApplication(journal, intent.LaunchToken); started = true;
                for (var until = DateTime.UtcNow + NormalLaunchTimeout(); DateTime.UtcNow < until; Thread.Sleep(100))
                {
                    intent = NormalLaunchHandshake.Read(journal.DataRoot, journal.OperationId); hadAuthorizedIdentity |= intent.State is NormalLaunchState.Identified or NormalLaunchState.Loaded;
                    if (intent.State != NormalLaunchState.Pending) break;
                }
                if (intent.State == NormalLaunchState.Pending) throw new TimeoutException("Normal application did not persist an identity.");
            }
        }
        catch { if (normal is not null || intentValidated && intent is not null) StopNormalApplication(normal, intent!, executable); throw; }
        finally { normal?.Dispose(); }
    }

    private static SchemaCandidateIdentity WaitForIdentity(UpdateJournal journal, SchemaUpdateJournal schema, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try { return ReadIdentity(journal, schema); }
            catch (FileNotFoundException) { Thread.Sleep(100); }
            catch (IOException) { Thread.Sleep(100); }
        }
        return ReadIdentity(journal, schema);
    }

    private static SchemaCandidateIdentity ReadIdentity(UpdateJournal journal, SchemaUpdateJournal schema)
    {
        var path = SchemaOperationFile(journal, "candidate-identity.json");
        var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text);
        UpdateProtocolJson.RequireObject(document.RootElement, "operationId", "launchToken", "pid", "startedUtc", "migrations");
        var identity = JsonSerializer.Deserialize<SchemaCandidateIdentity>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException();
        SchemaCandidateHandshake.Validate(identity, journal.OperationId, schema.LaunchToken);
        return identity;
    }

    private static void WriteAuthorization(UpdateJournal journal, SchemaUpdateJournal schema, SchemaCandidateIdentity identity)
    {
        var snapshot = schema.Snapshot ?? throw new InvalidDataException();
        var authorization = new SchemaCandidateAuthorization(journal.OperationId, schema.LaunchToken, identity.Pid, identity.StartedUtc, schema.TargetMigrations, snapshot.SourceSha256, schema.SourceMigrations);
        SchemaCandidateHandshake.Validate(authorization, identity);
        var path = SchemaOperationFile(journal, "candidate-authorization.json");
        DurableFile.Replace(path, JsonSerializer.Serialize(authorization, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static bool WaitForMigrationApplied(UpdateJournal journal, SchemaUpdateJournal schema, SchemaCandidateIdentity identity, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                var text = File.ReadAllText(SchemaOperationFile(journal, "migration-applied.json")); using var document = JsonDocument.Parse(text);
                UpdateProtocolJson.RequireObject(document.RootElement, "operationId", "launchToken", "pid", "startedUtc", "migrations");
                var migrations = document.RootElement.GetProperty("migrations").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
                if (document.RootElement.GetProperty("operationId").GetString() == journal.OperationId && document.RootElement.GetProperty("launchToken").GetString() == schema.LaunchToken && document.RootElement.GetProperty("pid").GetInt32() == identity.Pid && document.RootElement.GetProperty("startedUtc").GetDateTimeOffset() == identity.StartedUtc && migrations.SequenceEqual(schema.TargetMigrations, StringComparer.Ordinal)) return true;
            }
            catch (IOException) { }
            catch (JsonException) { }
            if (ParentExited(journal with { ParentPid = identity.Pid, ParentStartedUtc = identity.StartedUtc })) return false;
            Thread.Sleep(100);
        }
        return false;
    }

    private static string SchemaOperationFile(UpdateJournal journal, string fileName)
    {
        var operation = Path.Combine(journal.DataRoot, "updates", journal.OperationId);
        var path = Path.Combine(operation, fileName);
        if (!string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(path), fileName, StringComparison.Ordinal) || (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)) throw new InvalidDataException("Unsafe schema operation path.");
        ValidateOrdinaryTree(operation);
        return path;
    }

    internal static Process RequireProcessIdentity(UpdateJournal journal, SchemaCandidateIdentity identity)
    {
        var process = Process.GetProcessById(identity.Pid);
        try
        {
            var expected = Path.GetFullPath(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe"));
            var executable = process.MainModule?.FileName;
            if (Math.Abs((process.StartTime.ToUniversalTime() - identity.StartedUtc.UtcDateTime).TotalSeconds) > 1 || string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || !string.Equals(Path.GetFullPath(executable), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Candidate process identity does not match the journal application.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    private static bool OldVerifierExited(UpdateJournal journal, SchemaCandidateIdentity identity)
    {
        try { using var process = RequireProcessIdentity(journal, identity); return process.HasExited; }
        catch (ArgumentException) { return true; }
        catch { return false; }
    }

    private static void StopSchemaCandidate(UpdateJournal journal, SchemaUpdateJournal schema)
    {
        var identity = ReadIdentity(journal, schema);
        try
        {
            using var process = RequireProcessIdentity(journal, identity); process.Kill(true);
            if (!process.WaitForExit(5000)) throw new TimeoutException("Candidate did not exit before rollback.");
        }
        catch (ArgumentException) { }
    }

    private static UpdateJournal Read(string path)
    {
        var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text);
        var legacy = new[] { "OperationId", "ProductId", "InstallRoot", "DataRoot", "AppPath", "StagingPath", "OldPath", "PackageSha256", "SourceVersion", "TargetVersion", "ParentPid", "ParentStartedUtc", "Phase", "OldTree", "CandidateTree", "CreatedUtc", "UpdatedUtc", "CandidatePid", "CandidateStartedUtc", "LastError" };
        var hasSchema = document.RootElement.TryGetProperty("Schema", out var schema);
        UpdateProtocolJson.RequireObject(document.RootElement, hasSchema ? [.. legacy, "Schema"] : legacy);
        _ = UpdateProtocolJson.ReadEnum<UpdatePhase>(document.RootElement.GetProperty("Phase"));
        UpdateProtocolJson.RequireObject(document.RootElement.GetProperty("OldTree"), "Files", "Hash"); UpdateProtocolJson.RequireObject(document.RootElement.GetProperty("CandidateTree"), "Files", "Hash");
        if (hasSchema && schema.ValueKind != JsonValueKind.Null)
        {
            UpdateProtocolJson.RequireObject(schema, "Phase", "Snapshot", "SourceMigrations", "TargetMigrations", "LaunchToken", "CandidatePid", "CandidateStartedUtc", "LastError");
            _ = UpdateProtocolJson.ReadEnum<SchemaPhase>(schema.GetProperty("Phase"));
            var snapshot = schema.GetProperty("Snapshot");
            UpdateProtocolJson.RequireObject(snapshot, "OperationId", "SourceVersion", "DataRootIdentity", "SnapshotPath", "SourceSha256", "SnapshotSha256", "LogicalFingerprint", "SourceMigrations", "CreatedUtc");
        }
        if ((!hasSchema || schema.ValueKind == JsonValueKind.Null) && HasSchemaEvidence(path)) throw new InvalidDataException("Schema operation evidence requires a schema journal.");
        return JsonSerializer.Deserialize<UpdateJournal>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }) ?? throw new InvalidDataException();
    }
    private static bool HasSchemaEvidence(string journalPath)
    {
        var operation = Path.GetDirectoryName(journalPath)!;
        return new[] { "schema-source.db", "schema-restore.json", "candidate-identity.json", "candidate-authorization.json", "normal-launch.json" }.Any(file => File.Exists(Path.Combine(operation, file))) || (File.Exists(Path.Combine(operation, "health-ack.json")) && !UpdateProtocolJson.IsLegacyHealthAck(Path.Combine(operation, "health-ack.json")));
    }
    private static bool IsValidPhasePair(UpdatePhase outer, SchemaPhase schema) => outer switch
    {
        UpdatePhase.Prepared or UpdatePhase.MainExitRequested or UpdatePhase.MainExited or UpdatePhase.CandidateStaged or UpdatePhase.OldAppPreserved or UpdatePhase.SwitchStarted => schema == SchemaPhase.SnapshotVerified,
        UpdatePhase.CandidateActivated => schema is SchemaPhase.SnapshotVerified or SchemaPhase.MigrationAuthorized,
        UpdatePhase.CandidateStarted => schema == SchemaPhase.MigrationStarted,
        UpdatePhase.WaitingForHealthAck => schema is SchemaPhase.MigrationApplied or SchemaPhase.SchemaHealthVerified,
        UpdatePhase.Committed or UpdatePhase.Completed => schema == SchemaPhase.CandidateCommitted,
        UpdatePhase.RollbackRequired => schema == SchemaPhase.RollbackRequired,
        UpdatePhase.RollbackStarted => schema == SchemaPhase.CandidateStopped,
        UpdatePhase.OldAppRestored => schema is SchemaPhase.OldAppRestored or SchemaPhase.SnapshotRestoreStarted or SchemaPhase.SnapshotRestored or SchemaPhase.OldSchemaVerified or SchemaPhase.OldCandidateHealthVerified,
        UpdatePhase.RolledBack => schema == SchemaPhase.RolledBack,
        UpdatePhase.FailedNeedsManualRecovery => schema == SchemaPhase.FailedNeedsManualRecovery,
        _ => false
    };
    private static async Task<int> Advance(string path, UpdateJournal journal)
    {
        ValidateJournalLocation(path);
        DurableFile.Replace(path, JsonSerializer.Serialize(journal with { UpdatedUtc = DateTimeOffset.UtcNow }));
        PersistedPhase(journal);
        Checkpoint(journal.Phase);
        return await ResumeCoreAsync(path);
    }
    private static void MarkFailedNeedsManualRecovery(string path, UpdateJournal journal, Exception exception)
    {
        try
        {
            ValidateJournalLocation(path);
            var current = $"{DateTimeOffset.UtcNow:O} {exception.GetType().Name}: {exception.Message}";
            var error = journal.Schema?.LastError is { Length: > 0 } prior ? prior + " | recovery: " + current : current;
            var failed = journal with { Phase = UpdatePhase.FailedNeedsManualRecovery, LastError = error, UpdatedUtc = DateTimeOffset.UtcNow, Schema = journal.Schema is { } schema ? schema with { Phase = SchemaPhase.FailedNeedsManualRecovery, LastError = error } : null };
            DurableFile.Replace(path, JsonSerializer.Serialize(failed));
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "manual-recovery.log"), error + Environment.NewLine);
        }
        catch (Exception persistenceException) { Console.Error.WriteLine($"Unable to persist manual recovery state: {persistenceException}"); }
    }
    private static void Checkpoint(UpdatePhase phase)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("S9_T05_CHECKPOINT"), phase.ToString(), StringComparison.Ordinal))
            Thread.Sleep(Timeout.Infinite);
    }
    private static void PersistedPhase(UpdateJournal journal)
    {
#if S9T05_TEST
        var marker = Environment.GetEnvironmentVariable("S9_T07_DURABLE_PHASE_MARKER");
        if (!string.IsNullOrWhiteSpace(marker)) File.AppendAllText(marker, $"{journal.Phase}/{journal.Schema?.Phase}{Environment.NewLine}");
#endif
    }
    private static void HardKillMarker(UpdateJournal journal, string checkpoint, string actor, int pid, DateTime startedUtc, IReadOnlyList<string> migrations, string? hash, bool pause)
    {
#if S9T05_TEST
        if (!string.Equals(Environment.GetEnvironmentVariable("S9_T07_HARD_KILL_CHECKPOINT"), checkpoint, StringComparison.Ordinal)) return;
        var configured = Environment.GetEnvironmentVariable("S9_T07_HARD_KILL_MARKER");
        var marker = string.IsNullOrWhiteSpace(configured) ? SchemaOperationFile(journal, "hard-kill-marker.json") : Path.GetFullPath(configured);
        var writer = Process.GetCurrentProcess();
        DurableFile.Replace(marker, JsonSerializer.Serialize(new { operationId = journal.OperationId, checkpoint, actor, pid, startedUtc, writerPid = writer.Id, writerStartedUtc = writer.StartTime.ToUniversalTime(), journalPhase = journal.Phase.ToString(), schemaPhase = journal.Schema?.Phase.ToString(), timestampUtc = DateTimeOffset.UtcNow, hash, migrations, dataRoot = journal.DataRoot }));
        if (pause) Thread.Sleep(Timeout.Infinite);
#endif
    }
    private static bool WaitForHardKilledCandidate(SchemaCandidateIdentity identity)
    {
#if S9T05_TEST
        if (!string.Equals(Environment.GetEnvironmentVariable("S9_T07_HARD_KILL_CHECKPOINT"), "SchemaMigrationStarted", StringComparison.Ordinal)) return false;
        for (var until = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < until; Thread.Sleep(25))
        {
            try { using var process = Process.GetProcessById(identity.Pid); if (Math.Abs((process.StartTime.ToUniversalTime() - identity.StartedUtc.UtcDateTime).TotalSeconds) > 1 || process.HasExited) return true; }
            catch (ArgumentException) { return true; }
        }
        return false;
#else
        return false;
#endif
    }
    private static void TestFault(UpdatePhase phase)
    {
#if S9T05_TEST
        if (string.Equals(Environment.GetEnvironmentVariable("S9_T05_FAIL_PHASE"), phase.ToString(), StringComparison.Ordinal)) throw new IOException("S9-T05 controlled failure.");
#endif
    }
    private static void Validate(string path, UpdateJournal journal)
    {
        if (!Enum.IsDefined(journal.Phase) || !Guid.TryParse(journal.OperationId, out _) || journal.ProductId != "StoreExpiryInspector" || !Path.IsPathFullyQualified(path) ||
            !System.Text.RegularExpressions.Regex.IsMatch(journal.PackageSha256, "^[0-9A-Fa-f]{64}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(journal.SourceVersion, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(journal.TargetVersion, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")) throw new InvalidDataException();
        var temp = Path.GetFullPath(Path.GetTempPath()); var full = Path.GetFullPath(path);
#if S9T05_TEST
        RequireUnder(temp, journal.DataRoot);
        RequireUnder(temp, journal.InstallRoot);
#else
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!OperatingSystem.IsWindows()) throw new InvalidDataException();
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}_is1");
        var registeredRoot = key?.GetValue("Inno Setup: App Path") as string;
        if (!string.Equals(journal.DataRoot, Path.Combine(local, "StoreExpiryInspector"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registeredRoot is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(registeredRoot)), journal.InstallRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
#endif
        var expectedJournal = Path.Combine(journal.DataRoot, "updates", journal.OperationId, "journal.json");
        if (!string.Equals(full, Path.GetFullPath(expectedJournal), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        foreach (var value in new[] { journal.AppPath, journal.StagingPath, journal.OldPath }) RequireUnder(journal.InstallRoot, value);
        if (!string.Equals(journal.AppPath, Path.Combine(journal.InstallRoot, "app"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(journal.StagingPath, Path.Combine(journal.InstallRoot, "app.staging-" + journal.OperationId), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(journal.OldPath, Path.Combine(journal.InstallRoot, "app.old-" + journal.OperationId), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(journal.AppPath, journal.StagingPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(journal.AppPath, journal.OldPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(journal.StagingPath, journal.OldPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        ValidateOrdinaryTree(journal.InstallRoot);
        ValidateOrdinaryTree(journal.DataRoot);
        ValidateOrdinaryTree(Path.GetDirectoryName(path)!);
        if (journal.Schema is not null)
        {
            SchemaUpdateJournal.Validate(journal.Schema, journal.OperationId, journal.SourceVersion, journal.TargetVersion);
            SchemaUpgradeSnapshots.ValidateMetadata(journal.DataRoot, journal.Schema.Snapshot!);
            if (!IsValidPhasePair(journal.Phase, journal.Schema.Phase)) throw new InvalidDataException();
        }
    }
    private static void ValidateJournalLocation(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException();
        var full = Path.GetFullPath(path); var operation = Path.GetDirectoryName(full); var updates = operation is null ? null : Path.GetDirectoryName(operation); var data = updates is null ? null : Path.GetDirectoryName(updates);
        if (operation is null || updates is null || data is null || !Guid.TryParse(Path.GetFileName(operation), out _) || !string.Equals(Path.GetFileName(updates), "updates", StringComparison.OrdinalIgnoreCase) || !string.Equals(full, Path.Combine(operation, "journal.json"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
#if S9T05_TEST
        RequireUnder(Path.GetTempPath(), data);
#else
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.Equals(data, Path.Combine(local, "StoreExpiryInspector"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
#endif
        ValidateOrdinaryTree(data);
    }
    private static void RequireUnder(string root, string value, string? firstSegment = null)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(value));
        if (relative == "." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) throw new InvalidDataException();
        if (firstSegment is not null && !string.Equals(relative.Split(Path.DirectorySeparatorChar)[0], firstSegment, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
    }
    private static void ValidateOrdinaryTree(string root)
    {
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
    }
    private static void Require(TreeFingerprint actual, TreeFingerprint expected) { if (actual.Hash != expected.Hash || !actual.Files.SequenceEqual(expected.Files, StringComparer.Ordinal)) throw new InvalidDataException(); }
    private static bool Matches(string path, TreeFingerprint expected)
    {
        try { Require(TreeFingerprint.Create(path), expected); return true; }
        catch { return false; }
    }
    private static bool ParentExited(UpdateJournal journal)
    {
        try { var process = Process.GetProcessById(journal.ParentPid); return Math.Abs((process.StartTime.ToUniversalTime() - journal.ParentStartedUtc.UtcDateTime).TotalSeconds) > 1; }
        catch (ArgumentException) { return true; }
    }
    private static bool WaitForParentExit(UpdateJournal journal, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (ParentExited(journal)) return true;
            Thread.Sleep(100);
        }
        return ParentExited(journal);
    }
    private static TimeSpan ParentExitTimeout()
    {
#if S9T05_TEST
        if (int.TryParse(Environment.GetEnvironmentVariable("S9_T05_PARENT_WAIT_MS"), out var milliseconds) && milliseconds is > 0 and <= 30_000) return TimeSpan.FromMilliseconds(milliseconds);
#endif
        return TimeSpan.FromSeconds(30);
    }
    private static bool HasValidAck(UpdateJournal journal)
    {
        try
        {
            var path = Path.Combine(journal.DataRoot, "updates", journal.OperationId, "health-ack.json");
            ValidateAckLocation(journal, path);
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path)); var root = document.RootElement;
            UpdateProtocolJson.RequireObject(root, journal.Schema is null
                ? ["operationId", "version", "pid", "startedUtc", "migrationCount", "lastMigration", "integrity", "foreignKeys", "coreRead", "uiLoaded"]
                : ["operationId", "launchToken", "version", "pid", "startedUtc", "migrations", "migrationCount", "lastMigration", "integrity", "foreignKeys", "coreRead", "uiLoaded"]);
            var valid = root.TryGetProperty("operationId", out var operation) && operation.GetString() == journal.OperationId &&
               root.TryGetProperty("version", out var version) && version.GetString() == journal.TargetVersion &&
               root.TryGetProperty("integrity", out var integrity) && integrity.GetString() == "ok" &&
               root.TryGetProperty("foreignKeys", out var foreignKeys) && foreignKeys.GetString() == "ok" &&
               root.TryGetProperty("pid", out var pid) && pid.GetInt32() == journal.CandidatePid &&
               root.TryGetProperty("startedUtc", out var started) && DateTimeOffset.TryParse(started.GetString(), out var actualStart) && journal.CandidateStartedUtc is not null && Math.Abs((actualStart - journal.CandidateStartedUtc.Value).TotalSeconds) <= 1 &&
                root.TryGetProperty("coreRead", out var coreRead) && coreRead.GetBoolean() && root.TryGetProperty("uiLoaded", out var uiLoaded) && uiLoaded.GetBoolean();
            if (journal.Schema is { } schema)
            {
                valid &= root.TryGetProperty("launchToken", out var token) && token.GetString() == schema.LaunchToken &&
                    root.TryGetProperty("migrations", out var migrations) && migrations.ValueKind == JsonValueKind.Array &&
                    migrations.EnumerateArray().Select(item => item.GetString()).SequenceEqual(schema.TargetMigrations, StringComparer.Ordinal) &&
                    root.GetProperty("migrationCount").GetInt32() == schema.TargetMigrations.Count && root.GetProperty("lastMigration").GetString() == schema.TargetMigrations[^1];
            }
            else valid &= root.TryGetProperty("migrationCount", out var count) && count.GetInt32() == 9 && root.TryGetProperty("lastMigration", out var last) && last.GetString() == "20260901155124_AddPolicyAndBaselineFoundation";
            ValidateAckLocation(journal, path);
            return valid;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    private static void ValidateAckLocation(UpdateJournal journal, string path)
    {
        var operation = Path.Combine(journal.DataRoot, "updates", journal.OperationId);
        if (!string.Equals(Path.GetFullPath(path), Path.Combine(operation, "health-ack.json"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        ValidateOrdinaryTree(journal.DataRoot);
        ValidateOrdinaryTree(operation);
    }
    private static void TryDeleteOperationPackage(UpdateJournal journal)
    {
        try
        {
            var operation = Path.Combine(journal.DataRoot, "updates", journal.OperationId);
            var package = Path.Combine(operation, "candidate.zip");
            ValidateOrdinaryTree(operation);
            if (File.Exists(package)) File.Delete(package);
        }
        catch { }
    }
    private static bool WaitForAck(UpdateJournal journal, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (HasValidAck(journal)) return true;
            if (journal.CandidatePid != 0 && ParentExited(journal with { ParentPid = journal.CandidatePid, ParentStartedUtc = journal.CandidateStartedUtc ?? DateTimeOffset.MinValue })) return false;
            Thread.Sleep(100);
        }
        return false;
    }
    private static void StopCandidate(UpdateJournal journal)
    {
        if (journal.CandidatePid == 0) return;
        try
        {
            var process = Process.GetProcessById(journal.CandidatePid);
            if (journal.CandidateStartedUtc is not null && Math.Abs((process.StartTime.ToUniversalTime() - journal.CandidateStartedUtc.Value.UtcDateTime).TotalSeconds) <= 1)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
        catch (ArgumentException) { }
    }

    private static bool WaitForCandidateExit(UpdateJournal journal, TimeSpan timeout)
    {
        if (journal.CandidatePid == 0 || journal.CandidateStartedUtc is null) return false;
        try
        {
            using var process = Process.GetProcessById(journal.CandidatePid);
            if (Math.Abs((process.StartTime.ToUniversalTime() - journal.CandidateStartedUtc.Value.UtcDateTime).TotalSeconds) > 1) return false;
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException) { return true; }
    }

    private static TimeSpan NormalLaunchTimeout()
    {
#if S9T05_TEST
        if (int.TryParse(Environment.GetEnvironmentVariable("S9_T07_NORMAL_WAIT_MS"), out var milliseconds) && milliseconds is >= 100 and <= 30_000) return TimeSpan.FromMilliseconds(milliseconds);
#endif
        return TimeSpan.FromSeconds(30);
    }

    private static void StopNormalApplication(Process? started, NormalLaunchIntent intent, string executable)
    {
        var identities = new List<(int Pid, DateTime? Started)>();
        if (started is not null)
        {
            try { identities.Add((started.Id, started.StartTime.ToUniversalTime())); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { Console.Error.WriteLine($"Started normal application cleanup identity was unavailable: {exception.Message}"); }
        }
        if (intent.Pid > 0 && intent.StartedUtc is not null && !identities.Any(item => item.Pid == intent.Pid && item.Started == intent.StartedUtc.Value.UtcDateTime)) identities.Add((intent.Pid, intent.StartedUtc.Value.UtcDateTime));
        foreach (var identity in identities) StopExactNormalApplication(identity.Pid, identity.Started, executable);
    }

    private static void StopExactNormalApplication(int pid, DateTime? expectedStarted, string executable)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || expectedStarted is null || Math.Abs((process.StartTime.ToUniversalTime() - expectedStarted.Value).TotalSeconds) > 1 || !string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)) return;
            process.Kill(); if (!process.WaitForExit(5000)) Console.Error.WriteLine("Normal application did not exit during manual-recovery cleanup.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { Console.Error.WriteLine($"Normal application cleanup could not verify the exact process: {exception.Message}"); }
    }

    private static Process StartNormalApplication(UpdateJournal journal, string? normalToken = null)
    {
        var info = new ProcessStartInfo(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe")) { UseShellExecute = false };
#if S9T05_TEST
        var launchMarker = Environment.GetEnvironmentVariable("S9_T05_NORMAL_LAUNCH_MARKER");
        if (!string.IsNullOrWhiteSpace(launchMarker)) File.WriteAllText(launchMarker, journal.OperationId);
        info.Arguments = $"--data-root \"{journal.DataRoot}\" --allow-existing-isolated-data-root" + (normalToken is null ? string.Empty : $" --s9-t07-normal-launch {journal.OperationId} {normalToken}");
        info.Environment["S9_T05_NORMAL_LAUNCH"] = "1";
        info.Environment["S9_T05_OPERATION_ID"] = journal.OperationId;
        info.Environment["S9_T07_NORMAL_OPERATION"] = journal.OperationId;
#else
        if (normalToken is not null) info.Arguments = $"--s9-t07-normal-launch {journal.OperationId} {normalToken}";
#endif
        var started = Process.Start(info) ?? throw new InvalidDataException("Unable to restart application.");
#if S9T05_TEST
        var record = Environment.GetEnvironmentVariable("S9_T07_NORMAL_PROCESS_RECORD");
        if (!string.IsNullOrWhiteSpace(record)) File.WriteAllText(record, $"{started.Id}|{started.StartTime.ToUniversalTime():O}");
        var startedMarker = Environment.GetEnvironmentVariable("S9_T07_NORMAL_STARTED_MARKER");
        if (!string.IsNullOrWhiteSpace(startedMarker)) File.WriteAllText(startedMarker, $"{started.Id}|{started.StartTime.ToUniversalTime():O}");
#endif
        return started;
    }
    private static string VerificationArguments(UpdateJournal journal)
    {
#if S9T05_TEST
        return $"--data-root \"{journal.DataRoot}\" --allow-existing-isolated-data-root --s9-t05-verify {journal.OperationId}";
#else
        return $"--s9-t05-verify {journal.OperationId}";
#endif
    }
    private static string SchemaVerificationArguments(UpdateJournal journal, SchemaUpdateJournal schema, bool candidate)
    {
#if S9T05_TEST
        var fixtureTarget = candidate && schema.TargetMigrations.Count is 10 or 11 && schema.TargetMigrations.Count > schema.SourceMigrations.Count ? $" --s9-t07-fixture-target {schema.TargetMigrations.Count}" : string.Empty;
        return $"--data-root \"{journal.DataRoot}\" --allow-existing-isolated-data-root --s9-t07-verify {journal.OperationId} {schema.LaunchToken}{fixtureTarget}";
#else
        return $"--s9-t07-verify {journal.OperationId} {schema.LaunchToken}";
#endif
    }
}
