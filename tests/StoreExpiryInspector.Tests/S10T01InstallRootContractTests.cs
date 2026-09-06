using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S10T01InstallRootContractTests
{
    [Fact]
    public void Installer_keeps_data_fixed_but_allows_a_safe_fresh_program_root()
    {
        var root = FindRoot();
        var installer = File.ReadAllText(Path.Combine(root, "installer", "StoreExpiryInspector.iss"));
        var preparer = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "Application", "Updates", "UpdateInstallationPreparer.cs"));
        var updater = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector.Updater", "Program.cs"));

        Assert.Contains("DefaultLanguageName=chinesesimp", installer);
        Assert.Contains("DisableDirPage=no", installer);
        Assert.Contains("UsePreviousAppDir=yes", installer);
        Assert.Contains("function IsSafeInstallRoot", installer);
        Assert.Contains("Inno Setup: App Path", installer);
        Assert.Contains("Path.Combine(local, ProductId)", preparer);
        Assert.Contains("Path.Combine(installRoot, \"app\")", preparer);
        Assert.DoesNotContain("Path.Combine(local, \"Programs\", ProductId)", preparer);
        Assert.DoesNotContain("journal.InstallRoot, Path.Combine(local, \"Programs\", \"StoreExpiryInspector\")", updater);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
