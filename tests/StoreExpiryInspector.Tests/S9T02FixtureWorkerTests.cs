using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S9T02FixtureWorkerTests
{
    [Fact]
    public void Creates_requested_synthetic_fixture()
    {
        var root = Environment.GetEnvironmentVariable("S9T02_FIXTURE_ROOT");
        var kind = Environment.GetEnvironmentVariable("S9T02_FIXTURE_KIND");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(kind)) return;
        var fullRoot = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(Path.GetTempPath(), fullRoot);
        if (!Path.IsPathFullyQualified(root) || Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) || !Guid.TryParse(relative, out _))
            throw new InvalidOperationException("S9-T02 fixture root must be a TEMP GUID directory.");
        if (File.Exists(fullRoot) || Directory.Exists(fullRoot)) throw new InvalidOperationException("S9-T02 fixture root must be new.");
        for (var current = new DirectoryInfo(Path.GetDirectoryName(fullRoot)!); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("S9-T02 fixture root must be ordinary.");
        if (kind is not ("migration8" or "unknown" or "corrupt")) throw new InvalidOperationException("Unknown S9-T02 fixture kind.");
        var path = Path.Combine(fullRoot, "data", "app.db");
        if (kind == "migration8")
        {
            using var context = DatabaseInitializer.CreateContext(path);
            context.Database.GetService<IMigrator>().Migrate("20260826170403_AddLifecycleEvents");
            Assert.Equal(8, context.Database.GetAppliedMigrations().Count());
            return;
        }

        DatabaseInitializer.Initialize(path);
        if (kind == "unknown")
        {
            using var context = DatabaseInitializer.CreateContext(path);
            context.Database.ExecuteSqlRaw("INSERT INTO __EFMigrationsHistory(MigrationId, ProductVersion) VALUES ('99999999999999_Future', '10.0.0');");
            Assert.Equal(10, context.Database.GetAppliedMigrations().Count());
            return;
        }

        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(3, new FileInfo(path).Length);
    }
}
