using System.IO;
using System.Text.Json;

namespace StoreExpiryInspector.Application.Updates;

public static class UpdateProtocolJson
{
    private static readonly string[] LegacyAckFields = ["operationId", "version", "pid", "startedUtc", "migrationCount", "lastMigration", "integrity", "foreignKeys", "coreRead", "uiLoaded"];

    public static void RequireObject(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("更新协议 JSON 无效。");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!found.Add(property.Name) || !names.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException("更新协议 JSON 无效。");
        if (found.Count != names.Length) throw new InvalidDataException("更新协议 JSON 无效。");
    }
    public static TEnum ReadEnum<TEnum>(JsonElement element) where TEnum : struct, Enum
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number)) return (TEnum)Enum.ToObject(typeof(TEnum), number);
        if (element.ValueKind == JsonValueKind.String && Enum.TryParse<TEnum>(element.GetString(), false, out var value) && Enum.IsDefined(value) && string.Equals(element.GetString(), value.ToString(), StringComparison.Ordinal)) return value;
        throw new InvalidDataException("更新协议枚举无效。");
    }
    public static void RequireStringMap(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("更新协议 JSON 无效。");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!found.Add(property.Name) || !names.Contains(property.Name, StringComparer.Ordinal) || property.Value.ValueKind != JsonValueKind.String || !System.Text.RegularExpressions.Regex.IsMatch(property.Value.GetString() ?? string.Empty, "\\A[0-9A-F]{64}\\z")) throw new InvalidDataException("更新协议 JSON 无效。");
    }
    public static bool IsLegacyHealthAck(string path)
    {
        try { using var document = JsonDocument.Parse(File.ReadAllText(path)); RequireObject(document.RootElement, LegacyAckFields); return true; }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }
}
