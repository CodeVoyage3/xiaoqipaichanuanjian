using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class StoreDatabaseTests
{
    [Fact]
    public void InitialMigrationCreatesTablesAndRequiredIndexes()
    {
        using var database = TemporaryDatabase.Create();
        using var context = database.Open();

        Assert.Contains("InitialCreate", context.Database.GetAppliedMigrations().Single());
        Assert.Equal("wal", ReadPragma(context, "journal_mode"));
        Assert.Contains("products", ReadSchemaNames(context, "table"));
        Assert.Contains("batches", ReadSchemaNames(context, "table"));

        var indexes = ReadSchemaNames(context, "index");
        Assert.Contains("IX_products_product_code", indexes);
        Assert.Contains("IX_batches_product_id", indexes);
        Assert.Contains("IX_batches_expiry_date", indexes);
        Assert.Contains("IX_batches_tracking_status_next_trigger_date", indexes);
        Assert.Contains("IX_batches_product_id_production_date_expiry_date", indexes);
        Assert.Contains("IX_batches_product_id_expiry_date", indexes);

        Assert.Contains(
            "production_date IS NOT NULL",
            ReadIndexSql(context, "IX_batches_product_id_production_date_expiry_date"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "production_date IS NULL",
            ReadIndexSql(context, "IX_batches_product_id_expiry_date"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductCodeIsTrimmedAndUniqueInSqlite()
    {
        using var database = TemporaryDatabase.Create();
        using var context = database.Open();

        context.Products.Add(new Product { ProductCode = "  SKU-001  " });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        Assert.Equal("SKU-001", context.Products.AsNoTracking().Single().ProductCode);

        context.Products.Add(new Product { ProductCode = "SKU-001" });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void BatchesWithProductionDateUseTheThreePartUniqueKey()
    {
        using var database = TemporaryDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-001");
        var productionDate = new DateOnly(2026, 1, 1);
        var expiryDate = new DateOnly(2026, 12, 31);

        context.Batches.Add(NewBatch(product.Id, productionDate, expiryDate));
        context.SaveChanges();
        context.Batches.Add(NewBatch(product.Id, productionDate, expiryDate));

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void BatchesWithoutProductionDateUseTheTwoPartUniqueKey()
    {
        using var database = TemporaryDatabase.Create();
        using var context = database.Open();
        var product = AddProduct(context, "SKU-001");
        var expiryDate = new DateOnly(2026, 12, 31);

        context.Batches.Add(NewBatch(product.Id, null, expiryDate));
        context.SaveChanges();
        context.Batches.Add(NewBatch(product.Id, null, expiryDate));

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void DifferentProductsMayShareTheSameDates()
    {
        using var database = TemporaryDatabase.Create();
        using var context = database.Open();
        var firstProduct = AddProduct(context, "SKU-001");
        var secondProduct = AddProduct(context, "SKU-002");
        var productionDate = new DateOnly(2026, 1, 1);
        var expiryDate = new DateOnly(2026, 12, 31);

        context.Batches.Add(NewBatch(firstProduct.Id, productionDate, expiryDate));
        context.Batches.Add(NewBatch(secondProduct.Id, productionDate, expiryDate));

        context.SaveChanges();
        Assert.Equal(2, context.Batches.Count());
    }

    [Fact]
    public void SqliteRejectsInvalidUnitNegativeQuantitiesAndMissingProduct()
    {
        using (var database = TemporaryDatabase.Create())
        using (var context = database.Open())
        {
            var product = AddProduct(context, "SKU-001");
            var invalidUnitBatch = NewBatch(product.Id, null, new DateOnly(2026, 12, 31));
            invalidUnitBatch.ShelfLifeUnit = "W";
            context.Batches.Add(invalidUnitBatch);
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = TemporaryDatabase.Create())
        using (var context = database.Open())
        {
            context.Products.Add(new Product { ProductCode = "SKU-NEG", ExcelStockQty = -1 });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = TemporaryDatabase.Create())
        using (var context = database.Open())
        {
            var product = AddProduct(context, "SKU-NEG");
            var negativeBatch = NewBatch(product.Id, null, new DateOnly(2026, 12, 31));
            negativeBatch.CurrentArrivalQty = -1;
            context.Batches.Add(negativeBatch);
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = TemporaryDatabase.Create())
        using (var context = database.Open())
        {
            context.Batches.Add(NewBatch(999, null, new DateOnly(2026, 12, 31)));
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }
    }

    private static Product AddProduct(StoreDbContext context, string code)
    {
        var product = new Product { ProductCode = code };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch NewBatch(long productId, DateOnly? productionDate, DateOnly expiryDate)
    {
        return new Batch
        {
            ProductId = productId,
            ProductionDate = productionDate,
            ExpiryDate = expiryDate,
            ShelfLifeValue = 12,
            ShelfLifeUnit = "M",
            CurrentArrivalQty = 10,
            MaxArrivalQty = 10
        };
    }

    private static HashSet<string> ReadSchemaNames(StoreDbContext context, string type)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$type";
            parameter.Value = type;
            command.Parameters.Add(parameter);
            using var reader = command.ExecuteReader();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static string ReadIndexSql(StoreDbContext context, string indexName)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = indexName;
            command.Parameters.Add(parameter);
            return (string?)command.ExecuteScalar() ?? string.Empty;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static string ReadPragma(StoreDbContext context, string pragma)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA {pragma}";
            return (string?)command.ExecuteScalar() ?? string.Empty;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private TemporaryDatabase(string directory)
        {
            Directory = directory;
            Path = System.IO.Path.Combine(directory, "app.db");
        }

        public string Directory { get; }

        public string Path { get; }

        public static TemporaryDatabase Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "StoreExpiryInspectorTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var database = new TemporaryDatabase(directory);
            DatabaseInitializer.Initialize(database.Path);
            return database;
        }

        public StoreDbContext Open() => DatabaseInitializer.CreateContext(Path);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
