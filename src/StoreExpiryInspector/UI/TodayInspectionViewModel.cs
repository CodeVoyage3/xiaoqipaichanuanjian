using System.Collections.ObjectModel;
using System.Globalization;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.UI;

public sealed class TodayInspectionTaskViewModel : ViewModelBase
{
    private bool _isSelected;

    public TodayInspectionTaskViewModel(InspectionTaskListItem item)
    {
        Item = item;
    }

    public InspectionTaskListItem Item { get; }
    public long TaskId => Item.TaskId;
    public string? ProductName => Item.ProductName;
    public string ProductCode => Item.ProductCode;
    public string? ProductBarcode => Item.ProductBarcode;
    public string HighestStage => Item.HighestStage;
    public string CategoryName => Item.CategoryName;
    public int PendingBatchCount => Item.PendingBatchCount;
    public int EffectiveStockQty => Item.EffectiveStockQty;
    public DateOnly? NearestExpiryDate => Item.NearestExpiryDate;
    public bool HasValidDraft => Item.HasValidDraft;
    public string TaskStatus => HasValidDraft ? "已有已填写结果" : "待排查";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke();
        }
    }

    internal event Action? SelectionChanged;
}

public sealed class TodayInspectionPreviewRowViewModel(InspectionPlanRow row, string taskReason)
{
    public int RowNumber => row.RowNumber;
    public long? TaskId => row.TaskId;
    public string ProductBarcode => row.ProductBarcode ?? "—";
    public string ProductName => string.IsNullOrWhiteSpace(row.ProductName) ? row.ProductCode ?? "—" : row.ProductName;
    public string CurrentStage => StageLabels.ToDisplay(row.Stage);
    public string HighestStage => row.Stage ?? "none";
    public string ProductionDate => row.ProductionDate ?? string.Empty;
    public string ExpiryDate => row.ExpiryDate ?? row.BatchDisplay ?? string.Empty;
    public string CheckedQtyText => row.CheckedQty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    public string StatusText => row.Errors.Count != 0 ? "数据错误"
        : !string.IsNullOrWhiteSpace(taskReason) ? "需要重新导出"
        : row.CheckedQty is null ? "未填写" : "可提交";
    public string Reason => string.Join("；", row.Errors.Append(taskReason).Where(value => !string.IsNullOrWhiteSpace(value)));
    public bool HasIssue => !string.IsNullOrWhiteSpace(Reason);
}

public sealed record ExpiredInventoryWarning(int BatchCount, long TotalCheckedQty);

public sealed class TodayInspectionViewModel : ViewModelBase
{
    private readonly Func<InspectionTaskSearchResult> _loadTasks;
    private readonly Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult> _export;
    private readonly Func<string, InspectionPlanPreview> _preview;
    private readonly Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> _apply;
    private readonly Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> _submit;
    private readonly Func<IReadOnlyCollection<long>, Task> _refreshAfterSubmit;
    private readonly Func<IReadOnlyList<OverStockConfirmation>, bool>? _confirmOverStock;
    private readonly Func<ExpiredInventoryWarning, bool>? _confirmExpiredInventory;
    private readonly Func<bool>? _confirmSubmission;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateOnly> _businessToday;
    private readonly Func<DateTime> _utcNow;
    private bool _isLoadingTasks;
    private bool _isActionBusy;
    private bool _hasLoadedTasks;
    private bool _isBulkSelecting;
    private string _statusText = "正在加载今日任务…";
    private string _inspectorName = string.Empty;
    private string _checkDateText;
    private DateTime? _checkDateValue;
    private string _inspectorNameError = string.Empty;
    private string _checkDateError = string.Empty;
    private InspectionPlanPreview? _currentPreview;
    private ApplyInspectionPlanDraftResult? _draftResult;
    private IReadOnlyList<OverStockConfirmation> _pendingConfirmations = Array.Empty<OverStockConfirmation>();
    private SubmissionIntent? _submissionIntent;

    public TodayInspectionViewModel(
        Func<InspectionTaskSearchResult> loadTasks,
        Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult> export,
        Func<string, InspectionPlanPreview> preview,
        Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> apply,
        Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> submit,
        Func<IReadOnlyCollection<long>, Task> refreshAfterSubmit,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirmOverStock = null,
        Func<ExpiredInventoryWarning, bool>? confirmExpiredInventory = null,
        Func<bool>? confirmSubmission = null,
        Action<Exception>? logException = null,
        Func<DateOnly>? businessToday = null,
        Func<DateTime>? utcNow = null)
    {
        _loadTasks = loadTasks;
        _export = export;
        _preview = preview;
        _apply = apply;
        _submit = submit;
        _refreshAfterSubmit = refreshAfterSubmit;
        _confirmOverStock = confirmOverStock;
        _confirmExpiredInventory = confirmExpiredInventory;
        _confirmSubmission = confirmSubmission;
        _logException = logException;
        _businessToday = businessToday ?? (() => DateOnly.FromDateTime(DateTime.Today));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _checkDateText = _businessToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _checkDateValue = _businessToday().ToDateTime(TimeOnly.MinValue);
        ReloadCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !IsLoadingTasks && !IsActionBusy);
        SelectAllCommand = new RelayCommand(_ => SetSelection(true), _ => CanUseContent && Tasks.Count != 0);
        ClearSelectionCommand = new RelayCommand(_ => SetSelection(false), _ => CanUseContent && SelectedCount != 0);
        ExportCommand = new RelayCommand(_ => { }, _ => CanUseContent && SelectedCount != 0);
        PreviewCommand = new RelayCommand(_ => { }, _ => CanUseContent);
        SaveDraftCommand = new RelayCommand(_ => { _ = SaveDraftAsync(); }, _ => CanUseContent && CanSaveDraft);
        SubmitCommand = new RelayCommand(_ => { _ = SubmitAsync(); }, _ => CanUseContent && CanSaveDraft);
    }

    public IReadOnlyList<TodayInspectionTaskViewModel> Tasks { get; private set; } = Array.Empty<TodayInspectionTaskViewModel>();
    public IReadOnlyList<TodayInspectionTaskViewModel> VisibleTasks => SelectedCategory == "全部"
        ? Tasks : Tasks.Where(task => task.CategoryName == SelectedCategory).ToArray();
    public IReadOnlyList<string> Categories => ["全部", .. Tasks.Select(task => task.CategoryName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)];
    private string _selectedCategory = "全部";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            var category = string.IsNullOrEmpty(value) ? "全部" : value;
            if (_selectedCategory == category) return;
            _selectedCategory = category;
            OnPropertyChanged(); OnPropertyChanged(nameof(VisibleTasks)); OnPropertyChanged(nameof(SelectedCount)); RefreshCommands();
        }
    }
    public ObservableCollection<TodayInspectionPreviewRowViewModel> PreviewRows { get; } = [];
    public RelayCommand ReloadCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand SaveDraftCommand { get; }
    public RelayCommand SubmitCommand { get; }
    public IReadOnlyList<long> CompleteTaskIds => _draftResult?.Tasks.Where(task => task.Readiness.IsDraftComplete).Select(task => task.TaskId).ToArray() ?? [];
    public bool IsLoadingTasks { get => _isLoadingTasks; private set { if (_isLoadingTasks == value) return; _isLoadingTasks = value; OnPropertyChanged(); RefreshCommands(); } }
    public bool IsActionBusy { get => _isActionBusy; private set { if (_isActionBusy == value) return; _isActionBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); RefreshCommands(); } }
    public bool IsBusy => IsActionBusy;
    public bool HasLoadedTasks => _hasLoadedTasks;
    public bool CanUseContent => !IsLoadingTasks && !IsActionBusy;
    public int SelectedCount => Tasks.Count(task => task.IsSelected);
    public bool HasPreview => _currentPreview is not null;
    public bool CanSaveDraft => _currentPreview?.ApplicableTaskIds.Count > 0 && IsFormValid;
    public bool IsFormValid => !string.IsNullOrWhiteSpace(InspectorName) && TryGetCheckDate(out _);
    public string StatusText { get => _statusText; private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); } }
    public string InspectorName { get => _inspectorName; set { if (_inspectorName == value) return; _inspectorName = value; InvalidateDraftOnFormChange(); OnPropertyChanged(); ValidateForm(); } }
    public string CheckDateText { get => _checkDateText; set { if (_checkDateText == value) return; _checkDateText = value; _checkDateValue = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null; InvalidateDraftOnFormChange(); OnPropertyChanged(); OnPropertyChanged(nameof(CheckDateValue)); ValidateForm(); } }
    public DateTime? CheckDateValue { get => _checkDateValue; set { if (_checkDateValue == value) return; _checkDateValue = value; _checkDateText = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty; InvalidateDraftOnFormChange(); OnPropertyChanged(); OnPropertyChanged(nameof(CheckDateText)); ValidateForm(); } }
    public DateTime CheckDateMaxValue => _businessToday().ToDateTime(TimeOnly.MinValue);
    public string InspectorNameError { get => _inspectorNameError; private set { if (_inspectorNameError == value) return; _inspectorNameError = value; OnPropertyChanged(); } }
    public string CheckDateError { get => _checkDateError; private set { if (_checkDateError == value) return; _checkDateError = value; OnPropertyChanged(); } }
    public bool HasInspectorNameError => !string.IsNullOrEmpty(InspectorNameError);
    public bool HasCheckDateError => !string.IsNullOrEmpty(CheckDateError);
    public string PreviewSummaryText => _currentPreview is null ? "尚未读取排查结果文件" : $"本次共 {_currentPreview.Summary.ProductCount} 个商品 / {_currentPreview.Summary.BatchCount} 个批次，{_currentPreview.ApplicableTaskIds.Count} 条可提交";
    public string DraftStatusText => _draftResult is null ? "尚未处理排查结果" : CompleteTaskIds.Count == _draftResult.Tasks.Count ? "排查结果已填写完整，可以提交数据。" : "仍有未完成排查项，请填写完整后提交。";
    public bool HasPreviewIssues => PreviewRows.Any(row => row.HasIssue);
    public string PreviewIssueText => _currentPreview is null ? string.Empty : string.Join("　", new[]
    {
        _currentPreview.Summary.BlankCount > 0 ? $"未填写 {_currentPreview.Summary.BlankCount} 条" : null,
        _currentPreview.Summary.ErrorCount > 0 ? $"错误 {_currentPreview.Summary.ErrorCount} 条" : null,
        _currentPreview.Tasks.Count(task => !task.IsApplicable) is var stale && stale > 0 ? $"陈旧/失效 {stale} 条" : null
    }.Where(text => text is not null));
    public TodayInspectionPlanExportResult? LatestExportResult { get; private set; }
    public event Action<string>? SubmissionBlocked;
    public event Action<string>? PreviewFailed;

    public async Task LoadAsync()
    {
        await LoadTasksAsync();
    }

    private async Task<bool> LoadTasksAsync()
    {
        if (IsLoadingTasks) return false;
        var selected = Tasks.Where(task => task.IsSelected).Select(task => task.TaskId).ToHashSet();
        IsLoadingTasks = true;
        try
        {
            var tasks = await Task.Run(() => DatabaseRuntimeGate.Run(() => _loadTasks().Items.Select(item =>
            {
                var task = new TodayInspectionTaskViewModel(item) { IsSelected = selected.Contains(item.TaskId) };
                task.SelectionChanged += OnSelectionChanged;
                return task;
            }).ToArray()));
            Tasks = tasks;
            _hasLoadedTasks = true;
            OnPropertyChanged(nameof(Tasks)); OnPropertyChanged(nameof(Categories)); OnPropertyChanged(nameof(VisibleTasks));
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            StatusText = "加载今日任务失败";
            return false;
        }
        finally { IsLoadingTasks = false; }
        StatusText = Tasks.Count == 0 ? "当前没有可排查任务。" : $"已加载 {Tasks.Count} 个当前任务，已选择 {SelectedCount} 项。";
        OnSelectionChanged();
        return true;
    }

    public async Task ExportAsync(string path)
    {
        if (SelectedCount == 0) { StatusText = "请先选择至少一个任务，再导出计划。"; return; }
        var result = await RunAsync("导出今日排查计划失败", () => _export(path, Tasks.Where(task => task.IsSelected).Select(task => task.TaskId).ToArray()));
        if (result is not null) { LatestExportResult = result; OnPropertyChanged(nameof(LatestExportResult)); StatusText = $"已导出 {result.TaskCount} 个任务、{result.RowCount} 个批次：{result.OutputPath}"; }
    }

    public async Task PreviewAsync(string path)
    {
        ResetSession();
        var preview = await RunAsync("读取排查结果文件失败", () => _preview(path));
        if (preview is null) { PreviewFailed?.Invoke("无法读取排查结果文件。请确认选择的是最新的今日排查计划，并重新导出后再试。"); return; }
        _currentPreview = preview;
        PreviewRows.Clear();
        foreach (var row in _currentPreview.File.Rows)
        {
            _currentPreview.TaskReasons.TryGetValue(row.TaskId ?? 0, out var reason);
            PreviewRows.Add(new TodayInspectionPreviewRowViewModel(row, reason ?? string.Empty));
        }
        StatusText = _currentPreview.ApplicableTaskIds.Count == 0 ? "预览完成，但没有可提交的数据。请查看错误或陈旧原因。" : "预览完成，请填写排查人和日期后提交数据。";
        OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(PreviewSummaryText)); OnPropertyChanged(nameof(CanSaveDraft)); OnPropertyChanged(nameof(HasPreviewIssues)); OnPropertyChanged(nameof(PreviewIssueText));
    }

    public void CancelPreview()
    {
        if (_currentPreview is not null) ResetSession();
    }

    public async Task SaveDraftAsync()
    {
        if (_currentPreview is null) { BlockSubmission("请先读取排查结果文件。", "请先选择并读取已填写的排查计划。"); return; }
        ValidateForm();
        if (!IsFormValid || !TryGetCheckDate(out var checkDate)) { BlockSubmission("请完善排查人和排查日期。", string.Join("\n", new[] { InspectorNameError, CheckDateError }.Where(value => !string.IsNullOrEmpty(value)))); return; }
        InvalidateSubmissionIntent();
        DateTime savedAtUtc;
        try { savedAtUtc = RequireUtcNow(); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = "提交数据准备失败，请重新导出最新计划后重试"; return; }
        var draft = await RunAsync("提交数据准备失败，请重新导出最新计划后重试", () => _apply(new(_currentPreview, _currentPreview.ApplicableTaskIds, InspectorName, checkDate, _businessToday(), savedAtUtc)));
        if (draft is null) return;
        _draftResult = draft;
        StatusText = DraftStatusText;
        OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); RefreshCommands();
    }

    public async Task SubmitAsync()
    {
        if (!CanUseContent) return;
        if (_currentPreview is null) { BlockSubmission("请先读取排查结果文件。", "请先选择并读取已填写的排查计划。"); return; }
        if (_draftResult is null) await SaveDraftAsync();
        if (_draftResult is null)
        {
            if (!IsFormValid) return;
            BlockSubmission("暂时无法提交。", "排查结果未能保存，请重新导出最新计划后再试。");
            return;
        }
        if (_draftResult.Tasks.Count == 0 || _draftResult.Tasks.Any(task => !task.Readiness.IsDraftComplete))
        {
            BlockSubmission("仍有未完成排查项，请填写完整后提交。", "请补全所有可应用任务的排查数量后，再提交数据。");
            return;
        }
        if (!IsFormValid || !TryGetCheckDate(out var checkDate)) { BlockSubmission("请完善排查人和排查日期。", string.Join("\n", new[] { InspectorNameError, CheckDateError }.Where(value => !string.IsNullOrEmpty(value)))); return; }
        var expiredInventory = GetExpiredInventoryWarning();
        if (expiredInventory is not null && _confirmExpiredInventory is not null)
        {
            if (_confirmExpiredInventory(expiredInventory) != true) { StatusText = "请复核过期商品库存后再提交。"; return; }
        }
        else if (_confirmSubmission?.Invoke() != true) { StatusText = "已取消提交数据。"; return; }
        try { _submissionIntent ??= new(CompleteTaskIds, InspectorName, checkDate, _businessToday(), RequireUtcNow()); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = "提交数据失败，请检查当前状态后重试。"; return; }
        IsActionBusy = true;
        try
        {
            while (true)
            {
                var intent = _submissionIntent;
                var result = await Task.Run(() => DatabaseRuntimeGate.Run(() => _submit(new(intent.TaskIds, intent.InspectorName, intent.CheckDate, intent.BusinessDate, intent.SubmittedAtUtc, _pendingConfirmations))));
                if (result.Outcome is BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation or BulkInspectionSubmissionOutcome.OverStockConfirmationStale)
                {
                    _pendingConfirmations = result.OverStockConfirmations;
                    StatusText = result.Outcome == BulkInspectionSubmissionOutcome.OverStockConfirmationStale ? "超库存事实已变化，请重新确认。" : "存在超库存排查项，请返回检查或确认仍然提交。";
                    OnPropertyChanged(nameof(OverStockText));
                    if (_confirmOverStock?.Invoke(_pendingConfirmations) != true)
                    {
                        InvalidateSubmissionIntent();
                        OnPropertyChanged(nameof(OverStockText));
                        return;
                    }
                    continue;
                }
                StatusText = result.Outcome == BulkInspectionSubmissionOutcome.AlreadySubmitted ? "任务已提交过，正在刷新页面。" : "提交已成功，正在刷新页面。";
                await RefreshAfterSubmitAsync(intent.TaskIds);
                ClearSubmittedSession();
                return;
            }
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            BlockSubmission("提交数据失败，请检查当前状态后重试。", "请重新导出最新计划并确认数据后再提交。");
        }
        finally { IsActionBusy = false; }
    }

    public string OverStockText => _pendingConfirmations.Count == 0 ? string.Empty : string.Join("；", _pendingConfirmations.Select(item => $"商品 {item.ProductId}：库存 {item.EffectiveStockQty}，本次 {item.TotalCheckedQty}"));

    private ExpiredInventoryWarning? GetExpiredInventoryWarning()
    {
        if (_currentPreview is null) return null;
        var completeTaskIds = CompleteTaskIds.ToHashSet();
        var rows = _currentPreview.File.Rows.Where(row => row.Stage == ExpiryStageCalculator.Expired && row.CheckedQty > 0 &&
            row.TaskId is long taskId && _currentPreview.ApplicableTaskIds.Contains(taskId) && completeTaskIds.Contains(taskId)).ToArray();
        return rows.Length == 0 ? null : new(rows.Length, rows.Sum(row => (long)row.CheckedQty!.Value));
    }

    private async Task RefreshAfterSubmitAsync(IReadOnlyCollection<long> taskIds)
    {
        try { await Task.WhenAll(_refreshAfterSubmit(taskIds), ReloadTasksWhileBusyAsync()); StatusText = "提交已成功，首页、今日排查、待办任务、详情和历史已刷新。"; }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = "提交已成功，部分页面刷新失败，可手动刷新。"; }
    }

    private async Task<T?> RunAsync<T>(string failure, Func<T> action)
    {
        if (IsActionBusy) return default;
        IsActionBusy = true;
        try { return await Task.Run(() => DatabaseRuntimeGate.Run(action)); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = failure; return default; }
        finally { IsActionBusy = false; }
    }

    private bool TryGetCheckDate(out DateOnly date)
    {
        date = _checkDateValue is DateTime value ? DateOnly.FromDateTime(value) : default;
        return date != default && date <= _businessToday();
    }
    private void SetSelection(bool selected)
    {
        _isBulkSelecting = true;
        try { foreach (var task in VisibleTasks) task.IsSelected = selected; }
        finally { _isBulkSelecting = false; }
        OnSelectionChanged();
    }
    private void OnSelectionChanged() { if (_isBulkSelecting) return; OnPropertyChanged(nameof(SelectedCount)); RefreshCommands(); }
    private void ResetSession() { _currentPreview = null; _draftResult = null; InvalidateSubmissionIntent(); PreviewRows.Clear(); OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(PreviewSummaryText)); OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); OnPropertyChanged(nameof(OverStockText)); OnPropertyChanged(nameof(HasPreviewIssues)); OnPropertyChanged(nameof(PreviewIssueText)); RefreshCommands(); }
    private void InvalidateSubmissionIntent() { _submissionIntent = null; _pendingConfirmations = Array.Empty<OverStockConfirmation>(); }
    private void InvalidateDraftOnFormChange() { if (_draftResult is null) return; _draftResult = null; InvalidateSubmissionIntent(); OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); OnPropertyChanged(nameof(OverStockText)); RefreshCommands(); }
    private DateTime RequireUtcNow() { var value = _utcNow(); return value.Kind == DateTimeKind.Utc ? value : throw new InvalidOperationException("权威提交时间必须为 UTC。"); }
    private Task ReloadTasksWhileBusyAsync() => LoadTasksAsync();
    private void ClearSubmittedSession()
    {
        _currentPreview = null;
        _draftResult = null;
        InvalidateSubmissionIntent();
        PreviewRows.Clear();
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewSummaryText));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(CompleteTaskIds));
        OnPropertyChanged(nameof(OverStockText));
        OnPropertyChanged(nameof(CanSaveDraft));
        RefreshCommands();
    }
    private sealed record SubmissionIntent(IReadOnlyList<long> TaskIds, string InspectorName, DateOnly CheckDate, DateOnly BusinessDate, DateTime SubmittedAtUtc);
    private void ValidateForm()
    {
        InspectorNameError = string.IsNullOrWhiteSpace(InspectorName) ? "请输入排查人" : string.Empty;
        CheckDateError = _checkDateValue is null ? "请选择排查日期" : _checkDateValue.Value.Date > CheckDateMaxValue.Date ? "排查日期不能晚于今天" : string.Empty;
        OnPropertyChanged(nameof(HasInspectorNameError)); OnPropertyChanged(nameof(HasCheckDateError));
        OnPropertyChanged(nameof(IsFormValid)); OnPropertyChanged(nameof(CanSaveDraft)); RefreshCommands();
    }
    private void BlockSubmission(string status, string reason) { StatusText = status; SubmissionBlocked?.Invoke(reason); }
    private void RefreshCommands() { ReloadCommand.RaiseCanExecuteChanged(); SelectAllCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged(); SaveDraftCommand.RaiseCanExecuteChanged(); SubmitCommand.RaiseCanExecuteChanged(); }
}
