using System.Globalization;
using System.Text.Json;
using StoreExpiryInspector.Infrastructure.Logging;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class LocalFileLoggerTests
{
    private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "StoreExpiryInspectorTestTimeZone",
        TimeSpan.FromHours(8),
        "StoreExpiryInspector test time zone",
        "StoreExpiryInspector test time zone");

    [Fact]
    public void ConstructorHasNoFileSideEffectAndFirstWriteCreatesCurrentLog()
    {
        using var directory = TemporaryDirectory.Create();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 27, 7, 4, 5, TimeSpan.Zero));
        var logger = new LocalFileLogger(directory.Path, time);

        Assert.False(Directory.Exists(directory.Path));
        Assert.True(logger.TryWrite(" info ", "  startup  ", "  ready  "));

        var path = Path.Combine(directory.Path, "app-20260827.log");
        Assert.True(File.Exists(path));
        Assert.Equal(new[] { "app-20260827.log" }, Directory.GetFiles(directory.Path).Select(Path.GetFileName));
        using var document = JsonDocument.Parse(File.ReadAllLines(path).Single());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("exception").ValueKind);
    }

    [Fact]
    public void WritesExactJsonLinesWithUtcTimestampAndEscapedNewlines()
    {
        using var directory = TemporaryDirectory.Create();
        var utcNow = new DateTimeOffset(2026, 8, 27, 7, 4, 5, 123, TimeSpan.Zero).AddTicks(4567);
        var logger = new LocalFileLogger(directory.Path, new MutableTimeProvider(utcNow));
        const string message = "  first line\nsecond line\r\n  ";
        const string exception = "  failure\ntrace  ";

        Assert.True(logger.TryWrite("warning", "  import_failed  ", message, exception));

        var path = Path.Combine(directory.Path, "app-20260827.log");
        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        using var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;
        Assert.Equal(
            new[] { "timestamp_utc", "level", "event_code", "message", "exception" },
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            utcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            root.GetProperty("timestamp_utc").GetString());
        Assert.Equal("warning", root.GetProperty("level").GetString());
        Assert.Equal("import_failed", root.GetProperty("event_code").GetString());
        Assert.Equal("first line\nsecond line", root.GetProperty("message").GetString());
        Assert.Equal(exception, root.GetProperty("exception").GetString());
        Assert.DoesNotContain("\n", File.ReadAllText(path).TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AcceptsOnlySupportedLevelsAndRejectsBlankRequiredParameters()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new LocalFileLogger(directory.Path);

        Assert.True(logger.TryWrite("info", "info_event", "message"));
        Assert.True(logger.TryWrite("warning", "warning_event", "message"));
        Assert.True(logger.TryWrite("error", "error_event", "message"));
        Assert.Throws<ArgumentException>(() => logger.TryWrite("trace", "event", "message"));
        Assert.Throws<ArgumentException>(() => logger.TryWrite(" ", "event", "message"));
        Assert.ThrowsAny<ArgumentException>(() => logger.TryWrite(null!, "event", "message"));
        Assert.Throws<ArgumentException>(() => logger.TryWrite("info", " ", "message"));
        Assert.Throws<ArgumentException>(() => logger.TryWrite("info", "event", " "));

        var lines = File.ReadAllLines(Directory.GetFiles(directory.Path, "app-*.log").Single());
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public void AppendsWithinOneLocalDateAndRollsAtTheNextLocalDate()
    {
        using var directory = TemporaryDirectory.Create();
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 26, 15, 59, 0, TimeSpan.Zero),
            TestTimeZone);
        var logger = new LocalFileLogger(directory.Path, time);

        Assert.True(logger.TryWrite("info", "before_midnight", "one"));
        Assert.True(logger.TryWrite("info", "same_day", "two"));
        time.SetUtcNow(new DateTimeOffset(2026, 8, 26, 16, 0, 0, TimeSpan.Zero));
        Assert.True(logger.TryWrite("info", "after_midnight", "three"));

        Assert.Equal(
            new[] { "app-20260826.log", "app-20260827.log" },
            Directory.GetFiles(directory.Path).Select(Path.GetFileName).OrderBy(name => name));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(directory.Path, "app-20260826.log")).Length);
        Assert.Single(File.ReadAllLines(Path.Combine(directory.Path, "app-20260827.log")));
    }

    [Fact]
    public void ConcurrentWritesProduceCompleteUniqueJsonLines()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new LocalFileLogger(
            directory.Path,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 27, 7, 0, 0, TimeSpan.Zero)));
        var secondLogger = new LocalFileLogger(
            directory.Path,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 27, 7, 0, 0, TimeSpan.Zero)));
        const int writeCount = 1600;
        var results = new bool[writeCount];

        Parallel.For(0, writeCount, index =>
        {
            results[index] = (index & 1) == 0
                ? logger.TryWrite("info", $"event-{index}", $"message-{index}")
                : secondLogger.TryWrite("info", $"event-{index}", $"message-{index}");
        });

        Assert.All(results, Assert.True);
        var lines = File.ReadAllLines(Path.Combine(directory.Path, "app-20260827.log"));
        Assert.Equal(writeCount, lines.Length);
        var eventCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("info", document.RootElement.GetProperty("level").GetString());
            Assert.True(eventCodes.Add(document.RootElement.GetProperty("event_code").GetString()!));
        }

        Assert.Equal(writeCount, eventCodes.Count);
    }

    [Fact]
    public void RetainsFourteenNewestValidFilesWithoutDeletingUnrelatedEntries()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(directory.Path);
        for (var day = 1; day <= 15; day++)
        {
            File.WriteAllText(
                Path.Combine(directory.Path, $"app-202608{day:00}.log"),
                "old\n");
        }

        var unrelatedFile = Path.Combine(directory.Path, "keep.txt");
        var invalidDateFile = Path.Combine(directory.Path, "app-20260230.log");
        var invalidShapeFile = Path.Combine(directory.Path, "app-2026081.log");
        var backupFile = Path.Combine(directory.Path, "app-20260801.log.bak");
        var childDirectory = Path.Combine(directory.Path, "nested");
        var childLog = Path.Combine(childDirectory, "app-20260101.log");
        File.WriteAllText(unrelatedFile, "keep");
        File.WriteAllText(invalidDateFile, "keep");
        File.WriteAllText(invalidShapeFile, "keep");
        File.WriteAllText(backupFile, "keep");
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(childLog, "keep");

        var logger = new LocalFileLogger(
            directory.Path,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(logger.TryWrite("info", "retention", "current"));

        var expected = Enumerable.Range(3, 14)
            .Select(day => $"app-202608{day:00}.log")
            .OrderBy(name => name)
            .ToArray();
        var retained = Directory.GetFiles(directory.Path)
            .Select(Path.GetFileName)
            .Where(name => name is not null && IsValidLogFileName(name))
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(expected, retained);
        Assert.False(File.Exists(Path.Combine(directory.Path, "app-20260801.log")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "app-20260802.log")));
        Assert.True(File.Exists(unrelatedFile));
        Assert.True(File.Exists(invalidDateFile));
        Assert.True(File.Exists(invalidShapeFile));
        Assert.True(File.Exists(backupFile));
        Assert.True(File.Exists(childLog));
    }

    [Fact]
    public void DirectoryPathConflictReturnsFalseAndRecoversAfterPathIsFixed()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(directory.Path, "not a directory");
        var logger = new LocalFileLogger(
            directory.Path,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero)));

        Assert.False(logger.TryWrite("error", "path_conflict", "blocked"));
        File.Delete(directory.Path);

        Assert.True(logger.TryWrite("error", "path_recovered", "written"));
        Assert.True(File.Exists(Path.Combine(directory.Path, "app-20260827.log")));
    }

    [Fact]
    public void CleanupFailureReturnsFalseAndTheSameLoggerRecovers()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(directory.Path);
        for (var day = 1; day <= 15; day++)
        {
            File.WriteAllText(
                Path.Combine(directory.Path, $"app-202608{day:00}.log"),
                "old\n");
        }

        var blockedFile = Path.Combine(directory.Path, "app-20260801.log");
        var logger = new LocalFileLogger(
            directory.Path,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)));
        using (var blocker = new FileStream(blockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(logger.TryWrite("warning", "cleanup_blocked", "written_but_not_cleaned"));
        }

        Assert.True(logger.TryWrite("warning", "cleanup_recovered", "cleaned"));
        Assert.False(File.Exists(blockedFile));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null)
        {
            _utcNow = utcNow;
            LocalTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
        }

        public override TimeZoneInfo LocalTimeZone { get; }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    private static bool IsValidLogFileName(string? fileName)
    {
        return fileName is not null
            && fileName.Length == 16
            && fileName.StartsWith("app-", StringComparison.Ordinal)
            && fileName.EndsWith(".log", StringComparison.Ordinal)
            && DateTime.TryParseExact(
                fileName.Substring(4, 8),
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create() => new(
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"StoreExpiryInspector-logger-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
            else if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
