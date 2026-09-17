using System.Diagnostics;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S24T01SqliteSidecarRaceTests
{
    [Theory]
    [InlineData("app.db-shm", true, "data", true)]
    [InlineData("app.db-wal", true, "data", true)]
    [InlineData("app.db-journal", true, "data", true)]
    [InlineData("app.db", true, "data", false)]
    [InlineData("app.db-shm", true, "updates", false)]
    [InlineData("app.db-shm", false, "data", false)]
    public void Only_disappearing_database_sidecars_are_allowed(string name, bool allow, string folder, bool accepted)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var directory = Path.Combine(root, folder); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, name); File.WriteAllText(file, "sidecar");
        var visited = false;
        UpdateTransaction.OrdinaryTreeEntryProbe = entry => { if (entry == file) { visited = true; File.Delete(file); } };
        try
        {
            if (accepted) UpdateTransaction.ValidateOrdinaryTree(root, allow);
            else Assert.Throws<FileNotFoundException>(() => UpdateTransaction.ValidateOrdinaryTree(root, allow));
            Assert.True(visited);
        }
        finally { UpdateTransaction.OrdinaryTreeEntryProbe = null; Directory.Delete(directory); Directory.Delete(root); }
    }

    [Fact]
    public void Sidecar_replaced_with_a_reparse_directory_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(outside);
        var file = Path.Combine(data, "app.db-shm"); File.WriteAllText(file, "sidecar");
        UpdateTransaction.OrdinaryTreeEntryProbe = entry =>
        {
            if (entry != file) return;
            File.Delete(file);
            using var link = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{file}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            link.WaitForExit(); Assert.Equal(0, link.ExitCode);
        };
        try { Assert.Throws<InvalidDataException>(() => UpdateTransaction.ValidateOrdinaryTree(root, true)); }
        finally
        {
            UpdateTransaction.OrdinaryTreeEntryProbe = null;
            if (Directory.Exists(file)) Directory.Delete(file); else File.Delete(file);
            Directory.Delete(data); Directory.Delete(root); Directory.Delete(outside);
        }
    }
}
