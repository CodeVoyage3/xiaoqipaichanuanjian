using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoreExpiryInspector.Infrastructure.Logging;

public sealed class LocalFileLogger
{
    private const string LogFilePrefix = "app-";
    private const string LogFileSuffix = ".log";
    private const int RetainedFileCount = 14;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new();
    // ponytail: one process-wide lock keeps same-process appends atomic; partition by directory only if throughput matters.
    private static readonly object SyncRoot = new();

    private readonly string _logDirectory;
    private readonly TimeProvider _timeProvider;

    public LocalFileLogger(string logDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        _logDirectory = logDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryWrite(string level, string eventCode, string message, string? exception = null)
    {
        var normalizedLevel = NormalizeLevel(level);
        var normalizedEventCode = NormalizeRequired(eventCode, nameof(eventCode));
        var normalizedMessage = NormalizeRequired(message, nameof(message));

        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(_logDirectory);

                var utcNow = _timeProvider.GetUtcNow();
                var localDate = _timeProvider.GetLocalNow().Date;
                var filePath = Path.Combine(
                    _logDirectory,
                    LogFilePrefix
                        + localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                        + LogFileSuffix);
                var entry = new LogEntry(
                    utcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    normalizedLevel,
                    normalizedEventCode,
                    normalizedMessage,
                    exception);

                File.AppendAllText(
                    filePath,
                    JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine,
                    Utf8NoBom);
                DeleteOlderLogFiles();
                return true;
            }
            catch (Exception error) when (error is IOException
                or UnauthorizedAccessException
                or SecurityException
                or NotSupportedException
                or ArgumentException)
            {
                return false;
            }
        }
    }

    private static string NormalizeLevel(string level)
    {
        var normalized = NormalizeRequired(level, nameof(level));
        if (normalized is not ("info" or "warning" or "error"))
        {
            throw new ArgumentException("Level must be info, warning, or error.", nameof(level));
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private void DeleteOlderLogFiles()
    {
        var files = new List<(string Path, DateTime Date)>();
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = System.IO.Path.GetFileName(path);
            if (TryGetLogDate(fileName, out var date))
            {
                files.Add((path, date));
            }
        }

        files.Sort(static (left, right) => right.Date.CompareTo(left.Date));
        for (var index = RetainedFileCount; index < files.Count; index++)
        {
            File.Delete(files[index].Path);
        }
    }

    private static bool TryGetLogDate(string fileName, out DateTime date)
    {
        const int DateTextLength = 8;
        var expectedLength = LogFilePrefix.Length + DateTextLength + LogFileSuffix.Length;
        if (fileName.Length != expectedLength
            || !fileName.StartsWith(LogFilePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(LogFileSuffix, StringComparison.Ordinal))
        {
            date = default;
            return false;
        }

        var dateText = fileName.Substring(LogFilePrefix.Length, DateTextLength);
        return DateTime.TryParseExact(
            dateText,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private sealed record LogEntry(
        [property: JsonPropertyName("timestamp_utc")] string TimestampUtc,
        [property: JsonPropertyName("level")] string Level,
        [property: JsonPropertyName("event_code")] string EventCode,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("exception")] string? Exception);
}
