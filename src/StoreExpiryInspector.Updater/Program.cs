using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

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
internal sealed record UpdateJournal(string OperationId, string ProductId, string InstallRoot, string DataRoot, string AppPath, string StagingPath, string OldPath, string PackageSha256, string SourceVersion, string TargetVersion, int ParentPid, DateTimeOffset ParentStartedUtc, UpdatePhase Phase, TreeFingerprint OldTree, TreeFingerprint CandidateTree, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, int CandidatePid = 0, DateTimeOffset? CandidateStartedUtc = null, string? LastError = null);

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
        try { return ResumeCoreAsync(journalPath).GetAwaiter().GetResult(); }
        finally { if (ownsMutex) operationMutex.ReleaseMutex(); }
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
            if (validated && journal is not null && journal.Phase is UpdatePhase.MainExited or UpdatePhase.CandidateStaged or UpdatePhase.OldAppPreserved or UpdatePhase.SwitchStarted or UpdatePhase.CandidateActivated or UpdatePhase.CandidateStarted or UpdatePhase.WaitingForHealthAck)
            {
                try { return await Advance(journalPath, journal with { Phase = UpdatePhase.RollbackRequired, LastError = exception.Message }); }
                catch (Exception rollbackException) { MarkFailedNeedsManualRecovery(journalPath, journal, rollbackException); }
            }
            else if (validated && journal is not null) MarkFailedNeedsManualRecovery(journalPath, journal, exception);
            return 1;
        }
    }

    private static UpdateJournal Read(string path)
    {
        var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text);
        RequireProperties(document.RootElement, "OperationId", "ProductId", "InstallRoot", "DataRoot", "AppPath", "StagingPath", "OldPath", "PackageSha256", "SourceVersion", "TargetVersion", "ParentPid", "ParentStartedUtc", "Phase", "OldTree", "CandidateTree", "CreatedUtc", "UpdatedUtc", "CandidatePid", "CandidateStartedUtc", "LastError");
        RequireProperties(document.RootElement.GetProperty("OldTree"), "Files", "Hash"); RequireProperties(document.RootElement.GetProperty("CandidateTree"), "Files", "Hash");
        return JsonSerializer.Deserialize<UpdateJournal>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }) ?? throw new InvalidDataException();
    }
    private static void RequireProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException(); var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!found.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException();
        if (found.Count != expected.Length) throw new InvalidDataException();
    }
    private static async Task<int> Advance(string path, UpdateJournal journal)
    {
        ValidateJournalLocation(path);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(journal with { UpdatedUtc = DateTimeOffset.UtcNow }));
        File.Move(temporary, path, true);
        Checkpoint(journal.Phase);
        return await ResumeCoreAsync(path);
    }
    private static void MarkFailedNeedsManualRecovery(string path, UpdateJournal journal, Exception exception)
    {
        try
        {
            ValidateJournalLocation(path);
            var error = $"{DateTimeOffset.UtcNow:O} {exception.GetType().Name}: {exception.Message}";
            var failed = journal with { Phase = UpdatePhase.FailedNeedsManualRecovery, LastError = error, UpdatedUtc = DateTimeOffset.UtcNow };
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(failed));
            File.Move(temporary, path, true);
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "manual-recovery.log"), error + Environment.NewLine);
        }
        catch (Exception persistenceException) { Console.Error.WriteLine($"Unable to persist manual recovery state: {persistenceException}"); }
    }
    private static void Checkpoint(UpdatePhase phase)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("S9_T05_CHECKPOINT"), phase.ToString(), StringComparison.Ordinal))
            Thread.Sleep(Timeout.Infinite);
    }
    private static void TestFault(UpdatePhase phase)
    {
#if S9T05_TEST
        if (string.Equals(Environment.GetEnvironmentVariable("S9_T05_FAIL_PHASE"), phase.ToString(), StringComparison.Ordinal)) throw new IOException("S9-T05 controlled failure.");
#endif
    }
    private static void Validate(string path, UpdateJournal journal)
    {
        if (!Guid.TryParse(journal.OperationId, out _) || journal.ProductId != "StoreExpiryInspector" || !Path.IsPathFullyQualified(path) ||
            !System.Text.RegularExpressions.Regex.IsMatch(journal.PackageSha256, "^[0-9A-Fa-f]{64}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(journal.SourceVersion, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(journal.TargetVersion, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")) throw new InvalidDataException();
        var temp = Path.GetFullPath(Path.GetTempPath()); var full = Path.GetFullPath(path);
#if S9T05_TEST
        RequireUnder(temp, journal.DataRoot);
        RequireUnder(temp, journal.InstallRoot);
#else
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.Equals(journal.InstallRoot, Path.Combine(local, "Programs", "StoreExpiryInspector"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(journal.DataRoot, Path.Combine(local, "StoreExpiryInspector"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
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
            var valid = root.TryGetProperty("operationId", out var operation) && operation.GetString() == journal.OperationId &&
               root.TryGetProperty("version", out var version) && version.GetString() == journal.TargetVersion &&
               root.TryGetProperty("migrationCount", out var count) && count.GetInt32() == 9 &&
               root.TryGetProperty("lastMigration", out var last) && last.GetString() == "20260901155124_AddPolicyAndBaselineFoundation" &&
               root.TryGetProperty("integrity", out var integrity) && integrity.GetString() == "ok" &&
               root.TryGetProperty("foreignKeys", out var foreignKeys) && foreignKeys.GetString() == "ok" &&
               root.TryGetProperty("pid", out var pid) && pid.GetInt32() == journal.CandidatePid &&
               root.TryGetProperty("startedUtc", out var started) && DateTimeOffset.TryParse(started.GetString(), out var actualStart) && journal.CandidateStartedUtc is not null && Math.Abs((actualStart - journal.CandidateStartedUtc.Value).TotalSeconds) <= 1 &&
                root.TryGetProperty("coreRead", out var coreRead) && coreRead.GetBoolean() && root.TryGetProperty("uiLoaded", out var uiLoaded) && uiLoaded.GetBoolean();
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

    private static void StartNormalApplication(UpdateJournal journal)
    {
        var info = new ProcessStartInfo(Path.Combine(journal.AppPath, "StoreExpiryInspector.exe")) { UseShellExecute = false };
#if S9T05_TEST
        info.Arguments = $"--data-root \"{journal.DataRoot}\" --allow-existing-isolated-data-root";
        info.Environment["S9_T05_NORMAL_LAUNCH"] = "1";
        info.Environment["S9_T05_OPERATION_ID"] = journal.OperationId;
#endif
        if (Process.Start(info) is null) throw new InvalidDataException("Unable to restart application.");
    }
    private static string VerificationArguments(UpdateJournal journal)
    {
#if S9T05_TEST
        return $"--data-root \"{journal.DataRoot}\" --allow-existing-isolated-data-root --s9-t05-verify {journal.OperationId}";
#else
        return $"--s9-t05-verify {journal.OperationId}";
#endif
    }
}
