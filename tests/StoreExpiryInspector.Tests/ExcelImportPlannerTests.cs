using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure.Excel;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ExcelImportPlannerTests
{
    [Fact]
    public void MapsAllTenApprovedCategoriesToCanonicalScopeIdentities()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var rows = new[]
        {
            Row(2, category: "食品", code: "FOOD"),
            Row(3, category: "宠物", code: "PET"),
            Row(4, category: "日用", code: "DAILY", shelfLife: "181", shelfLifeUnit: "D"),
            Row(5, category: "美妆", code: "BEAUTY", shelfLife: "181", shelfLifeUnit: "D"),
            Row(6, category: "家居", code: "HOME", shelfLife: "181", shelfLifeUnit: "D"),
            Row(7, category: "香氛香水", code: "FRAGRANCE", shelfLife: "181", shelfLifeUnit: "D"),
            Row(8, category: "文具", code: "STATIONERY", shelfLife: "181", shelfLifeUnit: "D"),
            Row(9, category: "潮流玩具", code: "TOYS", shelfLife: "181", shelfLifeUnit: "D"),
            Row(10, category: "应季搭配", code: "SEASONAL"),
            Row(11, category: "赠品小样", code: "GIFT")
        };

        var plan = new ExcelImportPlanner().Plan(context, Classify(rows));

        Assert.Equal(10, plan.NewProducts.Count);
        Assert.Equal("food", plan.NewProducts.Single(product => product.ProductCode == "FOOD").CategoryCode);
        Assert.Equal(ExpiryPolicies.Food, plan.NewProducts.Single(product => product.ProductCode == "FOOD").PolicyCode);
        Assert.Equal("pet", plan.NewProducts.Single(product => product.ProductCode == "PET").CategoryCode);
        Assert.Equal(ExpiryPolicies.Pet, plan.NewProducts.Single(product => product.ProductCode == "PET").PolicyCode);
        Assert.All(plan.NewProducts.Where(product => product.ProductCode is "DAILY" or "BEAUTY" or "HOME" or "FRAGRANCE" or "STATIONERY" or "TOYS"), product =>
        {
            Assert.Equal(ExpiryManagementStatus.Managed, product.ExpiryManagementStatus);
            Assert.Equal(ExpiryPolicies.GeneralLong, product.PolicyCode);
            Assert.Equal(1, product.PolicyVersion);
        });
        Assert.All(plan.NewProducts.Where(product => product.ProductCode is "SEASONAL" or "GIFT"), product =>
        {
            Assert.Equal(ExpiryManagementStatus.Excluded, product.ExpiryManagementStatus);
            Assert.Null(product.PolicyCode);
            Assert.Null(product.PolicyVersion);
        });
    }

    [Theory]
    [InlineData("180", "D")]
    [InlineData("6", "M")]
    public void GeneralCategoriesAtOrBelow180DaysAreUnresolvedButStillPlanned(string shelfLife, string unit)
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var plan = new ExcelImportPlanner().Plan(context, Classify(
            Row(2, category: "日用", code: "SHORT", shelfLife: shelfLife, shelfLifeUnit: unit)));

        var product = Assert.Single(plan.NewProducts);
        Assert.Equal(ExpiryManagementStatus.Unresolved, product.ExpiryManagementStatus);
        Assert.Null(product.PolicyCode);
        Assert.Null(product.PolicyVersion);
        Assert.Single(plan.NewBatches);
        Assert.Contains(plan.Preview.PlanningIssues, issue => issue.Code == "expiry_policy_unresolved" && issue.ProductCode == "SHORT");
    }

    [Fact]
    public void ExistingProductScopeConflictIsAuditableAndDoesNotPlanChanges()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "P", "商品", "B", 1, 1, "excel");
            seed.SaveChanges();
        }
        using var context = database.Open();

        var plan = new ExcelImportPlanner().Plan(context, Classify(Row(2, category: "宠物", code: "P")));

        Assert.Empty(plan.NewProducts);
        Assert.Empty(plan.UpdatedProducts);
        Assert.Empty(plan.NewBatches);
        Assert.Contains(plan.Preview.PlanningIssues, issue => issue.Code == "product_scope_policy_conflict" && issue.ExcelRowNumber == 2);
    }

    [Fact]
    public void SourceProductScopeConflictDoesNotChooseOrCreateAProduct()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var plan = new ExcelImportPlanner().Plan(context, Classify(
            Row(2, category: "食品", code: "P", expiry: "2026-12-31"),
            Row(3, category: "宠物", code: "P", expiry: "2027-12-31")));

        Assert.Empty(plan.NewProducts);
        Assert.Empty(plan.NewBatches);
        Assert.Contains(plan.Preview.PlanningIssues, issue => issue.Code == "product_scope_policy_conflict" && issue.ExcelRowNumber == 2);
    }

    [Fact]
    public void PlansNewProductAndBatchAndExistingProductChangesWithoutWriting()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var existing = AddProduct(seed, "P-OLD", "旧商品", "旧条码", 4, 4, "excel");
            seed.Batches.Add(NewBatch(
                existing.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                shelfLife: 12,
                unit: "M",
                currentArrival: 3,
                maxArrival: 5,
                discount: "否"));
            seed.SaveChanges();
        }

        var classification = Classify(
            Row(2, code: "P-OLD", barcode: "新条码", name: "新商品", stock: "7", cumulativeArrival: "6"),
            Row(3, code: "P-NEW", barcode: "B-NEW", name: "新商品", stock: "2", production: null));
        using var context = database.Open();
        var beforeProductCount = context.Products.AsNoTracking().Count();
        var beforeBatchCount = context.Batches.AsNoTracking().Count();
        var beforeImportCount = context.Imports.AsNoTracking().Count();

        var plan = new ExcelImportPlanner().Plan(context, classification);

        var updatedProduct = Assert.Single(plan.UpdatedProducts);
        Assert.Equal("P-OLD", updatedProduct.ProductCode);
        Assert.Equal(
            ["CurrentName", "CurrentBarcode", "ExcelStockQty", "EffectiveStockQty"],
            updatedProduct.FieldChanges.Select(change => change.FieldName));
        Assert.Equal(4, updatedProduct.FieldChanges.Single(change => change.FieldName == "ExcelStockQty").Before);
        Assert.Equal(7, updatedProduct.FieldChanges.Single(change => change.FieldName == "ExcelStockQty").After);

        var newProduct = Assert.Single(plan.NewProducts);
        Assert.Equal("P-NEW", newProduct.ProductCode);
        Assert.Equal("food", newProduct.CategoryCode);
        Assert.Equal(ExpiryPolicies.Food, newProduct.PolicyCode);
        Assert.Equal(ExpiryPolicies.Version1, newProduct.PolicyVersion);
        Assert.Equal(ExpiryManagementStatus.Managed, newProduct.ExpiryManagementStatus);
        Assert.Equal(2, newProduct.ExcelStockQty);
        Assert.Equal(2, newProduct.EffectiveStockQty);
        Assert.Equal("excel", newProduct.EffectiveStockSource);
        Assert.Single(plan.NewBatches);
        Assert.Equal("P-NEW", plan.NewBatches[0].BatchKey.ProductCode);
        Assert.Equal(1, plan.NewBatches[0].CurrentArrivalQty);
        Assert.Equal(1, plan.NewBatches[0].MaxArrivalQty);
        Assert.True(plan.HasChanges);
        Assert.Equal(2, plan.Preview.InvolvedProductCount);
        Assert.Equal(2, plan.Preview.NormalBatchKeyCount);

        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Empty(context.ChangeTracker.Entries<Product>());
        Assert.Empty(context.ChangeTracker.Entries<Batch>());
        Assert.Equal(beforeProductCount, context.Products.AsNoTracking().Count());
        Assert.Equal(beforeBatchCount, context.Batches.AsNoTracking().Count());
        Assert.Equal(beforeImportCount, context.Imports.AsNoTracking().Count());
    }

    [Fact]
    public void MatchesDatedUndatedAndStoppedBatchesAndMaintainsMaximumArrival()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P", "商品", "B", 5, 5, "excel");
            seed.Batches.Add(NewBatch(
                product.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                shelfLife: 12,
                unit: "M",
                currentArrival: 6,
                maxArrival: 10,
                discount: "否"));
            seed.Batches.Add(NewBatch(
                product.Id,
                null,
                new DateOnly(2027, 1, 31),
                shelfLife: 20,
                unit: "D",
                currentArrival: 2,
                maxArrival: 2,
                discount: "是",
                trackingStatus: "stopped"));
            seed.SaveChanges();
        }

        var classification = Classify(
            Row(20, code: "P", expiry: "2026-12-31", shelfLife: "12", shelfLifeUnit: "M", cumulativeArrival: "12"),
            Row(10, code: "P", expiry: "2027-01-31", production: null, shelfLife: "20", shelfLifeUnit: "D", cumulativeArrival: "2", discount: "是"));
        using var context = database.Open();

        var plan = new ExcelImportPlanner().Plan(context, classification);

        Assert.Empty(plan.NewBatches);
        Assert.Single(plan.UpdatedBatches);
        var increased = Assert.Single(plan.UpdatedBatches, batch => batch.BatchKey.ExpiryDate == new DateOnly(2026, 12, 31));
        Assert.Equal(
            ["CurrentArrivalQty", "MaxArrivalQty"],
            increased.FieldChanges.Select(change => change.FieldName));
        Assert.Equal(6, increased.FieldChanges[0].Before);
        Assert.Equal(12, increased.FieldChanges[0].After);
        Assert.Equal(10, increased.FieldChanges[1].Before);
        Assert.Equal(12, increased.FieldChanges[1].After);
        Assert.Single(plan.UnchangedBatches);
        Assert.Equal(new DateOnly(2027, 1, 31), plan.UnchangedBatches[0].BatchKey.ExpiryDate);
        Assert.DoesNotContain(plan.UpdatedBatches, batch => batch.FieldChanges.Any(change =>
            change.FieldName.Contains("Tracking", StringComparison.OrdinalIgnoreCase)
            || change.FieldName.Contains("Stage", StringComparison.OrdinalIgnoreCase)
            || change.FieldName.Contains("Resume", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CompletelyEqualInputIsUnchangedAndHasNoChanges()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P", "商品", "B", 5, 5, "excel");
            seed.Batches.Add(NewBatch(
                product.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                shelfLife: 12,
                unit: "M",
                currentArrival: 3,
                maxArrival: 3,
                discount: "否"));
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(2, code: "P", barcode: "B", name: "商品", stock: "5", cumulativeArrival: "3")));

        Assert.Empty(plan.NewProducts);
        Assert.Empty(plan.UpdatedProducts);
        Assert.Single(plan.UnchangedProducts);
        Assert.Empty(plan.NewBatches);
        Assert.Empty(plan.UpdatedBatches);
        Assert.Single(plan.UnchangedBatches);
        Assert.False(plan.HasChanges);
        Assert.False(plan.Preview.HasChanges);
        Assert.Empty(context.Imports.AsNoTracking());
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public void LocalIncrementOnlyPlansAppearingProductAndLeavesOtherProductsAndBatchesUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            foreach (var code in new[] { "A", "B", "C" })
            {
                var product = AddProduct(seed, code, $"商品-{code}", $"条码-{code}", 5, 5, "excel");
                seed.Batches.Add(NewBatch(
                    product.Id,
                    new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 12, 31),
                    shelfLife: 12,
                    unit: "M",
                    currentArrival: 3,
                    maxArrival: 3,
                    discount: "否"));
            }

            seed.SaveChanges();
        }

        using var beforeContext = database.Open();
        var beforeProducts = ProductSnapshot(beforeContext);
        var beforeBatches = BatchSnapshot(beforeContext);
        var beforeImportCount = beforeContext.Imports.AsNoTracking().Count();
        var beforeTaskCount = beforeContext.Tasks.AsNoTracking().Count();
        var beforeLifecycleCount = beforeContext.LifecycleEvents.AsNoTracking().Count();
        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(2, code: "A", barcode: "条码-A", name: "商品-A", stock: "5", cumulativeArrival: "3")));

        Assert.Equal(["A"], plan.UnchangedProducts.Select(item => item.ProductCode));
        Assert.Equal(["A"], plan.UnchangedBatches.Select(item => item.BatchKey.ProductCode));
        Assert.DoesNotContain(plan.NewProducts, item => item.ProductCode is "B" or "C");
        Assert.DoesNotContain(plan.NewBatches, item => item.BatchKey.ProductCode is "B" or "C");
        Assert.DoesNotContain(plan.UpdatedProducts, item => item.ProductCode is "B" or "C");
        Assert.DoesNotContain(plan.UpdatedBatches, item => item.BatchKey.ProductCode is "B" or "C");

        using var afterContext = database.Open();
        Assert.Equal(beforeProducts, ProductSnapshot(afterContext));
        Assert.Equal(beforeBatches, BatchSnapshot(afterContext));
        Assert.Equal(beforeImportCount, afterContext.Imports.AsNoTracking().Count());
        Assert.Equal(beforeTaskCount, afterContext.Tasks.AsNoTracking().Count());
        Assert.Equal(beforeLifecycleCount, afterContext.LifecycleEvents.AsNoTracking().Count());
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Empty(context.ChangeTracker.Entries<Product>());
        Assert.Empty(context.ChangeTracker.Entries<Batch>());
    }

    [Fact]
    public void StockConflictContainingZeroDoesNotChooseAValueOrBlockOtherPlanning()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P", "旧商品", "旧条码", 8, 8, "manual");
            seed.SaveChanges();
            Assert.NotEqual(0, product.Id);
        }

        using var context = database.Open();
        var classification = Classify(
            Row(2, code: "P", barcode: "新条码", name: "新商品", stock: "0", cumulativeArrival: "1"),
            Row(3, code: "P", barcode: "新条码", name: "新商品", stock: "2", expiry: "2027-12-31", cumulativeArrival: "2"));
        var plan = new ExcelImportPlanner().Plan(context, classification);

        var update = Assert.Single(plan.UpdatedProducts);
        Assert.Equal(["CurrentName", "CurrentBarcode"], update.FieldChanges.Select(change => change.FieldName));
        Assert.DoesNotContain(update.FieldChanges, change =>
            change.FieldName is "ExcelStockQty" or "EffectiveStockQty" or "EffectiveStockSource");
        Assert.Null(classification.StockConflicts.Single().StockValue);
        Assert.Equal(1, plan.Preview.StockConflictCount);
        Assert.Equal(2, plan.NewBatches.Count);
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public void LegalZeroStockListsOnlyTheProductFieldsThatActuallyDiffer()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P", "商品", "B", 0, 7, "manual");
            seed.Batches.Add(NewBatch(
                product.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                shelfLife: 12,
                unit: "M",
                currentArrival: 1,
                maxArrival: 1,
                discount: "否"));
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(2, code: "P", stock: "0", cumulativeArrival: "1")));

        var update = Assert.Single(plan.UpdatedProducts);
        Assert.Equal(["EffectiveStockQty", "EffectiveStockSource"], update.FieldChanges.Select(change => change.FieldName));
        Assert.Equal(7, update.FieldChanges[0].Before);
        Assert.Equal(0, update.FieldChanges[0].After);
        Assert.Equal("manual", update.FieldChanges[1].Before);
        Assert.Equal("excel", update.FieldChanges[1].After);
        Assert.DoesNotContain(update.FieldChanges, change => change.FieldName == "ExcelStockQty");
        Assert.DoesNotContain(plan.Preview.PlanningIssues, issue => issue.Code.Contains("zero", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ImportPlan).GetProperties(),
            property => property.Name.Contains("Task", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Lifecycle", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Stop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExistingBatchUpdatesAllApprovedFieldsButNeverLowersMaximumArrival()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            var product = AddProduct(seed, "P", "商品", "B", 5, 5, "excel");
            seed.Batches.Add(NewBatch(
                product.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                shelfLife: 12,
                unit: "M",
                currentArrival: 10,
                maxArrival: 20,
                discount: "否"));
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(
                2,
                code: "P",
                shelfLife: "24",
                shelfLifeUnit: "D",
                cumulativeArrival: "5",
                discount: "是",
                stock: "5")));

        var update = Assert.Single(plan.UpdatedBatches);
        Assert.Equal(
            ["ShelfLifeValue", "ShelfLifeUnit", "CurrentArrivalQty", "SourceDiscountReference"],
            update.FieldChanges.Select(change => change.FieldName));
        Assert.Equal(12, update.FieldChanges[0].Before);
        Assert.Equal(24, update.FieldChanges[0].After);
        Assert.Equal("M", update.FieldChanges[1].Before);
        Assert.Equal("D", update.FieldChanges[1].After);
        Assert.Equal(10, update.FieldChanges[2].Before);
        Assert.Equal(5, update.FieldChanges[2].After);
        Assert.Equal("否", update.FieldChanges[3].Before);
        Assert.Equal("是", update.FieldChanges[3].After);
        Assert.DoesNotContain(update.FieldChanges, change => change.FieldName == "MaxArrivalQty");
        Assert.Empty(plan.NewBatches);
    }

    [Fact]
    public void DifferentProductCodesWithSameNameAndBarcodeRemainSeparatePlans()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(
                Row(2, code: "P1", name: "相同商品", barcode: "相同条码", stock: "1"),
                Row(3, code: "P2", name: "相同商品", barcode: "相同条码", stock: "1")));

        Assert.Equal(["P1", "P2"], plan.NewProducts.Select(product => product.ProductCode));
        Assert.Equal(["P1", "P2"], plan.NewBatches.Select(batch => batch.BatchKey.ProductCode));
        Assert.Equal(2, plan.Preview.InvolvedProductCount);
        Assert.Empty(plan.Preview.PlanningIssues);
    }

    [Fact]
    public void EmptyClassificationReturnsEmptyPlanWithoutTrackingOrChanges()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var plan = new ExcelImportPlanner().Plan(context, Classify());

        Assert.Empty(plan.NewProducts);
        Assert.Empty(plan.UpdatedProducts);
        Assert.Empty(plan.UnchangedProducts);
        Assert.Empty(plan.NewBatches);
        Assert.Empty(plan.UpdatedBatches);
        Assert.Empty(plan.UnchangedBatches);
        Assert.Equal(0, plan.Preview.InvolvedProductCount);
        Assert.Equal(0, plan.Preview.NormalBatchKeyCount);
        Assert.False(plan.HasChanges);
        Assert.False(plan.Preview.HasChanges);
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("", "missing_arrival_quantity")]
    [InlineData("-1", "invalid_arrival_quantity")]
    [InlineData("1.5", "invalid_arrival_quantity")]
    [InlineData("1e2", "invalid_arrival_quantity")]
    [InlineData("2147483648", "invalid_arrival_quantity")]
    public void InvalidArrivalValuesAreReportedAndDoNotCreateBatchPlans(string arrival, string code)
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "P", "商品", "B", 1, 1, "excel");
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(2, code: "P", cumulativeArrival: arrival)));

        Assert.Empty(plan.NewBatches);
        Assert.Empty(plan.UpdatedBatches);
        var issue = Assert.Single(plan.Preview.PlanningIssues);
        Assert.Equal(code, issue.Code);
        Assert.Equal("P", issue.ProductCode);
        Assert.Equal(2, issue.ExcelRowNumber);
        Assert.NotEmpty(issue.SafeSummary);
    }

    [Theory]
    [InlineData("0", "invalid_shelf_life_value")]
    [InlineData("-1", "invalid_shelf_life_value")]
    [InlineData("1.5", "invalid_shelf_life_value")]
    [InlineData("1e2", "invalid_shelf_life_value")]
    [InlineData("2147483648", "invalid_shelf_life_value")]
    public void InvalidShelfLifeValuesAreReportedAndDoNotCreateBatchPlans(string shelfLife, string code)
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "P", "商品", "B", 1, 1, "excel");
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(2, code: "P", shelfLife: shelfLife)));

        Assert.Empty(plan.NewBatches);
        Assert.Empty(plan.UpdatedBatches);
        var issue = Assert.Single(plan.Preview.PlanningIssues);
        Assert.Equal(code, issue.Code);
        Assert.Equal("P", issue.ProductCode);
        Assert.Equal(2, issue.ExcelRowNumber);
    }

    [Fact]
    public void MissingStockBlocksNewProductAndItsBatchButReportsTheSourceRow()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();

        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(22, code: "NEW", stock: null, cumulativeArrival: "4")));

        Assert.Empty(plan.NewProducts);
        Assert.Empty(plan.NewBatches);
        var issue = Assert.Single(plan.Preview.PlanningIssues);
        Assert.Equal("missing_stock_quantity", issue.Code);
        Assert.Equal("NEW", issue.ProductCode);
        Assert.Equal(22, issue.ExcelRowNumber);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void ExistingStockOnlyUpdateKeepsItsExcelSourceRow()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "P", "商品", "B", 1, 1, "manual");
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(Row(42, code: "P", expiry: "bad", stock: "3")));

        var update = Assert.Single(plan.UpdatedProducts);
        Assert.Equal([42], update.SourceExcelRowNumbers);
        Assert.Equal(["ExcelStockQty", "EffectiveStockQty", "EffectiveStockSource"], update.FieldChanges.Select(change => change.FieldName));
        Assert.Equal(1, update.FieldChanges[0].Before);
        Assert.Equal(3, update.FieldChanges[0].After);
        Assert.Empty(plan.NewBatches);
        Assert.Empty(plan.UpdatedBatches);
    }

    [Fact]
    public void InvalidStockDoesNotBecomeZeroAndOnlyExistingProductOtherFieldsCanPlan()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "EXISTING", "旧", "旧条码", 9, 9, "manual");
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(
                Row(2, code: "EXISTING", name: "新", barcode: "新条码", stock: "-1"),
                Row(3, code: "NEW", stock: "0.5", cumulativeArrival: "2")));

        var existingUpdate = Assert.Single(plan.UpdatedProducts);
        Assert.Equal(["CurrentName", "CurrentBarcode"], existingUpdate.FieldChanges.Select(change => change.FieldName));
        Assert.DoesNotContain(plan.NewProducts, item => item.ProductCode == "NEW");
        Assert.DoesNotContain(plan.NewBatches, item => item.BatchKey.ProductCode == "NEW");
        Assert.Equal(
            ["EXISTING", "NEW"],
            plan.Preview.PlanningIssues.Select(issue => issue.ProductCode));
        Assert.All(plan.Preview.PlanningIssues, issue => Assert.Equal("invalid_stock_quantity", issue.Code));
        Assert.DoesNotContain(plan.Preview.PlanningIssues, issue => issue.SafeSummary.Contains("0.5", StringComparison.Ordinal));
    }

    [Fact]
    public void AmbiguousProductValuesAreReportedAndNeverPickFirstOrLast()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "EXISTING", "数据库名", "数据库条码", 2, 2, "excel");
            seed.SaveChanges();
        }

        using var context = database.Open();
        var plan = new ExcelImportPlanner().Plan(
            context,
            Classify(
                Row(2, code: "EXISTING", name: "名称一", barcode: "条码一", stock: "2", expiry: "2026-12-31"),
                Row(3, code: "EXISTING", name: "名称二", barcode: "条码二", stock: "2", expiry: "2027-12-31"),
                Row(4, code: "NEW", name: "名称一", barcode: "条码一", stock: "3", expiry: "2026-12-31"),
                Row(5, code: "NEW", name: "名称二", barcode: "条码二", stock: "3", expiry: "2027-12-31")));

        var existing = Assert.Single(plan.UnchangedProducts);
        Assert.Equal("EXISTING", existing.ProductCode);
        Assert.Empty(plan.UpdatedProducts);
        var created = Assert.Single(plan.NewProducts, item => item.ProductCode == "NEW");
        Assert.Null(created.CurrentName);
        Assert.Null(created.CurrentBarcode);
        Assert.True(created.NameIsAmbiguous);
        Assert.True(created.BarcodeIsAmbiguous);
        Assert.Equal(
            ["ambiguous_product_barcode", "ambiguous_product_name", "ambiguous_product_barcode", "ambiguous_product_name"],
            plan.Preview.PlanningIssues
                .Where(issue => issue.ProductCode is "EXISTING" or "NEW")
                .OrderBy(issue => issue.ProductCode, StringComparer.Ordinal)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .Select(issue => issue.Code)
                .ToArray());
        Assert.Equal(4, plan.NewBatches.Count);
    }

    [Fact]
    public void InheritedClassifierResultsRemainInPreviewAndConflictBatchesDoNotPlan()
    {
        using var database = SqliteTestDatabase.Create();
        using var context = database.Open();
        var classification = Classify(
            Row(2, category: "非食品", code: "SKIP"),
            Row(3, code: "ISSUE", expiry: "bad"),
            Row(4, code: "DUP", expiry: "2026-12-31"),
            Row(5, code: "DUP", expiry: "2026-12-31"),
            Row(6, code: "CONFLICT", shelfLife: "12", expiry: "2026-12-31"),
            Row(7, code: "CONFLICT", shelfLife: "24", expiry: "2026-12-31"),
            Row(8, code: "STOCK", stock: "0", expiry: "2026-12-31"),
            Row(9, code: "STOCK", stock: "1", expiry: "2027-12-31"));

        var plan = new ExcelImportPlanner().Plan(context, classification);

        Assert.Equal(classification.SkippedRows, plan.Preview.SkippedRows);
        Assert.Equal(classification.RowIssues, plan.Preview.RowIssues);
        Assert.Equal(classification.DuplicateRows, plan.Preview.DuplicateRows);
        Assert.Equal(classification.BatchConflicts, plan.Preview.BatchConflicts);
        Assert.Equal(classification.StockConflicts, plan.Preview.StockConflicts);
        Assert.Equal(0, plan.Preview.SkippedRowCount);
        Assert.Equal(2, plan.Preview.RowIssueCount);
        Assert.Equal(1, plan.Preview.DuplicateRowCount);
        Assert.Equal(1, plan.Preview.BatchConflictCount);
        Assert.Equal(1, plan.Preview.StockConflictCount);
        Assert.DoesNotContain(plan.NewBatches, batch => batch.BatchKey.ProductCode == "CONFLICT");
        Assert.DoesNotContain(plan.UpdatedBatches, batch => batch.BatchKey.ProductCode == "CONFLICT");
    }

    [Fact]
    public async Task ImportIssueRowsExposeSafeChineseRowsForClassifierAndPlannerIssues()
    {
        using var database = SqliteTestDatabase.Create();
        var sourceRows = new[]
        {
            Row(2, code: "P-ROW", expiry: "bad"),
            Row(4, code: "P-DUP"),
            Row(5, code: "P-DUP"),
            Row(6, code: "P-CONFLICT", shelfLife: "12"),
            Row(7, code: "P-CONFLICT", shelfLife: "24"),
            Row(8, code: "P-PLAN", expiry: "2026-12-31", name: "商品一", barcode: "条码一"),
            Row(9, code: "P-PLAN", expiry: "2027-12-31", name: "商品二", barcode: "条码二")
        };
        var workbook = Workbook(sourceRows);

        ImportPlan plan;
        using (var context = database.Open())
        {
            plan = new ExcelImportPlanner().Plan(
                context,
                new ExcelFileClassifier().Classify(workbook));
        }

        var path = Path.Combine(Path.GetTempPath(), "s4t10-issue-table", workbook.SourceFileName);
        var identityWorkbook = new ExcelWorkbookDto(
            workbook.SourceFileName,
            new string('a', 64),
            workbook.WorksheetName,
            workbook.NormalizedHeaders,
            workbook.Rows);
        var identity = new ImportPreviewIdentity(path, identityWorkbook, plan);
        var vm = new ImportViewModel(
            parsePreview: _ => new ImportPreviewLoadResult(workbook, plan, identity));

        await vm.SelectFileAsync(path);

        Assert.Contains(vm.IssueRows, row =>
            row.ExcelRowNumber == 2
            && row.ProductCode == "—"
            && row.IssueType == "数据格式"
            && row.Description.Contains("有效日期", StringComparison.Ordinal));
        Assert.Contains(vm.IssueRows, row =>
            row.ExcelRowNumber == 5
            && row.ProductCode == "—"
            && row.IssueType == "重复数据"
            && row.Description == "与第 4 行内容重复");
        Assert.Contains(vm.IssueRows, row =>
            row.ExcelRowNumber == 6
            && row.ProductCode == "P-CONFLICT"
            && row.IssueType == "批次冲突"
            && row.Description.Contains("第 6、7 行的保质期不一致", StringComparison.Ordinal));
        Assert.Contains(vm.IssueRows, row =>
            row.ExcelRowNumber == 8
            && row.ProductCode == "P-PLAN"
            && row.IssueType == "数据校验"
            && row.Description.Contains("商品名称", StringComparison.Ordinal));

        var visibleText = vm.IssueRows
            .SelectMany(row => new[] { row.ProductCode, row.IssueType, row.Description });
        Assert.DoesNotContain(visibleText, text =>
            text.Contains("invalid_", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ambiguous_", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CanonicalStage", StringComparison.Ordinal)
            || text.Contains("Application", StringComparison.Ordinal));
    }

    [Fact]
    public void PlannerOutputIsDeterministicWhenInputRowsAreReordered()
    {
        using var database = SqliteTestDatabase.Create();
        using (var seed = database.Open())
        {
            AddProduct(seed, "A", "旧", "旧", 1, 1, "excel");
            seed.SaveChanges();
        }

        var rows = new[]
        {
            Row(20, code: "B", expiry: "2027-12-31", production: null, stock: "2"),
            Row(10, code: "A", name: "新", barcode: "新", stock: "2", cumulativeArrival: "3"),
            Row(30, code: "B", expiry: "2026-12-31", production: null, stock: "2")
        };
        ImportPlan first;
        ImportPlan second;
        using (var context = database.Open())
        {
            first = new ExcelImportPlanner().Plan(context, Classify(rows));
        }

        using (var context = database.Open())
        {
            second = new ExcelImportPlanner().Plan(context, Classify(rows.Reverse().ToArray()));
        }

        Assert.Equal(
            first.NewProducts.Select(item => item.ProductCode),
            second.NewProducts.Select(item => item.ProductCode));
        Assert.Equal(
            first.UpdatedProducts.Select(item => (item.ProductCode, Fields: item.FieldChanges.Select(change => (change.FieldName, change.Before, change.After)).ToArray())),
            second.UpdatedProducts.Select(item => (item.ProductCode, Fields: item.FieldChanges.Select(change => (change.FieldName, change.Before, change.After)).ToArray())));
        Assert.Equal(
            first.NewBatches.Select(item => item.BatchKey),
            second.NewBatches.Select(item => item.BatchKey));
        Assert.Equal(first.Preview.PlanningIssues.Select(issue => (issue.ProductCode, issue.Code, issue.FieldName)), second.Preview.PlanningIssues.Select(issue => (issue.ProductCode, issue.Code, issue.FieldName)));
    }

    [Fact]
    public void PlanDtoDoesNotExposeForbiddenStateActionsOrEfTypes()
    {
        var dtoTypes = new[]
        {
            typeof(ImportPlan),
            typeof(ImportPreview),
            typeof(ImportPreviewIssue),
            typeof(ImportFieldChange),
            typeof(NewProductPlan),
            typeof(ProductUpdatePlan),
            typeof(ProductUnchangedPlan),
            typeof(NewBatchPlan),
            typeof(BatchUpdatePlan),
            typeof(BatchUnchangedPlan)
        };
        var forbidden = new[] { "Delete", "Stop", "Close", "Zero", "Resume", "Task", "Lifecycle", "Tracking" };
        foreach (var type in dtoTypes)
        {
            Assert.DoesNotContain(type.GetProperties(), property => forbidden.Any(word =>
                property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(type.GetProperties(), property =>
                typeof(DbContext).IsAssignableFrom(property.PropertyType));
        }
    }

    private static ExcelClassificationResult Classify(params ExcelRowDto[] rows) => new ExcelFileClassifier().Classify(Workbook(rows));

    private static ExcelWorkbookDto Workbook(IReadOnlyList<ExcelRowDto> rows) => new(
        "test.xlsx",
        string.Empty,
        "Sheet1",
        Array.Empty<string>(),
        rows);

    private static ExcelRowDto Row(
        int rowNumber,
        string? category = "食品",
        string? code = "P",
        string? barcode = "B",
        string? name = "商品",
        string? production = "2026-01-01",
        string? expiry = "2026-12-31",
        string? shelfLife = "12",
        string? shelfLifeUnit = "M",
        string? discount = "否",
        string? cumulativeArrival = "1",
        string? stock = "5") => new(
        rowNumber,
        category,
        code,
        barcode,
        name,
        production,
        expiry,
        shelfLife,
        shelfLifeUnit,
        discount,
        cumulativeArrival,
        stock);

    private static Product AddProduct(
        StoreExpiryInspector.Infrastructure.StoreDbContext context,
        string code,
        string? name,
        string? barcode,
        int excelStockQty,
        int effectiveStockQty,
        string? effectiveStockSource)
    {
        var product = new Product
        {
            ProductCode = code,
            CurrentName = name,
            CurrentBarcode = barcode,
            ExcelStockQty = excelStockQty,
            EffectiveStockQty = effectiveStockQty,
            EffectiveStockSource = effectiveStockSource
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Batch NewBatch(
        long productId,
        DateOnly? productionDate,
        DateOnly expiryDate,
        int shelfLife,
        string unit,
        int currentArrival,
        int maxArrival,
        string? discount,
        string trackingStatus = "active") => new()
        {
            ProductId = productId,
            ProductionDate = productionDate,
            ExpiryDate = expiryDate,
            ShelfLifeValue = shelfLife,
            ShelfLifeUnit = unit,
            CurrentArrivalQty = currentArrival,
            MaxArrivalQty = maxArrival,
            SourceDiscountReference = discount,
            TrackingStatus = trackingStatus,
            StopReason = trackingStatus == "stopped" ? "test" : null,
            StoppedAtUtc = trackingStatus == "stopped" ? DateTime.UtcNow : null
        };

    private static object[] ProductSnapshot(StoreExpiryInspector.Infrastructure.StoreDbContext context) => context.Products
        .AsNoTracking()
        .OrderBy(product => product.ProductCode)
        .Select(product => new
        {
            product.Id,
            product.ProductCode,
            product.CurrentName,
            product.CurrentBarcode,
            product.CategoryCode,
            product.PolicyCode,
            product.ExcelStockQty,
            product.EffectiveStockQty,
            product.EffectiveStockSource,
            product.LifecycleGeneration,
            product.IsStockZeroTerminated,
            product.LastSeenImportId,
            product.CreatedAtUtc,
            product.UpdatedAtUtc
        })
        .AsEnumerable()
        .Cast<object>()
        .ToArray();

    private static object[] BatchSnapshot(StoreExpiryInspector.Infrastructure.StoreDbContext context) => context.Batches
        .AsNoTracking()
        .OrderBy(batch => batch.ProductId)
        .ThenBy(batch => batch.ProductionDate)
        .ThenBy(batch => batch.ExpiryDate)
        .Select(batch => new
        {
            batch.Id,
            batch.ProductId,
            batch.ProductionDate,
            batch.ExpiryDate,
            batch.ShelfLifeValue,
            batch.ShelfLifeUnit,
            batch.CurrentArrivalQty,
            batch.MaxArrivalQty,
            batch.SourceDiscountReference,
            batch.LifecycleGeneration,
            batch.TrackingStatus,
            batch.StopReason,
            batch.StoppedAtUtc,
            batch.CurrentStage,
            batch.NextTriggerDate,
            batch.AttentionVersion,
            batch.HandledAttentionVersion,
            batch.LastSeenImportId,
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc
        })
        .AsEnumerable()
        .Cast<object>()
        .ToArray();
}
