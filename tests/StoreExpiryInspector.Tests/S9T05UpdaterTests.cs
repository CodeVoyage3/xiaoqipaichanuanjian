using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T05UpdaterTests
{
    [Fact]
    public void TreeFingerprintChangesForAnyFileChange()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "a.dll"), "old");
            var before = TreeFingerprint.Create(root);
            File.WriteAllText(Path.Combine(root, "a.dll"), "new");
            Assert.NotEqual(before.Hash, TreeFingerprint.Create(root).Hash);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
