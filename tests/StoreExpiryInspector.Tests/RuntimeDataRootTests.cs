using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class RuntimeDataRootTests
{
    [Fact]
    public void Parse_accepts_only_a_guid_directory_directly_under_temp()
    {
        var temp = Path.Combine(Path.GetTempPath(), "s9-t01-runtime-root-tests");
        var id = Guid.NewGuid();

        var result = RuntimeDataRoot.Parse(["--data-root", Path.Combine(temp, id.ToString()), "--s9-t01-smoke-exit"], temp);

        Assert.True(result.IsIsolated);
        Assert.True(result.IsSmokeRun);
        Assert.Equal(Path.Combine(temp, id.ToString()), result.RootDirectory);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:\\outside")]
    public void Parse_rejects_a_root_outside_temp(string root)
    {
        Assert.Throws<ArgumentException>(() => RuntimeDataRoot.Parse(["--data-root", root], Path.GetTempPath()));
    }

    [Fact]
    public void Parse_rejects_a_smoke_fallback_to_default_data()
    {
        Assert.Throws<ArgumentException>(() => RuntimeDataRoot.Parse(["--s9-t01-smoke-exit"], Path.GetTempPath()));
    }

    [Fact]
    public void Parse_rejects_unknown_arguments()
    {
        Assert.Throws<ArgumentException>(() => RuntimeDataRoot.Parse(["--data-root=C:\\invalid"], Path.GetTempPath()));
    }

    [Fact]
    public void EnsureOrdinaryTree_rejects_an_existing_isolation_root()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var root = Path.Combine(temp, Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() => RuntimeDataRoot.EnsureOrdinaryTree(root, temp));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void EnsureOrdinaryTree_rejects_a_missing_temp_ancestor()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var root = Path.Combine(temp, Guid.NewGuid().ToString());

        Assert.Throws<InvalidOperationException>(() => RuntimeDataRoot.EnsureOrdinaryTree(root, temp));
    }
}
