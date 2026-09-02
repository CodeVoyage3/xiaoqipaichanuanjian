using System.Globalization;
using StoreExpiryInspector.Application.Tasks;

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
    public string HighestStage => StageLabels.ToDisplay(Item.HighestStage);
    public string CategoryName => Item.CategoryName;
    public int PendingBatchCount => Item.PendingBatchCount;
    public int EffectiveStockQty => Item.EffectiveStockQty;
    public DateOnly? NearestExpiryDate => Item.NearestExpiryDate;
    public bool HasValidDraft => Item.HasValidDraft;
    public string TaskStatus => HasValidDraft ? "已有草稿" : "待排查";

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
    public string Product => string.IsNullOrWhiteSpace(row.ProductName) ? row.ProductCode ?? "—" : row.ProductName;
    public string Batch => row.BatchDisplay ?? "—";
    public string CheckedQtyText => row.CheckedQty?.ToString(CultureInfo.InvariantCulture) ?? "未填写";
    public string StatusText => row.Errors.Count != 0
        ? string.IsNullOrWhiteSpace(taskReason) ? "行错误" : "行错误；陈旧/失效"
        : !string.IsNullOrWhiteSpace(taskReason) ? "陈旧/失效"
        : row.CheckedQty is null ? "未填写" : row.CheckedQty == 0 ? "0 件" : "已填写正数";
    public string Reason => string.Join("；", row.Errors.Append(taskReason).Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class TodayInspectionViewModel : ViewModelBase
{
    private readonly Func<InspectionTaskSearchResult> _loadTasks;
    private readonly Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult> _export;
    private readonly Func<string, InspectionPlanPreview> _preview;
    private readonly Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> _apply;
    private readonly Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> _submit;
    private readonly Func<IReadOnlyCollection<long>, Task> _refreshAfterSubmit;
    private readonly Func<IReadOnlyList<OverStockConfirmation>, bool>? _confirmOverStock;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateOnly> _businessToday;
    private readonly Func<DateTime> _utcNow;
    private bool _isLoadingTasks;
    private bool _isActionBusy;
    private bool _hasLoadedTasks;
    private string _statusText = "正在加载今日任务…";
    private string _inspectorName = string.Empty;
    private string _checkDateText;
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
        _logException = logException;
        _businessToday = businessToday ?? (() => DateOnly.FromDateTime(DateTime.Today));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _checkDateText = _businessToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ReloadCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !IsLoadingTasks && !IsActionBusy);
        SelectAllCommand = new RelayCommand(_ => SetSelection(true), _ => CanUseContent && Tasks.Count != 0);
        ClearSelectionCommand = new RelayCommand(_ => SetSelection(false), _ => CanUseContent && SelectedCount != 0);
        ExportCommand = new RelayCommand(_ => { }, _ => CanUseContent && SelectedCount != 0);
        PreviewCommand = new RelayCommand(_ => { }, _ => CanUseContent);
        SaveDraftCommand = new RelayCommand(_ => { _ = SaveDraftAsync(); }, _ => CanUseContent && CanSaveDraft);
        SubmitCommand = new RelayCommand(_ => { _ = SubmitAsync(); }, _ => CanUseContent && CompleteTaskIds.Count != 0);
    }

    public IReadOnlyList<TodayInspectionTaskViewModel> Tasks { get; private set; } = Array.Empty<TodayInspectionTaskViewModel>();
    public List<TodayInspectionPreviewRowViewModel> PreviewRows { get; } = [];
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
    public string InspectorName { get => _inspectorName; set { if (_inspectorName == value) return; _inspectorName = value; InvalidateDraftOnFormChange(); OnPropertyChanged(); OnPropertyChanged(nameof(IsFormValid)); OnPropertyChanged(nameof(CanSaveDraft)); RefreshCommands(); } }
    public string CheckDateText { get => _checkDateText; set { if (_checkDateText == value) return; _checkDateText = value; InvalidateDraftOnFormChange(); OnPropertyChanged(); OnPropertyChanged(nameof(IsFormValid)); OnPropertyChanged(nameof(CanSaveDraft)); RefreshCommands(); } }
    public string PreviewSummaryText => _currentPreview is null ? "尚未读取排查结果文件" : $"涉及商品 {_currentPreview.Summary.ProductCount}，批次 {_currentPreview.Summary.BatchCount}，Task {_currentPreview.Summary.TaskCount}，可应用 {_currentPreview.ApplicableTaskIds.Count}，已填写 {_currentPreview.Summary.FilledCount}，未填写 {_currentPreview.Summary.BlankCount}，错误 {_currentPreview.Summary.ErrorCount}，陈旧/失效 {_currentPreview.Tasks.Count(task => !task.IsApplicable)}";
    public string DraftStatusText => _draftResult is null ? "尚未保存草稿" : CompleteTaskIds.Count == _draftResult.Tasks.Count ? "草稿已保存，所有本次 Task 已完成，可集中正式提交。" : "草稿已保存，仍有未完成排查项，未完成 Task 不会正式提交。";

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
            OnPropertyChanged(nameof(Tasks));
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
        if (result is not null) StatusText = $"已导出 {result.TaskCount} 个任务、{result.RowCount} 个批次：{result.OutputPath}";
    }

    public async Task PreviewAsync(string path)
    {
        ResetSession();
        var preview = await RunAsync("读取排查结果文件失败", () => _preview(path));
        if (preview is null) return;
        _currentPreview = preview;
        PreviewRows.Clear();
        foreach (var row in _currentPreview.File.Rows)
        {
            _currentPreview.TaskReasons.TryGetValue(row.TaskId ?? 0, out var reason);
            PreviewRows.Add(new TodayInspectionPreviewRowViewModel(row, reason ?? string.Empty));
        }
        StatusText = _currentPreview.ApplicableTaskIds.Count == 0 ? "预览完成，但没有可保存的 Task。请查看错误或陈旧原因。" : "预览完成，请填写排查人和日期后保存待确认结果。";
        OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(PreviewSummaryText)); OnPropertyChanged(nameof(CanSaveDraft));
    }

    public async Task SaveDraftAsync()
    {
        if (_currentPreview is null) { StatusText = "请先读取排查结果文件。"; return; }
        if (!TryGetCheckDate(out var checkDate) || string.IsNullOrWhiteSpace(InspectorName)) { StatusText = "排查人必填，排查日期必须为今天或更早的 yyyy-MM-dd。"; return; }
        InvalidateSubmissionIntent();
        DateTime savedAtUtc;
        try { savedAtUtc = RequireUtcNow(); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = "保存待确认结果失败，请重新导出最新计划后重试"; return; }
        var draft = await RunAsync("保存待确认结果失败，请重新导出最新计划后重试", () => _apply(new(_currentPreview, _currentPreview.ApplicableTaskIds, InspectorName, checkDate, _businessToday(), savedAtUtc)));
        if (draft is null) return;
        _draftResult = draft;
        StatusText = DraftStatusText;
        OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); RefreshCommands();
    }

    public async Task SubmitAsync()
    {
        if (!CanUseContent) return;
        if (CompleteTaskIds.Count == 0) { StatusText = "仍有未完成排查项，暂无可集中正式提交的 Task。"; return; }
        if (!TryGetCheckDate(out var checkDate)) { StatusText = "排查日期必须为今天或更早的 yyyy-MM-dd。"; return; }
        try { _submissionIntent ??= new(CompleteTaskIds, InspectorName, checkDate, _businessToday(), RequireUtcNow()); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = "集中正式提交失败，请检查当前任务状态后重试。"; return; }
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
            StatusText = "集中正式提交失败，请检查当前任务状态后重试。";
        }
        finally { IsActionBusy = false; }
    }

    public string OverStockText => _pendingConfirmations.Count == 0 ? string.Empty : string.Join("；", _pendingConfirmations.Select(item => $"Task {item.TaskId}/商品 {item.ProductId}：库存 {item.EffectiveStockQty}，本次 {item.TotalCheckedQty}"));

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

    private bool TryGetCheckDate(out DateOnly date) => DateOnly.TryParseExact(CheckDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date != default && date <= _businessToday();
    private void SetSelection(bool selected) { foreach (var task in Tasks) task.IsSelected = selected; OnSelectionChanged(); }
    private void OnSelectionChanged() { OnPropertyChanged(nameof(SelectedCount)); RefreshCommands(); }
    private void ResetSession() { _currentPreview = null; _draftResult = null; InvalidateSubmissionIntent(); PreviewRows.Clear(); OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(PreviewSummaryText)); OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); OnPropertyChanged(nameof(OverStockText)); RefreshCommands(); }
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
    private void RefreshCommands() { ReloadCommand.RaiseCanExecuteChanged(); SelectAllCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged(); SaveDraftCommand.RaiseCanExecuteChanged(); SubmitCommand.RaiseCanExecuteChanged(); }
}
