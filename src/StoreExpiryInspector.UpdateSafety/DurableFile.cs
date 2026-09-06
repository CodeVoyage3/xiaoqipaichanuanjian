using System.Text;

namespace StoreExpiryInspector.Application.Updates;

// Flushes file contents before the Windows atomic rename.  Filesystem/power-loss
// guarantees beyond that OS contract require storage-specific validation.
public static class DurableFile
{
    public static void Replace(string path, ReadOnlySpan<byte> contents)
    {
        var temporary = path + ".tmp";
        PreserveInterruptedScratch(temporary);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public static void Replace(string path, string contents) => Replace(path, Encoding.UTF8.GetBytes(contents));

    // The target remains the only committed authority.  An interrupted scratch is
    // retained for diagnosis and is never parsed or promoted on a later run.
    public static void PreserveInterruptedScratch(string path)
    {
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("持久化暂存文件不安全。");
        File.Move(path, path + ".interrupted-" + Guid.NewGuid().ToString("N"));
    }
}
