using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T05JournalContractTests
{
    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("case")]
    public async Task ResumeRejectsStrictJournalMutations(string mutation)
    {
        var fixture = CreateValidJournalFixture();
        try
        {
            var json = await File.ReadAllTextAsync(fixture.Journal);
            json = mutation switch { "duplicate" => json.Replace("\"OperationId\":", "\"OperationId\":\"x\",\"OperationId\":"), "unknown" => json.Replace("{", "{\"Unknown\":1,"), _ => json.Replace("\"OperationId\"", "\"operationId\"") };
            await File.WriteAllTextAsync(fixture.Journal, json);
            Assert.Equal(1, await UpdateTransaction.ResumeAsync(fixture.Journal));
            Assert.True(File.Exists(Path.Combine(fixture.Install, "app", "payload.dll")));
        }
        finally { if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); if (Directory.Exists(fixture.Install)) Directory.Delete(fixture.Install, true); }
    }
    private static (string Root, string Install, string Journal, string App, string Staging, TreeFingerprint OldTree) CreateValidJournalFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString();
        var app = Path.Combine(install, "app"); var staging = Path.Combine(install, "app.staging-" + operation); var old = Path.Combine(install, "app.old-" + operation);
        Directory.CreateDirectory(app); Directory.CreateDirectory(staging); Directory.CreateDirectory(Path.Combine(root, "updates", operation)); File.WriteAllText(Path.Combine(app, "payload.dll"), "old"); File.WriteAllText(Path.Combine(staging, "payload.dll"), "candidate");
        var oldTree = TreeFingerprint.Create(app); var candidateTree = TreeFingerprint.Create(staging); var now = DateTimeOffset.UtcNow;
        var journal = new UpdateJournal(operation, "StoreExpiryInspector", install, root, app, staging, old, new string('0', 64), "0.9.9", "1.0.0", 0, now, UpdatePhase.Prepared, oldTree, candidateTree, now, now);
        var path = Path.Combine(root, "updates", operation, "journal.json"); File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(journal, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })); return (root, install, path, app, staging, oldTree);
    }
    [Fact]
    public async Task ResumeRejectsMissingStagingTree()
    {
        var fixture = CreateValidJournalFixture();
        try { Directory.Delete(fixture.Staging, true); Assert.Equal(1, await UpdateTransaction.ResumeAsync(fixture.Journal)); Assert.Equal(fixture.OldTree.Hash, TreeFingerprint.Create(fixture.App).Hash); Assert.Equal(UpdatePhase.FailedNeedsManualRecovery, ReadPhase(fixture.Journal)); Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.Journal)!, "manual-recovery.log"))); }
        finally { if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); if (Directory.Exists(fixture.Install)) Directory.Delete(fixture.Install, true); }
    }
    [Fact]
    public async Task ResumeRejectsTamperedCandidateTree()
    {
        var fixture = CreateValidJournalFixture();
        try { File.WriteAllText(Path.Combine(fixture.Staging, "payload.dll"), "tampered"); Assert.Equal(1, await UpdateTransaction.ResumeAsync(fixture.Journal)); Assert.Equal(fixture.OldTree.Hash, TreeFingerprint.Create(fixture.App).Hash); Assert.Equal(UpdatePhase.FailedNeedsManualRecovery, ReadPhase(fixture.Journal)); }
        finally { if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); if (Directory.Exists(fixture.Install)) Directory.Delete(fixture.Install, true); }
    }
    [Theory]
    [InlineData("OldPath")]
    [InlineData("StagingPath")]
    public async Task ResumeRejectsForgedSiblingOperationPath(string property)
    {
        var fixture = CreateValidJournalFixture(); var sibling = Path.Combine(fixture.Install, "unrelated");
        try
        {
            Directory.CreateDirectory(sibling); File.WriteAllText(Path.Combine(sibling, "keep.txt"), "keep");
            var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
            var journal = System.Text.Json.JsonSerializer.Deserialize<UpdateJournal>(await File.ReadAllTextAsync(fixture.Journal), options)!;
            await File.WriteAllTextAsync(fixture.Journal, System.Text.Json.JsonSerializer.Serialize(property == "OldPath" ? journal with { OldPath = sibling } : journal with { StagingPath = sibling }, options));
            Assert.Equal(1, await UpdateTransaction.ResumeAsync(fixture.Journal));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(sibling, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(fixture.App, "payload.dll")));
        }
        finally { if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); if (Directory.Exists(fixture.Install)) Directory.Delete(fixture.Install, true); }
    }
    [Fact]
    public async Task ResumeRejectsStagingJunction()
    {
        var fixture = CreateValidJournalFixture(); var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var link = Path.Combine(fixture.Staging, "junction");
        try { Directory.CreateDirectory(outside); var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false })!; await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); Assert.Equal(1, await UpdateTransaction.ResumeAsync(fixture.Journal)); Assert.Equal(fixture.OldTree.Hash, TreeFingerprint.Create(fixture.App).Hash); }
        finally { if (Directory.Exists(link)) Directory.Delete(link); if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); if (Directory.Exists(fixture.Install)) Directory.Delete(fixture.Install, true); if (Directory.Exists(outside)) Directory.Delete(outside, true); }
    }
    [Fact]
    public async Task ResumeRejectsRelativeJournalPath() => Assert.Equal(1, await UpdateTransaction.ResumeAsync("..\\escape.json"));

    [Fact]
    public async Task ResumeRejectsWrongProductIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var install = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var operation = Guid.NewGuid().ToString();
        try { Directory.CreateDirectory(Path.Combine(root, "updates", operation)); Directory.CreateDirectory(install); var path = Path.Combine(root, "updates", operation, "journal.json"); var tree = new TreeFingerprint([], "0"); var journal = new UpdateJournal(operation, "wrong", install, root, Path.Combine(install,"app"), Path.Combine(install,"stage"), Path.Combine(install,"old"), new string('0',64), "0.9.9", "1.0.0", 1, DateTimeOffset.UtcNow, UpdatePhase.Prepared, tree, tree, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(journal, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })); Assert.Equal(1, await UpdateTransaction.ResumeAsync(path)); }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); if (Directory.Exists(install)) Directory.Delete(install,true); }
    }

    [Fact]
    public async Task RollbackFailurePreservesTreesAndRecordsManualRecovery()
    {
        var fixture = CreateValidJournalFixture();
        try
        {
            var json = await File.ReadAllTextAsync(fixture.Journal);
            await File.WriteAllTextAsync(fixture.Journal, json.Replace("\"Phase\":\"Prepared\"", "\"Phase\":\"RollbackStarted\""));
            Assert.Equal(1, await UpdateTransaction.ResumeAsync(fixture.Journal));
            Assert.Equal(fixture.OldTree.Hash, TreeFingerprint.Create(fixture.App).Hash);
            Assert.Equal(UpdatePhase.FailedNeedsManualRecovery, ReadPhase(fixture.Journal));
            var log = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(fixture.Journal)!, "manual-recovery.log"));
            Assert.Contains("Win32Exception", log);
        }
        finally { if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); if (Directory.Exists(fixture.Install)) Directory.Delete(fixture.Install, true); }
    }

    private static UpdatePhase ReadPhase(string journal) => System.Text.Json.JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(journal), new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!.Phase;
}
