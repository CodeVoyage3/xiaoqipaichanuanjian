using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S18T02GuiFixtureTests
{
    [Fact]
    public void SeedsTheRequestedTemporaryGuiFixture()
    {
        var root = Environment.GetEnvironmentVariable("S18_T02_GUI_ROOT");
        var ownsRoot = string.IsNullOrWhiteSpace(root);
        root ??= Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.True(Guid.TryParse(Path.GetFileName(root), out _));
        Assert.StartsWith(Path.GetTempPath(), root, StringComparison.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "backups", "pre-import"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            var databasePath = Path.Combine(root, "data", "app.db");
            DatabaseInitializer.Initialize(databasePath);
            using var context = DatabaseInitializer.CreateContext(databasePath);
            var product = new Product
            {
                ProductCode = "S18-T02-GUI-001",
                CurrentName = "S18-T02 隔离扫码验收商品",
                CurrentBarcode = "6974396950994",
                ExcelStockQty = 18,
                EffectiveStockQty = 18,
                EffectiveStockSource = "fixture"
            };
            var batch = new Batch { Product = product, ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(5), ShelfLifeValue = 30, CurrentArrivalQty = 18, MaxArrivalQty = 18, CurrentStage = "discount_50" };
            var task = new ProductTask { Product = product, HighestStage = "discount_50" };
            task.Items.Add(new ProductTaskItem { Product = product, Batch = batch, Stage = "discount_50" });
            context.Tasks.Add(task);
            context.SaveChanges();
            Assert.Equal("6974396950994", context.Products.Single().CurrentBarcode);
            Assert.Equal(18, context.Products.Single().EffectiveStockQty);
            Assert.Single(context.Tasks);
        }
        finally
        {
            if (ownsRoot && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
