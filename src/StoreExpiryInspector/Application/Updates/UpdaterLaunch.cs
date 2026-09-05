using System.Diagnostics;
using System.IO;

namespace StoreExpiryInspector.Application.Updates;

internal static class UpdaterLaunch
{
    internal static ProcessStartInfo Create(string updaterPath, string journalPath)
    {
        var workingDirectory = Path.GetDirectoryName(updaterPath) ?? throw new InvalidOperationException("独立 Updater 路径无效。");
        var info = new ProcessStartInfo(updaterPath) { UseShellExecute = false, WorkingDirectory = workingDirectory };
        info.ArgumentList.Add("--journal");
        info.ArgumentList.Add(journalPath);
        return info;
    }
}
