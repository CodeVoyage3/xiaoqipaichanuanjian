using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace StoreExpiryInspector.Application.Updates;

public sealed record SchemaUpgradeSnapshot(
    string OperationId, string SourceVersion, string DataRootIdentity, string SnapshotPath,
    string SourceSha256, string SnapshotSha256, string LogicalFingerprint,
    IReadOnlyList<string> SourceMigrations, DateTimeOffset CreatedUtc);

public static class SchemaUpgradeSnapshots
{
    private static readonly string[] Sidecars = ["-wal", "-shm", "-journal"];
    private static readonly string[] Source9Tables = ["app_state", "backups", "batch_baselines", "batches", "draft_items", "drafts", "import_issues", "import_workbooks", "imports", "inspection_item_revisions", "inspection_items", "inspections", "inventory_adjustments", "lifecycle_events", "products", "scope_baselines", "settings", "task_items", "tasks", "__EFMigrationsHistory"];
    private static readonly string[] Source9Indexes = ["IX_backups_backup_type_created_at_utc_id", "IX_batch_baselines_baseline_id_batch_id", "IX_batch_baselines_batch_id", "IX_batch_baselines_source_task_id", "IX_batches_expiry_date", "IX_batches_last_seen_import_id", "IX_batches_product_id", "IX_batches_product_id_expiry_date", "IX_batches_product_id_production_date_expiry_date", "IX_batches_tracking_status_next_trigger_date", "IX_draft_items_draft_id_task_id", "IX_draft_items_draft_id_task_item_id", "IX_draft_items_task_item_id_task_id", "IX_drafts_task_id", "IX_import_issues_import_id_row_number_id", "IX_import_workbooks_import_id", "IX_imports_source_file_sha256", "IX_imports_status_confirmed_at_utc_id", "IX_inspection_item_revisions_inspection_item_id_changed_at_utc_id", "IX_inspection_items_batch_id_product_id", "IX_inspection_items_inspection_id_batch_id", "IX_inspection_items_inspection_id_product_id", "IX_inspection_items_product_id", "IX_inspections_product_id", "IX_inspections_task_id", "IX_inspections_task_id_product_id", "IX_inventory_adjustments_product_id_adjusted_at_utc_id", "IX_lifecycle_events_batch_id_product_id_occurred_at_utc_id", "IX_lifecycle_events_product_id_occurred_at_utc_id", "IX_lifecycle_events_source_adjustment_id", "IX_lifecycle_events_source_import_id", "IX_lifecycle_events_source_inspection_id_product_id", "IX_products_last_seen_import_id", "IX_products_product_code", "IX_scope_baselines_created_import_id", "IX_scope_baselines_scope_key_policy_code_policy_version", "IX_task_items_batch_id_product_id", "IX_task_items_product_id", "IX_task_items_task_id_batch_id", "IX_task_items_task_id_product_id", "IX_tasks_product_id_open", "IX_tasks_product_id_status"];
    private static readonly Dictionary<string, string> Source9CoreColumns = new(StringComparer.Ordinal) { ["__EFMigrationsHistory"] = "MigrationId", ["app_state"] = "id", ["backups"] = "id", ["batch_baselines"] = "id", ["batches"] = "id", ["draft_items"] = "id", ["drafts"] = "id", ["import_issues"] = "id", ["import_workbooks"] = "id", ["imports"] = "id", ["inspection_item_revisions"] = "id", ["inspection_items"] = "id", ["inspections"] = "id", ["inventory_adjustments"] = "id", ["lifecycle_events"] = "id", ["products"] = "id", ["scope_baselines"] = "id", ["settings"] = "id", ["task_items"] = "id", ["tasks"] = "id" };
    internal static Action<string>? TestCheckpoint { get; set; }

    public static void ValidateMetadata(string dataRoot, SchemaUpgradeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var root = Path.GetFullPath(dataRoot); var operation = Path.Combine(root, "updates", snapshot.OperationId); var expected = Path.Combine(operation, "schema-source.db");
        if (!Guid.TryParse(snapshot.OperationId, out _) || !Version.TryParse(snapshot.SourceVersion, out var version) || version.Build < 0 || version.Revision >= 0 || !string.Equals(snapshot.DataRootIdentity, DataRootIdentity(root), StringComparison.Ordinal) || !string.Equals(snapshot.SnapshotPath, expected, StringComparison.OrdinalIgnoreCase) || snapshot.CreatedUtc == default || !SchemaUpdateJournal.IsValidMigrationList(snapshot.SourceMigrations) || new[] { snapshot.SourceSha256, snapshot.SnapshotSha256, snapshot.LogicalFingerprint }.Any(value => !System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, "\\A[0-9A-F]{64}\\z"))) throw new InvalidDataException("升级快照元数据无效。");
        RequireOrdinaryTree(root); RequireOrdinaryTree(Path.Combine(root, "updates")); RequireOrdinaryTree(operation);
    }

    public static SchemaUpgradeSnapshot Create(string dataRoot, string operationId, string sourceVersion, IReadOnlyList<string> expectedSourceMigrations)
    {
        ArgumentNullException.ThrowIfNull(expectedSourceMigrations);
        if (expectedSourceMigrations.Count == 0 || expectedSourceMigrations.Any(id => string.IsNullOrWhiteSpace(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[0-9]{14}_[^\\s/\\\\]+\\z")) || expectedSourceMigrations.Distinct(StringComparer.Ordinal).Count() != expectedSourceMigrations.Count || !expectedSourceMigrations.SequenceEqual(expectedSourceMigrations.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal)) throw new InvalidDataException("升级源迁移声明无效。");
        var paths = ValidatePaths(dataRoot, operationId, sourceVersion);
        RejectSidecars(paths.Database);
        FileStream[]? reservations = null;
        FileStream[]? snapshotReservations = null;
        try
        {
            SchemaUpgradeSnapshot result;
            using (var mainLease = new FileStream(paths.Database, FileMode.Open, FileAccess.Read, FileShare.None))
            {
            reservations = ReserveSidecars(paths.Database);
            snapshotReservations = ReserveSidecars(paths.TemporarySnapshot);
            RejectUnexpectedSidecars(paths.Database, reservations);
            TestCheckpoint?.Invoke("source-locked");
            var sourceHash = Hash(mainLease);
            mainLease.Position = 0;
            using (var output = new FileStream(paths.TemporarySnapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { mainLease.CopyTo(output); output.Flush(flushToDisk: true); }
            TestCheckpoint?.Invoke("snapshot-copied");
            if (!string.Equals(sourceHash, Hash(paths.TemporarySnapshot), StringComparison.Ordinal)) throw new InvalidDataException("升级快照字节身份不一致。");
            mainLease.Position = 0;
            if (!string.Equals(sourceHash, Hash(mainLease), StringComparison.Ordinal)) throw new InvalidDataException("升级源数据库在快照期间变化。");
            RejectUnexpectedSidecars(paths.Database, reservations);
            var copied = Verify(paths.TemporarySnapshot);
            if (!copied.Migrations.SequenceEqual(expectedSourceMigrations, StringComparer.Ordinal) || !string.Equals(sourceHash, Hash(paths.TemporarySnapshot), StringComparison.Ordinal)) throw new InvalidDataException("升级快照验证期间发生变化。");
            RejectUnexpectedSidecars(paths.TemporarySnapshot, snapshotReservations);
            TestCheckpoint?.Invoke("snapshot-verified");
            File.Move(paths.TemporarySnapshot, paths.Snapshot);
            if (!string.Equals(sourceHash, Hash(paths.Snapshot), StringComparison.Ordinal)) throw new InvalidDataException("升级快照最终落位校验失败。");
            result = new(paths.OperationId, sourceVersion, DataRootIdentity(paths.DataRoot), paths.Snapshot, sourceHash, sourceHash, copied.LogicalFingerprint, copied.Migrations, DateTimeOffset.UtcNow);
            }
            ReleaseReservations(paths.Database, reservations);
            reservations = null;
            ReleaseReservations(paths.TemporarySnapshot, snapshotReservations);
            snapshotReservations = null;
            return result;
        }
        catch { TryDelete(paths.TemporarySnapshot); TryDelete(paths.Snapshot); throw; }
        finally { if (reservations is not null) ReleaseReservations(paths.Database, reservations); if (snapshotReservations is not null) ReleaseReservations(paths.TemporarySnapshot, snapshotReservations); }
    }

    public static void Restore(string dataRoot, SchemaUpgradeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var paths = ValidatePaths(dataRoot, snapshot.OperationId, snapshot.SourceVersion, requireSnapshot: true, requireDatabase: false);
        if (!string.Equals(paths.Snapshot, snapshot.SnapshotPath, StringComparison.OrdinalIgnoreCase) || !string.Equals(DataRootIdentity(paths.DataRoot), snapshot.DataRootIdentity, StringComparison.Ordinal)) throw new InvalidDataException("升级快照身份不匹配。");
        using var snapshotLease = new FileStream(paths.Snapshot, FileMode.Open, FileAccess.Read, FileShare.Read);
        var snapshotReservations = ReserveSidecars(paths.Snapshot);
        try
        {
            var verified = VerifySnapshot(paths, snapshot, reservedSidecars: true); TestCheckpoint?.Invoke("restore-snapshot-locked");
            var recovery = ReadRestoreState(paths, snapshot); var quarantine = Path.Combine(paths.Operation, "quarantine");
            if (Path.Exists(quarantine)) { RequireOrdinaryTree(quarantine); ValidateQuarantine(quarantine); } else Directory.CreateDirectory(quarantine);
            var staging = Path.Combine(paths.Operation, "schema-restore.tmp");
            if (recovery.Stage == RestoreStage.Replaced) { VerifyRestored(paths, snapshot, verified); return; }
            if (File.Exists(staging) && recovery.Stage == RestoreStage.Copying)
            {
                try { VerifyStaging(staging, snapshot, verified); }
                catch (InvalidDataException) { PreserveInterruptedStaging(staging); }
            }
            if (File.Exists(staging)) VerifyStaging(staging, snapshot, verified);
            else if (recovery.Stage == RestoreStage.Replacing && File.Exists(paths.Database) && IsSnapshot(paths.Database, snapshot, verified))
            {
                VerifyQuarantinedMain(quarantine, recovery); WriteRestoreState(paths, snapshot, RestoreStage.Replaced, recovery.Quarantined, recovery.MainSha256); VerifyRestored(paths, snapshot, verified); return;
            }
            else if (recovery.Stage is not (RestoreStage.None or RestoreStage.Copying)) throw new InvalidDataException("升级恢复暂存状态缺失或已篡改。");
            if (!File.Exists(staging))
            {
                WriteRestoreState(paths, snapshot, RestoreStage.Copying, recovery.Quarantined, recovery.MainSha256); TestCheckpoint?.Invoke("restore-copying-intent");
                CopySnapshot(paths.Snapshot, staging);
            }
            VerifyStaging(staging, snapshot, verified);
            if (recovery.Stage is RestoreStage.None or RestoreStage.Copying)
            {
                recovery = recovery with { Stage = RestoreStage.StagingVerified, MainSha256 = null }; WriteRestoreState(paths, snapshot, recovery.Stage, recovery.Quarantined, recovery.MainSha256); TestCheckpoint?.Invoke("restore-staging-verified");
            }
            foreach (var suffix in Sidecars)
            {
                var source = paths.Database + suffix; var target = Path.Combine(quarantine, "migrated-app.db" + suffix);
                if (recovery.Quarantined.TryGetValue(suffix, out var expected))
                {
                    if (File.Exists(source) && !File.Exists(target)) { RequireOrdinaryFile(source); if (!string.Equals(Hash(source), expected, StringComparison.Ordinal)) throw new InvalidDataException("升级恢复隔离证据已篡改。"); File.Move(source, target); }
                    else if (!File.Exists(source) && File.Exists(target)) { RequireOrdinaryFile(target); if (!string.Equals(Hash(target), expected, StringComparison.Ordinal)) throw new InvalidDataException("升级恢复隔离证据已篡改。"); }
                    else throw new InvalidDataException("升级恢复隔离证据冲突。");
                    continue;
                }
                if (!File.Exists(source)) { if (File.Exists(target)) throw new InvalidDataException("升级恢复隔离证据冲突。"); continue; }
                RequireOrdinaryFile(source); if (File.Exists(target)) throw new InvalidDataException("升级恢复隔离证据冲突。");
                var hash = Hash(source); recovery.Quarantined[suffix] = hash; WriteRestoreState(paths, snapshot, recovery.Stage, recovery.Quarantined, recovery.MainSha256); TestCheckpoint?.Invoke("restore-sidecar-intent"); File.Move(source, target); TestCheckpoint?.Invoke("restore-sidecar-quarantined");
            }
            var targetReservations = ReserveSidecars(paths.Database);
            try
            {
                if (recovery.Stage != RestoreStage.Replacing)
                {
                    recovery = recovery with { Stage = RestoreStage.Replacing, MainSha256 = File.Exists(paths.Database) ? Hash(paths.Database) : null };
                    WriteRestoreState(paths, snapshot, recovery.Stage, recovery.Quarantined, recovery.MainSha256); TestCheckpoint?.Invoke("restore-replace-intent");
                }
                else if ((recovery.MainSha256 is null && File.Exists(paths.Database)) || (recovery.MainSha256 is not null && (!File.Exists(paths.Database) || !string.Equals(Hash(paths.Database), recovery.MainSha256, StringComparison.Ordinal)))) throw new InvalidDataException("升级恢复主库身份已变化。");
                if (File.Exists(paths.Database)) { var old = Path.Combine(quarantine, "migrated-app.db"); if (File.Exists(old)) throw new InvalidDataException("升级恢复主库隔离证据冲突。"); File.Replace(staging, paths.Database, old, ignoreMetadataErrors: true); }
                else File.Move(staging, paths.Database);
                TestCheckpoint?.Invoke("restore-replaced"); WriteRestoreState(paths, snapshot, RestoreStage.Replaced, recovery.Quarantined, recovery.MainSha256);
                VerifyRestored(paths, snapshot, verified, targetReservations);
            }
            finally { ReleaseReservations(paths.Database, targetReservations); }
        }
        finally { ReleaseReservations(paths.Snapshot, snapshotReservations); }
    }

    public static void Verify(string dataRoot, SchemaUpgradeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var paths = ValidatePaths(dataRoot, snapshot.OperationId, snapshot.SourceVersion, requireSnapshot: true);
        if (!string.Equals(paths.Snapshot, snapshot.SnapshotPath, StringComparison.OrdinalIgnoreCase) || !string.Equals(DataRootIdentity(paths.DataRoot), snapshot.DataRootIdentity, StringComparison.Ordinal)) throw new InvalidDataException("升级快照身份不匹配。");
        _ = VerifySnapshot(paths, snapshot);
    }

    public static void VerifyFrozenSource(string dataRoot, SchemaUpgradeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var paths = ValidatePaths(dataRoot, snapshot.OperationId, snapshot.SourceVersion, requireSnapshot: true);
        VerifyFrozen(paths.Database, snapshot.SourceSha256, snapshot.SourceMigrations, snapshot.LogicalFingerprint);
    }

    public static void VerifyCurrentSource(string dataRoot, SchemaUpgradeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var paths = ValidatePaths(dataRoot, snapshot.OperationId, snapshot.SourceVersion, requireSnapshot: true);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Database, Mode = SqliteOpenMode.ReadOnly, ForeignKeys = true, Pooling = false }.ToString()); connection.Open();
        var current = Verify(connection);
        if (!current.Migrations.SequenceEqual(snapshot.SourceMigrations, StringComparer.Ordinal) || !string.Equals(current.LogicalFingerprint, snapshot.LogicalFingerprint, StringComparison.Ordinal)) throw new InvalidDataException("升级恢复后源数据库逻辑身份无效。");
    }

    public static void VerifyFrozenSource(string dataRoot, string sourceSha256, IReadOnlyList<string> sourceMigrations)
    {
        var root = Path.GetFullPath(dataRoot); var database = Path.Combine(root, "data", "app.db");
        RequireOrdinaryTree(root); RequireOrdinaryTree(Path.Combine(root, "data")); RequireOrdinaryFile(database);
        VerifyFrozen(database, sourceSha256, sourceMigrations, null);
    }

    public static void TakeOverFrozenSource(string dataRoot, string sourceSha256, IReadOnlyList<string> sourceMigrations, Action<SqliteConnection> firstOpen)
    {
        ArgumentNullException.ThrowIfNull(firstOpen);
        var root = Path.GetFullPath(dataRoot); var database = Path.Combine(root, "data", "app.db");
        RequireOrdinaryTree(root); RequireOrdinaryTree(Path.Combine(root, "data")); RequireOrdinaryFile(database);
        RejectSidecars(database);
        FileStream[]? reservations = null;
        try
        {
            reservations = ReserveSidecars(database);
            RejectUnexpectedSidecars(database, reservations);
            // Reservations close the pre-open gap; the opened SQLite VFS owns the handoff after this marker.
            TestCheckpoint?.Invoke("frozen-source-reservation-held");
            var sourceHash = Hash(database);
            if (!string.Equals(sourceHash, sourceSha256, StringComparison.Ordinal)) throw new InvalidDataException("升级授权前源数据库已变化。");
            TestCheckpoint?.Invoke("frozen-source-before-vfs-open");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, ForeignKeys = true }.ToString());
            connection.Open();
            TestCheckpoint?.Invoke("frozen-source-vfs-opened");
            RejectUnexpectedSidecars(database, reservations);
            ReleaseReservations(database, reservations);
            reservations = null;
            AcquireExclusiveOwnership(connection);
            // WAL-mode SQLite may create its own WAL/SHM while taking the exclusive lock.
            // Record only those VFS-owned files, then reject any late residue before the next SQLite read/DDL.
            var ownedSidecars = CaptureOwnedSidecars(database);
            TestCheckpoint?.Invoke("frozen-source-vfs-owned");
            RejectLateSidecars(database, ownedSidecars);
            if (!string.Equals(HashShared(database), sourceHash, StringComparison.Ordinal)) throw new InvalidDataException("升级授权前源数据库已变化。");
            var source = Verify(connection);
            if (!source.Migrations.SequenceEqual(sourceMigrations, StringComparer.Ordinal)) throw new InvalidDataException("升级授权前源数据库逻辑身份已变化。");
            TestCheckpoint?.Invoke("frozen-source-vfs-verified");
            firstOpen(connection);
        }
        finally { if (reservations is not null) ReleaseReservations(database, reservations); }
    }

    private static void AcquireExclusiveOwnership(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE; COMMIT;";
        command.ExecuteNonQuery();
    }

    private static IReadOnlyDictionary<string, string> CaptureOwnedSidecars(string database) => ExistingSidecars(database).ToDictionary(path => path, HashShared, StringComparer.Ordinal);
    private static void RejectLateSidecars(string database, IReadOnlyDictionary<string, string> owned)
    {
        var current = ExistingSidecars(database).ToArray();
        if (current.Length != owned.Count || current.Any(path => !owned.TryGetValue(path, out var hash) || !string.Equals(HashShared(path), hash, StringComparison.Ordinal))) throw new InvalidDataException("升级接管期间检测到未知 SQLite sidecar。");
    }

    private static void VerifyFrozen(string database, string sourceSha256, IReadOnlyList<string> sourceMigrations, string? logicalFingerprint)
    {
        RejectSidecars(database); FileStream[]? reservations = null;
        try
        {
            using var main = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.Read);
            reservations = ReserveSidecars(database); RejectUnexpectedSidecars(database, reservations);
            if (!string.Equals(Hash(main), sourceSha256, StringComparison.Ordinal)) throw new InvalidDataException("升级授权前源数据库已变化。");
            main.Position = 0; var source = Verify(database);
            if (!source.Migrations.SequenceEqual(sourceMigrations, StringComparer.Ordinal) || (logicalFingerprint is not null && !string.Equals(source.LogicalFingerprint, logicalFingerprint, StringComparison.Ordinal))) throw new InvalidDataException("升级授权前源数据库逻辑身份已变化。");
            main.Position = 0; if (!string.Equals(Hash(main), sourceSha256, StringComparison.Ordinal)) throw new InvalidDataException("升级授权前源数据库已变化。");
            RejectUnexpectedSidecars(database, reservations); TestCheckpoint?.Invoke("frozen-source-verified");
        }
        finally { if (reservations is not null) ReleaseReservations(database, reservations); }
    }

    public static bool IsStrictPrefix(IReadOnlyList<string> source, IReadOnlyList<string> target) => source.Count > 0 && source.Count < target.Count && source.SequenceEqual(target.Take(source.Count), StringComparer.Ordinal);

    private static SnapshotPaths ValidatePaths(string dataRoot, string operationId, string sourceVersion, bool requireSnapshot = false, bool requireDatabase = true)
    {
        if (!Guid.TryParse(operationId, out _) || !Version.TryParse(sourceVersion, out var version) || version.Build < 0 || version.Revision >= 0) throw new InvalidDataException("升级快照身份无效。");
        var root = Path.GetFullPath(dataRoot); if (root.StartsWith("\\\\", StringComparison.Ordinal) || HasAlternateDataStream(root)) throw new InvalidDataException("升级数据根无效。");
        var data = Path.Combine(root, "data"); var operation = Path.Combine(root, "updates", operationId); var database = Path.Combine(data, "app.db"); var snapshot = Path.Combine(operation, "schema-source.db"); var temporary = snapshot + ".tmp";
        RequireOrdinaryTree(root); RequireOrdinaryTree(data); RequireOrdinaryTree(Path.Combine(root, "updates")); RequireOrdinaryTree(operation); if (requireDatabase) RequireOrdinaryFile(database);
        if (!string.Equals(Path.GetFullPath(operation), operation, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFullPath(snapshot), snapshot, StringComparison.OrdinalIgnoreCase) || HasAlternateDataStream(snapshot)) throw new InvalidDataException("升级操作目录无效。");
        if (requireSnapshot ? !File.Exists(snapshot) : Path.Exists(snapshot) || Path.Exists(temporary)) throw new InvalidDataException("升级保护快照状态无效。");
        return new(root, operationId, operation, database, snapshot, temporary);
    }

    private static VerifiedDatabase VerifySnapshot(SnapshotPaths paths, SchemaUpgradeSnapshot snapshot, bool reservedSidecars = false)
    {
        RequireOrdinaryFile(paths.Snapshot);
        if ((!reservedSidecars && HasSidecars(paths.Snapshot)) || (reservedSidecars && ExistingSidecars(paths.Snapshot).Any(path => new FileInfo(path).Length != 0)) || !string.Equals(Hash(paths.Snapshot), snapshot.SnapshotSha256, StringComparison.Ordinal) || !string.Equals(snapshot.SourceSha256, snapshot.SnapshotSha256, StringComparison.Ordinal)) throw new InvalidDataException("升级快照字节身份无效。");
        var verified = Verify(paths.Snapshot);
        if (!verified.Migrations.SequenceEqual(snapshot.SourceMigrations, StringComparer.Ordinal) || !string.Equals(verified.LogicalFingerprint, snapshot.LogicalFingerprint, StringComparison.Ordinal)) throw new InvalidDataException("升级快照逻辑身份无效。");
        return verified;
    }

    private static RestoreRecovery ReadRestoreState(SnapshotPaths paths, SchemaUpgradeSnapshot snapshot)
    {
        var path = Path.Combine(paths.Operation, "schema-restore.json");
        DurableFile.PreserveInterruptedScratch(path + ".tmp");
        if (!File.Exists(path)) return new(RestoreStage.None, [], null);
        RequireOrdinaryFile(path);
        RestoreState? state;
        try { var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text); UpdateProtocolJson.RequireObject(document.RootElement, "OperationId", "SnapshotSha256", "Stage", "Quarantined", "MainSha256"); _ = UpdateProtocolJson.ReadEnum<RestoreStage>(document.RootElement.GetProperty("Stage")); UpdateProtocolJson.RequireStringMap(document.RootElement.GetProperty("Quarantined"), Sidecars); state = JsonSerializer.Deserialize<RestoreState>(text, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }); } catch (JsonException) { throw new InvalidDataException("升级恢复状态无效。"); }
        if (state is null || state.OperationId != snapshot.OperationId || !string.Equals(state.SnapshotSha256, snapshot.SnapshotSha256, StringComparison.Ordinal) || !Enum.IsDefined(state.Stage) || state.Quarantined is null || state.Quarantined.Keys.Any(key => !Sidecars.Contains(key, StringComparer.Ordinal)) || state.Quarantined.Values.Any(value => value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) || state.MainSha256 is { } main && (main.Length != 64 || main.Any(c => !Uri.IsHexDigit(c)))) throw new InvalidDataException("升级恢复状态无效。");
        return new(state.Stage, new Dictionary<string, string>(state.Quarantined, StringComparer.Ordinal), state.MainSha256);
    }

    private static void WriteRestoreState(SnapshotPaths paths, SchemaUpgradeSnapshot snapshot, RestoreStage stage, IReadOnlyDictionary<string, string> quarantined, string? mainSha256)
    {
        var path = Path.Combine(paths.Operation, "schema-restore.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RestoreState(snapshot.OperationId, snapshot.SnapshotSha256, stage, new Dictionary<string, string>(quarantined, StringComparer.Ordinal), mainSha256));
        DurableFile.Replace(path, bytes);
    }

    private static void VerifyStaging(string staging, SchemaUpgradeSnapshot snapshot, VerifiedDatabase verified)
    {
        RequireOrdinaryFile(staging); using var lease = new FileStream(staging, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (!string.Equals(Hash(lease), snapshot.SnapshotSha256, StringComparison.Ordinal)) throw new InvalidDataException("升级恢复暂存验证失败。");
        lease.Position = 0; if (!SameDatabase(verified, Verify(staging))) throw new InvalidDataException("升级恢复暂存验证失败。");
    }

    private static void CopySnapshot(string snapshot, string staging)
    {
        using var input = new FileStream(snapshot, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
#if S9T07_HARD_KILL_TEST
        var first = true;
#endif
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            output.Write(buffer, 0, read);
#if S9T07_HARD_KILL_TEST
            if (first) { first = false; TestCheckpoint?.Invoke("restore-staging-first-block");
                HardKillRestoreMarker(Path.GetDirectoryName(staging)!, snapshot);
            }
#endif
        }
        output.Flush(flushToDisk: true);
    }

    private static void PreserveInterruptedStaging(string staging)
    {
        RequireOrdinaryFile(staging);
        File.Move(staging, staging + ".interrupted-" + Guid.NewGuid().ToString("N"));
    }

#if S9T07_HARD_KILL_TEST
    private static void HardKillRestoreMarker(string operation, string snapshot)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("S9_T07_HARD_KILL_CHECKPOINT"), "SchemaSnapshotRestoreDuringCopy", StringComparison.Ordinal)) return;
        var process = Process.GetCurrentProcess();
        var verified = Verify(snapshot);
        var configured = Environment.GetEnvironmentVariable("S9_T07_HARD_KILL_MARKER");
        var marker = string.IsNullOrWhiteSpace(configured) ? Path.Combine(operation, "hard-kill-marker.json") : Path.GetFullPath(configured);
        DurableFile.Replace(marker, JsonSerializer.Serialize(new { operationId = Path.GetFileName(operation), checkpoint = "SchemaSnapshotRestoreDuringCopy", actor = "updater", pid = process.Id, startedUtc = process.StartTime.ToUniversalTime(), writerPid = process.Id, writerStartedUtc = process.StartTime.ToUniversalTime(), journalPhase = "OldAppRestored", schemaPhase = "SnapshotRestoreStarted", timestampUtc = DateTimeOffset.UtcNow, snapshotSha256 = Hash(snapshot), migrations = verified.Migrations, dataRoot = Directory.GetParent(operation)!.Parent!.FullName }));
        Thread.Sleep(Timeout.Infinite);
    }
#endif

    private static void ValidateQuarantine(string quarantine)
    {
        var allowed = new HashSet<string>(["migrated-app.db", "migrated-app.db-wal", "migrated-app.db-shm", "migrated-app.db-journal"], StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(quarantine))
        {
            if (!allowed.Contains(Path.GetFileName(entry)) || Directory.Exists(entry)) throw new InvalidDataException("升级恢复隔离证据未知或不安全。");
            RequireOrdinaryFile(entry);
        }
    }

    private static void VerifyQuarantinedMain(string quarantine, RestoreRecovery recovery)
    {
        var path = Path.Combine(quarantine, "migrated-app.db");
        if (recovery.MainSha256 is null) { if (File.Exists(path)) throw new InvalidDataException("升级恢复主库隔离证据冲突。"); }
        else { RequireOrdinaryFile(path); if (!string.Equals(Hash(path), recovery.MainSha256, StringComparison.Ordinal)) throw new InvalidDataException("升级恢复主库隔离证据已篡改。"); }
    }

    private static bool IsSnapshot(string database, SchemaUpgradeSnapshot snapshot, VerifiedDatabase verified) => !HasSidecars(database) && string.Equals(Hash(database), snapshot.SnapshotSha256, StringComparison.Ordinal) && SameDatabase(verified, Verify(database));
    private static void VerifyRestored(SnapshotPaths paths, SchemaUpgradeSnapshot snapshot, VerifiedDatabase verified, IReadOnlyList<FileStream>? reservations = null)
    {
        RequireOrdinaryFile(paths.Database);
        if ((reservations is null && HasSidecars(paths.Database)) || (reservations is not null && ExistingSidecars(paths.Database).Any(path => new FileInfo(path).Length != 0)) || !string.Equals(Hash(paths.Database), snapshot.SnapshotSha256, StringComparison.Ordinal) || !SameDatabase(verified, Verify(paths.Database))) throw new InvalidDataException("升级恢复后源数据库验证失败。");
    }

    private static VerifiedDatabase Verify(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new Uri(path).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, ForeignKeys = true, Pooling = false }.ToString()); connection.Open();
        return Verify(connection);
    }

    private static VerifiedDatabase Verify(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA integrity_check;"; if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("升级数据库完整性失败。"); }
        using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA foreign_key_check;"; using var reader = command.ExecuteReader(); if (reader.Read()) throw new InvalidDataException("升级数据库外键失败。"); }
        var migrations = ReadMigrations(connection); if (migrations.Count == 0 || migrations.Any(id => string.IsNullOrWhiteSpace(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[0-9]{14}_[^\\s/\\\\]+\\z")) || migrations.Distinct(StringComparer.Ordinal).Count() != migrations.Count || !migrations.SequenceEqual(migrations.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal)) throw new InvalidDataException("升级迁移历史无效。");
        ValidateCurrentSourceSchema(connection, migrations);
        return new(migrations, Fingerprint(connection));
    }

    private static void ValidateCurrentSourceSchema(SqliteConnection connection, IReadOnlyList<string> migrations)
    {
        // This is the current migration-9 source contract.  A future source migration
        // supplies its own contract with that app version; it is not rejected here merely for being newer.
        if (migrations.Count != 9 || migrations[^1] != "20260901155124_AddPolicyAndBaselineFoundation") return;
        using var pages = connection.CreateCommand(); pages.CommandText = "PRAGMA page_count;"; if (Convert.ToInt64(pages.ExecuteScalar()) <= 0) throw new InvalidDataException("升级源数据库页数无效。");
        var tables = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand()) { command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"; using var reader = command.ExecuteReader(); while (reader.Read()) tables.Add(reader.GetString(0)); }
        if (!Source9Tables.All(tables.Contains) || tables.Any(name => name != "__EFMigrationsLock" && !Source9Tables.Contains(name, StringComparer.Ordinal))) throw new InvalidDataException("升级源数据库结构不符合 migration9 契约。");
        var indexes = new List<string>(); using (var command = connection.CreateCommand()) { command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND sql IS NOT NULL;"; using var reader = command.ExecuteReader(); while (reader.Read()) indexes.Add(reader.GetString(0)); }
        if (!Source9Indexes.All(indexes.Contains) || indexes.Any(name => !Source9Indexes.Contains(name, StringComparer.Ordinal))) throw new InvalidDataException("升级源数据库索引不符合 migration9 契约。");
        foreach (var pair in Source9CoreColumns)
        {
            using var columns = connection.CreateCommand(); columns.CommandText = $"PRAGMA table_info(\"{pair.Key}\");"; using var reader = columns.ExecuteReader(); var found = false;
            while (reader.Read()) if (string.Equals(reader.GetString(1), pair.Value, StringComparison.Ordinal)) { found = true; break; }
            if (!found) throw new InvalidDataException("升级源数据库缺少核心列。");
        }
    }

    private static IReadOnlyList<string> ReadMigrations(SqliteConnection connection) { using var command = connection.CreateCommand(); command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId COLLATE BINARY;"; using var reader = command.ExecuteReader(); var result = new List<string>(); while (reader.Read()) result.Add(reader.GetString(0)); return result; }

    private static string Fingerprint(SqliteConnection connection)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using (var master = connection.CreateCommand()) { master.CommandText = "SELECT type,name,tbl_name,COALESCE(sql,'') FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type COLLATE BINARY,name COLLATE BINARY;"; using var reader = master.ExecuteReader(); while (reader.Read()) for (var i = 0; i < 4; i++) Append(hash, "sqlite-master", Encoding.UTF8.GetBytes(reader.GetString(i))); } using var tables = connection.CreateCommand(); tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE BINARY;"; using var tableReader = tables.ExecuteReader(); var names = new List<string>(); while (tableReader.Read()) names.Add(tableReader.GetString(0));
        foreach (var table in names)
        {
            Append(hash, "table", Encoding.UTF8.GetBytes(table));
            using (var schema = connection.CreateCommand()) { schema.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$name;"; schema.Parameters.AddWithValue("$name", table); Append(hash, "table-sql", Encoding.UTF8.GetBytes(schema.ExecuteScalar()?.ToString() ?? throw new InvalidDataException("升级表结构无效。"))); }
            using (var columns = connection.CreateCommand()) { columns.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");"; using var columnReader = columns.ExecuteReader(); while (columnReader.Read()) Append(hash, "table-column", Encoding.UTF8.GetBytes(string.Join("\u001f", Enumerable.Range(0, columnReader.FieldCount).Select(index => columnReader.IsDBNull(index) ? "<null>" : columnReader.GetValue(index)?.ToString() ?? string.Empty)))); }
            using var rows = connection.CreateCommand(); rows.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY rowid;"; using var rowReader = rows.ExecuteReader(); while (rowReader.Read()) { Append(hash, "row", Array.Empty<byte>()); for (var index = 0; index < rowReader.FieldCount; index++) { Append(hash, "column", Encoding.UTF8.GetBytes(rowReader.GetName(index))); if (rowReader.IsDBNull(index)) Append(hash, "null", Array.Empty<byte>()); else AppendValue(hash, rowReader, index); } }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendValue(IncrementalHash hash, SqliteDataReader reader, int index)
    {
        var type = reader.GetFieldType(index);
        if (type == typeof(byte[])) { Append(hash, "blob", BitConverter.GetBytes(reader.GetBytes(index, 0, null, 0, 0))); var buffer = new byte[81920]; long offset = 0; int read; while ((read = (int)reader.GetBytes(index, offset, buffer, 0, buffer.Length)) > 0) { hash.AppendData(buffer, 0, read); offset += read; } return; }
        if (type == typeof(long)) { Append(hash, "integer", BitConverter.GetBytes(reader.GetInt64(index))); return; }
        if (type == typeof(double)) { Append(hash, "real", BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(reader.GetDouble(index)))); return; }
        Append(hash, "text", Encoding.UTF8.GetBytes(reader.GetString(index)));
    }

    private static void Append(IncrementalHash hash, string tag, byte[] bytes) { var tagBytes = Encoding.ASCII.GetBytes(tag); Span<byte> size = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)tagBytes.Length); hash.AppendData(size); hash.AppendData(tagBytes); BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)bytes.Length); hash.AppendData(size); hash.AppendData(bytes); }
    private static FileStream[] ReserveSidecars(string database) { var leases = new List<FileStream>(); try { foreach (var suffix in Sidecars) leases.Add(new FileStream(database + suffix, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)); return leases.ToArray(); } catch { foreach (var lease in leases) lease.Dispose(); throw new InvalidDataException("升级快照边界出现 SQLite sidecar。"); } }
    private static void ReleaseReservations(string database, IReadOnlyList<FileStream> reservations) { foreach (var lease in reservations) lease.Dispose(); if (ExistingSidecars(database).Any()) throw new InvalidDataException("升级 SQLite sidecar reservation 无法清理。"); }
    private static void RejectUnexpectedSidecars(string database, IReadOnlyList<FileStream> reservations) { if (reservations.Count != Sidecars.Length || ExistingSidecars(database).Any(path => new FileInfo(path).Length != 0)) throw new InvalidDataException("升级快照边界出现 SQLite sidecar。"); }
    private static IEnumerable<string> ExistingSidecars(string database) => Sidecars.Select(suffix => database + suffix).Where(Path.Exists);
    private static bool HasSidecars(string database) => ExistingSidecars(database).Any();
    private static bool SameDatabase(VerifiedDatabase left, VerifiedDatabase right) => left.Migrations.SequenceEqual(right.Migrations, StringComparer.Ordinal) && string.Equals(left.LogicalFingerprint, right.LogicalFingerprint, StringComparison.Ordinal);
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Hash(stream); }
    private static string HashShared(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Hash(stream); }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static string DataRootIdentity(string root) { var directory = new DirectoryInfo(root); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{root.ToUpperInvariant()}|{directory.CreationTimeUtc.Ticks}"))); }
    private static bool HasAlternateDataStream(string path) => path.IndexOf(':', 2) >= 0;
    private static void RequireOrdinaryFile(string path) { if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || HasAlternateDataStream(path)) throw new InvalidDataException("升级路径不安全。"); }
    private static void RequireOrdinaryTree(string path) { for (var current = new DirectoryInfo(path); current is not null; current = current.Parent) if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0 || HasAlternateDataStream(current.FullName)) throw new InvalidDataException("升级路径不安全。"); }
    private static void RejectSidecars(string database) { if (ExistingSidecars(database).Any()) throw new InvalidDataException("升级快照前检测到未知 SQLite sidecar。"); }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private sealed record SnapshotPaths(string DataRoot, string OperationId, string Operation, string Database, string Snapshot, string TemporarySnapshot);
    private sealed record VerifiedDatabase(IReadOnlyList<string> Migrations, string LogicalFingerprint);
    private enum RestoreStage { None, Copying, StagingVerified, Replacing, Replaced }
    private sealed record RestoreState(string OperationId, string SnapshotSha256, RestoreStage Stage, Dictionary<string, string> Quarantined, string? MainSha256);
    private sealed record RestoreRecovery(RestoreStage Stage, Dictionary<string, string> Quarantined, string? MainSha256);
}
