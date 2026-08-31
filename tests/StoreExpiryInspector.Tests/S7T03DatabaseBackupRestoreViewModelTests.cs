using System.Security.Cryptography;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.UI;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class S7T03DatabaseBackupRestoreViewModelTests
{
    [Fact]
    public async Task ShellExposesBackupRestorePageAndLoadsInjectedList()
    {
        var item = BackupItem("backup-shell");
        var shell = CreateShell(() => new[] { item });
        ConfigureRuntime(shell);

        Assert.True(shell.NavigateBackupRestoreCommand.CanExecute(null));
        await shell.NavigateToAsync(ShellPage.BackupRestore);
        await WaitUntil(() => shell.BackupRestore.HasLoaded);

        Assert.Equal(ShellPage.BackupRestore, shell.CurrentPage);
        Assert.Single(shell.BackupRestore.Backups);
        Assert.Equal(item.BackupId, shell.BackupRestore.Backups[0].BackupId);
    }

    [Fact]
    public async Task EmptyListReportsAnExplicitEmptyState()
    {
        var vm = CreateVm(loadBackups: () => Array.Empty<LocalDatabaseBackupListItem>());

        await vm.LoadAsync();

        Assert.True(vm.HasLoaded);
        Assert.True(vm.HasNoBackups);
        Assert.False(vm.HasError);
        Assert.Empty(vm.Backups);
    }

    [Fact]
    public async Task QueryFailureDoesNotBecomeAnEmptySuccess()
    {
        var vm = CreateVm(loadBackups: () => throw new IOException("isolated directory unavailable"));

        await vm.LoadAsync();

        Assert.False(vm.HasLoaded);
        Assert.False(vm.HasNoBackups);
        Assert.True(vm.HasError);
        Assert.Contains("加载失败", vm.ErrorMessage);
    }

    [Fact]
    public async Task BackupCallsUseCaseOnceAndDisablesDuplicateRequests()
    {
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var vm = CreateVm(
            createBackup: () =>
            {
                Interlocked.Increment(ref calls);
                started.SetResult(null);
                release.Task.GetAwaiter().GetResult();
                return SuccessfulBackup("backup-once");
            });

        var first = vm.CreateBackupAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.IsBackingUp);
            Assert.False(vm.CanCreateBackup);

            await vm.CreateBackupAsync();
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult(null);
            await first;
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SuccessfulBackupShowsIdentityTimeSizeAndRefreshesList()
    {
        var item = BackupItem("backup-success");
        var loaded = 0;
        var vm = CreateVm(
            loadBackups: () =>
            {
                Interlocked.Increment(ref loaded);
                return new[] { item };
            },
            createBackup: () => SuccessfulBackup(item.BackupId));

        await vm.CreateBackupAsync();

        Assert.Equal(1, loaded);
        Assert.Single(vm.Backups);
        Assert.Contains("备份已完成并验证", vm.StatusMessage);
        Assert.Contains(item.BackupId, vm.StatusMessage);
        Assert.Contains("bytes", vm.StatusMessage);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task FailedBackupShowsFailureWithoutSuccessMessage()
    {
        var vm = CreateVm(createBackup: () => FailedBackup(LocalDatabaseBackupCodes.ValidationFailed, "备份校验失败"));

        await vm.CreateBackupAsync();

        Assert.True(vm.HasError);
        Assert.Contains("备份校验失败", vm.ErrorMessage);
        Assert.DoesNotContain("备份已完成并验证", vm.StatusMessage);
    }

    [Fact]
    public async Task BackupCannotStartWhenMaintenanceCannotBeEntered()
    {
        var createCalls = 0;
        var leaveCalls = 0;
        var vm = CreateVm(
            createBackup: () =>
            {
                Interlocked.Increment(ref createCalls);
                return SuccessfulBackup("never");
            },
            enterMaintenance: () => Task.FromResult(false),
            leaveMaintenance: _ => Interlocked.Increment(ref leaveCalls));

        await vm.CreateBackupAsync();

        Assert.Equal(0, createCalls);
        Assert.Equal(0, leaveCalls);
        Assert.True(vm.HasError);
        Assert.Contains("未开始", vm.ErrorMessage);
    }

    [Fact]
    public async Task RestoreRequiresAnExplicitSelection()
    {
        var restoreCalls = 0;
        var vm = CreateVm(
            restore: _ =>
            {
                Interlocked.Increment(ref restoreCalls);
                return SuccessfulRestore();
            });

        await vm.RestoreSelectedAsync();

        Assert.Equal(0, restoreCalls);
        Assert.True(vm.HasError);
        Assert.Contains("选择", vm.ErrorMessage);
    }

    [Fact]
    public async Task RestoreRevalidatesTheSelectedBackupBeforeConfirmation()
    {
        var selected = BackupItem("backup-selected");
        var revalidated = selected with { CanRestore = false };
        var confirmCalls = 0;
        var restoreCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { revalidated },
            confirmRestore: _ =>
            {
                Interlocked.Increment(ref confirmCalls);
                return true;
            },
            restore: _ =>
            {
                Interlocked.Increment(ref restoreCalls);
                return SuccessfulRestore();
            });
        vm.SelectedBackup = selected;

        await vm.RestoreSelectedAsync();

        Assert.Null(vm.SelectedBackup);
        Assert.Equal(0, confirmCalls);
        Assert.Equal(0, restoreCalls);
        Assert.Contains("验证失败", vm.ErrorMessage);
    }

    [Fact]
    public async Task CancellingRestoreConfirmationDoesNotCallRestore()
    {
        var selected = BackupItem("backup-cancel");
        var confirmCalls = 0;
        var restoreCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ =>
            {
                Interlocked.Increment(ref confirmCalls);
                return false;
            },
            restore: _ =>
            {
                Interlocked.Increment(ref restoreCalls);
                return SuccessfulRestore();
            });
        vm.SelectedBackup = selected;

        await vm.RestoreSelectedAsync();

        Assert.Equal(1, confirmCalls);
        Assert.Equal(0, restoreCalls);
        Assert.Contains("已取消恢复", vm.StatusMessage);
        Assert.False(vm.IsLocked);
    }

    [Fact]
    public async Task ConfirmedRestoreCallsUseCaseWithTheFrozenPath()
    {
        var selected = BackupItem("backup-confirmed");
        var other = BackupItem("backup-other");
        string? restoredPath = null;
        DatabaseBackupRestoreViewModel? vm = null;
        vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ =>
            {
                vm!.SelectedBackup = other;
                return true;
            },
            restore: path =>
            {
                restoredPath = path;
                return SuccessfulRestore(selected.BackupId);
            });
        vm.SelectedBackup = selected;

        await vm.RestoreSelectedAsync();

        Assert.Equal(selected.BackupPath, restoredPath);
        Assert.True(vm.IsRestartRequired);
        Assert.True(vm.IsLocked);
        Assert.Contains("需要重新启动", vm.StatusMessage);
    }

    [Fact]
    public async Task SuccessfulRestoreRetainsActualRuntimeLeaseAndLocksShell()
    {
        var selected = BackupItem("backup-runtime-lease");
        IDisposable? lease = null;
        var leaveCalls = 0;
        var oldWriteCalls = 0;
        var shell = CreateShell(
            backupLoader: () => new[] { selected },
            confirmRestore: _ => true,
            backupRestorer: _ => SuccessfulRestore(selected.BackupId));
        shell.ConfigureDatabaseProtectionRuntime(
            async () =>
            {
                lease = await DatabaseRuntimeGate.EnterMaintenanceAsync();
                return lease is not null;
            },
            _ =>
            {
                Interlocked.Increment(ref leaveCalls);
                lease?.Dispose();
                lease = null;
            },
            () => { });

        await shell.NavigateToAsync(ShellPage.BackupRestore);
        await WaitUntil(() => shell.BackupRestore.HasLoaded);
        shell.BackupRestore.SelectedBackup = selected;

        try
        {
            await shell.BackupRestore.RestoreSelectedAsync();

            Assert.True(shell.BackupRestore.IsRestartRequired);
            Assert.True(shell.BackupRestore.IsLocked);
            Assert.True(DatabaseRuntimeGate.IsMaintenance);
            Assert.Equal(0, leaveCalls);
            Assert.Throws<DatabaseRuntimeStoppedException>(() => DatabaseRuntimeGate.Run(() =>
            {
                oldWriteCalls++;
                return 1;
            }));
            Assert.Equal(0, oldWriteCalls);
            Assert.False(shell.CanNavigate);
            Assert.False(shell.CanOpenSettings);

            await shell.NavigateToAsync(ShellPage.Dashboard);
            Assert.Equal(ShellPage.BackupRestore, shell.CurrentPage);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    [Fact]
    public async Task RestoreInProgressRejectsDuplicateRestore()
    {
        var selected = BackupItem("backup-busy");
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ => true,
            restore: _ =>
            {
                Interlocked.Increment(ref restoreCalls);
                started.SetResult(null);
                release.Task.GetAwaiter().GetResult();
                return SuccessfulRestore(selected.BackupId);
            });
        vm.SelectedBackup = selected;

        var first = vm.RestoreSelectedAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.IsRestoring);
            Assert.False(vm.CanRestore);

            await vm.RestoreSelectedAsync();
            Assert.Equal(1, restoreCalls);
        }
        finally
        {
            release.TrySetResult(null);
            await first;
        }

        Assert.True(vm.IsRestartRequired);
    }

    [Fact]
    public async Task RestoreSafeFailureReleasesMaintenanceAndAllowsRetry()
    {
        var selected = BackupItem("backup-safe-failure");
        var leaveCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ => true,
            restore: _ => FailedRestore(DatabaseRestoreCodes.FinalValidationFailed, "正式库已安全回退"),
            leaveMaintenance: _ => Interlocked.Increment(ref leaveCalls));
        vm.SelectedBackup = selected;

        await vm.RestoreSelectedAsync();

        Assert.False(vm.IsCriticalFailure);
        Assert.False(vm.IsLocked);
        Assert.Equal(1, leaveCalls);
        Assert.Contains("已安全回退", vm.ErrorMessage);
    }

    [Fact]
    public async Task RestoreCriticalFailureLocksRuntimeAndRequiresExit()
    {
        var selected = BackupItem("backup-critical");
        var leaveCalls = 0;
        var exitCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ => true,
            restore: _ => FailedRestore(DatabaseRestoreCodes.CriticalRestoreFailure, "自动回退失败"),
            leaveMaintenance: _ => Interlocked.Increment(ref leaveCalls),
            requestExit: () => Interlocked.Increment(ref exitCalls));
        vm.SelectedBackup = selected;

        await vm.RestoreSelectedAsync();

        Assert.True(vm.IsCriticalFailure);
        Assert.True(vm.IsLocked);
        Assert.Equal(0, leaveCalls);
        Assert.True(vm.CanRequestExit);
        vm.ExitApplicationCommand.Execute(null);
        Assert.Equal(1, exitCalls);
    }

    [Fact]
    public async Task UnknownRestoreFailureCodeIsConservativelyCritical()
    {
        var selected = BackupItem("backup-unknown-code");
        var leaveCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ => true,
            restore: _ => FailedRestore("unexpected_restore_code", "未知恢复结果"),
            leaveMaintenance: _ => Interlocked.Increment(ref leaveCalls));
        vm.SelectedBackup = selected;

        await vm.RestoreSelectedAsync();

        Assert.True(vm.IsCriticalFailure);
        Assert.True(vm.IsLocked);
        Assert.Equal(0, leaveCalls);
    }

    [Fact]
    public async Task RestoreExceptionAfterMaintenanceKeepsRuntimeStopped()
    {
        var selected = BackupItem("backup-exception");
        IDisposable? lease = null;
        var leaveCalls = 0;
        var vm = CreateVm(
            loadBackups: () => new[] { selected },
            confirmRestore: _ => true,
            enterMaintenance: async () => (lease = await DatabaseRuntimeGate.EnterMaintenanceAsync()) is not null,
            leaveMaintenance: _ =>
            {
                Interlocked.Increment(ref leaveCalls);
                lease?.Dispose();
                lease = null;
            },
            restore: _ => throw new IOException("unexpected restore interruption"));
        vm.SelectedBackup = selected;

        try
        {
            await vm.RestoreSelectedAsync();

            Assert.True(vm.IsCriticalFailure);
            Assert.True(vm.IsLocked);
            Assert.Equal(0, leaveCalls);
            Assert.Throws<DatabaseRuntimeStoppedException>(() => DatabaseRuntimeGate.Run(() => 1));
        }
        finally
        {
            lease?.Dispose();
        }
    }

    [Fact]
    public async Task RuntimeGateWaitsForActiveDatabaseOperationBeforeMaintenance()
    {
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Task.Run(() => DatabaseRuntimeGate.Run(() =>
        {
            started.SetResult(null);
            release.Task.GetAwaiter().GetResult();
            return 1;
        }));
        IDisposable? lease = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var entering = DatabaseRuntimeGate.EnterMaintenanceAsync();
            await Task.Delay(10);
            Assert.False(entering.IsCompleted);

            release.TrySetResult(null);
            lease = await entering.WaitAsync(TimeSpan.FromSeconds(5));
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, DatabaseRuntimeGate.ActiveOperations);
        }
        finally
        {
            release.TrySetResult(null);
            lease?.Dispose();
            await operation;
        }
    }

    [Fact]
    public async Task DatabaseRuntimeGateRejectsNewDatabaseWorkersDuringMaintenance()
    {
        using var lease = await DatabaseRuntimeGate.EnterMaintenanceAsync();

        Assert.True(DatabaseRuntimeGate.IsMaintenance);
        Assert.Throws<DatabaseRuntimeStoppedException>(() => DatabaseRuntimeGate.Run(() => 1));
    }

    [Fact]
    public void DefaultShellProtectionCommandsRemainDisabledUntilAppRuntimeIsConfigured()
    {
        var shell = CreateShell(Array.Empty<LocalDatabaseBackupListItem>);

        Assert.False(shell.BackupRestore.CanCreateBackup);
        Assert.False(shell.BackupRestore.CanRestore);

        ConfigureRuntime(shell);
        Assert.True(shell.BackupRestore.CanCreateBackup);
    }

    [Fact]
    public async Task ProtectionMethodsRejectDirectCallsBeforeRuntimeConfiguration()
    {
        var backupCalls = 0;
        var restoreCalls = 0;
        var selected = BackupItem("backup-not-ready");
        var vm = new DatabaseBackupRestoreViewModel(
            loadBackups: () => new[] { selected },
            createBackup: () =>
            {
                Interlocked.Increment(ref backupCalls);
                return SuccessfulBackup(selected.BackupId);
            },
            restore: _ =>
            {
                Interlocked.Increment(ref restoreCalls);
                return SuccessfulRestore(selected.BackupId);
            },
            confirmRestore: _ => true,
            logException: _ => { });
        vm.SelectedBackup = selected;

        await vm.CreateBackupAsync();
        await vm.RestoreSelectedAsync();

        Assert.Equal(0, backupCalls);
        Assert.Equal(0, restoreCalls);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsLocked);
    }

    [Fact]
    public void QueryValidatesPublishedManualAndProtectionBackupsWithoutCreatingFormalDirectory()
    {
        using var database = SqliteTestDatabase.Create();
        var backupDirectory = Path.Combine(database.Directory, "backups");
        var missingFormalPath = Path.Combine(database.Directory, "missing-formal", "app.db");
        Directory.CreateDirectory(backupDirectory);
        try
        {
            var backupUseCase = new LocalDatabaseBackupUseCase();
            var manual = backupUseCase.Create(database.Path, backupDirectory);
            var protection = backupUseCase.CreatePreRestore(database.Path, backupDirectory);
            Assert.True(manual.Succeeded);
            Assert.True(protection.Succeeded);
            var sourceHash = ComputeSha256(database.Path);

            var query = new LocalDatabaseBackupQuery();
            var validItems = query.List(missingFormalPath, backupDirectory);
            Assert.Equal(2, validItems.Count);
            Assert.True(validItems[0].CreatedAtUtc >= validItems[1].CreatedAtUtc);

            Assert.NotNull(manual.BackupPath);
            using (var corrupted = new FileStream(
                manual.BackupPath!,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read))
            {
                var originalByte = corrupted.ReadByte();
                Assert.InRange(originalByte, 0, byte.MaxValue);
                corrupted.Position = 0;
                corrupted.WriteByte((byte)(originalByte ^ 0xFF));
                corrupted.Flush(flushToDisk: true);
            }

            File.Copy(manual.BackupPath!, Path.Combine(backupDirectory, "backup-unverified.db"));
            File.Copy(manual.BackupPath!, Path.Combine(backupDirectory, "pre-import-not-published.db"));
            File.Copy(manual.BackupPath!, Path.Combine(backupDirectory, "backup-x.restore-abc.db"));

            var items = query.List(missingFormalPath, backupDirectory);

            Assert.Contains(items, item => item.BackupId == protection.BackupId);
            Assert.DoesNotContain(items, item => item.BackupId == manual.BackupId);
            Assert.DoesNotContain(items, item => item.BackupId == "backup-unverified");
            Assert.DoesNotContain(items, item => item.FileName.StartsWith("pre-import-", StringComparison.Ordinal));
            Assert.DoesNotContain(items, item => item.FileName.Contains(".restore-", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(sourceHash, ComputeSha256(database.Path));
            Assert.False(Directory.Exists(Path.GetDirectoryName(missingFormalPath)!));
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void QueryDistinguishesMissingDirectoryFromUnreadableDirectoryPath()
    {
        using var database = SqliteTestDatabase.Create();
        var missingDirectory = Path.Combine(database.Directory, "does-not-exist");
        Assert.Empty(new LocalDatabaseBackupQuery().List(database.Path, missingDirectory));

        var pathToFile = Path.Combine(database.Directory, "not-a-directory");
        File.WriteAllText(pathToFile, "occupied");
        Assert.Throws<IOException>(() => new LocalDatabaseBackupQuery().List(database.Path, pathToFile));
    }

    [Fact]
    public async Task ImportLoadingBlocksNavigationToBackupPage()
    {
        var parseStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logged = 0;
        var shell = CreateShell(
            logException: _ => Interlocked.Increment(ref logged),
            backupLoader: () => Array.Empty<LocalDatabaseBackupListItem>(),
            parsePreview: path =>
            {
                parseStarted.SetResult(null);
                release.Task.GetAwaiter().GetResult();
                throw new InvalidDataException("done");
            });
        ConfigureRuntime(shell);

        var selecting = shell.Import.SelectFileAsync(Path.Combine(Path.GetTempPath(), "s7t03.xlsx"));
        try
        {
            await parseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(shell.CanNavigate);

            await shell.NavigateToAsync(ShellPage.BackupRestore);
            Assert.Equal(ShellPage.Dashboard, shell.CurrentPage);
        }
        finally
        {
            release.TrySetResult(null);
            await selecting;
        }

        Assert.True(logged >= 1);
    }

    [Fact]
    public async Task DetailActionBusyBlocksNavigationToBackupPage()
    {
        var actionStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new InspectionTaskListItem(4, 7, "商品", "SKU", "条码", "expired", 1, 2, new DateOnly(2026, 8, 31), false);
        var shell = CreateShell(
            backupLoader: () => Array.Empty<LocalDatabaseBackupListItem>(),
            dashboardLoader: () => new InspectionDashboardResult(1, 1, 0, 0, 0, new[] { item }),
            detailLoader: taskId => new InspectionTaskDetailResult(taskId, "open", 7, null, null, new InspectionTaskDetail(
                taskId,
                7,
                "商品",
                "SKU",
                "条码",
                2,
                "expired",
                new[] { new InspectionTaskItemResult(9, 10, null, new DateOnly(2026, 9, 1), "expired", 2, 1, true, 1, null) },
                Array.Empty<InspectionNormalBatchResult>(),
                null)),
            reconfirmItem: _ =>
            {
                actionStarted.SetResult(null);
                release.Task.GetAwaiter().GetResult();
                return new ReconfirmItemResult(true, 0, new InspectionDraftReadiness(1, 1, 0, 0, false, false, false, false));
            });
        ConfigureRuntime(shell);
        await shell.Dashboard.LoadAsync();
        shell.OpenDetail(item.TaskId);
        await WaitUntil(() => shell.Detail.IsOpen);

        var action = shell.Detail.ReconfirmItemAsync(shell.Detail.TaskItems[0]);
        try
        {
            await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(shell.CanNavigate);
            await shell.NavigateToAsync(ShellPage.BackupRestore);
            Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);
        }
        finally
        {
            release.TrySetResult(null);
            await action;
        }
    }

    [Fact]
    public async Task DetailToBackupNavigationLoadsBackupListAfterSaveGate()
    {
        var item = new InspectionTaskListItem(8, 7, "商品", "SKU", "条码", "expired", 1, 2, new DateOnly(2026, 8, 31), false);
        var backup = BackupItem("backup-from-detail");
        var shell = CreateShell(
            backupLoader: () => new[] { backup },
            dashboardLoader: () => new InspectionDashboardResult(1, 1, 0, 0, 0, new[] { item }),
            detailLoader: taskId => new InspectionTaskDetailResult(taskId, "open", 7, null, null, new InspectionTaskDetail(
                taskId,
                7,
                "商品",
                "SKU",
                "条码",
                2,
                "expired",
                new[] { new InspectionTaskItemResult(9, 10, null, new DateOnly(2026, 9, 1), "expired", 2, 1, false, null, null) },
                Array.Empty<InspectionNormalBatchResult>(),
                null)));
        ConfigureRuntime(shell);
        await shell.Dashboard.LoadAsync();
        shell.OpenDetail(item.TaskId);
        await WaitUntil(() => shell.Detail.IsOpen);

        await shell.NavigateToAsync(ShellPage.BackupRestore);
        await WaitUntil(() => shell.BackupRestore.HasLoaded);

        Assert.Equal(ShellPage.BackupRestore, shell.CurrentPage);
        Assert.Single(shell.BackupRestore.Backups);
    }

    [Fact]
    public async Task DraftSaveFailureKeepsDetailOpenWhenEnteringBackupPage()
    {
        var item = new InspectionTaskListItem(9, 7, "商品", "SKU", "条码", "expired", 1, 2, new DateOnly(2026, 8, 31), false);
        var shell = CreateShell(
            backupLoader: () => new[] { BackupItem("backup-save-failure") },
            dashboardLoader: () => new InspectionDashboardResult(1, 1, 0, 0, 0, new[] { item }),
            detailLoader: DetailResult,
            saveDraft: _ => throw new IOException("draft save failed"));
        ConfigureRuntime(shell);
        await shell.Dashboard.LoadAsync();
        shell.OpenDetail(item.TaskId);
        await WaitUntil(() => shell.Detail.IsOpen);

        shell.Detail.InspectorName = "未保存检查员";
        Assert.False(await shell.Detail.WaitForStableSaveAsync());
        Assert.True(shell.Detail.SaveFailed);

        await shell.NavigateToAsync(ShellPage.BackupRestore);

        Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);
        Assert.False(shell.BackupRestore.HasLoaded);
    }

    [Fact]
    public async Task UnstableDraftSaveIsSettledBeforeEnteringBackupPage()
    {
        var item = new InspectionTaskListItem(10, 7, "商品", "SKU", "条码", "expired", 1, 2, new DateOnly(2026, 8, 31), false);
        var saveStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shell = CreateShell(
            backupLoader: () => new[] { BackupItem("backup-save-wait") },
            dashboardLoader: () => new InspectionDashboardResult(1, 1, 0, 0, 0, new[] { item }),
            detailLoader: DetailResult,
            saveDraft: _ =>
            {
                saveStarted.SetResult(null);
                release.Task.GetAwaiter().GetResult();
                return new SaveDraftResult(true, 9, new InspectionDraftReadiness(1, 1, 0, 0, false, true, true, false));
            });
        ConfigureRuntime(shell);
        await shell.Dashboard.LoadAsync();
        shell.OpenDetail(item.TaskId);
        await WaitUntil(() => shell.Detail.IsOpen);

        shell.Detail.InspectorName = "等待保存检查员";
        var navigation = shell.NavigateToAsync(ShellPage.BackupRestore);
        try
        {
            await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ShellPage.InspectionDetail, shell.CurrentPage);
            Assert.False(shell.BackupRestore.HasLoaded);

            release.TrySetResult(null);
            await navigation.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntil(() => shell.BackupRestore.HasLoaded);
            Assert.Equal(ShellPage.BackupRestore, shell.CurrentPage);
        }
        finally
        {
            release.TrySetResult(null);
            await navigation;
        }
    }

    [Fact]
    public async Task HistoryEditBusyBlocksNavigationToBackupPage()
    {
        var editStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var historyItem = new InspectionHistoryListItem(1, 2, 3, "SKU", "商品", "条码", DateTime.UtcNow, 1);
        var detail = new InspectionHistoryDetail(1, 2, 3, "SKU", "商品", "条码", "expired", 2, "张三", new DateOnly(2026, 8, 31), DateTime.UtcNow, new[]
        {
            new InspectionHistoryItemDetail(4, 1, 3, 5, null, new DateOnly(2026, 9, 1), "expired", 2, 1, DateTime.UtcNow)
        });
        var shell = CreateShell(
            backupLoader: () => Array.Empty<LocalDatabaseBackupListItem>(),
            historyListLoader: () => new[] { historyItem },
            historyDetailLoader: _ => new InspectionHistoryDetailResult(1, "found", detail),
            historyEdit: _ =>
            {
                editStarted.SetResult(null);
                release.Task.GetAwaiter().GetResult();
                return new InspectionHistoryEditResult(1, 4, "changed", 1, 2, 3, DateTime.UtcNow);
            },
            confirmHistoryEdit: _ => true);
        ConfigureRuntime(shell);
        await shell.NavigateToAsync(ShellPage.History);
        await shell.History.LoadAsync();
        shell.History.OpenDetailCommand.Execute(historyItem);
        await WaitUntil(() => shell.History.IsDetailVisible && shell.History.DetailItems.Count > 0);
        shell.History.SelectedDetailItem = shell.History.DetailItems[0];
        shell.History.BeginEditCommand.Execute(null);
        shell.History.EditCheckedQtyText = "2";
        shell.History.SaveEditCommand.Execute(null);
        try
        {
            await editStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(shell.CanNavigate);
            await shell.NavigateToAsync(ShellPage.BackupRestore);
            Assert.Equal(ShellPage.History, shell.CurrentPage);
        }
        finally
        {
            release.TrySetResult(null);
            await WaitUntil(() => !shell.History.IsEditBusy);
        }
    }

    [Fact]
    public void AppWiresProtectionGateSchedulerAndCloseFallback()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "App.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "StoreExpiryInspector", "UI", "DatabaseBackupRestoreViewModel.cs"));

        Assert.Contains("DatabaseRuntimeGate.Run", app, StringComparison.Ordinal);
        Assert.Contains("DatabaseRuntimeStoppedException", app, StringComparison.Ordinal);
        Assert.Contains("await shell.Detail.WaitForStableSaveAsync()", app, StringComparison.Ordinal);
        Assert.Contains("shell.Import.IsLoading", app, StringComparison.Ordinal);
        Assert.Contains("shell.History.IsEditBusy", app, StringComparison.Ordinal);
        Assert.Contains("shell.Detail.IsActionBusy", app, StringComparison.Ordinal);
        Assert.Contains("_reminderScheduler?.Stop()", app, StringComparison.Ordinal);
        Assert.Contains("var lease = await DatabaseRuntimeGate.EnterMaintenanceAsync()", app, StringComparison.Ordinal);
        Assert.Contains("_trayIcon is null", app, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode = ShutdownMode.OnLastWindowClose", app, StringComparison.Ordinal);
        Assert.Contains("MainWindow.Hide()", app, StringComparison.Ordinal);
        Assert.Contains("MainWindow?.DataContext is ShellViewModel { IsDatabaseProtectionBusy: true }", app, StringComparison.Ordinal);

        var stopIndex = app.IndexOf("_reminderScheduler?.Stop()", StringComparison.Ordinal);
        var enterIndex = app.IndexOf("var lease = await DatabaseRuntimeGate.EnterMaintenanceAsync()", StringComparison.Ordinal);
        Assert.True(stopIndex >= 0 && stopIndex < enterIndex);

        var endStart = app.IndexOf("private void EndDatabaseMaintenance", StringComparison.Ordinal);
        Assert.True(endStart >= 0);
        var end = app[endStart..];
        Assert.Contains("if (resumeScheduler && !_explicitExit)", end, StringComparison.Ordinal);
        Assert.Contains("keepMaintenance = true", vm, StringComparison.Ordinal);
        Assert.Contains("if (entered && !keepMaintenance)", vm, StringComparison.Ordinal);
    }

    private static DatabaseBackupRestoreViewModel CreateVm(
        Func<IReadOnlyList<LocalDatabaseBackupListItem>>? loadBackups = null,
        Func<LocalDatabaseBackupResult>? createBackup = null,
        Func<string, DatabaseRestoreResult>? restore = null,
        Func<LocalDatabaseBackupListItem, bool>? confirmRestore = null,
        Func<Task<bool>>? enterMaintenance = null,
        Action<bool>? leaveMaintenance = null,
        Action? requestExit = null) => new(
        loadBackups: loadBackups ?? (() => Array.Empty<LocalDatabaseBackupListItem>()),
        createBackup: createBackup ?? (() => SuccessfulBackup("unused")),
        restore: restore ?? (_ => SuccessfulRestore()),
        confirmRestore: confirmRestore ?? (_ => false),
        enterMaintenance: enterMaintenance ?? (() => Task.FromResult(true)),
        leaveMaintenance: leaveMaintenance ?? (_ => { }),
        requestExit: requestExit ?? (() => { }),
        logException: _ => { });

    private static ShellViewModel CreateShell(
        Func<IReadOnlyList<LocalDatabaseBackupListItem>>? backupLoader = null,
        Func<InspectionDashboardResult>? dashboardLoader = null,
        Func<long, InspectionTaskDetailResult>? detailLoader = null,
        Func<string, ImportPreviewLoadResult>? parsePreview = null,
        Func<ReconfirmItemRequest, ReconfirmItemResult>? reconfirmItem = null,
        Func<IReadOnlyList<InspectionHistoryListItem>>? historyListLoader = null,
        Func<long, InspectionHistoryDetailResult>? historyDetailLoader = null,
        Func<InspectionHistoryEditRequest, InspectionHistoryEditResult>? historyEdit = null,
        Func<InspectionHistoryEditRequest, bool>? confirmHistoryEdit = null,
        Func<LocalDatabaseBackupListItem, bool>? confirmRestore = null,
        Func<string, DatabaseRestoreResult>? backupRestorer = null,
        Func<SaveDraftRequest, SaveDraftResult>? saveDraft = null,
        Action<Exception>? logException = null) => new(
        dashboardLoader: dashboardLoader ?? (() => new InspectionDashboardResult(0, 0, 0, 0, 0, Array.Empty<InspectionTaskListItem>())),
        taskLoader: request => new InspectionTaskSearchResult(Array.Empty<InspectionTaskListItem>(), 0, request.Page, request.PageSize),
        logException: logException ?? (_ => { }),
        utcNow: () => DateTime.UtcNow,
        detailLoader: detailLoader ?? (taskId => new InspectionTaskDetailResult(taskId, "not_found", null, null, null, null)),
        submit: _ => new InspectionSubmissionResult(InspectionSubmissionOutcome.AlreadySubmitted, null, 0, 0),
        historyListLoader: historyListLoader ?? (() => Array.Empty<InspectionHistoryListItem>()),
        historyDetailLoader: historyDetailLoader ?? (_ => new InspectionHistoryDetailResult(0, "not_found", null)),
        historyRevisionLoader: (_, _) => new InspectionItemRevisionHistoryResult(0, 0, "not_found", null),
        historyEdit: historyEdit ?? (_ => new InspectionHistoryEditResult(0, 0, "not_found", null, null, null, null)),
        confirmHistoryEdit: confirmHistoryEdit,
        confirmRestore: confirmRestore ?? (_ => true),
        backupLoader: backupLoader,
        backupCreator: () => SuccessfulBackup("shell-backup"),
        backupRestorer: backupRestorer ?? (_ => SuccessfulRestore()),
        importParser: parsePreview,
        saveDraft: saveDraft,
        reconfirmItem: reconfirmItem);

    private static InspectionTaskDetailResult DetailResult(long taskId) => new(
        taskId,
        "open",
        7,
        null,
        null,
        new InspectionTaskDetail(
            taskId,
            7,
            "商品",
            "SKU",
            "条码",
            2,
            "expired",
            new[] { new InspectionTaskItemResult(9, 10, null, new DateOnly(2026, 9, 1), "expired", 2, 1, false, null, null) },
            Array.Empty<InspectionNormalBatchResult>(),
            null));

    private static void ConfigureRuntime(ShellViewModel shell) => shell.ConfigureDatabaseProtectionRuntime(
        () => Task.FromResult(true),
        _ => { },
        () => { });

    private static LocalDatabaseBackupListItem BackupItem(string id) => new(
        id,
        id + ".db",
        Path.Combine(Path.GetTempPath(), id + ".db"),
        new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
        4096,
        new string('a', 64),
        new[] { "20260801000000_Initial" },
        LocalDatabaseBackupCodes.Success,
        true);

    private static LocalDatabaseBackupResult SuccessfulBackup(string id) => new(
        true,
        LocalDatabaseBackupCodes.Success,
        "本地数据库备份已创建并验证。",
        null,
        id,
        Path.Combine(Path.GetTempPath(), id + ".db"),
        Path.Combine(Path.GetTempPath(), id + ".db.metadata.json"),
        new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
        4096,
        new string('a', 64),
        new[] { "20260801000000_Initial" },
        LocalDatabaseBackupCodes.Success);

    private static LocalDatabaseBackupResult FailedBackup(string code, string summary) => new(
        false,
        code,
        summary,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        Array.Empty<string>(),
        null);

    private static DatabaseRestoreResult SuccessfulRestore(string id = "backup-restore") => new(
        true,
        DatabaseRestoreCodes.Restored,
        "本地数据库已从验证备份安全恢复。",
        id,
        "pre-restore-protection",
        Path.Combine(Path.GetTempPath(), "pre-restore-protection.db"),
        4096,
        new string('a', 64),
        new[] { "20260801000000_Initial" },
        "ok");

    private static DatabaseRestoreResult FailedRestore(string code, string summary) => new(
        false,
        code,
        summary,
        null,
        "pre-restore-protection",
        Path.Combine(Path.GetTempPath(), "pre-restore-protection.db"),
        null,
        null,
        Array.Empty<string>(),
        null);

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreExpiryInspector.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(2);
        }

        Assert.True(condition());
    }
}
