using System.IO.Compression;
using System.Data.Common;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure.Excel;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ConfirmedImportExecutorTests
{
    private static readonly string[] Headers =
    [
        "商品大类",
        "商品编码",
        "商品条码",
        "商品名称",
        "生产日期",
        "有效日期",
        "保质期",
        "保质期单位",
        "是否该做临期折扣",
        "该批次累计到货数量",
        "该商品门店库存总数"
    ];

    [Fact]
    public void ExecutesConfirmedChainAtomicallyAndPersistsOnlyApprovedFacts()
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "P-OLD", "new-barcode", "新商品", "2026-01-01", "2026-12-31", "24", "D", "否", "3", "0"],
            ["食品", "P-NEW", "new-code", "新商品", "", "2027-01-31", "12", "M", "是", "2", "0"],
            ["食品", "P-BAD", "bad-code", "坏行", "2026-01-01", "bad-date", "12", "M", "否", "2", "0"],
            ["非食品", "P-SKIP", "skip-code", "跳过", "2026-01-01", "2027-01-31", "12", "M", "否", "2", "0"]
        ]);
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-OLD",
                CurrentName = "旧商品",
                CurrentBarcode = "old-barcode",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "manual"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 12, 31),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 2,
                SourceDiscountReference = "否"
            });
            seed.SaveChanges();
        }

        ImportConfirmationContract contract;
        var parsedAtUtc = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var classification = new ExcelFileClassifier().Classify(workbook);
            var plan = new ExcelImportPlanner().Plan(preview, classification);
            var identity = new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan);
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(identity).Contract);
            Assert.True(contract.Plan.HasChanges);
            Assert.Single(contract.Plan.NewProducts);
            Assert.Single(contract.Plan.UpdatedProducts);
            Assert.Single(contract.Plan.NewBatches);
            Assert.Single(contract.Plan.UpdatedBatches);
        }

        var confirmAtUtc = new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);
        var snapshotDirectory = Path.Combine(database.Directory, "snapshots");
        ConfirmedImportResult result;
        using (var context = database.Open())
        {
            result = new ConfirmedImportExecutor(utcNow: () => confirmAtUtc)
                .Execute(contract, context, snapshotDirectory, parsedAtUtc);
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        Assert.True(result.Succeeded);
        Assert.Equal(ConfirmedImportCodes.Succeeded, result.Code);
        var importId = Assert.IsType<long>(result.ImportId);
        var snapshotPath = Assert.IsType<string>(result.SnapshotPath);
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal(snapshotPath, result.SnapshotMetadata!.SnapshotPath);
        Assert.Equal(result.SnapshotMetadata.Sha256, Sha256(snapshotPath));
        Assert.True(new Infrastructure.Backups.PreImportSnapshotService().ValidateSnapshot(result.SnapshotMetadata));

        using (var verify = database.Open())
        {
            var import = Assert.Single(verify.Imports.AsNoTracking());
            Assert.Equal(importId, import.Id);
            Assert.Equal("source.xlsx", import.SourceFileName);
            Assert.Equal(contract.SourceFileSha256, import.SourceFileSha256);
            Assert.Equal(parsedAtUtc, import.ParsedAtUtc);
            Assert.Equal(confirmAtUtc, import.ConfirmedAtUtc);
            Assert.Equal(ImportStatuses.Succeeded, import.Status);
            Assert.Equal(3, import.ProductCount);
            Assert.Equal(2, import.BatchCount);
            Assert.Equal(1, import.NewProductCount);
            Assert.Equal(1, import.NewBatchCount);
            Assert.Equal(1, import.UpdatedBatchCount);
            Assert.Equal(1, import.IssueCount);
            Assert.Equal(1, import.UnsupportedCategoryCount);
            Assert.Equal(0, import.NewTaskProductCount);
            Assert.Equal(snapshotPath, import.PreImportSnapshotPath);
            Assert.False(import.IsUndone);
            Assert.Null(import.UndoneAtUtc);

            Assert.Single(verify.BackupRecords.AsNoTracking());
            var backup = verify.BackupRecords.AsNoTracking().Single();
            Assert.Equal("pre_import", backup.BackupType);
            Assert.Equal(snapshotPath, backup.FilePath);
            Assert.Equal(result.SnapshotMetadata.Sha256, backup.Sha256);
            Assert.Equal("verified", backup.VerificationStatus);

            var workbook = Assert.Single(verify.ImportWorkbooks.AsNoTracking());
            Assert.Equal(contract.SourceFileName, workbook.OriginalFileName);
            Assert.Equal(contract.WorkbookBytes.ToArray(), workbook.Content);
            Assert.Equal(contract.SourceFileSha256, workbook.Sha256);
            Assert.Equal(confirmAtUtc, workbook.SavedAtUtc);

            var issue = Assert.Single(verify.ImportIssues.AsNoTracking());
            Assert.Equal(importId, issue.ImportId);
            Assert.Equal(4, issue.RowNumber);
            Assert.Equal("invalid_expiry_date", issue.IssueType);
            Assert.Equal("有效日期", issue.FieldName);
            Assert.Contains("无法", issue.SafeSummary);

            var oldProduct = verify.Products.AsNoTracking().Single(product => product.ProductCode == "P-OLD");
            Assert.Equal("新商品", oldProduct.CurrentName);
            Assert.Equal("new-barcode", oldProduct.CurrentBarcode);
            Assert.Equal(0, oldProduct.ExcelStockQty);
            Assert.Equal(0, oldProduct.EffectiveStockQty);
            Assert.Equal("excel", oldProduct.EffectiveStockSource);
            Assert.Equal(importId, oldProduct.LastSeenImportId);
            Assert.False(oldProduct.IsStockZeroTerminated);
            Assert.Equal(0, oldProduct.LifecycleGeneration);

            var newProduct = verify.Products.AsNoTracking().Single(product => product.ProductCode == "P-NEW");
            Assert.Equal("food", newProduct.CategoryCode);
            Assert.Equal("food_v1", newProduct.PolicyCode);
            Assert.Equal(0, newProduct.ExcelStockQty);
            Assert.Equal(0, newProduct.EffectiveStockQty);
            Assert.Equal("excel", newProduct.EffectiveStockSource);
            Assert.Equal(importId, newProduct.LastSeenImportId);
            Assert.False(newProduct.IsStockZeroTerminated);

            var oldBatch = verify.Batches.AsNoTracking().Single(batch => batch.ProductId == oldProduct.Id);
            Assert.Equal(24, oldBatch.ShelfLifeValue);
            Assert.Equal("D", oldBatch.ShelfLifeUnit);
            Assert.Equal(3, oldBatch.CurrentArrivalQty);
            Assert.Equal(3, oldBatch.MaxArrivalQty);
            Assert.Equal(importId, oldBatch.LastSeenImportId);
            Assert.Equal("active", oldBatch.TrackingStatus);
            Assert.Equal("none", oldBatch.CurrentStage);
            Assert.Equal(0, oldBatch.LifecycleGeneration);

            var newBatches = verify.Batches.AsNoTracking()
                .Where(batch => batch.ProductId == newProduct.Id)
                .ToArray();
            var newBatch = Assert.Single(newBatches);
            Assert.Equal(2, newBatch.CurrentArrivalQty);
            Assert.Equal(2, newBatch.MaxArrivalQty);
            Assert.Equal(importId, newBatch.LastSeenImportId);

            Assert.Empty(verify.Tasks.AsNoTracking());
            Assert.Empty(verify.Drafts.AsNoTracking());
            Assert.Empty(verify.Inspections.AsNoTracking());
            Assert.Empty(verify.InventoryAdjustments.AsNoTracking());
            Assert.Empty(verify.LifecycleEvents.AsNoTracking());
        }
    }

    [Fact]
    public void RejectsStalePlanAfterSnapshotAndLeavesSnapshotWithoutWrites()
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "P", "B", "商品", "2026-01-01", "2026-12-31", "12", "M", "否", "1", "2"]
        ]);
        using (var seed = database.Open())
        {
            seed.Products.Add(new Product
            {
                ProductCode = "P",
                CurrentName = "商品",
                CurrentBarcode = "B",
                ExcelStockQty = 1,
                EffectiveStockQty = 1,
                EffectiveStockSource = "excel"
            });
            seed.SaveChanges();
        }

        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        using (var mutate = database.Open())
        {
            mutate.Products.Single(product => product.ProductCode == "P").ExcelStockQty = 99;
            mutate.SaveChanges();
        }

        using var execute = database.Open();
        var result = new ConfirmedImportExecutor().Execute(
            contract,
            execute,
            Path.Combine(database.Directory, "snapshots"),
            new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));

        Assert.False(result.Succeeded);
        Assert.Equal(ConfirmedImportCodes.StalePlan, result.Code);
        var snapshotPath = Assert.IsType<string>(result.SnapshotPath);
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal(0, execute.Imports.AsNoTracking().Count());
        Assert.Equal(1, execute.Products.AsNoTracking().Count());
        Assert.Equal(99, execute.Products.AsNoTracking().Single().ExcelStockQty);
        Assert.False(execute.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("changed", ConfirmedImportCodes.FileChanged)]
    [InlineData("missing", ConfirmedImportCodes.FileMissing)]
    [InlineData("locked", ConfirmedImportCodes.FileUnavailable)]
    public void RejectsChangedOrUnavailableSourceBeforeCreatingSnapshot(
        string sourceState,
        string expectedCode)
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "P", "B", "商品", "2026-01-01", "2026-12-31", "12", "M", "否", "1", "2"]
        ]);
        using (var seed = database.Open())
        {
            seed.Products.Add(new Product
            {
                ProductCode = "P",
                CurrentName = "旧商品",
                CurrentBarcode = "旧条码",
                ExcelStockQty = 1,
                EffectiveStockQty = 1,
                EffectiveStockSource = "excel"
            });
            seed.SaveChanges();
        }

        ImportConfirmationContract contract;
        ProductState beforeProduct;
        using (var preview = database.Open())
        {
            beforeProduct = ReadProduct(preview, "P");
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        var snapshotDirectory = Path.Combine(database.Directory, "snapshots");
        ConfirmedImportResult result;
        using (var execute = database.Open())
        {
            if (sourceState == "changed")
            {
                File.WriteAllBytes(source.Path, [1, 2, 3]);
            }
            else if (sourceState == "missing")
            {
                File.Delete(source.Path);
            }

            if (sourceState == "locked")
            {
                using var sourceLock = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.None);
                result = new ConfirmedImportExecutor().Execute(
                    contract,
                    execute,
                    snapshotDirectory,
                    new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));
            }
            else
            {
                result = new ConfirmedImportExecutor().Execute(
                    contract,
                    execute,
                    snapshotDirectory,
                    new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));
            }

            Assert.Equal(0, execute.Imports.AsNoTracking().Count());
            Assert.Equal(0, execute.BackupRecords.AsNoTracking().Count());
            Assert.Equal(0, execute.ImportIssues.AsNoTracking().Count());
            Assert.Equal(0, execute.ImportWorkbooks.AsNoTracking().Count());
            Assert.Equal(1, execute.Products.AsNoTracking().Count());
            Assert.Equal(0, execute.Batches.AsNoTracking().Count());
        }

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.SnapshotPath);
        Assert.False(Directory.Exists(snapshotDirectory));
        using var verify = database.Open();
        Assert.Equal(beforeProduct, ReadProduct(verify, "P"));
        Assert.Empty(verify.Batches.AsNoTracking());
        Assert.Empty(verify.Imports.AsNoTracking());
        Assert.Empty(verify.BackupRecords.AsNoTracking());
        Assert.Empty(verify.ImportIssues.AsNoTracking());
        Assert.Empty(verify.ImportWorkbooks.AsNoTracking());
    }

    [Fact]
    public void RejectsSnapshotDestinationFileWithoutStartingFormalWrites()
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "P", "B", "新商品", "2026-01-01", "2026-12-31", "12", "M", "否", "2", "2"]
        ]);
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P",
                CurrentName = "旧商品",
                CurrentBarcode = "旧条码",
                ExcelStockQty = 1,
                EffectiveStockQty = 1,
                EffectiveStockSource = "manual"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 12, 31),
                ShelfLifeValue = 6,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                SourceDiscountReference = "否"
            });
            seed.SaveChanges();
        }

        ProductState beforeProduct;
        BatchState beforeBatch;
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            beforeProduct = ReadProduct(preview, "P");
            beforeBatch = ReadBatch(preview, "P");
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        var blockedDestination = Path.Combine(database.Directory, "snapshot-target");
        File.WriteAllText(blockedDestination, "not a directory");
        using (var execute = database.Open())
        {
            var result = new ConfirmedImportExecutor().Execute(
                contract,
                execute,
                blockedDestination,
                new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));

            Assert.False(result.Succeeded);
            Assert.Equal(ConfirmedImportCodes.SnapshotFailed, result.Code);
            Assert.Null(result.ImportId);
            Assert.Null(result.SnapshotPath);
            Assert.Empty(execute.Imports.AsNoTracking());
            Assert.Empty(execute.BackupRecords.AsNoTracking());
            Assert.Empty(execute.ImportIssues.AsNoTracking());
            Assert.Empty(execute.ImportWorkbooks.AsNoTracking());
            Assert.Equal(beforeProduct, ReadProduct(execute, "P"));
            Assert.Equal(beforeBatch, ReadBatch(execute, "P"));
        }

        Assert.True(File.Exists(blockedDestination));
        Assert.False(Directory.Exists(blockedDestination));
    }

    [Fact]
    public void RejectsDirtyExecutionContextWithoutChangingPendingCallerState()
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "P", "B", "新商品", "2026-01-01", "2026-12-31", "12", "M", "否", "1", "2"]
        ]);
        using (var seed = database.Open())
        {
            seed.Products.Add(new Product
            {
                ProductCode = "P",
                CurrentName = "旧商品",
                CurrentBarcode = "旧条码",
                ExcelStockQty = 1,
                EffectiveStockQty = 1,
                EffectiveStockSource = "excel"
            });
            seed.SaveChanges();
        }

        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        var snapshotDirectory = Path.Combine(database.Directory, "snapshots");
        using (var execute = database.Open())
        {
            var tracked = execute.Products.Single(product => product.ProductCode == "P");
            tracked.CurrentName = "调用方未提交的修改";
            Assert.Equal(EntityState.Modified, execute.Entry(tracked).State);

            var result = new ConfirmedImportExecutor().Execute(
                contract,
                execute,
                snapshotDirectory,
                new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));

            Assert.False(result.Succeeded);
            Assert.Equal(ConfirmedImportCodes.InvalidContract, result.Code);
            Assert.Null(result.SnapshotPath);
            Assert.Equal("调用方未提交的修改", tracked.CurrentName);
            Assert.Equal(EntityState.Modified, execute.Entry(tracked).State);
            Assert.Single(execute.ChangeTracker.Entries());
            Assert.Empty(execute.Imports.AsNoTracking());
        }

        Assert.False(Directory.Exists(snapshotDirectory));
        using var verify = database.Open();
        Assert.Equal("旧商品", verify.Products.AsNoTracking().Single().CurrentName);
        Assert.Empty(verify.Imports.AsNoTracking());
    }

    [Fact]
    public void StockConflictKeepsInventoryUnchosenAndLeavesAbsentProductsUntouched()
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "A", "A-BAR", "商品-A", "2026-01-01", "2026-12-31", "12", "M", "否", "1", "0"],
            ["食品", "A", "A-BAR", "商品-A", "2026-01-01", "2027-12-31", "12", "M", "否", "2", "2"]
        ]);
        using (var seed = database.Open())
        {
            foreach (var code in new[] { "A", "B", "C" })
            {
                var product = new Product
                {
                    ProductCode = code,
                    CurrentName = "商品-" + code,
                    CurrentBarcode = code == "A" ? "A-BAR" : "条码-" + code,
                    ExcelStockQty = 5,
                    EffectiveStockQty = 5,
                    EffectiveStockSource = "manual"
                };
                seed.Products.Add(product);
                seed.SaveChanges();
                seed.Batches.Add(new Batch
                {
                    ProductId = product.Id,
                    ProductionDate = new DateOnly(2026, 1, 1),
                    ExpiryDate = new DateOnly(2028, 12, 31),
                    ShelfLifeValue = 12,
                    ShelfLifeUnit = "M",
                    CurrentArrivalQty = 3,
                    MaxArrivalQty = 3,
                    SourceDiscountReference = "否"
                });
                seed.SaveChanges();
            }
        }

        ProductState beforeB;
        ProductState beforeC;
        BatchState beforeBatchB;
        BatchState beforeBatchC;
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            beforeB = ReadProduct(preview, "B");
            beforeC = ReadProduct(preview, "C");
            beforeBatchB = ReadBatch(preview, "B");
            beforeBatchC = ReadBatch(preview, "C");
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(workbook));
            Assert.Single(plan.Preview.StockConflicts);
            Assert.Empty(plan.UpdatedProducts);
            Assert.Single(plan.UnchangedProducts);
            Assert.Equal(2, plan.NewBatches.Count);
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        using (var execute = database.Open())
        {
            var result = new ConfirmedImportExecutor(utcNow: () => new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc))
                .Execute(
                    contract,
                    execute,
                    Path.Combine(database.Directory, "snapshots"),
                    new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));
            Assert.True(result.Succeeded);
            Assert.Equal(2, execute.ImportIssues.AsNoTracking().Count());
        }

        using (var verify = database.Open())
        {
            var import = Assert.Single(verify.Imports.AsNoTracking());
            var productA = verify.Products.AsNoTracking().Single(product => product.ProductCode == "A");
            Assert.Equal(5, productA.ExcelStockQty);
            Assert.Equal(5, productA.EffectiveStockQty);
            Assert.Equal("manual", productA.EffectiveStockSource);
            Assert.Equal(import.Id, productA.LastSeenImportId);
            Assert.Equal(beforeB, ReadProduct(verify, "B"));
            Assert.Equal(beforeC, ReadProduct(verify, "C"));
            Assert.Equal(beforeBatchB, ReadBatch(verify, "B"));
            Assert.Equal(beforeBatchC, ReadBatch(verify, "C"));
            Assert.Equal(3, verify.Batches.AsNoTracking().Count(batch => batch.ProductId == productA.Id));
            Assert.Equal(
                2,
                verify.Batches.AsNoTracking().Count(batch => batch.ProductId == productA.Id && batch.LastSeenImportId == import.Id));
            Assert.Null(verify.Batches.AsNoTracking().Single(batch =>
                batch.ProductId == productA.Id && batch.ExpiryDate == new DateOnly(2028, 12, 31)).LastSeenImportId);
        }
    }

    [Fact]
    public void BatchOnlyPlanMarksExistingProductSeenWithoutTouchingAbsentProducts()
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "A", "条码-A", "名称-A", "2026-01-01", "2026-12-31", "12", "M", "否", "3", "0"],
            ["食品", "A", "条码-B", "名称-B", "2026-01-01", "2027-12-31", "12", "M", "否", "4", "2"]
        ]);
        using (var seed = database.Open())
        {
            var productA = new Product
            {
                ProductCode = "A",
                CurrentName = null,
                CurrentBarcode = null,
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "manual"
            };
            seed.Products.Add(productA);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = productA.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 12, 31),
                ShelfLifeValue = 6,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 1,
                SourceDiscountReference = "否"
            });

            foreach (var code in new[] { "B", "C" })
            {
                var product = new Product
                {
                    ProductCode = code,
                    CurrentName = "商品-" + code,
                    CurrentBarcode = "条码-" + code,
                    ExcelStockQty = 5,
                    EffectiveStockQty = 5,
                    EffectiveStockSource = "manual"
                };
                seed.Products.Add(product);
                seed.SaveChanges();
                seed.Batches.Add(new Batch
                {
                    ProductId = product.Id,
                    ProductionDate = new DateOnly(2026, 1, 1),
                    ExpiryDate = new DateOnly(2028, 12, 31),
                    ShelfLifeValue = 12,
                    ShelfLifeUnit = "M",
                    CurrentArrivalQty = 3,
                    MaxArrivalQty = 3,
                    SourceDiscountReference = "否"
                });
                seed.SaveChanges();
            }

            seed.SaveChanges();
        }

        ProductState beforeA;
        ProductState beforeB;
        ProductState beforeC;
        BatchState beforeBatchB;
        BatchState beforeBatchC;
        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            beforeA = ReadProduct(preview, "A");
            beforeB = ReadProduct(preview, "B");
            beforeC = ReadProduct(preview, "C");
            beforeBatchB = ReadBatch(preview, "B");
            beforeBatchC = ReadBatch(preview, "C");
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(
                preview,
                new ExcelFileClassifier().Classify(workbook));
            Assert.Single(plan.Preview.StockConflicts);
            Assert.Empty(plan.UpdatedProducts);
            Assert.Empty(plan.UnchangedProducts);
            Assert.Single(plan.UpdatedBatches);
            Assert.Single(plan.NewBatches);
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        var confirmedAtUtc = new DateTime(2026, 8, 27, 9, 1, 0, DateTimeKind.Utc);
        using (var execute = database.Open())
        {
            var result = new ConfirmedImportExecutor(utcNow: () => confirmedAtUtc)
                .Execute(
                    contract,
                    execute,
                    Path.Combine(database.Directory, "snapshots"),
                    new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));
            Assert.True(result.Succeeded);
            Assert.Equal(4, execute.ImportIssues.AsNoTracking().Count());
        }

        using var verify = database.Open();
        var import = Assert.Single(verify.Imports.AsNoTracking());
        var productAAfter = verify.Products.AsNoTracking().Single(product => product.ProductCode == "A");
        Assert.Equal(beforeA with { LastSeenImportId = import.Id, UpdatedAtUtc = confirmedAtUtc }, ReadProduct(verify, "A"));
        Assert.Equal(5, productAAfter.ExcelStockQty);
        Assert.Equal("manual", productAAfter.EffectiveStockSource);
        Assert.Equal(beforeB, ReadProduct(verify, "B"));
        Assert.Equal(beforeC, ReadProduct(verify, "C"));
        Assert.Equal(beforeBatchB, ReadBatch(verify, "B"));
        Assert.Equal(beforeBatchC, ReadBatch(verify, "C"));

        var batchesA = verify.Batches.AsNoTracking()
            .Where(batch => batch.ProductId == productAAfter.Id)
            .ToArray();
        Assert.Equal(2, batchesA.Length);
        var updatedBatch = Assert.Single(batchesA, batch => batch.ExpiryDate == new DateOnly(2026, 12, 31));
        Assert.Equal(12, updatedBatch.ShelfLifeValue);
        Assert.Equal(3, updatedBatch.CurrentArrivalQty);
        Assert.Equal(3, updatedBatch.MaxArrivalQty);
        Assert.Equal(import.Id, updatedBatch.LastSeenImportId);
        var newBatch = Assert.Single(batchesA, batch => batch.ExpiryDate == new DateOnly(2027, 12, 31));
        Assert.Equal(import.Id, newBatch.LastSeenImportId);
    }

    [Theory]
    [InlineData("imports")]
    [InlineData("products")]
    [InlineData("batches")]
    [InlineData("import_issues")]
    [InlineData("import_workbooks")]
    [InlineData("backups")]
    [InlineData("commit")]
    public void SQLiteFailureAtEachWriteBoundaryRollsBackAndRetainsSnapshot(string table)
    {
        using var database = SqliteTestDatabase.Create();
        using var source = CreateSource(database.Directory, [
            ["食品", "P-OLD", "new-barcode", "新商品", "2026-01-01", "2026-12-31", "24", "D", "否", "3", "0"],
            ["食品", "P-NEW", "new-code", "新商品", "", "2027-01-31", "12", "M", "是", "2", "0"],
            ["食品", "P-BAD", "bad-code", "坏行", "2026-01-01", "bad-date", "12", "M", "否", "2", "0"]
        ]);
        using (var seed = database.Open())
        {
            var product = new Product
            {
                ProductCode = "P-OLD",
                CurrentName = "旧商品",
                CurrentBarcode = "old-barcode",
                ExcelStockQty = 5,
                EffectiveStockQty = 5,
                EffectiveStockSource = "manual"
            };
            seed.Products.Add(product);
            seed.SaveChanges();
            seed.Batches.Add(new Batch
            {
                ProductId = product.Id,
                ProductionDate = new DateOnly(2026, 1, 1),
                ExpiryDate = new DateOnly(2026, 12, 31),
                ShelfLifeValue = 12,
                ShelfLifeUnit = "M",
                CurrentArrivalQty = 1,
                MaxArrivalQty = 2,
                SourceDiscountReference = "否"
            });
            seed.SaveChanges();
        }

        ImportConfirmationContract contract;
        using (var preview = database.Open())
        {
            var workbook = new ExcelTemplateReader().Read(source.Path);
            var plan = new ExcelImportPlanner().Plan(preview, new ExcelFileClassifier().Classify(workbook));
            contract = Assert.IsType<ImportConfirmationContract>(new ImportConfirmationGuard().Confirm(
                new ImportConfirmationGuard().BindPreview(source.Path, workbook, plan)).Contract);
        }

        var commitInterceptor = table == "commit" ? new ThrowOnCommitInterceptor() : null;
        using var execute = commitInterceptor is null
            ? database.Open()
            : OpenWithInterceptor(database.Path, commitInterceptor);
        var triggerSql = table switch
        {
            "imports" => "CREATE TRIGGER fail_imports AFTER INSERT ON imports BEGIN SELECT RAISE(ABORT, 'forced failure'); END;",
            "products" => "CREATE TRIGGER fail_products AFTER INSERT ON products BEGIN SELECT RAISE(ABORT, 'forced failure'); END;",
            "batches" => "CREATE TRIGGER fail_batches AFTER INSERT ON batches BEGIN SELECT RAISE(ABORT, 'forced failure'); END;",
            "import_issues" => "CREATE TRIGGER fail_import_issues AFTER INSERT ON import_issues BEGIN SELECT RAISE(ABORT, 'forced failure'); END;",
            "import_workbooks" => "CREATE TRIGGER fail_import_workbooks AFTER INSERT ON import_workbooks BEGIN SELECT RAISE(ABORT, 'forced failure'); END;",
            "backups" => "CREATE TRIGGER fail_backups AFTER INSERT ON backups BEGIN SELECT RAISE(ABORT, 'forced failure'); END;",
            "commit" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
        };
        if (triggerSql is not null)
        {
            execute.Database.ExecuteSqlRaw(triggerSql);
        }
        var result = new ConfirmedImportExecutor().Execute(
            contract,
            execute,
            Path.Combine(database.Directory, "snapshots"),
            new DateTime(2020, 8, 27, 9, 0, 0, DateTimeKind.Utc));

        Assert.False(result.Succeeded);
        Assert.Equal(ConfirmedImportCodes.TransactionFailed, result.Code);
        Assert.Null(result.ImportId);
        if (table == "commit")
        {
            Assert.True(commitInterceptor!.Called);
        }
        var snapshotPath = Assert.IsType<string>(result.SnapshotPath);
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal(Sha256(snapshotPath), result.SnapshotMetadata is null
            ? Sha256(snapshotPath)
            : result.SnapshotMetadata.Sha256);
        Assert.Equal(0, execute.Imports.AsNoTracking().Count());
        Assert.Equal(1, execute.Products.AsNoTracking().Count());
        Assert.Equal(1, execute.Batches.AsNoTracking().Count());
        Assert.Equal(0, execute.ImportIssues.AsNoTracking().Count());
        Assert.Equal(0, execute.ImportWorkbooks.AsNoTracking().Count());
        Assert.Equal(0, execute.BackupRecords.AsNoTracking().Count());
        Assert.False(execute.ChangeTracker.HasChanges());
        execute.SaveChanges();
        Assert.Equal(0, execute.Imports.AsNoTracking().Count());
    }

    private static SourceFixture CreateSource(string parentDirectory, IReadOnlyList<string[]> rows)
    {
        var directory = Path.Combine(parentDirectory, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source.xlsx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var header = string.Join(string.Empty, Headers.Select((value, index) => InlineCell(ColumnName(index), 1, value)));
            var body = rows.Select((row, rowIndex) =>
                $"<row r=\"{rowIndex + 2}\">{string.Join(string.Empty, row.Select((value, index) => InlineCell(ColumnName(index), rowIndex + 2, value)))}</row>");
            AddEntry(archive, "xl/worksheets/sheet1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{header}</row>{string.Join(string.Empty, body)}</sheetData></worksheet>");
        }

        return new SourceFixture(directory, path);
    }

    private static string InlineCell(string column, int row, string value) =>
        $"<c r=\"{column}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBasedColumn)
    {
        var value = zeroBasedColumn + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }

        return result;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static ProductState ReadProduct(StoreExpiryInspector.Infrastructure.StoreDbContext context, string code)
    {
        var value = context.Products.AsNoTracking().Single(product => product.ProductCode == code);
        return new ProductState(
            value.Id,
            value.ProductCode,
            value.CurrentName,
            value.CurrentBarcode,
            value.CategoryCode,
            value.PolicyCode,
            value.ExcelStockQty,
            value.EffectiveStockQty,
            value.EffectiveStockSource,
            value.LifecycleGeneration,
            value.IsStockZeroTerminated,
            value.LastSeenImportId,
            value.CreatedAtUtc,
            value.UpdatedAtUtc);
    }

    private static BatchState ReadBatch(StoreExpiryInspector.Infrastructure.StoreDbContext context, string code)
    {
        var productId = context.Products.AsNoTracking().Single(product => product.ProductCode == code).Id;
        var value = context.Batches.AsNoTracking().Single(batch => batch.ProductId == productId);
        return new BatchState(
            value.Id,
            value.ProductId,
            value.ProductionDate,
            value.ExpiryDate,
            value.ShelfLifeValue,
            value.ShelfLifeUnit,
            value.CurrentArrivalQty,
            value.MaxArrivalQty,
            value.SourceDiscountReference,
            value.LifecycleGeneration,
            value.TrackingStatus,
            value.StopReason,
            value.StoppedAtUtc,
            value.CurrentStage,
            value.NextTriggerDate,
            value.AttentionVersion,
            value.HandledAttentionVersion,
            value.LastSeenImportId,
            value.CreatedAtUtc,
            value.UpdatedAtUtc);
    }

    private sealed record ProductState(
        long Id,
        string ProductCode,
        string? CurrentName,
        string? CurrentBarcode,
        string CategoryCode,
        string PolicyCode,
        int ExcelStockQty,
        int EffectiveStockQty,
        string? EffectiveStockSource,
        int LifecycleGeneration,
        bool IsStockZeroTerminated,
        long? LastSeenImportId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record BatchState(
        long Id,
        long ProductId,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate,
        int ShelfLifeValue,
        string ShelfLifeUnit,
        int CurrentArrivalQty,
        int MaxArrivalQty,
        string? SourceDiscountReference,
        int LifecycleGeneration,
        string TrackingStatus,
        string? StopReason,
        DateTime? StoppedAtUtc,
        string CurrentStage,
        DateOnly? NextTriggerDate,
        int AttentionVersion,
        int HandledAttentionVersion,
        long? LastSeenImportId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private static StoreExpiryInspector.Infrastructure.StoreDbContext OpenWithInterceptor(
        string databasePath,
        DbTransactionInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<StoreExpiryInspector.Infrastructure.StoreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new StoreExpiryInspector.Infrastructure.StoreDbContext(options);
    }

    private sealed class ThrowOnCommitInterceptor : DbTransactionInterceptor
    {
        public bool Called { get; private set; }

        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result)
        {
            Called = true;
            throw new InvalidOperationException("forced commit failure");
        }
    }

    private sealed class SourceFixture : IDisposable
    {
        public SourceFixture(string directory, string path)
        {
            Directory = directory;
            Path = path;
        }

        public string Directory { get; }

        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
