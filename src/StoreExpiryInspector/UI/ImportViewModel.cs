using System.IO;
using System.Security;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.UI;

public enum ImportPageState
{
    Initial,
    Parsing,
    PreviewReady,
    NoChanges,
    Confirming,
    Succeeded,
    Failed
}

public sealed record ImportPreviewLoadResult(
    ExcelWorkbookDto Workbook,
    ImportPlan Plan,
    ImportPreviewIdentity Identity);

public sealed class DataImportCoordinator
{
    private readonly Func<StoreDbContext> _createContext;
    private readonly string _snapshotDirectory;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<DateOnly> _businessDate;

    public DataImportCoordinator(
        Func<StoreDbContext>? createContext = null,
        string? snapshotDirectory = null,
        Func<DateTime>? utcNow = null,
        Func<DateOnly>? businessDate = null)
    {
        _createContext = createContext ?? (() => DatabaseInitializer.CreateContext());
        _snapshotDirectory = snapshotDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoreExpiryInspector",
            "backups",
            "pre-import");
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _businessDate = businessDate ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }

    public ImportPreviewLoadResult Parse(string sourceFilePath)
    {
        var fullPath = Path.GetFullPath(sourceFilePath);
        var workbook = new ExcelTemplateReader().Read(fullPath);
        var classification = new ExcelFileClassifier().Classify(workbook);
        ImportPlan plan;
        using (var context = _createContext())
        {
            plan = new ExcelImportPlanner().Plan(context, classification);
        }

        var identity = new ImportConfirmationGuard().BindPreview(fullPath, workbook, plan);
        return new(workbook, plan, identity);
    }

    public ImportConfirmationResult Confirm(ImportPreviewIdentity identity) =>
        new ImportConfirmationGuard().Confirm(identity);

    public ConfirmedImportResult Execute(
        ImportConfirmationContract contract,
        DateTime parsedAtUtc)
    {
        using var context = _createContext();
        var occurredAtUtc = AsUtc(_utcNow());
        return new ConfirmedImportLifecycleOrchestrator().Execute(
            context,
            new ConfirmedImportLifecycleRequest(
                contract,
                _snapshotDirectory,
                AsUtc(parsedAtUtc),
                _businessDate(),
                occurredAtUtc));
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed class ImportViewModel : ViewModelBase
{
    private readonly Func<string, ImportPreviewLoadResult> _parsePreview;
    private readonly Func<ImportPreviewIdentity, ImportConfirmationResult> _confirmPreview;
    private readonly Func<ImportConfirmationContract, DateTime, ConfirmedImportResult> _executeImport;
    private readonly Func<Task> _refreshDashboard;
    private readonly Func<Task> _refreshPendingTasks;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateTime> _utcNow;
    private int _operationVersion;
    private bool _isLoading;
    private bool _canConfirm;
    private bool _canRetry;
    private bool _requiresReparse;
    private bool _hasRefreshFailure;
    private ImportPageState _state = ImportPageState.Initial;
    private string _selectedFilePath = string.Empty;
    private string _selectedFileName = string.Empty;
    private string _statusMessage = "请选择要导入的 Excel 文件。";
    private string _errorMessage = string.Empty;
    private string _refreshErrorMessage = string.Empty;
    private string _lastCode = string.Empty;
    private ImportPlan? _plan;
    private ImportPreviewIdentity? _previewIdentity;
    private ImportConfirmationContract? _confirmationContract;
    private DateTime? _parsedAtUtc;
    private long? _lastImportId;

    public ImportViewModel(
        Func<string, ImportPreviewLoadResult>? parsePreview = null,
        Func<ImportPreviewIdentity, ImportConfirmationResult>? confirmPreview = null,
        Func<ImportConfirmationContract, DateTime, ConfirmedImportResult>? executeImport = null,
        Func<Task>? refreshDashboard = null,
        Func<Task>? refreshPendingTasks = null,
        Action<Exception>? logException = null,
        Func<DateTime>? utcNow = null)
    {
        var coordinator = new DataImportCoordinator();
        _parsePreview = parsePreview ?? coordinator.Parse;
        _confirmPreview = confirmPreview ?? coordinator.Confirm;
        _executeImport = executeImport ?? coordinator.Execute;
        _refreshDashboard = refreshDashboard ?? (() => Task.CompletedTask);
        _refreshPendingTasks = refreshPendingTasks ?? (() => Task.CompletedTask);
        _logException = logException;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        ConfirmCommand = new RelayCommand(_ => { _ = ConfirmAsync(); }, _ => CanConfirm && !IsLoading);
        RetryCommand = new RelayCommand(_ => { _ = RetryAsync(); }, _ => CanRetry && !IsLoading);
    }

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand RetryCommand { get; }

    public ImportPageState State => _state;

    public bool IsInitial => State == ImportPageState.Initial;

    public bool IsParsing => State == ImportPageState.Parsing;

    public bool IsPreviewReady => State == ImportPageState.PreviewReady;

    public bool IsNoChanges => State == ImportPageState.NoChanges;

    public bool IsConfirming => State == ImportPageState.Confirming;

    public bool IsSucceeded => State == ImportPageState.Succeeded;

    public bool IsFailed => State == ImportPageState.Failed;

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
            OnPropertyChanged(nameof(CanSelectFile));
            ConfirmCommand.RaiseCanExecuteChanged();
            RetryCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanSelectFile => !IsLoading;

    public bool CanConfirm
    {
        get => _canConfirm;
        private set
        {
            if (_canConfirm == value)
            {
                return;
            }

            _canConfirm = value;
            OnPropertyChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanRetry
    {
        get => _canRetry;
        private set
        {
            if (_canRetry == value)
            {
                return;
            }

            _canRetry = value;
            OnPropertyChanged();
            RetryCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedFilePath => _selectedFilePath;

    public string SelectedFileName => string.IsNullOrEmpty(_selectedFileName) ? "未选择文件" : _selectedFileName;

    public string StatusMessage => _statusMessage;

    public string ErrorMessage => _errorMessage;

    public string RefreshErrorMessage => _refreshErrorMessage;

    public string LastCode => _lastCode;

    public bool HasError => IsFailed && !string.IsNullOrEmpty(ErrorMessage);

    public bool HasRefreshError => _hasRefreshFailure;

    public bool HasPreview => _plan is not null;

    public ImportPlan? Plan => _plan;

    public ImportPreview? Preview => _plan?.Preview;

    public ImportPreviewIdentity? PreviewIdentity => _previewIdentity;

    public ImportConfirmationContract? ConfirmationContract => _confirmationContract;

    public DateTime? ParsedAtUtc => _parsedAtUtc;

    public long? LastImportId => _lastImportId;

    public string ImportIdText => LastImportId.HasValue ? $"ImportId：{LastImportId.Value}" : string.Empty;

    public int InvolvedProductCount => Preview?.InvolvedProductCount ?? 0;

    public int NormalBatchKeyCount => Preview?.NormalBatchKeyCount ?? 0;

    public int NewProductCount => Plan?.NewProductCount ?? 0;

    public int UpdatedProductCount => Plan?.UpdatedProductCount ?? 0;

    public int NewBatchCount => Plan?.NewBatchCount ?? 0;

    public int UpdatedBatchCount => Plan?.UpdatedBatchCount ?? 0;

    public int SkippedRowCount => Preview?.SkippedRowCount ?? 0;

    public int RowIssueCount => Preview?.RowIssueCount ?? 0;

    public int DuplicateRowCount => Preview?.DuplicateRowCount ?? 0;

    public int BatchConflictCount => Preview?.BatchConflictCount ?? 0;

    public int StockConflictCount => Preview?.StockConflictCount ?? 0;

    public int PlanningIssueCount => Preview?.PlanningIssueCount ?? 0;

    public bool HasChanges => Plan?.HasChanges == true;

    public string ChangeStatusText => HasChanges ? "有变化，可确认导入" : "无变化，无需确认导入";

    public bool HasWarningDetails => RowIssueCount > 0
        || DuplicateRowCount > 0
        || BatchConflictCount > 0
        || StockConflictCount > 0
        || PlanningIssueCount > 0;

    public bool HasRowIssues => RowIssueCount > 0;

    public bool HasPlanningIssues => PlanningIssueCount > 0;

    public bool HasBatchConflicts => BatchConflictCount > 0;

    public bool HasStockConflicts => StockConflictCount > 0;

    public bool TrySelectFile(string sourceFilePath)
    {
        if (!CanSelectFile)
        {
            return false;
        }

        SelectFile(sourceFilePath);
        return true;
    }

    public void SelectFile(string sourceFilePath) => _ = SelectFileAsync(sourceFilePath);

    public async Task SelectFileAsync(string sourceFilePath)
    {
        var version = Interlocked.Increment(ref _operationVersion);
        var path = GetFullPathOrOriginal(sourceFilePath);
        InvalidatePreview(path);
        IsLoading = true;
        SetState(ImportPageState.Parsing, "正在解析 Excel 并生成预览。", string.Empty);

        try
        {
            var loaded = await Task.Run(() => _parsePreview(path));
            if (!IsCurrent(version))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(loaded);
            ArgumentNullException.ThrowIfNull(loaded.Plan);
            ArgumentNullException.ThrowIfNull(loaded.Identity);
            var identityPath = Path.GetFullPath(loaded.Identity.SourceFilePath);
            if (!string.Equals(identityPath, path, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The preview identity does not match the selected file.");
            }

            _plan = loaded.Plan;
            _previewIdentity = loaded.Identity;
            _parsedAtUtc = AsUtc(_utcNow());
            _lastImportId = null;
            _confirmationContract = null;
            _requiresReparse = false;
            _errorMessage = string.Empty;
            _refreshErrorMessage = string.Empty;
            _lastCode = string.Empty;
            _hasRefreshFailure = false;
            CanConfirm = loaded.Plan.HasChanges;
            CanRetry = false;
            SetState(
                loaded.Plan.HasChanges ? ImportPageState.PreviewReady : ImportPageState.NoChanges,
                loaded.Plan.HasChanges
                    ? "预览完成，可以确认导入。"
                    : "预览没有商品或批次变化，无需确认导入。",
                string.Empty);
        }
        catch (Exception exception)
        {
            if (!IsCurrent(version))
            {
                return;
            }

            LogException(exception);
            _plan = null;
            _previewIdentity = null;
            _confirmationContract = null;
            _parsedAtUtc = null;
            _lastImportId = null;
            _requiresReparse = true;
            _hasRefreshFailure = false;
            _lastCode = "parse_failed";
            CanConfirm = false;
            CanRetry = true;
            var safeMessage = SafeParseMessage(exception);
            SetState(ImportPageState.Failed, safeMessage, safeMessage);
        }
        finally
        {
            if (IsCurrent(version))
            {
                IsLoading = false;
                NotifyPreviewProperties();
            }
        }
    }

    public async Task ConfirmAsync()
    {
        if (!CanConfirm || IsLoading || _previewIdentity is null)
        {
            return;
        }

        var version = Volatile.Read(ref _operationVersion);
        var identity = _previewIdentity;
        var parsedAtUtc = _parsedAtUtc ?? AsUtc(_utcNow());
        _confirmationContract = null;
        CanConfirm = false;
        CanRetry = false;
        _requiresReparse = false;
        SetState(ImportPageState.Confirming, "正在确认并导入，请稍候。", string.Empty);
        IsLoading = true;

        try
        {
            var confirmation = await Task.Run(() => _confirmPreview(identity));
            if (!IsCurrent(version))
            {
                return;
            }

            if (!confirmation.CanConfirm || confirmation.Contract is null)
            {
                HandleConfirmationRejected(confirmation);
                return;
            }

            var contract = confirmation.Contract;
            _confirmationContract = contract;
            NotifyPreviewProperties();
            var result = await Task.Run(() => _executeImport(contract, parsedAtUtc));
            if (!IsCurrent(version))
            {
                return;
            }

            _confirmationContract = null;
            if (!result.Succeeded)
            {
                HandleImportFailure(result);
                return;
            }

            _lastImportId = result.ImportId;
            _lastCode = result.Code;
            _errorMessage = string.Empty;
            _requiresReparse = false;
            _hasRefreshFailure = false;
            _refreshErrorMessage = string.Empty;
            CanConfirm = false;
            CanRetry = false;
            var successMessage = string.IsNullOrWhiteSpace(result.SafeUserMessage)
                ? "确认导入已成功提交。"
                : result.SafeUserMessage;
            if (result.ImportId.HasValue)
            {
                successMessage += $" ImportId：{result.ImportId.Value}";
            }

            SetState(ImportPageState.Succeeded, successMessage, string.Empty);
            await RefreshPagesAsync(version);
        }
        catch (Exception exception)
        {
            if (!IsCurrent(version))
            {
                return;
            }

            LogException(exception);
            _confirmationContract = null;
            _lastCode = "execution_failed";
            _requiresReparse = false;
            CanConfirm = false;
            CanRetry = true;
            var safeMessage = "确认导入失败，请重试或重新选择文件。";
            SetState(ImportPageState.Failed, safeMessage, safeMessage);
        }
        finally
        {
            if (IsCurrent(version))
            {
                IsLoading = false;
                NotifyPreviewProperties();
            }
        }
    }

    public async Task RetryAsync()
    {
        if (!CanRetry || IsLoading)
        {
            return;
        }

        if (_hasRefreshFailure && IsSucceeded)
        {
            var version = Volatile.Read(ref _operationVersion);
            CanRetry = false;
            IsLoading = true;
            await RefreshPagesAsync(version);
            if (IsCurrent(version))
            {
                IsLoading = false;
                NotifyPreviewProperties();
            }

            return;
        }

        if (_requiresReparse || _previewIdentity is null)
        {
            if (!string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                await SelectFileAsync(_selectedFilePath);
            }

            return;
        }

        await ConfirmAsync();
    }

    private async Task RefreshPagesAsync(int version)
    {
        var refreshFailed = false;
        try
        {
            await _refreshDashboard();
        }
        catch (Exception exception)
        {
            refreshFailed = true;
            LogException(exception);
        }

        try
        {
            await _refreshPendingTasks();
        }
        catch (Exception exception)
        {
            refreshFailed = true;
            LogException(exception);
        }

        if (!IsCurrent(version))
        {
            return;
        }

        _hasRefreshFailure = refreshFailed;
        _refreshErrorMessage = refreshFailed ? "数据已导入，但页面刷新失败" : string.Empty;
        CanRetry = refreshFailed;
        NotifyPreviewProperties();
    }

    private void HandleConfirmationRejected(ImportConfirmationResult confirmation)
    {
        _confirmationContract = null;
        _lastCode = confirmation.Code;
        var safeMessage = string.IsNullOrWhiteSpace(confirmation.SafeUserMessage)
            ? "文件身份无法确认，请重新解析并预览。"
            : confirmation.SafeUserMessage;
        _requiresReparse = confirmation.Code is ImportConfirmationCodes.FileChanged
            or ImportConfirmationCodes.FileMissing
            or ImportConfirmationCodes.FileUnavailable
            or ImportConfirmationCodes.NoChanges;
        CanConfirm = false;
        CanRetry = confirmation.Code is not ImportConfirmationCodes.NoChanges;
        SetState(
            confirmation.Code == ImportConfirmationCodes.NoChanges
                ? ImportPageState.NoChanges
                : ImportPageState.Failed,
            safeMessage,
            confirmation.Code == ImportConfirmationCodes.NoChanges ? string.Empty : safeMessage);
    }

    private void HandleImportFailure(ConfirmedImportResult result)
    {
        var safeMessage = string.IsNullOrWhiteSpace(result.SafeUserMessage)
            ? "确认导入失败，请重试或重新选择文件。"
            : result.SafeUserMessage;
        _lastCode = result.Code;
        _requiresReparse = result.Code is ConfirmedImportCodes.StalePlan
            or ConfirmedImportCodes.FileChanged
            or ConfirmedImportCodes.FileMissing
            or ConfirmedImportCodes.FileUnavailable;
        CanConfirm = false;
        CanRetry = true;
        SetState(ImportPageState.Failed, safeMessage, safeMessage);
    }

    private void InvalidatePreview(string path)
    {
        _selectedFilePath = path;
        _selectedFileName = GetFileName(path);
        _plan = null;
        _previewIdentity = null;
        _confirmationContract = null;
        _parsedAtUtc = null;
        _lastImportId = null;
        _lastCode = string.Empty;
        _requiresReparse = true;
        _hasRefreshFailure = false;
        _refreshErrorMessage = string.Empty;
        CanConfirm = false;
        CanRetry = false;
        NotifyPreviewProperties();
    }

    private void SetState(ImportPageState state, string statusMessage, string errorMessage)
    {
        _state = state;
        _statusMessage = statusMessage;
        _errorMessage = errorMessage;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ErrorMessage));
        NotifyStateProperties();
    }

    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(IsInitial));
        OnPropertyChanged(nameof(IsParsing));
        OnPropertyChanged(nameof(IsPreviewReady));
        OnPropertyChanged(nameof(IsNoChanges));
        OnPropertyChanged(nameof(IsConfirming));
        OnPropertyChanged(nameof(IsSucceeded));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasRefreshError));
        OnPropertyChanged(nameof(ImportIdText));
        ConfirmCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
    }

    private void NotifyPreviewProperties()
    {
        OnPropertyChanged(nameof(SelectedFilePath));
        OnPropertyChanged(nameof(SelectedFileName));
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewIdentity));
        OnPropertyChanged(nameof(ConfirmationContract));
        OnPropertyChanged(nameof(ParsedAtUtc));
        OnPropertyChanged(nameof(LastImportId));
        OnPropertyChanged(nameof(ImportIdText));
        OnPropertyChanged(nameof(InvolvedProductCount));
        OnPropertyChanged(nameof(NormalBatchKeyCount));
        OnPropertyChanged(nameof(NewProductCount));
        OnPropertyChanged(nameof(UpdatedProductCount));
        OnPropertyChanged(nameof(NewBatchCount));
        OnPropertyChanged(nameof(UpdatedBatchCount));
        OnPropertyChanged(nameof(SkippedRowCount));
        OnPropertyChanged(nameof(RowIssueCount));
        OnPropertyChanged(nameof(DuplicateRowCount));
        OnPropertyChanged(nameof(BatchConflictCount));
        OnPropertyChanged(nameof(StockConflictCount));
        OnPropertyChanged(nameof(PlanningIssueCount));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangeStatusText));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasWarningDetails));
        OnPropertyChanged(nameof(HasRowIssues));
        OnPropertyChanged(nameof(HasPlanningIssues));
        OnPropertyChanged(nameof(HasBatchConflicts));
        OnPropertyChanged(nameof(HasStockConflicts));
        OnPropertyChanged(nameof(RefreshErrorMessage));
        OnPropertyChanged(nameof(LastCode));
        NotifyStateProperties();
    }

    private bool IsCurrent(int version) => version == Volatile.Read(ref _operationVersion);

    private void LogException(Exception exception) => _logException?.Invoke(exception);

    private static string GetFullPathOrOriginal(string sourceFilePath)
    {
        try
        {
            return Path.GetFullPath(sourceFilePath);
        }
        catch (Exception) when (sourceFilePath is null or { Length: 0 })
        {
            return sourceFilePath ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return sourceFilePath;
        }
    }

    private static string GetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string SafeParseMessage(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "文件不存在或已移动，请重新选择 Excel 文件。",
        UnauthorizedAccessException or SecurityException => "无法读取该文件，请检查文件权限后重试。",
        InvalidDataException or FormatException => "Excel 文件格式无效或模板不完整，请选择标准 .xlsx 文件。",
        IOException => "文件暂时无法读取，请关闭占用它的程序后重试。",
        _ => "Excel 解析失败，请重试或选择其他文件。"
    };

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
