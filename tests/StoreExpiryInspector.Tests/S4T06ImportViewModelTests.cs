using System.IO.Compression;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S4T06ImportViewModelTests
{
    [Fact]
    public void StartsWithoutAFileAndCannotConfirm()
    {
        var vm = new ImportViewModel();

        Assert.Equal(ImportPageState.Initial, vm.State);
        Assert.Equal("未选择文件", vm.SelectedFileName);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectsWorkbookAndDisplaysExistingPreviewCounts()
    {
        using var fixture = TestFixture.Create();
        var vm = new ImportViewModel(parsePreview: fixture.Coordinator.Parse);

        await vm.SelectFileAsync(fixture.Path);

        Assert.Equal(ImportPageState.PreviewReady, vm.State);
        Assert.True(vm.HasPreview);
        Assert.True(vm.CanConfirm);
        Assert.Equal("A.xlsx", vm.SelectedFileName);
        Assert.Equal(1, vm.InvolvedProductCount);
        Assert.Equal(1, vm.NormalBatchKeyCount);
        Assert.Equal(1, vm.NewProductCount);
        Assert.Equal(1, vm.NewBatchCount);
        Assert.Equal(0, vm.RowIssueCount);
        Assert.Same(vm.PreviewIdentity!.Plan, vm.Plan);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task CancellingSelectionKeepsTheCurrentPreviewWithoutParsingAgain()
    {
        using var fixture = TestFixture.Create();
        var parseCount = 0;
        var vm = new ImportViewModel(parsePreview: path =>
        {
            parseCount++;
            return fixture.Coordinator.Parse(path);
        });
        await vm.SelectFileAsync(fixture.Path);
        var identity = vm.PreviewIdentity;

        Assert.False(vm.TrySelectFile(null));

        Assert.Equal(1, parseCount);
        Assert.Same(identity, vm.PreviewIdentity);
        Assert.True(vm.CanConfirm);
        Assert.Equal(ImportPageState.PreviewReady, vm.State);
    }

    [Fact]
    public async Task AcceptingNewWorkbookInvalidatesOldIdentityBeforeTheNewReadCompletes()
    {
        using var fixture = TestFixture.Create();
        var secondPath = fixture.CreateWorkbook("B.xlsx", includeRow: true, productCode: "P-B");
        var secondReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ImportViewModel(parsePreview: path =>
        {
            if (string.Equals(path, Path.GetFullPath(secondPath), StringComparison.OrdinalIgnoreCase))
            {
                secondReadStarted.SetResult();
                releaseSecondRead.Task.GetAwaiter().GetResult();
            }

            return fixture.Coordinator.Parse(path);
        });
        await vm.SelectFileAsync(fixture.Path);
        Assert.True(vm.CanConfirm);

        var secondTask = vm.SelectFileAsync(secondPath);
        await secondReadStarted.Task;

        Assert.Equal("B.xlsx", vm.SelectedFileName);
        Assert.Equal(ImportPageState.Parsing, vm.State);
        Assert.Null(vm.PreviewIdentity);
        Assert.Null(vm.ConfirmationContract);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        releaseSecondRead.SetResult();
        await secondTask;
        Assert.Equal(Path.GetFullPath(secondPath), vm.PreviewIdentity!.SourceFilePath);
    }

    [Fact]
    public async Task AcceptedNewWorkbookImmediatelyInvalidatesOldPreviewWhenNewReadFails()
    {
        using var fixture = TestFixture.Create();
        var firstPath = fixture.Path;
        var secondPath = fixture.CreateWorkbook("B.xlsx", includeRow: false);
        var executeCount = 0;
        var vm = new ImportViewModel(
            parsePreview: path =>
            {
                if (string.Equals(path, Path.GetFullPath(secondPath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("invalid test workbook");
                }

                return fixture.Coordinator.Parse(path);
            },
            executeImport: (_, _) =>
            {
                executeCount++;
                throw new InvalidOperationException("must not execute");
            });

        await vm.SelectFileAsync(firstPath);
        var firstIdentity = vm.PreviewIdentity;
        Assert.True(vm.CanConfirm);

        await vm.SelectFileAsync(secondPath);

        Assert.Equal(ImportPageState.Failed, vm.State);
        Assert.Equal("B.xlsx", vm.SelectedFileName);
        Assert.Null(vm.PreviewIdentity);
        Assert.Null(vm.ConfirmationContract);
        Assert.False(vm.CanConfirm);
        Assert.NotSame(firstIdentity, vm.PreviewIdentity);
        Assert.Contains("Excel 文件格式无效", vm.ErrorMessage);
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        await vm.ConfirmAsync();
        Assert.Equal(0, executeCount);
    }

    [Fact]
    public async Task SuccessfulSecondPreviewCanOnlyImportTheSecondWorkbook()
    {
        using var fixture = TestFixture.Create();
        var secondPath = fixture.CreateWorkbook("B.xlsx", includeRow: true, productCode: "P-B");
        var vm = new ImportViewModel(
            parsePreview: fixture.Coordinator.Parse,
            confirmPreview: fixture.Coordinator.Confirm,
            executeImport: fixture.Coordinator.Execute,
            utcNow: () => fixture.UtcNow);

        await vm.SelectFileAsync(fixture.Path);
        var firstIdentity = vm.PreviewIdentity;
        await vm.SelectFileAsync(secondPath);

        Assert.NotSame(firstIdentity, vm.PreviewIdentity);
        Assert.Equal(Path.GetFullPath(secondPath), vm.PreviewIdentity!.SourceFilePath);
        await vm.ConfirmAsync();

        Assert.Equal(ImportPageState.Succeeded, vm.State);
        using var context = fixture.Database.Open();
        Assert.Equal("B.xlsx", context.Imports.AsNoTracking().Single().SourceFileName);
        Assert.Equal("B.xlsx", context.ImportWorkbooks.AsNoTracking().Single().OriginalFileName);
        Assert.Equal("P-B", context.Products.AsNoTracking().Single().ProductCode);
    }

    [Fact]
    public async Task LateFirstReadCannotOverwriteTheCurrentSecondSelection()
    {
        using var fixture = TestFixture.Create();
        var secondPath = fixture.CreateWorkbook("B.xlsx", includeRow: true, productCode: "P-B");
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ImportViewModel(parsePreview: path =>
        {
            if (string.Equals(path, Path.GetFullPath(fixture.Path), StringComparison.OrdinalIgnoreCase))
            {
                firstReadStarted.SetResult();
                releaseFirstRead.Task.GetAwaiter().GetResult();
            }

            return fixture.Coordinator.Parse(path);
        });

        var firstTask = vm.SelectFileAsync(fixture.Path);
        await firstReadStarted.Task;
        var secondTask = vm.SelectFileAsync(secondPath);
        await secondTask;
        Assert.Equal(Path.GetFullPath(secondPath), vm.SelectedFilePath);
        Assert.Equal("P-B", vm.Plan!.NewProducts.Single().ProductCode);

        releaseFirstRead.SetResult();
        await firstTask;

        Assert.Equal(Path.GetFullPath(secondPath), vm.SelectedFilePath);
        Assert.Equal("P-B", vm.Plan!.NewProducts.Single().ProductCode);
        Assert.Equal(Path.GetFullPath(secondPath), vm.PreviewIdentity!.SourceFilePath);
    }

    [Fact]
    public async Task GuardRejectsChangedFileAndRetryReparsesInsteadOfReusingContract()
    {
        using var fixture = TestFixture.Create();
        var executeCount = 0;
        var vm = new ImportViewModel(
            parsePreview: fixture.Coordinator.Parse,
            confirmPreview: fixture.Coordinator.Confirm,
            executeImport: (_, _) =>
            {
                executeCount++;
                throw new InvalidOperationException("must not execute");
            },
            utcNow: () => fixture.UtcNow);

        await vm.SelectFileAsync(fixture.Path);
        File.AppendAllText(fixture.Path, "changed", Encoding.UTF8);
        await vm.ConfirmAsync();

        Assert.Equal(ImportPageState.Failed, vm.State);
        Assert.Equal(ImportConfirmationCodes.FileChanged, vm.LastCode);
        Assert.False(vm.CanConfirm);
        Assert.Null(vm.ConfirmationContract);
        Assert.Equal(0, executeCount);
    }

    [Fact]
    public async Task WorkbookWithoutChangesShowsNoChangesAndCannotConfirm()
    {
        using var fixture = TestFixture.Create();
        var emptyPath = fixture.CreateWorkbook("empty.xlsx", includeRow: false);
        var vm = new ImportViewModel(parsePreview: fixture.Coordinator.Parse);

        await vm.SelectFileAsync(emptyPath);

        Assert.Equal(ImportPageState.NoChanges, vm.State);
        Assert.True(vm.HasPreview);
        Assert.False(vm.HasChanges);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ParseFailureCanRetryAndBuildsANewPreview()
    {
        using var fixture = TestFixture.Create();
        var parseCount = 0;
        var vm = new ImportViewModel(parsePreview: path =>
        {
            parseCount++;
            if (parseCount == 1)
            {
                throw new IOException("locked");
            }

            return fixture.Coordinator.Parse(path);
        });

        await vm.SelectFileAsync(fixture.Path);
        Assert.Equal(ImportPageState.Failed, vm.State);
        Assert.True(vm.CanRetry);
        Assert.Null(vm.PreviewIdentity);

        await vm.RetryAsync();

        Assert.Equal(2, parseCount);
        Assert.Equal(ImportPageState.PreviewReady, vm.State);
        Assert.True(vm.CanConfirm);
        Assert.NotNull(vm.PreviewIdentity);
    }

    [Fact]
    public async Task ExecutionFailureRetriesThroughTheGuardAndThenSucceeds()
    {
        using var fixture = TestFixture.Create();
        var confirmCount = 0;
        var executeCount = 0;
        var vm = new ImportViewModel(
            parsePreview: fixture.Coordinator.Parse,
            confirmPreview: identity =>
            {
                confirmCount++;
                return fixture.Coordinator.Confirm(identity);
            },
            executeImport: (contract, parsedAtUtc) =>
            {
                executeCount++;
                if (executeCount == 1)
                {
                    throw new IOException("temporary failure");
                }

                return fixture.Coordinator.Execute(contract, parsedAtUtc);
            },
            utcNow: () => fixture.UtcNow);

        await vm.SelectFileAsync(fixture.Path);
        await vm.ConfirmAsync();
        Assert.Equal(ImportPageState.Failed, vm.State);
        Assert.Equal("execution_failed", vm.LastCode);
        Assert.True(vm.CanRetry);

        await vm.RetryAsync();

        Assert.Equal(2, confirmCount);
        Assert.Equal(2, executeCount);
        Assert.Equal(ImportPageState.Succeeded, vm.State);
    }

    [Fact]
    public async Task CriticalStateChangesRaisePropertyChanged()
    {
        using var fixture = TestFixture.Create();
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var vm = new ImportViewModel(parsePreview: fixture.Coordinator.Parse);
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        await vm.SelectFileAsync(fixture.Path);

        Assert.Contains(nameof(ImportViewModel.State), changed);
        Assert.Contains(nameof(ImportViewModel.IsLoading), changed);
        Assert.Contains(nameof(ImportViewModel.CanConfirm), changed);
        Assert.Contains(nameof(ImportViewModel.SelectedFileName), changed);
        Assert.Contains(nameof(ImportViewModel.PreviewIdentity), changed);
        Assert.Contains(nameof(ImportViewModel.HasPreview), changed);
    }

    [Fact]
    public async Task ConcurrentConfirmAttemptsExecuteOnlyOnce()
    {
        using var fixture = TestFixture.Create();
        var executeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecute = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executeCount = 0;
        var vm = new ImportViewModel(
            parsePreview: fixture.Coordinator.Parse,
            confirmPreview: fixture.Coordinator.Confirm,
            executeImport: (contract, parsedAtUtc) =>
            {
                executeCount++;
                executeStarted.SetResult();
                releaseExecute.Task.GetAwaiter().GetResult();
                return fixture.Coordinator.Execute(contract, parsedAtUtc);
            },
            utcNow: () => fixture.UtcNow);
        await vm.SelectFileAsync(fixture.Path);

        var firstConfirm = vm.ConfirmAsync();
        await executeStarted.Task;
        var secondConfirm = vm.ConfirmAsync();
        await secondConfirm;
        Assert.Equal(1, executeCount);
        Assert.False(vm.CanConfirm);
        Assert.True(vm.IsConfirming);
        Assert.True(vm.IsLoading);
        Assert.Equal("正在导入，请稍候…", vm.StatusMessage);
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        releaseExecute.SetResult();
        await firstConfirm;
        Assert.Equal(1, executeCount);
        Assert.Equal(ImportPageState.Succeeded, vm.State);
    }

    [Fact]
    public async Task SuccessfulImportRefreshesDashboardAndTasksOnceAndCannotRepeat()
    {
        using var fixture = TestFixture.Create();
        var dashboardRefreshes = 0;
        var taskRefreshes = 0;
        var vm = new ImportViewModel(
            parsePreview: fixture.Coordinator.Parse,
            confirmPreview: fixture.Coordinator.Confirm,
            executeImport: fixture.Coordinator.Execute,
            refreshDashboard: () => { dashboardRefreshes++; return Task.CompletedTask; },
            refreshPendingTasks: () => { taskRefreshes++; return Task.CompletedTask; },
            utcNow: () => fixture.UtcNow);

        await vm.SelectFileAsync(fixture.Path);
        await vm.ConfirmAsync();

        Assert.True(vm.State == ImportPageState.Succeeded, $"{vm.LastCode}: {vm.ErrorMessage} / {vm.StatusMessage}");
        Assert.StartsWith("导入成功", vm.StatusMessage, StringComparison.Ordinal);
        Assert.False(vm.CanConfirm);
        Assert.Null(vm.ConfirmationContract);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, taskRefreshes);
        Assert.NotNull(vm.LastImportId);

        await vm.ConfirmAsync();
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, taskRefreshes);
    }

    [Fact]
    public async Task RefreshFailureKeepsSuccessfulImportAndRetryOnlyRefreshesPages()
    {
        using var fixture = TestFixture.Create();
        var dashboardRefreshes = 0;
        var taskRefreshes = 0;
        var vm = new ImportViewModel(
            parsePreview: fixture.Coordinator.Parse,
            confirmPreview: fixture.Coordinator.Confirm,
            executeImport: fixture.Coordinator.Execute,
            refreshDashboard: () =>
            {
                dashboardRefreshes++;
                throw new InvalidOperationException("dashboard unavailable");
            },
            refreshPendingTasks: () => { taskRefreshes++; return Task.CompletedTask; },
            utcNow: () => fixture.UtcNow);

        await vm.SelectFileAsync(fixture.Path);
        await vm.ConfirmAsync();

        Assert.True(vm.State == ImportPageState.Succeeded, $"{vm.LastCode}: {vm.ErrorMessage} / {vm.StatusMessage}");
        Assert.True(vm.HasRefreshError);
        Assert.True(vm.CanRetry);
        Assert.Contains("数据已导入", vm.RefreshErrorMessage);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, taskRefreshes);

        await vm.RetryAsync();
        Assert.Equal(2, dashboardRefreshes);
        Assert.Equal(2, taskRefreshes);
        Assert.True(vm.HasRefreshError);
        Assert.Equal(ImportPageState.Succeeded, vm.State);
    }

    [Fact]
    public void CoordinatorRunsTheRealXlsxSqliteImportChain()
    {
        using var fixture = TestFixture.Create();
        var loaded = fixture.Coordinator.Parse(fixture.Path);
        var confirmation = fixture.Coordinator.Confirm(loaded.Identity);

        Assert.Equal(ImportConfirmationCodes.Ready, confirmation.Code);
        Assert.NotNull(confirmation.Contract);
        var result = fixture.Coordinator.Execute(
            confirmation.Contract!,
            new DateTime(2026, 8, 28, 1, 2, 3, DateTimeKind.Utc));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ImportId);
        using var context = fixture.Database.Open();
        Assert.Single(context.Imports.AsNoTracking());
        Assert.Single(context.ImportWorkbooks.AsNoTracking());
        Assert.Empty(context.ImportIssues.AsNoTracking());
        Assert.Single(context.BackupRecords.AsNoTracking());
        Assert.Single(context.Products.AsNoTracking());
        Assert.Single(context.Batches.AsNoTracking());
        Assert.True(File.Exists(context.BackupRecords.AsNoTracking().Single().FilePath));
    }

    private sealed class TestFixture : IDisposable
    {
        private TestFixture(string directory, string path, SqliteTestDatabase database, DataImportCoordinator coordinator)
        {
            Directory = directory;
            Path = path;
            Database = database;
            Coordinator = coordinator;
        }

        public string Directory { get; }

        public string Path { get; }

        public SqliteTestDatabase Database { get; }

        public DataImportCoordinator Coordinator { get; }

        public DateTime UtcNow => new(2026, 8, 28, 1, 2, 3, DateTimeKind.Utc);

        public static TestFixture Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "StoreExpiryInspectorS4T06Tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var database = SqliteTestDatabase.Create();
            var snapshotDirectory = System.IO.Path.Combine(directory, "snapshots");
            var path = System.IO.Path.Combine(directory, "A.xlsx");
            WriteWorkbook(path, includeRow: true, productCode: "P-A");
            var coordinator = new DataImportCoordinator(
                database.Open,
                snapshotDirectory,
                () => new DateTime(2026, 8, 28, 1, 2, 3, DateTimeKind.Utc),
                () => new DateOnly(2026, 8, 28));
            return new(directory, path, database, coordinator);
        }

        public string CreateWorkbook(string fileName, bool includeRow, string productCode = "P-A")
        {
            var path = System.IO.Path.Combine(Directory, fileName);
            WriteWorkbook(path, includeRow, productCode);
            return path;
        }

        public void Dispose()
        {
            Database.Dispose();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }

        private static void WriteWorkbook(string path, bool includeRow, string productCode)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            AddEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            AddEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");

            var headers = new[]
            {
                "商品大类", "商品编码", "商品条码", "商品名称", "生产日期", "有效日期",
                "保质期", "保质期单位", "是否该做临期折扣", "该批次累计到货数量", "该商品门店库存总数"
            };
            var header = string.Concat(headers.Select((value, index) => InlineCell(index, 1, value)));
            var row = includeRow
                ? string.Concat(new[]
                {
                    "食品", productCode, $"B-{productCode[2..]}", "S4-T06 商品", "2026-01-01", "2026-12-31",
                    "12", "M", "否", "3", "5"
                }.Select((value, index) => InlineCell(index, 2, value)))
                : string.Empty;
            AddEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{header}</row>{(includeRow ? $"<row r=\"2\">{row}</row>" : string.Empty)}</sheetData></worksheet>");
        }

        private static string InlineCell(int column, int row, string value) =>
            $"<c r=\"{ColumnName(column)}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";

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
    }
}
