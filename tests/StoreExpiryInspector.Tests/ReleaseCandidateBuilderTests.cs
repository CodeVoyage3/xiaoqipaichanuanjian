using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UpdateSafety;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ReleaseCandidateBuilderTests
{
    [Fact]
    public void Version113ContractPreservesVersion112SameSchemaUpgradeIdentity()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "release", "release-contract.json")));
        var release = contract.RootElement.GetProperty("releases").EnumerateArray().Single(item => item.GetProperty("targetVersion").GetString() == "1.1.3");
        Assert.Equal("v1.1.2", release.GetProperty("previousRelease").GetString());
        Assert.Equal(2, release.GetProperty("minimumProtocolVersion").GetInt32());
        var setup = release.GetProperty("setupCompatibility");
        Assert.Equal("SAME_SCHEMA_SLIM", setup.GetProperty("setupMode").GetString());
        Assert.Equal("1.1.2", setup.GetProperty("minimumDirectVersion").GetString());
        Assert.False(setup.GetProperty("crossSchemaAllowed").GetBoolean());
        var source = release.GetProperty("source");
        Assert.Equal("1.1.2", source.GetProperty("minVersion").GetString());
        Assert.Equal("1.1.2", source.GetProperty("maxVersion").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, source.GetProperty("minMigration").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, source.GetProperty("maxMigration").GetString());
        Assert.Equal(10, CurrentSchemaIdentity.Migrations.Count);
        Assert.Equal("ACCEPTED", release.GetProperty("schemaEvidence").GetProperty("status").GetString());
        Assert.Equal(".ai-dev/ACCEPTANCE/S21-T01.md", release.GetProperty("schemaEvidence").GetProperty("reference").GetString());
    }

    [Fact]
    public void Version114ContractUsesVerifiedGenerationAndVersion113Predecessor()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "release", "release-contract.json")));
        var release = contract.RootElement.GetProperty("releases").EnumerateArray().Single(item => item.GetProperty("targetVersion").GetString() == "1.1.4");
        Assert.Equal("v1.1.3", release.GetProperty("previousRelease").GetString());
        Assert.Equal(2, release.GetProperty("minimumProtocolVersion").GetInt32());
        var setup = release.GetProperty("setupCompatibility");
        Assert.Equal("SAME_SCHEMA_SLIM", setup.GetProperty("setupMode").GetString());
        Assert.Equal("1.1.0", setup.GetProperty("minimumDirectVersion").GetString());
        Assert.False(setup.GetProperty("crossSchemaAllowed").GetBoolean());
        var source = release.GetProperty("source");
        Assert.Equal("1.1.0", source.GetProperty("minVersion").GetString());
        Assert.Equal("1.1.3", source.GetProperty("maxVersion").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, source.GetProperty("minMigration").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, source.GetProperty("maxMigration").GetString());
        var generation = contract.RootElement.GetProperty("compatibilityPolicy").GetProperty("generations").EnumerateArray().Single();
        Assert.Equal("G1-m10-protocol2", generation.GetProperty("id").GetString());
        Assert.Equal("VERIFIED", generation.GetProperty("minimumStatus").GetString());
        Assert.Equal(2, generation.GetProperty("minimumProtocolVersion").GetInt32());
        Assert.Equal(10, generation.GetProperty("migrations").GetArrayLength());
    }

    [Fact]
    public void ContractAndReceiptSchemaStayNarrow()
    {
        var root = RepositoryRoot();
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "release", "release-contract.json")));
        var raw = contract.RootElement.GetRawText();
        Assert.DoesNotContain("targetMigrations", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256", raw, StringComparison.OrdinalIgnoreCase);

        var builder = File.ReadAllText(Path.Combine(root, "tools", "release", "Build-ReleaseCandidate.ps1"));
        Assert.DoesNotContain("-p:Version", builder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git push", builder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gh release", builder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Join-Path $source 'tests\\StoreExpiryInspector.Tests", builder, StringComparison.Ordinal);
        Assert.Contains("S20_RELEASE_CANDIDATE_SCHEMA_ASSEMBLY", builder, StringComparison.Ordinal);

        var releases = contract.RootElement.GetProperty("releases").EnumerateArray().ToArray();
        Assert.Equal(releases.Length, releases.Select(item => item.GetProperty("targetVersion").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, contract.RootElement.GetProperty("schemaVersion").GetInt32());
        var historicalSetup = releases.Single(item => item.GetProperty("targetVersion").GetString() == "1.1.0").GetProperty("setupCompatibility");
        Assert.Equal("CROSS_SCHEMA_FULL", historicalSetup.GetProperty("setupMode").GetString());
        Assert.Equal("1.0.9", historicalSetup.GetProperty("minimumDirectVersion").GetString());
        Assert.True(historicalSetup.GetProperty("crossSchemaAllowed").GetBoolean());
        var currentRelease = releases.Single(item => item.GetProperty("targetVersion").GetString() == "1.1.1");
        Assert.Equal("v1.1.0", currentRelease.GetProperty("previousRelease").GetString());
        Assert.Equal(2, currentRelease.GetProperty("minimumProtocolVersion").GetInt32());
        var currentSetup = currentRelease.GetProperty("setupCompatibility");
        Assert.Equal("SAME_SCHEMA_SLIM", currentSetup.GetProperty("setupMode").GetString());
        Assert.Equal("1.1.0", currentSetup.GetProperty("minimumDirectVersion").GetString());
        Assert.False(currentSetup.GetProperty("crossSchemaAllowed").GetBoolean());
        var currentSource = currentRelease.GetProperty("source");
        Assert.Equal("1.1.0", currentSource.GetProperty("minVersion").GetString());
        Assert.Equal("1.1.0", currentSource.GetProperty("maxVersion").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, currentSource.GetProperty("minMigration").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, currentSource.GetProperty("maxMigration").GetString());
        var nextRelease = releases.Single(item => item.GetProperty("targetVersion").GetString() == "1.1.2");
        Assert.Equal("v1.1.1", nextRelease.GetProperty("previousRelease").GetString());
        Assert.Equal(2, nextRelease.GetProperty("minimumProtocolVersion").GetInt32());
        var nextSetup = nextRelease.GetProperty("setupCompatibility");
        Assert.Equal("SAME_SCHEMA_SLIM", nextSetup.GetProperty("setupMode").GetString());
        Assert.Equal("1.1.1", nextSetup.GetProperty("minimumDirectVersion").GetString());
        Assert.False(nextSetup.GetProperty("crossSchemaAllowed").GetBoolean());
        var nextSource = nextRelease.GetProperty("source");
        Assert.Equal("1.1.1", nextSource.GetProperty("minVersion").GetString());
        Assert.Equal("1.1.1", nextSource.GetProperty("maxVersion").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, nextSource.GetProperty("minMigration").GetString());
        Assert.Equal(CurrentSchemaIdentity.LastMigration, nextSource.GetProperty("maxMigration").GetString());

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "release", "release-receipt.schema.json")));
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("builderSha", required);
        Assert.Contains("builderSourceClean", required);
        Assert.Contains("mode", required);
        Assert.Contains("publishAuthorized", required);
        Assert.Contains("setupMode", required);
        Assert.Contains("minimumDirectSetupVersion", required);
        Assert.Contains("embeddedUpdateAssets", required);
        Assert.Contains("zipBytes", required);
        Assert.Contains("zipSha256", required);
        Assert.Contains("advanceCompVersion", required);
        Assert.Contains("zipHeaderNormalization", required);
        Assert.Contains("giteeSizeRemainingBytes", required);
        Assert.Contains("giteeSizeWarning", required);
        Assert.Contains("productionExtractAuditedArchive", required);
        Assert.Equal(5, schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        var changeImpactRequired = schema.RootElement.GetProperty("properties").GetProperty("changeImpact")
            .GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("baseProductSource", changeImpactRequired);
        Assert.Contains("changedFiles", changeImpactRequired);
        Assert.Contains("requiredEvidence", changeImpactRequired);
        Assert.Contains("reusableEvidence", changeImpactRequired);

        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "release", "change-impact-policy.json")));
        var categories = policy.RootElement.GetProperty("categories").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("TEST_ONLY", categories);
        Assert.Contains("UNKNOWN", categories);
        Assert.DoesNotContain("ROLLBACK_EVIDENCE_REUSABLE_PASS", policy.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SetupCompatibilityProbeRequiresExplicitModeAndValidatesSchemaImpact()
    {
        static object Contract(string setupMode, string minimumDirectVersion, bool crossSchemaAllowed, bool accepted = true) => new
        {
            setupCompatibility = new { setupMode, minimumDirectVersion, crossSchemaAllowed },
            schemaEvidence = accepted ? new { status = "ACCEPTED", reference = ".ai-dev/GOVERNANCE/SCHEMA_CHANGE_RELEASE_GATE.md" } : null
        };

        using var slim = RunSetupModeProbe(new { targetVersion = "1.1.1", schemaChanged = false, contract = Contract("SAME_SCHEMA_SLIM", "1.1.0", false) });
        Assert.Equal("PASS", slim.RootElement.GetProperty("status").GetString());
        var slimSetup = slim.RootElement.GetProperty("setupCompatibility");
        Assert.Equal("SAME_SCHEMA_SLIM", slimSetup.GetProperty("setupMode").GetString());
        Assert.Equal("1.1.0", slimSetup.GetProperty("minimumDirectSetupVersion").GetString());
        Assert.False(slimSetup.GetProperty("embeddedUpdateAssets").GetBoolean());

        using var historicalFull = RunSetupModeProbe(new { targetVersion = "1.1.0", schemaChanged = true, contract = Contract("CROSS_SCHEMA_FULL", "1.0.9", true) });
        Assert.Equal("PASS", historicalFull.RootElement.GetProperty("status").GetString());
        Assert.True(historicalFull.RootElement.GetProperty("setupCompatibility").GetProperty("embeddedUpdateAssets").GetBoolean());

        using var slimSchemaChange = RunSetupModeProbe(new { targetVersion = "1.1.1", schemaChanged = true, contract = Contract("SAME_SCHEMA_SLIM", "1.1.0", false) });
        AssertSetupCompatibilityFailed(slimSchemaChange, "schemaChanged");
        using var fullWithoutSchemaChange = RunSetupModeProbe(new { targetVersion = "1.1.1", schemaChanged = false, contract = Contract("CROSS_SCHEMA_FULL", "1.1.0", true) });
        AssertSetupCompatibilityFailed(fullWithoutSchemaChange, "schemaChanged");
        using var unapprovedFull = RunSetupModeProbe(new { targetVersion = "1.1.1", schemaChanged = true, contract = Contract("CROSS_SCHEMA_FULL", "1.1.0", true, accepted: false) });
        AssertSetupCompatibilityFailed(unapprovedFull, "accepted explicit");
    }

    [Fact]
    public void ChangeImpactProbeClassifiesOwnedPathsAndFailsClosedOnUnknownOrSpecialObjects()
    {
        var regular = new { status = "M", oldMode = "100644", newMode = "100644" };
        object Entry(string path) => new { regular.status, regular.oldMode, regular.newMode, path };
        var input = new
        {
            previousRelease = "v0.0.0",
            baseProductSource = new string('a', 40),
            candidateSha = new string('b', 40),
            entries = new object[]
            {
                Entry(".ai-dev/PROJECT_STATUS.md"),
                Entry("tests/StoreExpiryInspector.Tests/NewFocusedTests.cs"),
                Entry("tests/S24T01-RunInstallerCandidate.ps1"),
                Entry("tests/UnownedHarness.ps1"),
                Entry("src/StoreExpiryInspector/UI/MainWindow.xaml.cs"),
                Entry("src/StoreExpiryInspector/Application/Tasks/ProductTaskQuery.cs"),
                Entry("src/StoreExpiryInspector/Infrastructure/Excel/ExcelTemplateReader.cs"),
                Entry("src/StoreExpiryInspector/Migrations/Example.cs"),
                Entry("src/StoreExpiryInspector/Application/Backups/DatabaseRestoreUseCase.cs"),
                Entry("src/StoreExpiryInspector/Application/Updates/SignedUpdatePackageDownloader.cs"),
                Entry("src/StoreExpiryInspector.Updater/Program.cs"),
                Entry("installer/StoreExpiryInspector.iss"),
                Entry("tools/release/Build-ReleaseCandidate.ps1"),
                Entry("tools/release/release-contract.json"),
                Entry("tests/StoreExpiryInspector.Tests/ReleaseCandidateBuilderTests.cs"),
                Entry("src/StoreExpiryInspector.UpdateSafety/CurrentSchemaIdentity.cs"),
                Entry("src/StoreExpiryInspector/App.xaml.cs"),
                Entry("new-component/Unknown.cs"),
                new { status = "R100", oldMode = "100644", newMode = "100644", oldPath = "src/StoreExpiryInspector/UI/Old.xaml", newPath = "src/StoreExpiryInspector/UI/New.xaml" },
                new { status = "C100", oldMode = "100644", newMode = "100644", oldPath = "tests/StoreExpiryInspector.Tests/OldTests.cs", newPath = "tests/StoreExpiryInspector.Tests/NewTests.cs" },
                new { status = "A", oldMode = "000000", newMode = "120000", path = "docs/link" }
            }
        };

        using var result = RunChangeImpactProbe(input);
        Assert.Equal("FAILED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("CHANGE_IMPACT", result.RootElement.GetProperty("failedGate").GetString());
        var impact = result.RootElement.GetProperty("changeImpact");
        Assert.Equal(21, impact.GetProperty("changedFileCount").GetInt32());
        AssertCategories(FindChangedFile(impact, ".ai-dev/PROJECT_STATUS.md"), "GOVERNANCE_ONLY");
        AssertCategories(FindChangedFile(impact, "tests/StoreExpiryInspector.Tests/NewFocusedTests.cs"), "TEST_ONLY");
        AssertCategories(FindChangedFile(impact, "tests/S24T01-RunInstallerCandidate.ps1"), "TEST_ONLY");
        AssertCategories(FindChangedFile(impact, "tests/UnownedHarness.ps1"), "UNKNOWN");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/UI/MainWindow.xaml.cs"), "UI");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/Application/Tasks/ProductTaskQuery.cs"), "BUSINESS_LOGIC");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/Infrastructure/Excel/ExcelTemplateReader.cs"), "EXCEL_IMPORT_EXPORT");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/Migrations/Example.cs"), "DATABASE_SCHEMA");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/Application/Backups/DatabaseRestoreUseCase.cs"), "BACKUP_RESTORE", "BUSINESS_LOGIC");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/Application/Updates/SignedUpdatePackageDownloader.cs"), "UPDATE_PACKAGE_SECURITY");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector.Updater/Program.cs"), "UPDATER_TRANSACTION");
        AssertCategories(FindChangedFile(impact, "installer/StoreExpiryInspector.iss"), "INSTALLER");
        AssertCategories(FindChangedFile(impact, "tools/release/Build-ReleaseCandidate.ps1"), "RELEASE_TOOLING");
        AssertCategories(FindChangedFile(impact, "tools/release/release-contract.json"), "RELEASE_TOOLING", "UPDATE_PACKAGE_SECURITY");
        AssertCategories(FindChangedFile(impact, "tests/StoreExpiryInspector.Tests/ReleaseCandidateBuilderTests.cs"), "RELEASE_TOOLING", "TEST_ONLY");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector.UpdateSafety/CurrentSchemaIdentity.cs"), "DATABASE_SCHEMA", "UPDATER_TRANSACTION");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/App.xaml.cs"), "BUSINESS_LOGIC", "UI", "UPDATE_PACKAGE_SECURITY", "UPDATER_TRANSACTION");
        AssertCategories(FindChangedFile(impact, "new-component/Unknown.cs"), "UNKNOWN");
        AssertCategories(FindChangedFile(impact, "src/StoreExpiryInspector/UI/New.xaml"), "UI");
        AssertCategories(FindChangedFile(impact, "tests/StoreExpiryInspector.Tests/NewTests.cs"), "TEST_ONLY");
        AssertCategories(FindChangedFile(impact, "docs/link"), "GOVERNANCE_ONLY", "UNKNOWN");
        var unknown = Strings(impact.GetProperty("unknownFiles"));
        Assert.Equal(new[] { "docs/link", "new-component/Unknown.cs", "tests/UnownedHarness.ps1" }, unknown);
        AssertSortedDistinctAndDisjoint(impact);

        using var governanceOnly = RunChangeImpactProbe(new
        {
            previousRelease = "v0.0.0",
            baseProductSource = new string('a', 40),
            candidateSha = new string('b', 40),
            entries = new[] { Entry(".ai-dev/STAGES/STAGE-20.md") }
        });
        var governanceImpact = governanceOnly.RootElement.GetProperty("changeImpact");
        Assert.Empty(Strings(governanceImpact.GetProperty("requiredEvidence")));
        Assert.Equal(9, Strings(governanceImpact.GetProperty("reusableEvidence")).Length);
        AssertSortedDistinctAndDisjoint(governanceImpact);
    }

    [Fact]
    public void ChangeImpactProbeRequiresAnnotatedAncestorTagAndClassifiesHistoricalCandidate()
    {
        var root = RepositoryRoot();
        using var actual = RunChangeImpactProbe(new
        {
            repository = root,
            previousRelease = "v1.0.9",
            candidateSha = "18230c3e6013a098874426575e6a14c202fa7f7c"
        });
        Assert.True(actual.RootElement.GetProperty("status").GetString() == "PASS",
            actual.RootElement.GetRawText());
        var impact = actual.RootElement.GetProperty("changeImpact");
        Assert.Equal("9bf4ee71f1579097d816032d867041c0519cf789", impact.GetProperty("baseProductSource").GetString());
        Assert.Equal(61, impact.GetProperty("changedFileCount").GetInt32());
        Assert.Empty(Strings(impact.GetProperty("unknownFiles")));
        Assert.Equal(new[]
        {
            "BACKUP_RESTORE", "BUSINESS_LOGIC", "DATABASE_SCHEMA", "EXCEL_IMPORT_EXPORT", "GOVERNANCE_ONLY", "INSTALLER",
            "RELEASE_TOOLING", "TEST_ONLY", "UI", "UPDATER_TRANSACTION", "UPDATE_PACKAGE_SECURITY"
        }, Strings(impact.GetProperty("categories")));

        var repository = CreateProbeRepository();
        try
        {
            var commits = Git(repository, "rev-list", "--reverse", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            using var missing = RunRepositoryProbe(repository, "missing", commits[1]);
            AssertFailedChangeImpact(missing);

            Git(repository, "tag", "v-lightweight", commits[0]);
            using var lightweight = RunRepositoryProbe(repository, "v-lightweight", commits[1]);
            AssertFailedChangeImpact(lightweight, "annotated tag");

            Git(repository, "tag", "-a", "v-not-ancestor", "-m", "not ancestor", commits[1]);
            using var nonAncestor = RunRepositoryProbe(repository, "v-not-ancestor", commits[0]);
            AssertFailedChangeImpact(nonAncestor, "not an ancestor");
        }
        finally
        {
            DeleteTree(repository);
        }
    }

    [Fact]
    public void CandidateIdentityProbeReadsCandidateAssembly()
    {
        var output = Environment.GetEnvironmentVariable("S20_RELEASE_IDENTITY_PROBE");
        if (string.IsNullOrWhiteSpace(output)) return;

        var assemblyPath = Path.GetFullPath(Environment.GetEnvironmentVariable("S20_RELEASE_CANDIDATE_SCHEMA_ASSEMBLY")!);
        Assert.True(File.Exists(assemblyPath));
        var loadContext = new AssemblyLoadContext("S20 candidate schema probe", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            Assert.Equal(assemblyPath, assembly.Location, ignoreCase: true);
            var type = assembly.GetType("StoreExpiryInspector.UpdateSafety.CurrentSchemaIdentity", throwOnError: true)!;
            var migrations = ((IEnumerable<string>)type.GetProperty("Migrations")!.GetValue(null)!).ToArray();
            var count = (int)type.GetProperty("Count")!.GetValue(null)!;
            var latestMigration = (string)type.GetProperty("LastMigration")!.GetValue(null)!;
            Assert.Equal(migrations.Length, count);
            Assert.Equal(migrations[^1], latestMigration);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                candidateAssemblyPath = assembly.Location,
                candidateAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant(),
                currentSchemaIdentity = migrations,
                migrationCount = count,
                latestMigration
            }));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ProductionEfMigrationsMatchCurrentSchemaIdentity()
    {
        using var context = new StoreDbContextFactory().CreateDbContext([]);
        var ef = context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.True(ef.SequenceEqual(CurrentSchemaIdentity.Migrations, StringComparer.Ordinal));
    }

    [Fact]
    public void ReleaseZipProbePreservesPayloadAndChangesOnlyFlagHeaders()
    {
        var fixture = CreateZipFixture();
        try
        {
            var before = File.ReadAllBytes(fixture.Zip);
            using var result = RunZipProbe(fixture, normalize: true);
            Assert.Equal("PASS", result.RootElement.GetProperty("status").GetString());
            var details = result.RootElement.GetProperty("result");
            var changedHeaders = details.GetProperty("ChangedHeaderCount").GetInt32();
            Assert.True(changedHeaders > 0);
            var after = File.ReadAllBytes(fixture.Zip);
            Assert.Equal(before.Length, after.Length);
            Assert.Equal(changedHeaders, before.Zip(after).Count(pair => pair.First != pair.Second));
            using var secondPass = RunZipProbe(fixture, normalize: true);
            Assert.Equal(0, secondPass.RootElement.GetProperty("result").GetProperty("ChangedHeaderCount").GetInt32());
        }
        finally { DeleteTree(fixture.Root); }
    }

    [Fact]
    public void ReleaseZipProbeRejectsHeaderPayloadMethodAndSizeMismatches()
    {
        var header = CreateZipFixture();
        try
        {
            var bytes = File.ReadAllBytes(header.Zip); bytes[6] ^= 0x02; File.WriteAllBytes(header.Zip, bytes);
            AssertZipFailed(RunZipProbe(header, normalize: true), "mismatch");
        }
        finally { DeleteTree(header.Root); }

        var payload = CreateZipFixture();
        try
        {
            File.WriteAllText(Path.Combine(payload.Payload, "extra.dll"), "extra");
            AssertZipFailed(RunZipProbe(payload, normalize: true), "paths");
        }
        finally { DeleteTree(payload.Root); }

        var hash = CreateZipFixture();
        try
        {
            File.WriteAllText(Path.Combine(hash.Payload, "StoreExpiryInspector.exe"), "changed");
            AssertZipFailed(RunZipProbe(hash, normalize: true), "mismatch");
        }
        finally { DeleteTree(hash.Root); }

        var method = CreateZipFixture();
        try
        {
            var bytes = File.ReadAllBytes(method.Zip); bytes[8] = 0; bytes[9] = 0;
            var central = FindSignature(bytes, 0x02014b50); bytes[central + 10] = 0; bytes[central + 11] = 0; File.WriteAllBytes(method.Zip, bytes);
            AssertZipFailed(RunZipProbe(method, normalize: true), "metadata");
        }
        finally { DeleteTree(method.Root); }

        var size = CreateZipFixture();
        try { AssertZipFailed(RunZipProbe(size, normalize: true, gateBytes: 100000000), "smaller"); }
        finally { DeleteTree(size.Root); }
    }

    [Fact]
    public void ReleaseZipSizeGateWarnsAt97000000AndBlocksAt100000000Bytes()
    {
        var fixture = CreateZipFixture();
        try
        {
            using var below = RunZipProbe(fixture, normalize: true, gateBytes: 96999999);
            Assert.False(below.RootElement.GetProperty("sizeGate").GetProperty("warning").GetBoolean());
            Assert.Equal(3000001, below.RootElement.GetProperty("sizeGate").GetProperty("remainingBytes").GetInt64());
            using var warning = RunZipProbe(fixture, normalize: true, gateBytes: 97000000);
            Assert.True(warning.RootElement.GetProperty("sizeGate").GetProperty("warning").GetBoolean());
            Assert.Equal(3000000, warning.RootElement.GetProperty("sizeGate").GetProperty("remainingBytes").GetInt64());
            using var lastByte = RunZipProbe(fixture, normalize: true, gateBytes: 99999999);
            Assert.True(lastByte.RootElement.GetProperty("sizeGate").GetProperty("warning").GetBoolean());
            Assert.Equal(1, lastByte.RootElement.GetProperty("sizeGate").GetProperty("remainingBytes").GetInt64());
            AssertZipFailed(RunZipProbe(fixture, normalize: true, gateBytes: 100000000), "smaller");
        }
        finally { DeleteTree(fixture.Root); }
    }

    [Fact]
    public void ReleaseZipProbeFailsClosedWhenAdvanceCompIsMissingOrWrongVersion()
    {
        var fixture = CreateZipFixture();
        try
        {
            AssertZipFailed(RunZipProbe(fixture, normalize: true, advanceComp: Path.Combine(fixture.Root, "missing.exe")), "unavailable");
            AssertZipFailed(RunZipProbe(fixture, normalize: true, advanceComp: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")), "v2.6");
            var failing = Path.Combine(fixture.Root, "failing-advzip.cmd");
            File.WriteAllLines(failing, ["@echo off", "if \"%~1\"==\"--version\" (echo advancecomp v2.6 test& exit /b 0)", "echo simulated failure 1>&2", "exit /b 7"]);
            AssertZipFailed(RunZipProbe(fixture, normalize: true, advanceComp: failing), "exit 7");
            var unexpected = Path.Combine(fixture.Root, "unexpected-advzip.cmd");
            File.WriteAllLines(unexpected, ["@echo off", "if \"%~1\"==\"--version\" (echo advancecomp v2.6 test& exit /b 0)", "echo unexpected", "exit /b 0"]);
            AssertZipFailed(RunZipProbe(fixture, normalize: true, advanceComp: unexpected), "unexpected output");
        }
        finally { DeleteTree(fixture.Root); }
    }

    [Fact]
    public void ProductionTrustAnchorRevalidatesReleaseCandidate()
    {
        var assets = Environment.GetEnvironmentVariable("S20_RELEASE_ASSET_DIR");
        if (string.IsNullOrWhiteSpace(assets)) return;

        var versionText = Environment.GetEnvironmentVariable("S20_RELEASE_VERSION")!;
        var resultPath = Environment.GetEnvironmentVariable("S20_RELEASE_REVALIDATION_RESULT")!;
        var packagePath = Path.Combine(assets, $"StoreExpiryInspector-{versionText}-win-x64.zip");
        var manifest = File.ReadAllBytes(Path.Combine(assets, "update-manifest.json"));
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var source = root.GetProperty("source");
        var version = Version.Parse(root.GetProperty("version").GetString()!);
        var migrations = root.GetProperty("targetMigrations").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var package = new VerifiedUpdatePackage(assets, packagePath, version,
            root.GetProperty("package").GetProperty("sha256").GetString()!, migrations,
            manifest, File.ReadAllBytes(Path.Combine(assets, "update-manifest.sig")),
            new CheckedRelease(version, 1, root.GetProperty("releaseTag").GetString()!,
                ["update-manifest.json", "update-manifest.sig", Path.GetFileName(packagePath)]),
            root.GetProperty("minimumProtocolVersion").GetInt32(),
            Version.Parse(source.GetProperty("minVersion").GetString()!), Version.Parse(source.GetProperty("maxVersion").GetString()!),
            source.GetProperty("minMigration").GetString(), source.GetProperty("maxMigration").GetString());
        var result = new SignedUpdatePackageDownloader(options: ProductionUpdateTrustAnchor.Options).RevalidateForInstall(package, CancellationToken.None);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(new { outcome = result.Outcome.ToString(), result.Message }));
        Assert.Equal(UpdatePackageOutcome.Verified, result.Outcome);
    }

    [Fact]
    public void ProductionUpdaterExtractsReleaseCandidateWithoutChangingPayload()
    {
        var zip = Environment.GetEnvironmentVariable("S26_RELEASE_ZIP_PATH");
        var payload = Environment.GetEnvironmentVariable("S26_RELEASE_PUBLISH_DIR");
        if (string.IsNullOrWhiteSpace(zip) || string.IsNullOrWhiteSpace(payload)) return;
        var staging = Path.Combine(Path.GetTempPath(), "S26Extract", Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = File.OpenRead(zip);
            var method = typeof(UpdateInstallationPreparer).GetMethod("ExtractAuditedArchive", BindingFlags.NonPublic | BindingFlags.Static)!;
            method.Invoke(null, [stream, staging, CancellationToken.None]);
            var expected = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).Where(path => IncludePackageFile(Path.GetRelativePath(payload, path).Replace('\\', '/')))
                .ToDictionary(path => Path.GetRelativePath(payload, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
            var actual = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(staging, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(expected.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase), actual.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase));
        }
        finally { DeleteTree(staging); }
    }

    [Fact]
    public void ProductionUpdaterReadsAndAuditsReleaseZip()
    {
        var zip = Environment.GetEnvironmentVariable("S26_RELEASE_ZIP_PATH");
        var manifestPath = Environment.GetEnvironmentVariable("S26_RELEASE_MANIFEST_PATH");
        if (string.IsNullOrWhiteSpace(zip) || string.IsNullOrWhiteSpace(manifestPath)) return;
        var type = typeof(SignedUpdatePackageDownloader); var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var directoryArguments = new object?[] { zip, null };
        Assert.True((bool)type.GetMethod("TryReadZipDirectory", flags)!.Invoke(null, directoryArguments)!);
        using (var archive = ZipFile.OpenRead(zip)) Assert.Equal(archive.Entries.Count, ((System.Collections.IDictionary)directoryArguments[1]!).Count);

        var manifestArguments = new object?[] { File.ReadAllBytes(manifestPath), null };
        Assert.True((bool)type.GetMethod("TryParseManifest", flags)!.Invoke(null, manifestArguments)!);
        var scratch = Path.Combine(Path.GetTempPath(), "S26Audit", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            var outcome = (UpdatePackageOutcome)type.GetMethod("AuditArchive", flags)!.Invoke(null, [zip, scratch, manifestArguments[1], CancellationToken.None])!;
            Assert.Equal(UpdatePackageOutcome.Verified, outcome);
        }
        finally { DeleteTree(scratch); }
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "StoreExpiryInspector.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("repository root not found");
    }

    private static JsonDocument RunRepositoryProbe(string repository, string previousRelease, string candidateSha) =>
        RunChangeImpactProbe(new { repository, previousRelease, candidateSha });

    private static JsonDocument RunChangeImpactProbe(object input) =>
        RunBuilderProbe(input, "S20T02", "S20_RELEASE_CHANGE_IMPACT_INPUT", "S20_RELEASE_CHANGE_IMPACT_PROBE");

    private static JsonDocument RunSetupModeProbe(object input) =>
        RunBuilderProbe(input, "S21T01", "S21_RELEASE_SETUP_MODE_INPUT", "S21_RELEASE_SETUP_MODE_PROBE");

    private static JsonDocument RunZipProbe((string Root, string Payload, string Zip) fixture, bool normalize, long? gateBytes = null, string? advanceComp = null) =>
        RunBuilderProbe(new { zipPath = fixture.Zip, payloadRoot = fixture.Payload, normalize, gateBytes, advanceComp }, "S26ZIP", "S26_RELEASE_ZIP_INPUT", "S26_RELEASE_ZIP_PROBE");

    private static JsonDocument RunBuilderProbe(object input, string temporaryName, string inputVariable, string outputVariable)
    {
        var temporary = Path.Combine(Path.GetTempPath(), temporaryName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var inputPath = Path.Combine(temporary, "input.json");
            var outputPath = Path.Combine(temporary, "output.json");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(input));
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepositoryRoot()
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(Path.Combine(RepositoryRoot(), "tools", "release", "Build-ReleaseCandidate.ps1"));
            start.ArgumentList.Add("-Version");
            start.ArgumentList.Add("0.0.0");
            start.ArgumentList.Add("-CandidateSha");
            start.ArgumentList.Add(new string('0', 40));
            start.Environment[inputVariable] = inputPath;
            start.Environment[outputVariable] = outputPath;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"probe exit={process.ExitCode}\nstdout={stdout.Result}\nstderr={stderr.Result}");
            Assert.True(File.Exists(outputPath), $"probe output missing\nstdout={stdout.Result}\nstderr={stderr.Result}");
            return JsonDocument.Parse(File.ReadAllText(outputPath));
        }
        finally
        {
            DeleteTree(temporary);
        }
    }

    private static JsonElement FindChangedFile(JsonElement impact, string path) => impact.GetProperty("changedFiles").EnumerateArray()
        .Single(item => (item.TryGetProperty("path", out var direct) ? direct : item.GetProperty("newPath")).GetString() == path);

    private static void AssertCategories(JsonElement changedFile, params string[] expected) =>
        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), Strings(changedFile.GetProperty("categories")));

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static void AssertSortedDistinctAndDisjoint(JsonElement impact)
    {
        var required = Strings(impact.GetProperty("requiredEvidence"));
        var reusable = Strings(impact.GetProperty("reusableEvidence"));
        Assert.Equal(required.OrderBy(value => value, StringComparer.Ordinal).Distinct(StringComparer.Ordinal), required);
        Assert.Equal(reusable.OrderBy(value => value, StringComparer.Ordinal).Distinct(StringComparer.Ordinal), reusable);
        Assert.Empty(required.Intersect(reusable, StringComparer.Ordinal));
        Assert.DoesNotContain(required.Concat(reusable), value => value.Contains("ROLLBACK", StringComparison.Ordinal));
        Assert.DoesNotContain(required.Concat(reusable), value => value.EndsWith("_PASS", StringComparison.Ordinal));
    }

    private static void AssertFailedChangeImpact(JsonDocument result, string? reasonFragment = null)
    {
        Assert.Equal("FAILED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("CHANGE_IMPACT", result.RootElement.GetProperty("failedGate").GetString());
        var reason = result.RootElement.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason));
        if (reasonFragment is not null) Assert.Contains(reasonFragment, reason!, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSetupCompatibilityFailed(JsonDocument result, string reasonFragment)
    {
        Assert.Equal("FAILED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("SETUP_COMPATIBILITY", result.RootElement.GetProperty("failedGate").GetString());
        Assert.Contains(reasonFragment, result.RootElement.GetProperty("failureReason").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Root, string Payload, string Zip) CreateZipFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "S26ZIPFixture", Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "payload"); var zip = Path.Combine(root, "candidate.zip");
        Directory.CreateDirectory(Path.Combine(payload, "Updater"));
        File.WriteAllText(Path.Combine(payload, "StoreExpiryInspector.exe"), new string('a', 4096));
        File.WriteAllText(Path.Combine(payload, "StoreExpiryInspector.dll"), new string('b', 2048));
        File.WriteAllText(Path.Combine(payload, "Updater", "StoreExpiryInspector.Updater.exe"), new string('c', 4096));
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var path in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(payload, path).Replace('\\', '/');
            ZipFileExtensions.CreateEntryFromFile(archive, path, relative, CompressionLevel.SmallestSize);
        }
        return (root, payload, zip);
    }

    private static int FindSignature(byte[] bytes, uint signature)
    {
        for (var index = 0; index <= bytes.Length - 4; index++) if (BitConverter.ToUInt32(bytes, index) == signature) return index;
        throw new InvalidDataException("ZIP signature not found");
    }

    private static void AssertZipFailed(JsonDocument result, string reasonFragment)
    {
        Assert.Equal("FAILED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("ZIP", result.RootElement.GetProperty("failedGate").GetString());
        Assert.Contains(reasonFragment, result.RootElement.GetProperty("failureReason").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IncludePackageFile(string relative) =>
        !relative.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
        (!relative.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("runtimes/win-x64/", StringComparison.OrdinalIgnoreCase)) &&
        (relative is "StoreExpiryInspector.exe" or "createdump.exe" or "StoreExpiryInspector.dll" or "Updater/StoreExpiryInspector.Updater.exe" or "Updater/createdump.exe" || relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase));

    private static string CreateProbeRepository()
    {
        var repository = Path.Combine(Path.GetTempPath(), "S20T02Git", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        Git(repository, "init", "-q");
        Git(repository, "config", "user.email", "s20-t02@example.invalid");
        Git(repository, "config", "user.name", "S20 T02 Probe");
        File.WriteAllText(Path.Combine(repository, "first.txt"), "first");
        Git(repository, "add", "first.txt");
        Git(repository, "commit", "-q", "-m", "first");
        File.WriteAllText(Path.Combine(repository, "second.txt"), "second");
        Git(repository, "add", "second.txt");
        Git(repository, "commit", "-q", "-m", "second");
        return repository;
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private static string Git(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repository
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"safe.directory={repository}");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} exit={process.ExitCode}\n{stderr.Result}");
        return stdout.Result.Trim();
    }
}
