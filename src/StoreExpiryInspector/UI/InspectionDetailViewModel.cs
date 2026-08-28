using System.Collections.ObjectModel;
using System.Globalization;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Tasks;

namespace StoreExpiryInspector.UI;

public enum InspectionDetailPageState
{
    Initial,
    Loading,
    Open,
    Completed,
    SystemClosed,
    NotFound,
    Error
}

public sealed class InspectionDetailRowViewModel : ViewModelBase
{
    private readonly Action<InspectionDetailRowViewModel> _onChanged;
    private readonly Action<InspectionDetailRowViewModel> _onReconfirm;
    private readonly InspectionTaskItemResult _item;
    private string _checkedQtyText;
    private string _inputError = string.Empty;
    private bool _isDirty;
    private long _changeVersion;

    public InspectionDetailRowViewModel(
        InspectionTaskItemResult item,
        Action<InspectionDetailRowViewModel> onChanged,
        Action<InspectionDetailRowViewModel> onReconfirm)
    {
        ArgumentNullException.ThrowIfNull(item);
        _item = item;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _onReconfirm = onReconfirm ?? throw new ArgumentNullException(nameof(onReconfirm));
        _checkedQtyText = FormatCheckedQty(item.CheckedQty);
        ReconfirmCommand = new RelayCommand(
            _ => _onReconfirm(this),
            _ => CanReconfirm);
    }

    public long TaskItemId => _item.TaskItemId;

    public long BatchId => _item.BatchId;

    public DateOnly? ProductionDate => _item.ProductionDate;

    public DateOnly ExpiryDate => _item.ExpiryDate;

    public string Stage => _item.Stage;

    public int CurrentArrivalQty => _item.CurrentArrivalQty;

    public int AttentionVersion => _item.AttentionVersion;

    public bool RequiresReconfirmation => _item.RequiresReconfirmation;

    public InspectionLatestInspectionResult? LastInspection => _item.LastInspection;

    public string CheckedQtyText
    {
        get => _checkedQtyText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_checkedQtyText, value, StringComparison.Ordinal))
            {
                return;
            }

            _checkedQtyText = value;
            _isDirty = true;
            _changeVersion++;
            UpdateInputError();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckedQty));
            OnPropertyChanged(nameof(HasCheckedQty));
            OnPropertyChanged(nameof(CheckedQtyStateText));
            OnPropertyChanged(nameof(InputError));
            OnPropertyChanged(nameof(HasInputError));
            OnPropertyChanged(nameof(CanReconfirm));
            ReconfirmCommand.RaiseCanExecuteChanged();
            _onChanged(this);
        }
    }

    public int? CheckedQty => TryParseCheckedQty(_checkedQtyText, out var value) ? value : null;

    public bool HasCheckedQty => CheckedQty.HasValue && !HasInputError;

    public string CheckedQtyStateText => HasInputError
        ? InputError
        : HasCheckedQty
            ? "已填写"
            : "未填写";

    public string InputError => _inputError;

    public bool HasInputError => !string.IsNullOrEmpty(InputError);

    public bool CanReconfirm => RequiresReconfirmation && HasCheckedQty;

    public string LastInspectionText => LastInspection is null
        ? "无"
        : $"{LastInspection.CheckedQty} / {LastInspection.SubmittedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

    public RelayCommand ReconfirmCommand { get; }

    internal bool IsDirty => _isDirty;

    internal long ChangeVersion => _changeVersion;

    internal bool IsValidForSave => !HasInputError;

    internal void MarkPersisted(long version)
    {
        if (_changeVersion != version)
        {
            return;
        }

        _isDirty = false;
        OnPropertyChanged(nameof(CheckedQtyStateText));
    }

    internal RowInputSnapshot CaptureInput() => new(
        CheckedQtyText,
        _isDirty,
        _changeVersion);

    internal void RestoreInput(RowInputSnapshot snapshot)
    {
        _checkedQtyText = snapshot.CheckedQtyText;
        _isDirty = snapshot.IsDirty;
        _changeVersion = snapshot.ChangeVersion;
        UpdateInputError();
        OnPropertyChanged(nameof(CheckedQtyText));
        OnPropertyChanged(nameof(CheckedQty));
        OnPropertyChanged(nameof(HasCheckedQty));
        OnPropertyChanged(nameof(CheckedQtyStateText));
        OnPropertyChanged(nameof(InputError));
        OnPropertyChanged(nameof(HasInputError));
        OnPropertyChanged(nameof(CanReconfirm));
        ReconfirmCommand.RaiseCanExecuteChanged();
    }

    private void UpdateInputError()
    {
        _inputError = string.IsNullOrWhiteSpace(_checkedQtyText)
            || TryParseCheckedQty(_checkedQtyText, out _)
            ? string.Empty
            : "请输入非负整数";
    }

    private static string FormatCheckedQty(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool TryParseCheckedQty(string value, out int checkedQty)
    {
        return int.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out checkedQty)
            && checkedQty >= 0;
    }
}

public sealed record RowInputSnapshot(
    string CheckedQtyText,
    bool IsDirty,
    long ChangeVersion);

public sealed class InspectionDetailViewModel : ViewModelBase
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(600);

    private readonly Func<long, InspectionTaskDetailResult> _loadDetail;
    private readonly Func<SaveDraftRequest, SaveDraftResult> _saveDraft;
    private readonly Func<ReconfirmItemRequest, ReconfirmItemResult> _reconfirmItem;
    private readonly Func<ClearDraftRequest, ClearDraftResult> _clearDraft;
    private readonly Func<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult> _adjustInventory;
    private readonly Func<Task> _refreshDashboard;
    private readonly Func<Task> _refreshPendingTasks;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<DateOnly> _businessDate;
    private readonly Func<bool> _confirmClearDraft;
    private readonly Func<bool> _confirmZeroInventory;
    private readonly Func<Task>? _goBack;
    private readonly object _saveSync = new();

    private InspectionTaskDetail? _detail;
    private InspectionDetailPageState _state;
    private long _taskId;
    private int _loadVersion;
    private bool _isLoading;
    private bool _isActionBusy;
    private bool _isSaving;
    private bool _saveFailed;
    private bool _inspectorDirty;
    private bool _checkDateDirty;
    private long _editVersion;
    private long _inspectorEditVersion;
    private long _checkDateEditVersion;
    private DateTime? _lastSavedAtUtc;
    private string _errorMessage = string.Empty;
    private string _actionErrorMessage = string.Empty;
    private string _feedbackMessage = string.Empty;
    private string _inspectorName = string.Empty;
    private DateOnly? _checkDate;
    private bool _isInventoryEditorVisible;
    private string _inventoryText = string.Empty;
    private string _inventoryError = string.Empty;
    private string _inventoryFeedback = string.Empty;
    private Task? _saveLoop;
    private bool _flushRequested;

    public InspectionDetailViewModel(
        Func<long, InspectionTaskDetailResult>? loadDetail = null,
        Func<SaveDraftRequest, SaveDraftResult>? saveDraft = null,
        Func<ReconfirmItemRequest, ReconfirmItemResult>? reconfirmItem = null,
        Func<ClearDraftRequest, ClearDraftResult>? clearDraft = null,
        Func<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult>? adjustInventory = null,
        Func<Task>? refreshDashboard = null,
        Func<Task>? refreshPendingTasks = null,
        Action<Exception>? logException = null,
        Func<DateTime>? utcNow = null,
        Func<DateOnly>? businessDate = null,
        Func<bool>? confirmClearDraft = null,
        Func<bool>? confirmZeroInventory = null,
        Func<Task>? goBack = null)
    {
        _loadDetail = loadDetail ?? (_ => throw new InvalidOperationException("Detail query is not configured."));
        _saveDraft = saveDraft ?? (_ => throw new InvalidOperationException("Draft save is not configured."));
        _reconfirmItem = reconfirmItem ?? (_ => throw new InvalidOperationException("Reconfirm is not configured."));
        _clearDraft = clearDraft ?? (_ => throw new InvalidOperationException("Draft clear is not configured."));
        _adjustInventory = adjustInventory ?? (_ => throw new InvalidOperationException("Inventory adjustment is not configured."));
        _refreshDashboard = refreshDashboard ?? (() => Task.CompletedTask);
        _refreshPendingTasks = refreshPendingTasks ?? (() => Task.CompletedTask);
        _logException = logException;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _businessDate = businessDate ?? (() => DateOnly.FromDateTime(DateTime.Now));
        _confirmClearDraft = confirmClearDraft ?? (() => false);
        _confirmZeroInventory = confirmZeroInventory ?? (() => false);
        _goBack = goBack;
        _state = InspectionDetailPageState.Initial;

        RetryLoadCommand = new RelayCommand(
            _ => { _ = LoadAsync(TaskId); },
            _ => State == InspectionDetailPageState.Error && !IsLoading);
        RetrySaveCommand = new RelayCommand(
            _ => { _ = RetrySaveAsync(); },
            _ => IsOpen && SaveFailed && !IsSaving && !IsActionBusy);
        BackCommand = new RelayCommand(
            _ => { _ = _goBack?.Invoke(); },
            _ => !IsActionBusy);
        ClearDraftCommand = new RelayCommand(
            _ => { _ = ClearDraftAsync(); },
            _ => CanEdit && HasRecoveredDraft);
        OpenInventoryCommand = new RelayCommand(
            _ => IsInventoryEditorVisible = true,
            _ => CanEdit && !IsInventoryEditorVisible);
        CancelInventoryCommand = new RelayCommand(
            _ => CancelInventoryEdit(),
            _ => IsInventoryEditorVisible && !IsActionBusy);
        AdjustInventoryCommand = new RelayCommand(
            _ => { _ = AdjustInventoryAsync(); },
            _ => IsInventoryEditorVisible && CanEdit && !IsActionBusy);
    }

    public ObservableCollection<InspectionDetailRowViewModel> TaskItems { get; } = [];

    public ObservableCollection<InspectionNormalBatchResult> NormalBatches { get; } = [];

    public RelayCommand RetryLoadCommand { get; }

    public RelayCommand RetrySaveCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand ClearDraftCommand { get; }

    public RelayCommand OpenInventoryCommand { get; }

    public RelayCommand CancelInventoryCommand { get; }

    public RelayCommand AdjustInventoryCommand { get; }

    public InspectionDetailPageState State => _state;

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
            RetryLoadCommand.RaiseCanExecuteChanged();
            NotifyActionCommands();
        }
    }

    public bool IsOpen => State == InspectionDetailPageState.Open;

    public bool IsCompleted => State == InspectionDetailPageState.Completed;

    public bool IsSystemClosed => State == InspectionDetailPageState.SystemClosed;

    public bool IsNotFound => State == InspectionDetailPageState.NotFound;

    public bool HasError => State == InspectionDetailPageState.Error;

    public bool IsTerminal => IsCompleted || IsSystemClosed || IsNotFound;

    public long TaskId => _taskId;

    public long? ProductId => _detail?.ProductId;

    public string ProductName => _detail?.ProductName ?? string.Empty;

    public string ProductCode => _detail?.ProductCode ?? string.Empty;

    public string ProductBarcode => _detail?.ProductBarcode ?? "—";

    public int EffectiveStockQty => _detail?.EffectiveStockQty ?? 0;

    public string HighestStage => _detail?.HighestStage ?? string.Empty;

    public string Stage => HighestStage;

    public int PendingBatchCount => TaskItems.Count;

    public int NormalBatchCount => NormalBatches.Count;

    public bool HasNormalBatches => NormalBatches.Count > 0;

    public bool HasRecoveredDraft => _detail?.Draft is not null;

    public string DraftStatusText => HasRecoveredDraft ? "已恢复未完成草稿" : string.Empty;

    public string StatusMessage => State switch
    {
        InspectionDetailPageState.Loading => "正在加载排查详情…",
        InspectionDetailPageState.Completed => "该任务已完成排查",
        InspectionDetailPageState.SystemClosed => "该任务已由系统结束",
        InspectionDetailPageState.NotFound => "任务不存在或已无法访问",
        InspectionDetailPageState.Error => "排查详情加载失败",
        _ => string.Empty
    };

    public string ErrorMessage => _errorMessage;

    public string ActionErrorMessage => _actionErrorMessage;

    public bool HasActionError => !string.IsNullOrEmpty(ActionErrorMessage);

    public string FeedbackMessage => _feedbackMessage;

    public bool HasFeedback => !string.IsNullOrEmpty(FeedbackMessage);

    public string InspectorName
    {
        get => _inspectorName;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_inspectorName, value, StringComparison.Ordinal))
            {
                return;
            }

            _inspectorName = value;
            _inspectorDirty = true;
            _inspectorEditVersion = ++_editVersion;
            NotifyInputChanged();
            ScheduleSaveIfPossible();
        }
    }

    public DateOnly? CheckDate => _checkDate;

    public DateTime? CheckDateValue
    {
        get => _checkDate?.ToDateTime(TimeOnly.MinValue);
        set
        {
            DateOnly? date = value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
            if (_checkDate == date)
            {
                return;
            }

            _checkDate = date;
            _checkDateDirty = true;
            _checkDateEditVersion = ++_editVersion;
            NotifyInputChanged();
            ScheduleSaveIfPossible();
        }
    }

    public bool CanEdit => IsOpen && !IsLoading && !IsActionBusy;

    public bool IsActionBusy
    {
        get => _isActionBusy;
        private set
        {
            if (_isActionBusy == value)
            {
                return;
            }

            _isActionBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEdit));
            NotifyActionCommands();
            foreach (var item in TaskItems)
            {
                item.ReconfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (_isSaving == value)
            {
                return;
            }

            _isSaving = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(SaveStatusText));
            RetrySaveCommand.RaiseCanExecuteChanged();
        }
    }

    public bool SaveFailed
    {
        get => _saveFailed;
        private set
        {
            if (_saveFailed == value)
            {
                return;
            }

            _saveFailed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SaveStatusText));
            RetrySaveCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasUnsavedChanges => _inspectorDirty
        || _checkDateDirty
        || TaskItems.Any(item => item.IsDirty);

    public bool HasInputErrors => TaskItems.Any(item => item.HasInputError);

    public DateTime? LastSavedAtUtc => _lastSavedAtUtc;

    public string SaveStatusText
    {
        get
        {
            if (IsSaving)
            {
                return "正在保存…";
            }

            if (SaveFailed)
            {
                return "保存失败，请重试";
            }

            if (HasUnsavedChanges)
            {
                return "有未保存更改";
            }

            return LastSavedAtUtc.HasValue
                ? $"已保存 {LastSavedAtUtc.Value.ToLocalTime():HH:mm}"
                : HasRecoveredDraft ? "已保存" : "未保存";
        }
    }

    public bool IsInventoryEditorVisible
    {
        get => _isInventoryEditorVisible;
        private set
        {
            if (_isInventoryEditorVisible == value)
            {
                return;
            }

            _isInventoryEditorVisible = value;
            OnPropertyChanged();
            NotifyActionCommands();
        }
    }

    public string InventoryText
    {
        get => _inventoryText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_inventoryText, value, StringComparison.Ordinal))
            {
                return;
            }

            _inventoryText = value;
            _inventoryError = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InventoryError));
        }
    }

    public string InventoryError => _inventoryError;

    public string InventoryFeedback => _inventoryFeedback;

    public async Task LoadAsync(long taskId)
    {
        if (taskId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskId));
        }

        _taskId = taskId;
        var version = Interlocked.Increment(ref _loadVersion);
        _lastSavedAtUtc = null;
        SaveFailed = false;
        _editVersion = 0;
        _inventoryText = string.Empty;
        _inventoryError = string.Empty;
        _inventoryFeedback = string.Empty;
        IsInventoryEditorVisible = false;
        IsLoading = true;
        SetState(InspectionDetailPageState.Loading);
        _errorMessage = string.Empty;
        _actionErrorMessage = string.Empty;
        _feedbackMessage = string.Empty;
        NotifyMessages();

        try
        {
            var result = await Task.Run(() => _loadDetail(taskId));
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            ApplyResult(result, preserveInput: false);
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            _logException?.Invoke(exception);
            _errorMessage = "排查详情加载失败";
            SetState(InspectionDetailPageState.Error);
            NotifyMessages();
        }
        finally
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                IsLoading = false;
            }
        }
    }

    public async Task<bool> WaitForStableSaveAsync()
    {
        if (!HasUnsavedChanges && !IsSaving)
        {
            return true;
        }

        if (!IsOpen || HasInputErrors || SaveFailed)
        {
            return false;
        }

        while (true)
        {
            var worker = EnsureSaveLoop(immediate: true);
            await worker;
            if (!HasUnsavedChanges && !IsSaving)
            {
                return true;
            }

            if (!IsOpen || HasInputErrors || SaveFailed)
            {
                return false;
            }
        }
    }

    public async Task RetrySaveAsync()
    {
        if (!IsOpen || !SaveFailed || IsSaving || IsActionBusy)
        {
            return;
        }

        SaveFailed = false;
        _actionErrorMessage = string.Empty;
        NotifyMessages();
        var worker = EnsureSaveLoop(immediate: true);
        await worker;
    }

    public async Task ReconfirmItemAsync(InspectionDetailRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsOpen || !row.CanReconfirm || IsActionBusy)
        {
            return;
        }

        if (!await WaitForStableSaveAsync())
        {
            return;
        }

        IsActionBusy = true;
        _actionErrorMessage = string.Empty;
        _feedbackMessage = string.Empty;
        NotifyMessages();
        try
        {
            await Task.Run(() => _reconfirmItem(new ReconfirmItemRequest(
                _taskId,
                ProductId!.Value,
                row.TaskItemId,
                row.BatchId,
                row.AttentionVersion,
                AsUtc(_utcNow()))));
            await ReloadAsync(preserveInput: false);
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            _actionErrorMessage = "重新确认失败，请重试";
            NotifyMessages();
            await ReloadAsync(preserveInput: true);
        }
        finally
        {
            IsActionBusy = false;
        }
    }

    public async Task ClearDraftAsync()
    {
        if (!CanEdit || !HasRecoveredDraft || IsActionBusy || !_confirmClearDraft())
        {
            return;
        }

        if (!await WaitForStableSaveAsync())
        {
            return;
        }

        IsActionBusy = true;
        _actionErrorMessage = string.Empty;
        _feedbackMessage = string.Empty;
        NotifyMessages();
        try
        {
            await Task.Run(() => _clearDraft(new ClearDraftRequest(_taskId, ProductId!.Value)));
            _feedbackMessage = "草稿已清空";
            await ReloadAsync(preserveInput: false);
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            _actionErrorMessage = "草稿清空失败，请重试";
            NotifyMessages();
            await ReloadAsync(preserveInput: true);
        }
        finally
        {
            IsActionBusy = false;
        }
    }

    public async Task AdjustInventoryAsync()
    {
        if (!CanEdit || !IsInventoryEditorVisible || IsActionBusy)
        {
            return;
        }

        if (!int.TryParse(
                InventoryText.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var correctedStockQty)
            || correctedStockQty < 0)
        {
            _inventoryError = "请输入非负整数";
            OnPropertyChanged(nameof(InventoryError));
            return;
        }

        if (!await WaitForStableSaveAsync())
        {
            return;
        }

        if (correctedStockQty == 0 && !_confirmZeroInventory())
        {
            return;
        }

        IsActionBusy = true;
        _actionErrorMessage = string.Empty;
        _inventoryFeedback = string.Empty;
        NotifyMessages();
        try
        {
            var result = await Task.Run(() => _adjustInventory(new ManualInventoryAdjustmentRequest(
                ProductId!.Value,
                correctedStockQty,
                correctedStockQty == 0,
                AsUtc(_utcNow()))));
            _inventoryFeedback = result.NoChange ? "库存未变化" : "库存修正已保存";
            IsInventoryEditorVisible = false;
            _inventoryText = string.Empty;
            OnPropertyChanged(nameof(InventoryText));
            await ReloadAsync(preserveInput: false);
            if (result.ProductTerminated || (correctedStockQty == 0 && result.Changed))
            {
                await _refreshDashboard();
                await _refreshPendingTasks();
            }
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            _actionErrorMessage = "库存修正失败，请重试";
            NotifyMessages();
            await ReloadAsync(preserveInput: true);
        }
        finally
        {
            IsActionBusy = false;
        }
    }

    private async Task ReloadAsync(bool preserveInput)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var input = preserveInput ? CaptureInput() : null;
        IsLoading = true;
        try
        {
            var result = await Task.Run(() => _loadDetail(_taskId));
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            ApplyResult(result, preserveInput, input);
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            _logException?.Invoke(exception);
            _errorMessage = "排查详情加载失败";
            SetState(InspectionDetailPageState.Error);
            NotifyMessages();
        }
        finally
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyResult(
        InspectionTaskDetailResult result,
        bool preserveInput,
        DetailInputSnapshot? input = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.TaskId != _taskId)
        {
            _errorMessage = "排查详情加载失败";
            SetState(InspectionDetailPageState.Error);
            NotifyMessages();
            return;
        }

        switch (result.Status)
        {
            case "open" when result.Detail is not null:
                ApplyOpenDetail(result.Detail, preserveInput, input);
                return;
            case "completed":
                _errorMessage = string.Empty;
                ClearOpenDetail();
                SetState(InspectionDetailPageState.Completed);
                return;
            case "system_closed":
                _errorMessage = string.Empty;
                ClearOpenDetail();
                SetState(InspectionDetailPageState.SystemClosed);
                return;
            case "not_found":
                _errorMessage = string.Empty;
                ClearOpenDetail();
                SetState(InspectionDetailPageState.NotFound);
                return;
            default:
                _errorMessage = "排查详情加载失败";
                SetState(InspectionDetailPageState.Error);
                NotifyMessages();
                return;
        }
    }

    private void ApplyOpenDetail(
        InspectionTaskDetail detail,
        bool preserveInput,
        DetailInputSnapshot? input)
    {
        _detail = detail;
        _errorMessage = string.Empty;
        var draft = detail.Draft;
        _inspectorName = draft?.InspectorName?.Trim() ?? string.Empty;
        _checkDate = draft?.CheckDate
            ?? (draft is null ? _businessDate() : null);
        _inspectorDirty = false;
        _checkDateDirty = false;
        _inspectorEditVersion = 0;
        _checkDateEditVersion = 0;
        TaskItems.Clear();
        foreach (var taskItem in detail.TaskItems)
        {
            var row = new InspectionDetailRowViewModel(taskItem, OnRowChanged, BeginReconfirm);
            if (preserveInput && input?.Rows.TryGetValue(taskItem.TaskItemId, out var rowInput) == true)
            {
                row.RestoreInput(rowInput);
            }

            TaskItems.Add(row);
        }

        NormalBatches.Clear();
        foreach (var batch in detail.NormalBatches)
        {
            NormalBatches.Add(batch);
        }

        if (preserveInput && input is not null)
        {
            _inspectorName = input.InspectorName;
            _checkDate = input.CheckDate;
            _inspectorDirty = input.InspectorDirty;
            _checkDateDirty = input.CheckDateDirty;
            _inspectorEditVersion = input.InspectorEditVersion;
            _checkDateEditVersion = input.CheckDateEditVersion;
            _editVersion = Math.Max(_editVersion, input.EditVersion);
        }
        else
        {
            _lastSavedAtUtc = draft is null ? null : _lastSavedAtUtc;
            _saveFailed = false;
        }

        SetState(InspectionDetailPageState.Open);
        NotifyDetailProperties();
    }

    private void ClearOpenDetail()
    {
        _detail = null;
        TaskItems.Clear();
        NormalBatches.Clear();
        _inspectorName = string.Empty;
        _checkDate = null;
        _inspectorDirty = false;
        _checkDateDirty = false;
        _lastSavedAtUtc = null;
        IsInventoryEditorVisible = false;
        NotifyDetailProperties();
    }

    private void BeginReconfirm(InspectionDetailRowViewModel row) =>
        _ = ReconfirmItemAsync(row);

    private void OnRowChanged(InspectionDetailRowViewModel row)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasInputErrors));
        OnPropertyChanged(nameof(SaveStatusText));
        _actionErrorMessage = string.Empty;
        NotifyMessages();
        if (row.IsValidForSave)
        {
            ScheduleSaveIfPossible();
        }
    }

    private void NotifyInputChanged()
    {
        OnPropertyChanged(nameof(CheckDate));
        OnPropertyChanged(nameof(CheckDateValue));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SaveStatusText));
        _actionErrorMessage = string.Empty;
        NotifyMessages();
    }

    private void ScheduleSaveIfPossible()
    {
        if (IsOpen && !HasInputErrors)
        {
            EnsureSaveLoop(immediate: false);
        }
    }

    private Task EnsureSaveLoop(bool immediate)
    {
        lock (_saveSync)
        {
            if (immediate)
            {
                _flushRequested = true;
                _editVersion++;
            }

            if (_saveLoop is null || _saveLoop.IsCompleted)
            {
                _saveLoop = RunSaveLoopAsync();
            }

            return _saveLoop;
        }
    }

    private async Task RunSaveLoopAsync()
    {
        while (true)
        {
            var immediate = ConsumeFlushRequest();
            if (!immediate)
            {
                var observedVersion = Volatile.Read(ref _editVersion);
                await Task.Delay(SaveDebounce);
                if (observedVersion != Volatile.Read(ref _editVersion))
                {
                    continue;
                }
            }

            var snapshot = CaptureSaveSnapshot();
            if (snapshot is null)
            {
                return;
            }

            IsSaving = true;
            SaveFailed = false;
            OnPropertyChanged(nameof(SaveStatusText));
            try
            {
                var result = await Task.Run(() => _saveDraft(snapshot.Request));
                ApplySaveResult(snapshot, result);
            }
            catch (Exception exception)
            {
                _logException?.Invoke(exception);
                IsSaving = false;
                SaveFailed = true;
                _actionErrorMessage = string.Empty;
                NotifyMessages();
                await ReloadAsync(preserveInput: true);
                return;
            }
            finally
            {
                IsSaving = false;
            }

            if (!HasSavableDirtyWork())
            {
                return;
            }
        }
    }

    private SaveSnapshot? CaptureSaveSnapshot()
    {
        if (!IsOpen)
        {
            return null;
        }

        var items = TaskItems
            .Where(item => item.IsDirty && item.IsValidForSave)
            .Select(item => new SaveItemSnapshot(
                item,
                item.ChangeVersion,
                new SaveDraftItemRequest(
                    item.TaskItemId,
                    item.BatchId,
                    item.AttentionVersion,
                    item.CheckedQty)))
            .ToArray();
        if (items.Length == 0 && !_inspectorDirty && !_checkDateDirty)
        {
            return null;
        }

        return new(
            new SaveDraftRequest(
                _taskId,
                ProductId!.Value,
                _businessDate(),
                AsUtc(_utcNow()),
                string.IsNullOrWhiteSpace(InspectorName) ? null : InspectorName,
                CheckDate,
                items.Select(item => item.Request).ToArray()),
            items,
            _inspectorEditVersion,
            _checkDateEditVersion);
    }

    private void ApplySaveResult(SaveSnapshot snapshot, SaveDraftResult result)
    {
        foreach (var item in snapshot.Items)
        {
            item.Row.MarkPersisted(item.ChangeVersion);
        }

        if (_inspectorEditVersion == snapshot.InspectorEditVersion)
        {
            _inspectorDirty = false;
        }

        if (_checkDateEditVersion == snapshot.CheckDateEditVersion)
        {
            _checkDateDirty = false;
        }

        _lastSavedAtUtc = snapshot.Request.SavedAtUtc;
        if (_detail is not null)
        {
            _detail = _detail with
            {
                Draft = new InspectionDraftResult(
                    result.DraftId,
                    string.IsNullOrWhiteSpace(InspectorName) ? null : InspectorName.Trim(),
                    CheckDate,
                    Array.AsReadOnly(TaskItems
                        .Select(item => new InspectionDraftItemResult(item.TaskItemId, item.CheckedQty))
                        .ToArray()),
                    TaskItems.Any(item => item.RequiresReconfirmation))
            };
        }
        SaveFailed = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasInputErrors));
        OnPropertyChanged(nameof(HasRecoveredDraft));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(LastSavedAtUtc));
        OnPropertyChanged(nameof(SaveStatusText));
        ClearDraftCommand.RaiseCanExecuteChanged();
        _ = result;
    }

    private bool HasSavableDirtyWork() =>
        (_inspectorDirty || _checkDateDirty)
        || TaskItems.Any(item => item.IsDirty && item.IsValidForSave);

    private bool ConsumeFlushRequest()
    {
        lock (_saveSync)
        {
            var immediate = _flushRequested;
            _flushRequested = false;
            return immediate;
        }
    }

    private DetailInputSnapshot CaptureInput() => new(
        InspectorName,
        CheckDate,
        _inspectorDirty,
        _checkDateDirty,
        _inspectorEditVersion,
        _checkDateEditVersion,
        _editVersion,
        TaskItems.ToDictionary(item => item.TaskItemId, item => item.CaptureInput()));

    private void CancelInventoryEdit()
    {
        _inventoryText = string.Empty;
        _inventoryError = string.Empty;
        _inventoryFeedback = string.Empty;
        IsInventoryEditorVisible = false;
        OnPropertyChanged(nameof(InventoryText));
        OnPropertyChanged(nameof(InventoryError));
        OnPropertyChanged(nameof(InventoryFeedback));
    }

    private void SetState(InspectionDetailPageState state)
    {
        if (state != InspectionDetailPageState.Open)
        {
            IsInventoryEditorVisible = false;
        }

        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsSystemClosed));
        OnPropertyChanged(nameof(IsNotFound));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsTerminal));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(CanEdit));
        RetryLoadCommand.RaiseCanExecuteChanged();
        NotifyActionCommands();
    }

    private void NotifyDetailProperties()
    {
        OnPropertyChanged(nameof(TaskId));
        OnPropertyChanged(nameof(ProductId));
        OnPropertyChanged(nameof(ProductName));
        OnPropertyChanged(nameof(ProductCode));
        OnPropertyChanged(nameof(ProductBarcode));
        OnPropertyChanged(nameof(EffectiveStockQty));
        OnPropertyChanged(nameof(HighestStage));
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(PendingBatchCount));
        OnPropertyChanged(nameof(NormalBatchCount));
        OnPropertyChanged(nameof(HasNormalBatches));
        OnPropertyChanged(nameof(HasRecoveredDraft));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(InspectorName));
        OnPropertyChanged(nameof(CheckDate));
        OnPropertyChanged(nameof(CheckDateValue));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasInputErrors));
        OnPropertyChanged(nameof(SaveStatusText));
        OnPropertyChanged(nameof(ActionErrorMessage));
        OnPropertyChanged(nameof(HasActionError));
        OnPropertyChanged(nameof(FeedbackMessage));
        OnPropertyChanged(nameof(HasFeedback));
        ClearDraftCommand.RaiseCanExecuteChanged();
        RetrySaveCommand.RaiseCanExecuteChanged();
        NotifyActionCommands();
    }

    private void NotifyActionCommands()
    {
        BackCommand.RaiseCanExecuteChanged();
        ClearDraftCommand.RaiseCanExecuteChanged();
        OpenInventoryCommand.RaiseCanExecuteChanged();
        CancelInventoryCommand.RaiseCanExecuteChanged();
        AdjustInventoryCommand.RaiseCanExecuteChanged();
    }

    private void NotifyMessages()
    {
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ActionErrorMessage));
        OnPropertyChanged(nameof(HasActionError));
        OnPropertyChanged(nameof(FeedbackMessage));
        OnPropertyChanged(nameof(HasFeedback));
        OnPropertyChanged(nameof(InventoryError));
        OnPropertyChanged(nameof(InventoryFeedback));
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record SaveItemSnapshot(
        InspectionDetailRowViewModel Row,
        long ChangeVersion,
        SaveDraftItemRequest Request);

    private sealed record SaveSnapshot(
        SaveDraftRequest Request,
        IReadOnlyList<SaveItemSnapshot> Items,
        long InspectorEditVersion,
        long CheckDateEditVersion);

    private sealed record DetailInputSnapshot(
        string InspectorName,
        DateOnly? CheckDate,
        bool InspectorDirty,
        bool CheckDateDirty,
        long InspectorEditVersion,
        long CheckDateEditVersion,
        long EditVersion,
        IReadOnlyDictionary<long, RowInputSnapshot> Rows);
}
