using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S16T02InstallerLaunchTests
{
    [Fact]
    public void Manual_setup_uses_the_standard_postinstall_run_checkbox_and_unattended_modes_skip_it()
    {
        var installer = File.ReadAllText(Path.Combine(FindRoot(), "installer", "StoreExpiryInspector.iss"));

        Assert.Contains("[Run]", installer, StringComparison.Ordinal);
        Assert.Contains("Description: \"安装完成后运行门店效期排查软件\"; Flags: postinstall nowait skipifsilent; Check: ShouldLaunchApplication", installer, StringComparison.Ordinal);
        Assert.Contains("not WizardSilent", installer, StringComparison.Ordinal);
        Assert.Contains("/NOPOSTINSTALLRUN", installer, StringComparison.Ordinal);
        Assert.Contains("/SUPPRESSMSGBOXES", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Online_updater_keeps_its_existing_direct_tree_switch_and_single_normal_launch_owner()
    {
        var root = FindRoot();
        var updater = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector.Updater", "Program.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));

        Assert.DoesNotContain("Setup.exe", updater, StringComparison.Ordinal);
        Assert.Contains("StartNormalApplication(journal", updater, StringComparison.Ordinal);
        Assert.Contains("new Mutex(true, RuntimeDataRoot.MutexName, out _ownsInstanceMutex)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_versions_are_1_0_8()
    {
        var root = FindRoot();
        Assert.Contains("<Version>1.0.8</Version>", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "StoreExpiryInspector.csproj")), StringComparison.Ordinal);
        Assert.Contains("<Version>1.0.8</Version>", File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector.Updater", "StoreExpiryInspector.Updater.csproj")), StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
