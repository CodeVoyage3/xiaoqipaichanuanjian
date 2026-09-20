using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StoreExpiryInspector.ReleaseTools;

public sealed record ReleaseZipResult(int EntryCount, int ChangedHeaderCount, long ZipBytes, string ZipSha256, string CompressedDataSha256);

public static class ReleaseZipArchive
{
    private sealed record Entry(string Name, int CentralOffset, int LocalOffset, ushort Flags, ushort Method, uint Crc, int Compressed, int Uncompressed, int DataOffset);

    public static ReleaseZipResult VerifyAndNormalize(string zipPath, string payloadRoot, string[] expectedPaths, bool normalize)
    {
        var bytes = File.ReadAllBytes(zipPath);
        var before = Parse(bytes);
        VerifyPayload(zipPath, payloadRoot, expectedPaths, before);
        var compressedHash = HashCompressed(bytes, before);
        var changed = 0;

        if (normalize)
        {
            foreach (var entry in before)
            {
                if ((entry.Flags & ~0x0802) != 0) throw new InvalidDataException($"unsupported ZIP flags: {entry.Name}");
                var flags = (ushort)(entry.Flags & ~0x0002);
                if (flags != entry.Flags)
                {
                    Write16(bytes, entry.CentralOffset + 8, flags);
                    Write16(bytes, entry.LocalOffset + 6, flags);
                    changed += 2;
                }
            }
            File.WriteAllBytes(zipPath, bytes);
        }

        var afterBytes = File.ReadAllBytes(zipPath);
        if (afterBytes.Length != bytes.Length) throw new InvalidDataException("ZIP length changed during header normalization");
        var after = Parse(afterBytes);
        if (after.Count != before.Count) throw new InvalidDataException("ZIP entry count changed during header normalization");
        for (var index = 0; index < before.Count; index++)
        {
            var left = before[index]; var right = after[index];
            var expectedFlags = normalize ? (ushort)(left.Flags & ~0x0002) : left.Flags;
            if (left.Name != right.Name || left.Method != right.Method || left.Crc != right.Crc || left.Compressed != right.Compressed || left.Uncompressed != right.Uncompressed || left.DataOffset != right.DataOffset || right.Flags != expectedFlags)
                throw new InvalidDataException($"ZIP structure changed during header normalization: {left.Name}");
            if (normalize && (right.Flags & ~0x0800) != 0) throw new InvalidDataException($"ZIP flags remain incompatible with the current updater: {right.Name}");
        }
        if (HashCompressed(afterBytes, after) != compressedHash) throw new InvalidDataException("compressed ZIP streams changed during header normalization");
        VerifyPayload(zipPath, payloadRoot, expectedPaths, after);
        return new(after.Count, changed, afterBytes.LongLength, Hex(SHA256.HashData(afterBytes)), compressedHash);
    }

    private static List<Entry> Parse(byte[] bytes)
    {
        if (bytes.Length < 22) throw new InvalidDataException("ZIP is truncated");
        var end = bytes.Length - 22;
        while (end >= Math.Max(0, bytes.Length - 65557) && Read32(bytes, end) != 0x06054b50) end--;
        if (end < 0 || end + 22 != bytes.Length || Read16(bytes, end + 20) != 0 || Read16(bytes, end + 4) != 0 || Read16(bytes, end + 6) != 0 || Read16(bytes, end + 8) != Read16(bytes, end + 10)) throw new InvalidDataException("unsupported ZIP end record");
        var count = Read16(bytes, end + 10); var centralSize = Read32(bytes, end + 12); var centralOffset = Read32(bytes, end + 16);
        if (count == ushort.MaxValue || centralSize == uint.MaxValue || centralOffset == uint.MaxValue || (long)centralOffset + centralSize != end) throw new InvalidDataException("ZIP64 or malformed central directory is unsupported");
        var entries = new List<Entry>(count); var cursor = checked((int)centralOffset); var nextLocal = 0;
        for (var index = 0; index < count; index++)
        {
            if (Read32(bytes, cursor) != 0x02014b50) throw new InvalidDataException("malformed ZIP central directory");
            var flags = Read16(bytes, cursor + 8); var method = Read16(bytes, cursor + 10); var crc = Read32(bytes, cursor + 16);
            var compressed = checked((int)Read32(bytes, cursor + 20)); var uncompressed = checked((int)Read32(bytes, cursor + 24));
            var nameLength = Read16(bytes, cursor + 28); var extraLength = Read16(bytes, cursor + 30); var commentLength = Read16(bytes, cursor + 32); var localOffset = checked((int)Read32(bytes, cursor + 42));
            if (Read16(bytes, cursor + 34) != 0 || method != 8 || extraLength != 0 || commentLength != 0 || localOffset != nextLocal || (flags & ~0x0802) != 0) throw new InvalidDataException("unsupported ZIP entry metadata");
            var nameBytes = bytes.AsSpan(cursor + 46, nameLength); if (nameBytes.IndexOfAnyExceptInRange((byte)0, (byte)127) >= 0) throw new InvalidDataException("non-ASCII ZIP path is unsupported by the current updater");
            var name = Encoding.UTF8.GetString(nameBytes);
            if (Read32(bytes, localOffset) != 0x04034b50 || Read16(bytes, localOffset + 6) != flags || Read16(bytes, localOffset + 8) != method || Read32(bytes, localOffset + 14) != crc || Read32(bytes, localOffset + 18) != compressed || Read32(bytes, localOffset + 22) != uncompressed || Read16(bytes, localOffset + 26) != nameLength || Read16(bytes, localOffset + 28) != 0 || !nameBytes.SequenceEqual(bytes.AsSpan(localOffset + 30, nameLength))) throw new InvalidDataException($"local/central ZIP header mismatch: {name}");
            var dataOffset = checked(localOffset + 30 + nameLength); if (dataOffset + compressed > centralOffset) throw new InvalidDataException($"ZIP payload overlaps central directory: {name}");
            entries.Add(new(name, cursor, localOffset, flags, method, crc, compressed, uncompressed, dataOffset));
            nextLocal = dataOffset + compressed; cursor += 46 + nameLength + extraLength + commentLength;
        }
        if (cursor != end || nextLocal != centralOffset) throw new InvalidDataException("ZIP records are not contiguous");
        return entries;
    }

    private static void VerifyPayload(string zipPath, string root, string[] expectedPaths, List<Entry> parsed)
    {
        if (expectedPaths.Length != parsed.Count || !expectedPaths.SequenceEqual(parsed.Select(entry => entry.Name), StringComparer.Ordinal)) throw new InvalidDataException("ZIP entry paths or order differ from the publish payload");
        if (!expectedPaths.Contains("StoreExpiryInspector.exe", StringComparer.Ordinal) || !expectedPaths.Contains("Updater/StoreExpiryInspector.Updater.exe", StringComparer.Ordinal)) throw new InvalidDataException("ZIP is missing App or Updater");
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count != parsed.Count) throw new InvalidDataException("ZIP entry count mismatch");
        for (var index = 0; index < parsed.Count; index++)
        {
            var metadata = parsed[index]; var entry = archive.Entries[index]; var source = Path.Combine(root, metadata.Name.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName != metadata.Name || !File.Exists(source) || entry.Length != new FileInfo(source).Length) throw new InvalidDataException($"ZIP payload mismatch: {metadata.Name}");
            using var input = entry.Open(); using var actual = new MemoryStream(); input.CopyTo(actual);
            var content = actual.ToArray(); if (Crc32(content) != metadata.Crc || !SHA256.HashData(content).SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new InvalidDataException($"ZIP CRC or SHA256 mismatch: {metadata.Name}");
        }
    }

    private static string HashCompressed(byte[] bytes, List<Entry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries) hash.AppendData(bytes, entry.DataOffset, entry.Compressed);
        return Hex(hash.GetHashAndReset());
    }
    private static uint Crc32(byte[] bytes) { var value = uint.MaxValue; foreach (var item in bytes) { value ^= item; for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 1 ? 0xEDB88320u : 0); } return ~value; }
    private static ushort Read16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);
    private static uint Read32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private static void Write16(byte[] data, int offset, ushort value) { data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); }
    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
}
