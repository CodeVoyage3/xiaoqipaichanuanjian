using System.Collections.ObjectModel;
using System.Globalization;
using StoreExpiryInspector.Application.Backups;

namespace StoreExpiryInspector.UI;

public sealed class DatabaseBackupRestoreViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<LocalDatabaseBackupListItem>> _loadBackups;
    private readonly Func<LocalDatabaseBackupResult> _createBackup;
    private readonly Func<string, DatabaseRestoreResult> _restore;
    private readonly Action<Exception>? _logException;
    private Func<Task<bool>> _enterMaintenance;
    private Action<bool> _leaveMaintenance;
    private Action _requestExit;
    private LocalDatabaseBackupListItem? _selectedBackup;
    private bool _isLoading;
    private bool _isBackingUp;
    private bool _isRestoring;
    private bool _hasLoaded;
    private bool _hasError;
    private bool _isRestartRequired;
    private bool _isCriticalFailure;
    private bool _runtimeReady;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public DatabaseBackupRestoreViewModel(
        Func<IReadOnlyList<LocalDatabaseBackupListItem>>? loadBackups = null,
        Func<LocalDatabaseBackupResult>? createBackup = null,
        Func<string, DatabaseRestoreResult>? restore = null,
        Func<LocalDatabaseBackupListItem, bool>? confirmRestore = null,
        Func<Task<bool>>? enterMaintenance = null,
        Action<bool>? leaveMaintenance = null,
        Action? requestExit = null,
        Action<Exception>? logException = null)
    {
        _loadBackups = loadBackups ?? (() => new LocalDatabaseBackupQuery().List());
        _createBackup = createBackup ?? (() => new LocalDatabaseBackupUseCase().Create());
        _restore = restore ?? (path => new DatabaseRestoreUseCase().Restore(path, true));
        _confirmRestore = confirmRestore ?? (_ => false);
        _enterMaintenance = enterMaintenance ?? EnterDefaultMaintenanceAsync;
        _leaveMaintenance = leaveMaintenance ?? LeaveDefaultMaintenance;
        _requestExit = requestExit ?? (() => { });
        _logException = logException;
        _runtimeReady = enterMaintenance is not null;

        RefreshCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => CanRefresh);
        CreateBackupCommand = new RelayCommand(_ => { _ = CreateBackupAsync(); }, _ => CanCreateBackup);
        RestoreCommand = new RelayCommand(_ => { _ = RestoreSelectedAsync(); }, _ => CanRestore);
        ExitApplicationCommand = new RelayCommand(_ => _requestExit(), _ => CanRequestExit);
    }

    private readonly Func<LocalDatabaseBackupListItem, bool> _confirmRestore;
    private IDisposable? _defaultMaintenanceLease;

    public ObservableCollection<LocalDatabaseBackupListItem> Backups { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand CreateBackupCommand { get; }

    public RelayCommand RestoreCommand { get; }

    public RelayCommand ExitApplicationCommand { get; }

    public LocalDatabaseBackupListItem? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (ReferenceEquals(_selectedBackup, value))
            {
                return;
            }

            _selectedBackup = value;
            OnPropertyChanged();
            NotifyCommands();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            NotifyCommands();
        }
    }

    public bool IsBackingUp
    {
        get => _isBackingUp;
        private set
        {
            if (_isBackingUp == value)
            {
                return;
            }

            _isBackingUp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            NotifyCommands();
        }
    }

    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (_isRestoring == value)
            {
                return;
            }

            _isRestoring = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            NotifyCommands();
        }
    }

    public bool IsBusy => IsLoading || IsBackingUp || IsRestoring;

    public bool HasLoaded => _hasLoaded;

    public bool HasError => _hasError;

    public bool IsRestartRequired => _isRestartRequired;

    public bool IsCriticalFailure => _isCriticalFailure;

    public bool IsLocked => IsRestartRequired || IsCriticalFailure;

    public bool HasNoBackups => HasLoaded && !HasError && !IsLoading && Backups.Count == 0;

    public bool CanRefresh => !IsBusy && !IsLocked;

    public bool CanCreateBackup => _runtimeReady && !IsBusy && !IsLocked;

    public bool CanRestore => _runtimeReady && !IsBusy && !IsLocked && SelectedBackup?.CanRestore == true;

    public bool CanRequestExit => !IsBusy && IsLocked;

    public string StatusMessage => _statusMessage;

    public string ErrorMessage => _errorMessage;

    public Task LoadAsync() => LoadAsync(force: false, clearMessage: true);

    public Task LoadAsync(bool force) => LoadAsync(force, clearMessage: true);

    private async Task LoadAsync(bool force, bool clearMessage)
    {
        if ((!force && !CanRefresh) || IsBusy || IsLocked)
        {
            return;
        }

        IsLoading = true;
        if (clearMessage)
        {
            SetMessage(string.Empty, string.Empty);
        }
        try
        {
            var items = await Task.Run(() => DatabaseRuntimeGate.Run(_loadBackups));
            Backups.Clear();
            foreach (var item in items)
            {
                Backups.Add(item);
            }

            SelectedBackup = null;
            _hasLoaded = true;
            OnPropertyChanged(nameof(HasLoaded));
            OnPropertyChanged(nameof(HasNoBackups));
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            Backups.Clear();
            SelectedBackup = null;
            _hasLoaded = false;
            OnPropertyChanged(nameof(HasLoaded));
            OnPropertyChanged(nameof(HasNoBackups));
            SetMessage("备份列表加载失败，请重试。", "备份列表加载失败，请重试。");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoBackups));
        }
    }

    public async Task CreateBackupAsync()
    {
        if (!CanCreateBackup)
        {
            return;
        }

        IsBackingUp = true;
        SetMessage("正在创建并验证备份，请稍候…", string.Empty);
        var entered = false;
        var succeeded = false;
        try
        {
            entered = await _enterMaintenance();
            if (!entered)
            {
                SetMessage("无法暂停数据库运行状态，备份未开始，请稍后重试。", "无法暂停数据库运行状态，备份未开始，请稍后重试。");
                return;
            }

            var result = await Task.Run(_createBackup);
            if (!result.Succeeded)
            {
                SetMessage(result.SafeSummary, result.SafeSummary);
                return;
            }

            succeeded = true;
            SetMessage(FormatBackupSuccess(result), string.Empty);
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            SetMessage("备份失败，请稍后重试。", "备份失败，请稍后重试。");
        }
        finally
        {
            if (entered)
            {
                LeaveMaintenance(resumeScheduler: true);
            }

            IsBackingUp = false;
        }

        if (succeeded)
        {
            await LoadAsync(force: true, clearMessage: false);
        }
    }

    public async Task RestoreSelectedAsync()
    {
        if (!_runtimeReady || IsBusy || IsLocked)
        {
            return;
        }

        // Keep the user's choice stable while the list is revalidated. The grid is
        // disabled by IsRestoring, but retaining a local value also protects this
        // operation from a late binding update or a refresh racing the await.
        var selectedBeforeRevalidation = SelectedBackup;
        if (selectedBeforeRevalidation?.CanRestore != true)
        {
            SetMessage("请先选择一份已验证的可恢复备份。", "请先选择一份已验证的可恢复备份。");
            return;
        }

        IsRestoring = true;
        SetMessage("正在重新验证所选备份…", string.Empty);
        var entered = false;
        var restoreStarted = false;
        var keepMaintenance = false;
        try
        {
            var items = await Task.Run(() => DatabaseRuntimeGate.Run(_loadBackups));
            var selected = items.FirstOrDefault(item =>
                string.Equals(item.BackupId, selectedBeforeRevalidation.BackupId, StringComparison.Ordinal) &&
                string.Equals(item.BackupPath, selectedBeforeRevalidation.BackupPath, StringComparison.OrdinalIgnoreCase));
            if (selected?.CanRestore != true)
            {
                SelectedBackup = null;
                SetMessage("所选备份已不存在或验证失败，未执行恢复。", "所选备份已不存在或验证失败，未执行恢复。");
                return;
            }

            SelectedBackup = selected;
            if (!_confirmRestore(selected))
            {
                SetMessage("已取消恢复，未写入当前数据。", string.Empty);
                return;
            }

            SetMessage("正在恢复，请勿关闭应用…", string.Empty);
            entered = await _enterMaintenance();
            if (!entered)
            {
                SetMessage("无法暂停数据库运行状态，恢复未开始，请稍后重试。", "无法暂停数据库运行状态，恢复未开始，请稍后重试。");
                return;
            }

            // From this point an exception leaves the state of the database
            // unknown. Keep the runtime stopped and require an explicit exit.
            restoreStarted = true;
            var result = await Task.Run(() => _restore(selected.BackupPath));
            if (result.Succeeded)
            {
                _isRestartRequired = true;
                keepMaintenance = true;
                OnPropertyChanged(nameof(IsRestartRequired));
                OnPropertyChanged(nameof(IsLocked));
                SetMessage(
                    $"恢复成功，应用需要重新启动以加载恢复后的数据。已恢复：{result.RestoredBackupId ?? selected.BackupId}。恢复前保护备份：{result.PreRestoreBackupId ?? "已创建"}。",
                    string.Empty);
                return;
            }

            if (!IsKnownSafeRestoreFailure(result.Code))
            {
                MarkCriticalFailure(result.SafeSummary);
                keepMaintenance = true;
                return;
            }

            SetMessage($"恢复未完成：{result.SafeSummary}", $"恢复未完成：{result.SafeSummary}");
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            if (restoreStarted)
            {
                MarkCriticalFailure("恢复过程发生未预期错误，无法确认当前数据库状态。");
                keepMaintenance = true;
            }
            else
            {
                SetMessage("恢复失败，当前数据未确认改变，请重试。", "恢复失败，当前数据未确认改变，请重试。");
            }
        }
        finally
        {
            if (entered && !keepMaintenance)
            {
                LeaveMaintenance(resumeScheduler: true);
            }

            IsRestoring = false;
        }
    }

    private static bool IsKnownSafeRestoreFailure(string code) => code switch
    {
        DatabaseRestoreCodes.BackupNotFound => true,
        DatabaseRestoreCodes.BackupInvalid => true,
        DatabaseRestoreCodes.HashMismatch => true,
        DatabaseRestoreCodes.IntegrityFailed => true,
        DatabaseRestoreCodes.MigrationIncompatible => true,
        DatabaseRestoreCodes.DatabaseInUse => true,
        DatabaseRestoreCodes.PreRestoreBackupFailed => true,
        DatabaseRestoreCodes.StagingFailed => true,
        DatabaseRestoreCodes.ReplaceFailed => true,
        DatabaseRestoreCodes.FinalValidationFailed => true,
        _ => false
    };

    private void MarkCriticalFailure(string summary)
    {
        _isCriticalFailure = true;
        OnPropertyChanged(nameof(IsCriticalFailure));
        OnPropertyChanged(nameof(IsLocked));
        SetMessage(
            $"严重恢复失败：{summary} 必须退出应用，并人工处理恢复前保护备份。",
            $"严重恢复失败：{summary} 必须退出应用，并人工处理恢复前保护备份。");
    }

    public void ConfigureRuntime(
        Func<Task<bool>> enterMaintenance,
        Action<bool> leaveMaintenance,
        Action requestExit)
    {
        _enterMaintenance = enterMaintenance ?? throw new ArgumentNullException(nameof(enterMaintenance));
        _leaveMaintenance = leaveMaintenance ?? throw new ArgumentNullException(nameof(leaveMaintenance));
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        _runtimeReady = true;
        ExitApplicationCommand.RaiseCanExecuteChanged();
        NotifyCommands();
    }

    private async Task<bool> EnterDefaultMaintenanceAsync()
    {
        _defaultMaintenanceLease = await DatabaseRuntimeGate.EnterMaintenanceAsync();
        return _defaultMaintenanceLease is not null;
    }

    private void LeaveDefaultMaintenance(bool resumeScheduler)
    {
        _defaultMaintenanceLease?.Dispose();
        _defaultMaintenanceLease = null;
    }

    private void LeaveMaintenance(bool resumeScheduler)
    {
        try
        {
            _leaveMaintenance(resumeScheduler);
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
        }
    }

    private string FormatBackupSuccess(LocalDatabaseBackupResult result)
    {
        var timestamp = result.CreatedAtUtc.HasValue
            ? DateTime.SpecifyKind(result.CreatedAtUtc.Value, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "未知时间";
        var size = result.FileSize?.ToString("N0", CultureInfo.InvariantCulture) ?? "未知大小";
        var identity = result.BackupId ?? result.BackupPath ?? "未知身份";
        return $"备份已完成并验证。创建时间：{timestamp}；文件大小：{size} bytes；备份身份：{identity}。";
    }

    private void SetMessage(string statusMessage, string errorMessage)
    {
        _statusMessage = statusMessage;
        _errorMessage = errorMessage;
        _hasError = !string.IsNullOrEmpty(errorMessage);
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private void NotifyCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        CreateBackupCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        ExitApplicationCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanCreateBackup));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(CanRequestExit));
    }
}
