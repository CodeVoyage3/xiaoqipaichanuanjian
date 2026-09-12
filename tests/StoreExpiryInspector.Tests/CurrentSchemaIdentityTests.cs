using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UpdateSafety;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class CurrentSchemaIdentityTests
{
    [Fact]
    public void CurrentSchemaIdentity_MatchesActualEfMigrations()
    {
        using var context = new StoreDbContextFactory().CreateDbContext([]);
        Assert.True(context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(CurrentSchemaIdentity.Migrations, StringComparer.Ordinal));
    }
}
