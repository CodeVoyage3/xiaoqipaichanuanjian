using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

public static class PendingUpdateRecovery
{
    public static bool TryResume(string dataRoot)
    {
        var updates = Path.Combine(dataRoot, "updates");
        if (!Directory.Exists(updates)) return false;
        EnsureOrdinaryDirectory(dataRoot);
        EnsureOrdinaryDirectory(updates);
        var pending = Directory.EnumerateDirectories(updates).Select(Read).Where(item => item.Pending).ToArray();
        if (pending.Length == 0) return false;
        if (pending.Length != 1) throw new InvalidOperationException("检测到多个未完成更新，已停止启动以保护程序和数据。");
        var item = pending[0];
        var updaterDirectory = Path.Combine(item.Directory, "updater");
        var updater = Path.Combine(updaterDirectory, "StoreExpiryInspector.Updater.exe");
        EnsureOrdinaryDirectory(updaterDirectory);
        if (!File.Exists(updater) || (File.GetAttributes(updater) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("未完成更新缺少安全的独立 Updater，已停止启动以保护程序和数据。");
        _ = Process.Start(UpdaterLaunch.Create(updater, item.JournalPath)) ?? throw new InvalidOperationException("未完成更新无法恢复，已停止启动以保护程序和数据。");
        return true;
    }

    private static (string Directory, string JournalPath, bool Pending) Read(string directory)
    {
        EnsureOrdinaryDirectory(directory);
        if (!Guid.TryParse(Path.GetFileName(directory), out _)) throw new InvalidOperationException("更新目录身份无效，已停止启动以保护程序和数据。");
        var journal = Path.Combine(directory, "journal.json");
        if (!File.Exists(journal)) return (directory, journal, false); // Preparation has not yet atomically created a journal, so the active app was never switched.
        if ((File.GetAttributes(journal) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        JsonDocument document;
        try { document = JsonDocument.Parse(File.ReadAllText(journal)); }
        catch (JsonException) { throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。"); }
        using (document)
        {
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("Phase", out var phase))
            throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        var value = phase.ValueKind == JsonValueKind.Number && phase.TryGetInt32(out var number) ? number : phase.ValueKind == JsonValueKind.String ? phase.GetString() switch { "Prepared" => 0, "MainExitRequested" => 1, "MainExited" => 2, "CandidateStaged" => 3, "OldAppPreserved" => 4, "SwitchStarted" => 5, "CandidateActivated" => 6, "CandidateStarted" => 7, "WaitingForHealthAck" => 8, "Committed" => 9, "Completed" => 10, "RollbackRequired" => 11, "RollbackStarted" => 12, "OldAppRestored" => 13, "RollbackVerified" => 14, "RolledBack" => 15, "FailedNeedsManualRecovery" => 16, _ => -1 } : -1;
        if (value == 16) throw new InvalidOperationException("上次更新需要人工恢复，已停止启动以保护程序和数据。");
        if (value is < 0 or > 16) throw new InvalidOperationException("未完成更新记录无效，已停止启动以保护程序和数据。");
        return (directory, journal, value is not (9 or 10 or 14 or 15));
        }
    }

    private static void EnsureOrdinaryDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("更新目录不安全，已停止启动以保护程序和数据。");
    }
}
