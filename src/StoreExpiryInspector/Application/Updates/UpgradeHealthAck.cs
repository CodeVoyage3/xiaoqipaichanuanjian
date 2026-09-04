using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

public static class UpgradeHealthAck
{
    public static void Write(string isolatedRoot, string operationId, string version, int migrationCount, string lastMigration)
    {
        if (!Guid.TryParse(operationId, out _)) throw new InvalidOperationException("Invalid update operation.");
        var directory = Path.Combine(isolatedRoot, "updates", operationId);
        ValidateOrdinaryDirectory(isolatedRoot);
        Directory.CreateDirectory(directory);
        ValidateOrdinaryDirectory(directory);
        var process = Process.GetCurrentProcess();
        var ack = JsonSerializer.Serialize(new { operationId, version, pid = process.Id, startedUtc = process.StartTime.ToUniversalTime().ToString("O"), migrationCount, lastMigration, integrity = "ok", foreignKeys = "ok", coreRead = true, uiLoaded = true });
        var temporary = Path.Combine(directory, "health-ack.tmp");
        var target = Path.Combine(directory, "health-ack.json");
        File.WriteAllText(temporary, ack);
        File.Move(temporary, target, true);
    }

    private static void ValidateOrdinaryDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Unsafe ACK path.");
    }
}
