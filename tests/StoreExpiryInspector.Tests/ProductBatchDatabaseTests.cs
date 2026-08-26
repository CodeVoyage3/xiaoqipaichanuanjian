using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ProductBatchDatabaseTests
{
    [Fact]
    public void ProductCodeIsTrimmedAndUniqueInSqlite()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        context.Products.Add(new Product { ProductCode = "  SKU-001  " });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        Assert.Equal("SKU-001", context.Products.AsNoTracking().Single().ProductCode);

        context.Products.Add(new Product { ProductCode = "SKU-001" });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        var rawCode = " SKU-002 ";
        Assert.Throws<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolated($"INSERT INTO products (product_code, excel_stock_qty, effective_stock_qty) VALUES ({rawCode}, 0, 0)"));
    }

    [Fact]
    public void CategoryAndPolicyCodesRejectBlankValuesButAllowStableFutureCodes()
    {
        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            context.Products.Add(new Product
            {
                ProductCode = "SKU-BLANK-CATEGORY",
                CategoryCode = " ",
                PolicyCode = "food_v1"
            });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            context.Products.Add(new Product
            {
                ProductCode = "SKU-BLANK-POLICY",
                CategoryCode = "food",
                PolicyCode = " "
            });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            context.Products.Add(new Product
            {
                ProductCode = "SKU-FUTURE",
                CategoryCode = "medicine",
                PolicyCode = "medicine_v1"
            });
            context.SaveChanges();
            Assert.Equal("medicine", context.Products.Single().CategoryCode);
            Assert.Equal("medicine_v1", context.Products.Single().PolicyCode);
        }
    }

    [Fact]
    public void BatchesWithProductionDateUseTheThreePartUniqueKey()
    {
        using var database = SqliteTestDatabase.Create();
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
        using var database = SqliteTestDatabase.Create();
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
        using var database = SqliteTestDatabase.Create();
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
        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            var product = AddProduct(context, "SKU-001");
            var invalidUnitBatch = NewBatch(product.Id, null, new DateOnly(2026, 12, 31));
            invalidUnitBatch.ShelfLifeUnit = "W";
            context.Batches.Add(invalidUnitBatch);
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            context.Products.Add(new Product { ProductCode = "SKU-NEG-EXCEL", ExcelStockQty = -1 });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            context.Products.Add(new Product { ProductCode = "SKU-NEG-EFFECTIVE", EffectiveStockQty = -1 });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            var product = AddProduct(context, "SKU-NEG");
            var negativeBatch = NewBatch(product.Id, null, new DateOnly(2026, 12, 31));
            negativeBatch.CurrentArrivalQty = -1;
            context.Batches.Add(negativeBatch);
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
        using (var context = database.Open())
        {
            var product = AddProduct(context, "SKU-NEG-MAX");
            var negativeBatch = NewBatch(product.Id, null, new DateOnly(2026, 12, 31));
            negativeBatch.MaxArrivalQty = -1;
            context.Batches.Add(negativeBatch);
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using (var database = SqliteTestDatabase.Create())
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
}
