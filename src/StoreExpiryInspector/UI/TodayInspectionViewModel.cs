using System.Collections.ObjectModel;
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
    public int PendingBatchCount => Item.PendingBatchCount;
    public int EffectiveStockQty => Item.EffectiveStockQty;
    public DateOnly? NearestExpiryDate => Item.NearestExpiryDate;
    public bool HasValidDraft => Item.HasValidDraft;

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
    public string StatusText => row.Errors.Count != 0 ? "行错误" : string.IsNullOrWhiteSpace(taskReason) ? (row.CheckedQty is null ? "未填写" : "可应用") : "不可应用";
    public string Reason => row.Errors.Count != 0 ? string.Join("；", row.Errors) : taskReason;
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
    private bool _isBusy;
    private string _statusText = "正在加载今日任务…";
    private string _inspectorName = string.Empty;
    private string _checkDateText;
    private InspectionPlanPreview? _currentPreview;
    private ApplyInspectionPlanDraftResult? _draftResult;
    private IReadOnlyList<OverStockConfirmation> _pendingConfirmations = Array.Empty<OverStockConfirmation>();

    public TodayInspectionViewModel(
        Func<InspectionTaskSearchResult> loadTasks,
        Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult> export,
        Func<string, InspectionPlanPreview> preview,
        Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> apply,
        Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> submit,
        Func<IReadOnlyCollection<long>, Task> refreshAfterSubmit,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirmOverStock = null,
        Action<Exception>? logException = null,
        Func<DateOnly>? businessToday = null)
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
        _checkDateText = _businessToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ReloadCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !IsBusy);
        SelectAllCommand = new RelayCommand(_ => SetSelection(true), _ => !IsBusy && Tasks.Count != 0);
        ClearSelectionCommand = new RelayCommand(_ => SetSelection(false), _ => !IsBusy && SelectedCount != 0);
        ExportCommand = new RelayCommand(_ => { }, _ => !IsBusy && SelectedCount != 0);
        PreviewCommand = new RelayCommand(_ => { }, _ => !IsBusy);
        SaveDraftCommand = new RelayCommand(_ => { _ = SaveDraftAsync(); }, _ => !IsBusy && CanSaveDraft);
        SubmitCommand = new RelayCommand(_ => { _ = SubmitAsync(); }, _ => !IsBusy && CompleteTaskIds.Count != 0);
    }

    public ObservableCollection<TodayInspectionTaskViewModel> Tasks { get; } = [];
    public ObservableCollection<TodayInspectionPreviewRowViewModel> PreviewRows { get; } = [];
    public RelayCommand ReloadCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand SaveDraftCommand { get; }
    public RelayCommand SubmitCommand { get; }
    public IReadOnlyList<long> CompleteTaskIds => _draftResult?.Tasks.Where(task => task.Readiness.IsDraftComplete).Select(task => task.TaskId).ToArray() ?? [];
    public bool IsBusy { get => _isBusy; private set { if (_isBusy == value) return; _isBusy = value; OnPropertyChanged(); RefreshCommands(); } }
    public int SelectedCount => Tasks.Count(task => task.IsSelected);
    public bool HasPreview => _currentPreview is not null;
    public bool CanSaveDraft => _currentPreview?.ApplicableTaskIds.Count > 0 && IsFormValid;
    public bool IsFormValid => !string.IsNullOrWhiteSpace(InspectorName) && TryGetCheckDate(out _);
    public string StatusText { get => _statusText; private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); } }
    public string InspectorName { get => _inspectorName; set { if (_inspectorName == value) return; _inspectorName = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFormValid)); OnPropertyChanged(nameof(CanSaveDraft)); RefreshCommands(); } }
    public string CheckDateText { get => _checkDateText; set { if (_checkDateText == value) return; _checkDateText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFormValid)); OnPropertyChanged(nameof(CanSaveDraft)); RefreshCommands(); } }
    public string PreviewSummaryText => _currentPreview is null ? "尚未读取排查结果文件" : $"涉及商品 {_currentPreview.Summary.ProductCount}，批次 {_currentPreview.Summary.BatchCount}，Task {_currentPreview.Summary.TaskCount}，可应用 {_currentPreview.ApplicableTaskIds.Count}，已填写 {_currentPreview.Summary.FilledCount}，未填写 {_currentPreview.Summary.BlankCount}，错误 {_currentPreview.Summary.ErrorCount}";
    public string DraftStatusText => _draftResult is null ? "尚未保存草稿" : CompleteTaskIds.Count == _draftResult.Tasks.Count ? "草稿已保存，所有本次 Task 已完成，可集中正式提交。" : "草稿已保存，仍有未完成排查项，未完成 Task 不会正式提交。";

    public async Task LoadAsync()
    {
        var selected = Tasks.Where(task => task.IsSelected).Select(task => task.TaskId).ToHashSet();
        var result = await RunAsync("加载今日任务失败", _loadTasks);
        if (result is null) return;
        Tasks.Clear();
        foreach (var item in result.Items)
        {
            var task = new TodayInspectionTaskViewModel(item) { IsSelected = selected.Contains(item.TaskId) };
            task.SelectionChanged += OnSelectionChanged;
            Tasks.Add(task);
        }
        StatusText = Tasks.Count == 0 ? "当前没有可排查任务。" : $"已加载 {Tasks.Count} 个当前任务，已选择 {SelectedCount} 项。";
        OnSelectionChanged();
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
        var draft = await RunAsync("保存待确认结果失败，请重新导出最新计划后重试", () => _apply(new(_currentPreview, _currentPreview.ApplicableTaskIds, InspectorName, checkDate, _businessToday(), DateTime.UtcNow)));
        if (draft is null) return;
        _draftResult = draft;
        _pendingConfirmations = Array.Empty<OverStockConfirmation>();
        StatusText = DraftStatusText;
        OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); RefreshCommands();
    }

    public async Task SubmitAsync()
    {
        if (CompleteTaskIds.Count == 0) { StatusText = "仍有未完成排查项，暂无可集中正式提交的 Task。"; return; }
        if (!TryGetCheckDate(out var checkDate)) { StatusText = "排查日期必须为今天或更早的 yyyy-MM-dd。"; return; }
        var request = new BulkInspectionSubmissionRequest(CompleteTaskIds, InspectorName, checkDate, _businessToday(), DateTime.UtcNow, _pendingConfirmations);
        var result = await RunAsync("集中正式提交失败", () => _submit(request));
        if (result is null) return;
        if (result.Outcome is BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation or BulkInspectionSubmissionOutcome.OverStockConfirmationStale)
        {
            _pendingConfirmations = result.OverStockConfirmations;
            StatusText = result.Outcome == BulkInspectionSubmissionOutcome.OverStockConfirmationStale ? "超库存事实已变化，请重新确认。" : "存在超库存排查项，请返回检查或确认仍然提交。";
            OnPropertyChanged(nameof(OverStockText));
            if (_confirmOverStock?.Invoke(_pendingConfirmations) == true) await SubmitAsync();
            return;
        }
        _pendingConfirmations = Array.Empty<OverStockConfirmation>();
        StatusText = result.Outcome == BulkInspectionSubmissionOutcome.AlreadySubmitted ? "任务已提交过，正在刷新页面。" : "提交已成功，正在刷新页面。";
        OnPropertyChanged(nameof(OverStockText));
        _ = RefreshAfterSubmitAsync(CompleteTaskIds);
    }

    public string OverStockText => _pendingConfirmations.Count == 0 ? string.Empty : string.Join("；", _pendingConfirmations.Select(item => $"Task {item.TaskId}：库存 {item.EffectiveStockQty}，本次 {item.TotalCheckedQty}"));

    private async Task RefreshAfterSubmitAsync(IReadOnlyCollection<long> taskIds)
    {
        try { await _refreshAfterSubmit(taskIds); StatusText = "提交已成功，首页、今日排查、待办任务、详情和历史已刷新。"; }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = "提交已成功，部分页面刷新失败，可手动刷新。"; }
    }

    private async Task<T?> RunAsync<T>(string failure, Func<T> action)
    {
        if (IsBusy) return default;
        IsBusy = true;
        try { return await Task.Run(() => DatabaseRuntimeGate.Run(action)); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = $"{failure}：{exception.Message}"; return default; }
        finally { IsBusy = false; }
    }

    private bool TryGetCheckDate(out DateOnly date) => DateOnly.TryParseExact(CheckDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date != default && date <= _businessToday();
    private void SetSelection(bool selected) { foreach (var task in Tasks) task.IsSelected = selected; OnSelectionChanged(); }
    private void OnSelectionChanged() { OnPropertyChanged(nameof(SelectedCount)); RefreshCommands(); }
    private void ResetSession() { _currentPreview = null; _draftResult = null; _pendingConfirmations = Array.Empty<OverStockConfirmation>(); PreviewRows.Clear(); OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(PreviewSummaryText)); OnPropertyChanged(nameof(DraftStatusText)); OnPropertyChanged(nameof(CompleteTaskIds)); OnPropertyChanged(nameof(OverStockText)); RefreshCommands(); }
    private void RefreshCommands() { ReloadCommand.RaiseCanExecuteChanged(); SelectAllCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged(); SaveDraftCommand.RaiseCanExecuteChanged(); SubmitCommand.RaiseCanExecuteChanged(); }
}
