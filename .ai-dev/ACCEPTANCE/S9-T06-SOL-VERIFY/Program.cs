using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Application.Backups;
using System.Diagnostics;
using System.Reflection;

// Evidence harness only. Never constructs App, RuntimeDataRoot or a production database.
var runRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
var relative = Path.GetRelativePath(Path.GetTempPath(), runRoot);
if (Path.IsPathRooted(relative) || !Guid.TryParse(relative, out _)) throw new InvalidOperationException("Run from TEMP/GUID only");
var results = new List<object>();
var failures = 0;
void Check(string name, bool pass, object? details = null)
{
    results.Add(new { name, pass, details });
    Console.WriteLine(name + ": " + (pass ? "PASS" : "FAIL"));
    if (!pass) failures++;
}
using var publicKey = RSA.Create();
publicKey.ImportParameters(ProductionUpdateTrustAnchor.CreatePublicKey());
var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo()));
Check("embedded-production-public-key", fingerprint == "565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC", fingerprint);
var service = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options with { CacheRoot = Path.Combine(runRoot, Guid.NewGuid().ToString()) });
string SyntheticDatabase(string dataRoot)
{
    var path = Path.GetFullPath(dataRoot);
    if (!Guid.TryParse(Path.GetRelativePath(Path.GetTempPath(), path), out _)) throw new InvalidDataException("TEMP/GUID data root required");
    for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked data path");
    var db = Path.Combine(path, "data", "app.db");
    if (!File.Exists(db) || (File.GetAttributes(db) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ordinary existing synthetic database required");
    return db;
}
StoreDbContext Open(string db, bool readOnly) => new(new DbContextOptionsBuilder<StoreDbContext>().UseSqlite(new SqliteConnectionStringBuilder
{
    DataSource = readOnly ? new Uri(db).AbsoluteUri + "?immutable=1" : db,
    Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false
}.ToString()).Options);
if (args[0] == "local")
{
    var assets = Path.GetFullPath(args[1]);
    var manifest = File.ReadAllBytes(Path.Combine(assets, "update-manifest.json"));
    var signature = File.ReadAllBytes(Path.Combine(assets, "update-manifest.sig"));
    using var document = JsonDocument.Parse(manifest);
    var root = document.RootElement;
    var version = Version.Parse(root.GetProperty("version").GetString()!);
    var package = root.GetProperty("package");
    var packageName = package.GetProperty("fileName").GetString()!;
    if (packageName != $"StoreExpiryInspector-{version.ToString(3)}-win-x64.zip") throw new InvalidDataException();
    var packagePath = Path.Combine(assets, packageName);
    var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath)));
    Check("raw-production-manifest-signature", publicKey.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    Check("package-size-sha", new FileInfo(packagePath).Length == package.GetProperty("bytes").GetInt64() && sha.Equals(package.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), new { bytes = new FileInfo(packagePath).Length, sha });
    var migrations = root.GetProperty("targetMigrations").EnumerateArray().Select(x => x.GetString()!).ToArray();
    var scratch = Path.Combine(runRoot, Guid.NewGuid().ToString());
    Directory.CreateDirectory(scratch);
    var verified = new VerifiedUpdatePackage(scratch, packagePath, version, sha, migrations, manifest, signature,
        new CheckedRelease(version, 1, "v" + version.ToString(3), [packageName, "update-manifest.json", "update-manifest.sig"]));
    var accepted = service.RevalidateForInstall(verified, CancellationToken.None);
    Check("production-client-full-archive-revalidation", accepted.Outcome == UpdatePackageOutcome.Verified, accepted.Outcome.ToString());
    var wrongSignature = signature.ToArray(); wrongSignature[0] ^= 1;
    var wrong = service.RevalidateForInstall(verified with { ManifestSignature = wrongSignature }, CancellationToken.None);
    Check("wrong-production-signature-rejected", wrong.Outcome == UpdatePackageOutcome.InvalidManifestSignature, wrong.Outcome.ToString());
    using var testKey = RSA.Create(3072);
    var testSigned = testKey.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    var rejected = service.RevalidateForInstall(verified with { ManifestSignature = testSigned }, CancellationToken.None);
    Check("test-key-rejected-by-production-client", rejected.Outcome == UpdatePackageOutcome.InvalidManifestSignature, rejected.Outcome.ToString());
}
else if (args[0] is "github" or "prepare-github")
{
    var current = new Version(1, 0, 0);
    var check = await new GitHubReleaseUpdateChecker().CheckAsync(current, CancellationToken.None);
    Check("real-anonymous-github-check", check.Outcome == UpdateCheckOutcome.UpdateAvailable && check.LatestVersion == new Version(1, 0, 1), check.Outcome.ToString());
    if (check.Release is not null)
    {
        var downloaded = await service.PrepareAsync(check.Release, current, null, CancellationToken.None);
        Check("real-github-production-download-verify", downloaded.Outcome == UpdatePackageOutcome.Verified,
            new { outcome = downloaded.Outcome.ToString(), downloaded.Package?.Version, downloaded.Package?.Sha256, downloaded.Package?.PackagePath });
        if (downloaded.Package is not null)
        {
            Check("real-download-consumer-revalidation", service.RevalidateForInstall(downloaded.Package, CancellationToken.None).Outcome == UpdatePackageOutcome.Verified);
            if (args[0] == "prepare-github" && failures == 0)
            {
                _ = SyntheticDatabase(args[2]);
                using var parent = Process.GetCurrentProcess();
                if (parent.MainModule!.FileVersionInfo.ProductVersion!.Split('+')[0] != "1.0.0") throw new InvalidDataException("Test host must identify as 1.0.0");
                var method = typeof(UpdateInstallationPreparer).GetMethod("PrepareForTest", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var prepared = (PreparedUpdateInstallation)method.Invoke(new UpdateInstallationPreparer(service), [downloaded.Package, parent, args[1], args[2], args[3], CancellationToken.None])!;
                File.WriteAllText(Path.Combine(runRoot, "prepared-installation.json"), JsonSerializer.Serialize(new { prepared, testHostParent = true, actualOldWpfGuiParent = false, realGitHubDownload = true }));
                _ = Process.Start(new ProcessStartInfo(prepared.UpdaterPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, ArgumentList = { "--journal", prepared.JournalPath } }) ?? throw new InvalidOperationException("Updater did not start");
                Check("real-package-prepared-and-isolated-updater-started", true, prepared);
            }
        }
    }
}
else if (args[0] == "network-negative")
{
    var release = new CheckedRelease(new Version(1, 0, 1), 1, "v1.0.1", ["update-manifest.json", "update-manifest.sig", "StoreExpiryInspector-1.0.1-win-x64.zip"]);
    foreach (var mode in new[] { "offline", "timeout", "cancel" })
    {
        var cache = Path.Combine(runRoot, Guid.NewGuid().ToString());
        using var handler = new FailureTransport(mode);
        var client = new SignedUpdatePackageDownloader(handler, ProductionUpdateTrustAnchor.Options with { MetadataTimeout = TimeSpan.FromMilliseconds(80), CacheRoot = cache });
        using var cancel = new CancellationTokenSource();
        if (mode == "cancel") cancel.Cancel();
        var result = await client.PrepareAsync(release, new Version(1, 0, 0), null, cancel.Token);
        Check("isolated-network-" + mode, result.Package is null && result.Outcome is UpdatePackageOutcome.NetworkUnavailable or UpdatePackageOutcome.Cancelled,
            new { outcome = result.Outcome.ToString(), updaterStarted = false, noVerifiedPackage = result.Package is null });
    }
}
else if (args[0] == "seed-workbook")
{
    var db = SyntheticDatabase(args[1]);
    var workbook = new ExcelTemplateReader().Read(Path.GetFullPath(args[2]));
    if (workbook.Rows.Count != 3 || workbook.Rows.Any(r => r.ProductCode is null || !r.ProductCode.StartsWith("S9T06-"))) throw new InvalidDataException("Only the three-row synthetic workbook is allowed");
    using (var context = Open(db, false))
    {
        if (context.Products.Any() || context.Imports.Any() || context.Database.GetAppliedMigrations().Count() != 9) throw new InvalidDataException("An empty migration9 synthetic DB is required; no reset allowed");
        var classified = new ExcelFileClassifier().Classify(workbook);
        var plan = new ExcelImportPlanner().Plan(context, classified);
        var guard = new ImportConfirmationGuard();
        var contract = guard.Confirm(guard.BindPreview(Path.GetFullPath(args[2]), workbook, plan)).Contract ?? throw new InvalidDataException("Synthetic workbook did not produce a confirmed plan");
        var imported = new ConfirmedImportLifecycleOrchestrator().Execute(context, new(contract, Path.Combine(args[1], "backups", "pre-import"), DateTime.UtcNow, DateOnly.FromDateTime(DateTime.Now), DateTime.UtcNow));
        Check("actual-workbook-import", imported.Succeeded, imported);
        if (!imported.Succeeded) throw new InvalidDataException("Import failed");
        context.ChangeTracker.Clear();
        var task = context.Tasks.AsNoTracking().First(t => t.Status == "open");
        var items = context.TaskItems.AsNoTracking().Where(i => i.TaskId == task.Id).Select(i => new SaveDraftItemRequest(i.Id, i.BatchId, i.AttentionVersion, 2)).ToArray();
        _ = new InspectionDraftUseCase().SaveDraft(context, new(task.Id, task.ProductId, DateOnly.FromDateTime(DateTime.Now), DateTime.UtcNow, "S9T06合成验收员", DateOnly.FromDateTime(DateTime.Now), items));
        context.ChangeTracker.Clear();
        var submitted = new InspectionSubmissionUseCase().Submit(context, new(task.Id, task.ProductId, DateOnly.FromDateTime(DateTime.Now), DateTime.UtcNow));
        Check("actual-synthetic-inspection-submitted", submitted.Submitted, submitted);
        context.ChangeTracker.Clear();
        context.Settings.Single().ReminderMinuteOfDay = 1439;
        context.SaveChanges();
    }
    var backup = new LocalDatabaseBackupUseCase().Create(db, Path.Combine(args[1], "backups"));
    Check("synthetic-backup-created", backup.Succeeded, backup);
}
else if (args[0] == "read-core")
{
    var db = SyntheticDatabase(args[1]);
    var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(db)));
    using (var context = Open(db, true))
    {
        var query = new InspectionTaskQuery();
        var dashboard = query.Dashboard(context);
        var pending = query.SearchOpenTasks(context, new());
        var history = new InspectionHistoryQuery().List(context);
        var reminders = query.GetReminderCandidates(context);
        var today = new TodayInspectionPlanExportUseCase().Execute(context, new(Path.Combine(runRoot, "today-" + Guid.NewGuid() + ".xlsx")));
        Check("authoritative-Dashboard-Pending-Today-History-Reminder", dashboard.ProductCount == 3 && dashboard.BatchCount == 3 && pending.TotalCount > 0 && history.Count == 1 && reminders.Count > 0 && today.TaskCount > 0,
            new { dashboard, pending = pending.TotalCount, today, history = history.Count, reminders = reminders.Count, workbookBlobs = context.ImportWorkbooks.Count() });
    }
    Check("core-reads-database-bytes-unchanged", before == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(db))));
}
else throw new ArgumentException("Unknown verification mode");
File.WriteAllText(Path.Combine(runRoot, "result-" + args[0] + ".json"), JsonSerializer.Serialize(new { mode = args[0], failures, results }, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

sealed class FailureTransport(string mode) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Headers.Authorization is not null) throw new InvalidOperationException("Unexpected client credential");
        if (mode == "offline") throw new HttpRequestException("Synthetic offline transport");
        await Task.Delay(Timeout.Infinite, token);
        return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
    }
}
