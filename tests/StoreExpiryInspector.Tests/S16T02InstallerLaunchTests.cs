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
    public void Installer_releases_its_install_mutex_only_when_postinstall_is_about_to_launch_the_app()
    {
        var installer = File.ReadAllText(Path.Combine(FindRoot(), "installer", "StoreExpiryInspector.iss"));

        var prepare = installer.IndexOf("function PrepareToInstall", StringComparison.Ordinal);
        var create = installer.IndexOf("InstallMutex := CreateInstallMutex", StringComparison.Ordinal);
        var release = installer.IndexOf("procedure ReleaseInstallMutexForApplicationLaunch", StringComparison.Ordinal);
        var check = installer.IndexOf("function ShouldLaunchApplication", StringComparison.Ordinal);
        var launch = installer.IndexOf("Check: ShouldLaunchApplication", StringComparison.Ordinal);

        Assert.True(prepare >= 0 && create > prepare);
        Assert.True(release >= 0 && check > release && launch >= 0);
        Assert.Contains("if InstallMutex <> 0", installer[release..check], StringComparison.Ordinal);
        Assert.Contains("CloseHandle(InstallMutex)", installer[release..check], StringComparison.Ordinal);
        Assert.Contains("InstallMutex := 0", installer[release..check], StringComparison.Ordinal);
        Assert.Contains("if Result then ReleaseInstallMutexForApplicationLaunch", installer[check..], StringComparison.Ordinal);
    }

    [Fact]
    public void Application_keeps_its_existing_single_instance_then_window_and_tray_startup_path()
    {
        var app = File.ReadAllText(Path.Combine(FindRoot(), "src", "StoreExpiryInspector", "App.xaml.cs"));

        var mutex = app.IndexOf("new Mutex(true, RuntimeDataRoot.MutexName, out _ownsInstanceMutex)", StringComparison.Ordinal);
        var duplicate = app.IndexOf("门店效期排查软件已在运行，请从系统托盘打开。", StringComparison.Ordinal);
        var normalStartup = app.IndexOf("base.OnStartup(e);", duplicate, StringComparison.Ordinal);
        var window = app.IndexOf("MainWindow.Show()", normalStartup, StringComparison.Ordinal);
        var tray = app.IndexOf("InitializeTray(mainWindow, _logger)", normalStartup, StringComparison.Ordinal);

        Assert.True(mutex >= 0 && duplicate > mutex && normalStartup > duplicate && window > normalStartup && tray > window);
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
    public void Current_app_and_updater_versions_share_the_existing_identity_format()
    {
        AssertCurrentVersionIdentity(FindRoot());
    }

    internal static void AssertCurrentVersionIdentity(string root)
    {
        string? appVersion = null;
        foreach (var name in new[] { "StoreExpiryInspector", "StoreExpiryInspector.Updater" })
        {
            var project = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", name, name + ".csproj"));
            var version = Assert.Single(project.Descendants("Version")).Value;
            Assert.True(Version.TryParse(version, out var parsed));
            Assert.Equal(3, version.Split('.').Length);
            Assert.True(parsed!.Build >= 0 && parsed.Revision == -1);
            Assert.Equal("$(Version).0", Assert.Single(project.Descendants("AssemblyVersion")).Value);
            Assert.Equal("$(Version).0", Assert.Single(project.Descendants("FileVersion")).Value);
            if (appVersion is not null) Assert.Equal(appVersion, version);
            appVersion = version;
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
