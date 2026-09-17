using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
    public DateOnly? PlannedInspectionDate => Item.PlannedInspectionDate;
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
    public string StatusText => IsStateChanged ? "需要重新导出"
        : row.Errors.Count != 0 ? "数据错误"
        : row.CheckedQty is null ? "未填写" : "可提交";
    public string ResultText => StatusText switch { "需要重新导出" => "状态变化", "数据错误" => "填写错误", _ => StatusText };
    private bool IsStateChanged => !string.IsNullOrWhiteSpace(taskReason) || row.Errors.Any(error => error.Contains("状态已经变化", StringComparison.Ordinal) || error.Contains("无法匹配当前", StringComparison.Ordinal));
    public string Reason => string.Join("；", row.Errors.Append(taskReason).Where(value => !string.IsNullOrWhiteSpace(value)));
    public string DisplayReason => !string.IsNullOrWhiteSpace(Reason) ? Reason : StatusText == "未填写" ? "本次未填写，已跳过" : "—";
    public bool HasIssue => !string.IsNullOrWhiteSpace(Reason);
}

public sealed record ExpiredInventoryWarning(int BatchCount, long TotalCheckedQty);

public sealed class TodayInspectionViewModel : ViewModelBase
{
    private readonly Func<InspectionTaskSearchResult> _loadTasks;
    private readonly Func<InspectionTaskSearchRequest, InspectionTaskSearchResult>? _searchTasks;
    private readonly Func<IReadOnlyList<string>>? _loadCategories;
    private readonly Func<string?, IReadOnlyList<long>>? _loadTaskIds;
    private readonly Func<IReadOnlyCollection<long>, IReadOnlyList<long>>? _loadOpenTaskIds;
    private readonly Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult> _export;
    private readonly Func<IReadOnlyCollection<long>, Task> _refreshAfterSubmit;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateOnly> _businessToday;
    private bool _isLoadingTasks;
    private bool _isExportBusy;
    private bool _hasLoadedTasks;
    private bool _isBulkSelecting;
    private bool _isBulkSelectionBusy;
    private int _loadVersion;
    private int _selectionVersion;
    private string _statusText = "正在加载今日任务…";
    private readonly HashSet<long> _selectedTaskIds = [];
    private IReadOnlyList<string> _categories = ["全部"];
    private int _currentPage = 1;
    private int _totalCount;
    private readonly Func<DateOnly, int[]>? _loadPlanCounts;
    private readonly Func<string, DateOnly, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? _exportFuture;
    private int _selectedDayOffset;
    private int[] _planCounts = [0, 0, 0];
    private DateOnly _planBusinessDate;

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
        Func<DateTime>? utcNow = null,
        Func<InspectionTaskSearchRequest, InspectionTaskSearchResult>? searchTasks = null,
        Func<IReadOnlyList<string>>? loadCategories = null,
        Func<string?, IReadOnlyList<long>>? loadTaskIds = null,
        Func<IReadOnlyCollection<long>, IReadOnlyList<long>>? loadOpenTaskIds = null,
        Func<DateOnly, int[]>? loadPlanCounts = null,
        Func<string, DateOnly, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>? exportFuture = null)
    {
        _loadTasks = loadTasks;
        _loadPlanCounts = loadPlanCounts;
        _exportFuture = exportFuture;
        _searchTasks = searchTasks;
        _loadCategories = loadCategories;
        _loadTaskIds = loadTaskIds;
        _loadOpenTaskIds = loadOpenTaskIds;
        _export = export;
        _refreshAfterSubmit = refreshAfterSubmit;
        _logException = logException;
        _businessToday = businessToday ?? (() => DateOnly.FromDateTime(DateTime.Today));
        _planBusinessDate = _businessToday();
        ImportSession = new InspectionResultImportSessionViewModel(
            preview, apply, submit, RefreshAfterSubmitAsync, confirmOverStock, confirmExpiredInventory,
            confirmSubmission, logException, _businessToday, utcNow);
        ImportSession.PropertyChanged += (_, _) => NotifyImportSessionChanged();
        ImportSession.Submitted += RemoveSelectedTaskIds;
        ReloadCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => CanUseContent);
        SelectAllCommand = new RelayCommand(_ => { _ = SetSelectionAsync(true); }, _ => CanUseContent && !_isBulkSelectionBusy && Tasks.Count != 0);
        ClearSelectionCommand = new RelayCommand(_ => { _ = SetSelectionAsync(false); }, _ => CanUseContent && !_isBulkSelectionBusy && SelectedCount != 0);
        PreviousPageCommand = new RelayCommand(_ => { _ = GoToPageAsync(CurrentPage - 1); }, _ => CanUseContent && CurrentPage > 1);
        NextPageCommand = new RelayCommand(_ => { _ = GoToPageAsync(CurrentPage + 1); }, _ => CanUseContent && CurrentPage < TotalPages);
        SelectDayCommand = new RelayCommand(day => { if (int.TryParse(day?.ToString(), out var offset)) _ = SelectDayAsync(offset); }, _ => CanUseContent);
        ExportCommand = new RelayCommand(_ => { }, _ => CanUseContent && SelectedCount != 0 && (!IsFuturePlan || _exportFuture is not null));
        PreviewCommand = new RelayCommand(_ => { }, _ => CanUseContent && !IsFuturePlan);
    }

    public IReadOnlyList<TodayInspectionTaskViewModel> Tasks { get; private set; } = Array.Empty<TodayInspectionTaskViewModel>();
    public InspectionResultImportSessionViewModel ImportSession { get; }
    public IReadOnlyList<TodayInspectionTaskViewModel> VisibleTasks => _searchTasks is null && SelectedCategory != "全部"
        ? Tasks.Where(task => task.CategoryName == SelectedCategory).ToArray()
        : Tasks;
    public IReadOnlyList<string> Categories => _categories;
    private string _selectedCategory = "全部";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            var category = string.IsNullOrEmpty(value) ? "全部" : value;
            if (_selectedCategory == category) return;
            _selectedCategory = category;
            _currentPage = 1;
            _selectionVersion++;
            OnPropertyChanged(); OnPropertyChanged(nameof(VisibleTasks)); OnPropertyChanged(nameof(SelectedCount)); OnPropertyChanged(nameof(CurrentPage)); RefreshCommands();
            _ = LoadAsync();
        }
    }
    public ObservableCollection<TodayInspectionPreviewRowViewModel> PreviewRows => ImportSession.PreviewRows;
    public IReadOnlyList<string> PreviewFilters => ImportSession.PreviewFilters;
    public string SelectedPreviewFilter { get => ImportSession.SelectedPreviewFilter; set => ImportSession.SelectedPreviewFilter = value; }
    public IReadOnlyList<TodayInspectionPreviewRowViewModel> VisiblePreviewRows => ImportSession.VisiblePreviewRows;
    public string AllPreviewFilterText => ImportSession.AllPreviewFilterText;
    public string SubmittablePreviewFilterText => ImportSession.SubmittablePreviewFilterText;
    public string BlankPreviewFilterText => ImportSession.BlankPreviewFilterText;
    public string StalePreviewFilterText => ImportSession.StalePreviewFilterText;
    public string InvalidPreviewFilterText => ImportSession.InvalidPreviewFilterText;
    public RelayCommand ReloadCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand SaveDraftCommand => ImportSession.SaveDraftCommand;
    public RelayCommand SubmitCommand => ImportSession.SubmitCommand;
    public RelayCommand PreviewFilterCommand => ImportSession.PreviewFilterCommand;
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand SelectDayCommand { get; }
    public int SelectedDayOffset => _selectedDayOffset;
    public DateOnly TargetDate => _planBusinessDate.AddDays(SelectedDayOffset);
    public bool IsFuturePlan => SelectedDayOffset != 0;
    public bool IsTodaySelected => SelectedDayOffset == 0;
    public bool IsTomorrowSelected => SelectedDayOffset == 1;
    public bool IsDayAfterTomorrowSelected => SelectedDayOffset == 2;
    public int TodayPlanCount => _planCounts[0];
    public int TomorrowPlanCount => _planCounts[1];
    public int DayAfterTomorrowPlanCount => _planCounts[2];
    public string TodayPlanDateText => _planBusinessDate.ToString("MM'月'dd'日'", CultureInfo.InvariantCulture);
    public string TomorrowPlanDateText => _planBusinessDate.AddDays(1).ToString("MM'月'dd'日'", CultureInfo.InvariantCulture);
    public string DayAfterTomorrowPlanDateText => _planBusinessDate.AddDays(2).ToString("MM'月'dd'日'", CultureInfo.InvariantCulture);
    public string ImportAvailabilityText => IsFuturePlan ? "到排查日后可导入排查结果" : string.Empty;
    public async Task SelectDayAsync(int offset)
    {
        if (!CanUseContent || offset < 0 || offset > 2 || offset == SelectedDayOffset) return;
        _selectedDayOffset = offset;
        _selectedTaskIds.Clear(); _selectionVersion++; _currentPage = 1;
        ResetSession(); LatestExportResult = null;
        foreach (var property in new[] { nameof(SelectedDayOffset), nameof(TargetDate), nameof(IsFuturePlan), nameof(IsTodaySelected), nameof(IsTomorrowSelected), nameof(IsDayAfterTomorrowSelected), nameof(ImportAvailabilityText), nameof(SelectedCount), nameof(CurrentPage), nameof(LatestExportResult) }) OnPropertyChanged(property);
        RefreshCommands();
        await LoadAsync();
    }
    public IReadOnlyList<long> CompleteTaskIds => ImportSession.CompleteTaskIds;
    public bool IsLoadingTasks { get => _isLoadingTasks; private set { if (_isLoadingTasks == value) return; _isLoadingTasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanUseContent)); OnPropertyChanged(nameof(CanGoPrevious)); OnPropertyChanged(nameof(CanGoNext)); RefreshCommands(); } }
    public bool IsActionBusy => _isExportBusy || ImportSession.IsActionBusy;
    public bool IsBusy => IsActionBusy;
    public bool IsReadingPlan => ImportSession.IsReadingPlan;
    public bool HasLoadedTasks => _hasLoadedTasks;
    public bool CanUseContent => !IsLoadingTasks && !IsActionBusy && !IsReadingPlan && !_isBulkSelectionBusy;
    public int SelectedCount => _selectedTaskIds.Count;
    public int CurrentPage => _currentPage;
    public int TotalCount => _totalCount;
    public int TotalPages => Math.Max(1, (TotalCount + 49) / 50);
    public bool CanGoPrevious => CanUseContent && CurrentPage > 1;
    public bool CanGoNext => CanUseContent && CurrentPage < TotalPages;
    public string PageSummary => $"第 {CurrentPage} / {TotalPages} 页 · 共 {TotalCount} 个当前任务";
    public bool HasPreview => ImportSession.HasPreview;
    public bool CanSaveDraft => !IsFuturePlan && ImportSession.CanSaveDraft;
    public bool IsFormValid => ImportSession.IsFormValid;
    public string StatusText { get => _statusText; private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); } }
    public string InspectorName { get => ImportSession.InspectorName; set => ImportSession.InspectorName = value; }
    public string CheckDateText { get => ImportSession.CheckDateText; set => ImportSession.CheckDateText = value; }
    public DateTime? CheckDateValue { get => ImportSession.CheckDateValue; set => ImportSession.CheckDateValue = value; }
    public DateTime CheckDateMaxValue => ImportSession.CheckDateMaxValue;
    public string InspectorNameError => ImportSession.InspectorNameError;
    public string CheckDateError => ImportSession.CheckDateError;
    public bool HasInspectorNameError => ImportSession.HasInspectorNameError;
    public bool HasCheckDateError => ImportSession.HasCheckDateError;
    public string PreviewSummaryText => ImportSession.PreviewSummaryText;
    public string DraftStatusText => ImportSession.DraftStatusText;
    public bool HasPreviewIssues => ImportSession.HasPreviewIssues;
    public string PreviewIssueText => ImportSession.PreviewIssueText;
    public string PreviewDetailText => ImportSession.PreviewDetailText;
    public TodayInspectionPlanExportResult? LatestExportResult { get; private set; }
    public event Action<string>? SubmissionBlocked { add => ImportSession.SubmissionBlocked += value; remove => ImportSession.SubmissionBlocked -= value; }
    public event Action<string>? PreviewFailed { add => ImportSession.PreviewFailed += value; remove => ImportSession.PreviewFailed -= value; }

    public string? GetPreviewProductName(long taskId) => ImportSession.GetPreviewProductName(taskId);

    public async Task LoadAsync()
    {
        _loadVersion++;
        await LoadTasksAsync();
    }

    public async Task ReloadAfterBusinessDataResetAsync()
    {
        _selectedTaskIds.Clear();
        LatestExportResult = null;
        OnPropertyChanged(nameof(LatestExportResult));
        ResetSession();
        await LoadAsync();
    }

    private async Task<bool> LoadTasksAsync()
    {
        if (IsLoadingTasks) return false;
        IsLoadingTasks = true;
        var businessDate = _businessToday();
        _planBusinessDate = businessDate;
        OnPropertyChanged(nameof(TodayPlanDateText)); OnPropertyChanged(nameof(TomorrowPlanDateText)); OnPropertyChanged(nameof(DayAfterTomorrowPlanDateText));
        try
        {
            while (true)
            {
            var version = _loadVersion;
            var page = CurrentPage;
            var category = SelectedCategory;
            var targetDate = TargetDate;
            var future = IsFuturePlan;
            var result = await Task.Run(() => DatabaseRuntimeGate.Run(() => _searchTasks is null
                ? _loadTasks()
                : _searchTasks(new InspectionTaskSearchRequest(Page: page, PageSize: 50, CategoryName: category == "全部" ? null : category, TargetDate: future ? targetDate : null))));
            if (version != _loadVersion) continue;
            if (page > Math.Max(1, (result.TotalCount + 49) / 50))
            {
                _currentPage = Math.Max(1, (result.TotalCount + 49) / 50);
                _loadVersion++;
                continue;
            }
            var tasks = result.Items.Select(item =>
            {
                var task = new TodayInspectionTaskViewModel(item with { PlannedInspectionDate = targetDate }) { IsSelected = _selectedTaskIds.Contains(item.TaskId) };
                task.SelectionChanged += OnSelectionChanged;
                return task;
            }).ToArray();
            var futureRows = future && _searchTasks is not null
                ? (await Task.Run(() => DatabaseRuntimeGate.Run(() => _searchTasks(new(PageSize: int.MaxValue, TargetDate: targetDate))))).Items
                : null;
            var categories = futureRows is not null
                ? (IReadOnlyList<string>)["全部", .. futureRows.Select(item => item.CategoryName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)]
                : _loadCategories is not null
                ? (IReadOnlyList<string>)["全部", .. await Task.Run(() => DatabaseRuntimeGate.Run(_loadCategories))]
                : ["全部", .. tasks.Select(task => task.CategoryName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)];
            if (version != _loadVersion) continue;
            if (futureRows is not null) _selectedTaskIds.IntersectWith(futureRows.Select(item => item.TaskId));
            if (!future && _selectedTaskIds.Count != 0 && (_loadOpenTaskIds is not null || _searchTasks is null))
            {
                var openTaskIds = _loadOpenTaskIds is null
                    ? tasks.Select(task => task.TaskId).ToArray()
                    : await Task.Run(() => DatabaseRuntimeGate.Run(() => _loadOpenTaskIds(_selectedTaskIds.ToArray())));
                if (version != _loadVersion) continue;
                _selectedTaskIds.IntersectWith(openTaskIds);
            }
            if (_loadPlanCounts is not null)
            {
                var counts = await Task.Run(() => DatabaseRuntimeGate.Run(() => _loadPlanCounts(businessDate)));
                if (version != _loadVersion) continue;
                _planCounts = counts;
            }
            else if (!future) _planCounts[0] = result.TotalCount;
            Tasks = tasks;
            _categories = categories;
            _totalCount = result.TotalCount;
            OnPropertyChanged(nameof(TodayPlanCount)); OnPropertyChanged(nameof(TomorrowPlanCount)); OnPropertyChanged(nameof(DayAfterTomorrowPlanCount));
            OnPropertyChanged(nameof(TargetDate));
            _hasLoadedTasks = true;
            OnPropertyChanged(nameof(Tasks)); OnPropertyChanged(nameof(Categories)); OnPropertyChanged(nameof(VisibleTasks)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(TotalPages)); OnPropertyChanged(nameof(PageSummary)); OnPropertyChanged(nameof(CanGoPrevious)); OnPropertyChanged(nameof(CanGoNext));
            StatusText = Tasks.Count == 0 ? "当前没有可排查任务。" : $"已加载 {Tasks.Count} 个当前任务，已选择 {SelectedCount} 项。";
            OnSelectionChanged();
            return true;
            }
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            StatusText = "加载今日任务失败";
            return false;
        }
        finally { IsLoadingTasks = false; }
    }

    public async Task ExportAsync(string path)
    {
        if (!CanUseContent) return;
        if (SelectedCount == 0) { StatusText = "请先选择至少一个任务，再导出计划。"; return; }
        if (IsFuturePlan && _exportFuture is null) { StatusText = "未来安排导出暂不可用。"; return; }
        var future = IsFuturePlan; var date = TargetDate; var ids = _selectedTaskIds.ToArray();
        var result = await RunAsync(future ? "导出未来工作安排失败" : "导出今日排查计划失败", () => future ? _exportFuture!(path, date, ids.Select(id => -id).ToArray()) : _export(path, ids));
        if (result is not null) { LatestExportResult = result; OnPropertyChanged(nameof(LatestExportResult)); StatusText = future ? $"已导出 {result.TaskCount} 个商品的未来工作安排：{result.OutputPath}" : $"已导出 {result.TaskCount} 个任务、{result.RowCount} 个批次：{result.OutputPath}"; }
    }

    public async Task PreviewAsync(string path)
    {
        if (IsFuturePlan) { StatusText = ImportAvailabilityText; return; }
        await ImportSession.PreviewAsync(path);
    }

    public void CancelPreview()
    {
        ImportSession.CancelPreview();
    }

    public async Task SaveDraftAsync()
    {
        if (IsFuturePlan) { StatusText = ImportAvailabilityText; return; }
        await ImportSession.SaveDraftAsync();
    }

    public async Task SubmitAsync()
    {
        if (IsFuturePlan) { StatusText = ImportAvailabilityText; return; }
        if (!CanUseContent) return;
        await ImportSession.SubmitAsync();
        if (ImportSession.StatusText == "提交已成功，相关页面已刷新。") StatusText = "提交已成功，首页、今日排查、待办任务、详情和历史已刷新。";
    }

    public string OverStockText => ImportSession.OverStockText;

    private async Task RefreshAfterSubmitAsync(IReadOnlyCollection<long> taskIds)
    {
        try { await Task.WhenAll(_refreshAfterSubmit(taskIds), LoadTasksAsync()); }
        catch (Exception exception) { _logException?.Invoke(exception); throw; }
    }

    private async Task<T?> RunAsync<T>(string failure, Func<T> action)
    {
        if (IsActionBusy) return default;
        _isExportBusy = true;
        NotifyImportSessionChanged();
        try { return await Task.Run(() => DatabaseRuntimeGate.Run(action)); }
        catch (Exception exception) { _logException?.Invoke(exception); StatusText = failure; return default; }
        finally { _isExportBusy = false; NotifyImportSessionChanged(); }
    }
    private async Task SetSelectionAsync(bool selected)
    {
        if (_isBulkSelectionBusy) return;
        _isBulkSelectionBusy = true;
        OnPropertyChanged(nameof(CanUseContent)); OnPropertyChanged(nameof(CanGoPrevious)); OnPropertyChanged(nameof(CanGoNext)); RefreshCommands();
        _isBulkSelecting = true;
        try
        {
            var selectionVersion = _selectionVersion;
            var category = SelectedCategory;
            var date = TargetDate;
            var ids = IsFuturePlan && _searchTasks is not null
                ? (await Task.Run(() => DatabaseRuntimeGate.Run(() => _searchTasks(new(PageSize: int.MaxValue, CategoryName: category == "全部" ? null : category, TargetDate: date))))).Items.Select(item => item.TaskId).ToArray()
                : _loadTaskIds is null
                ? VisibleTasks.Select(task => task.TaskId).ToArray()
                : await Task.Run(() => DatabaseRuntimeGate.Run(() => _loadTaskIds(category == "全部" ? null : category)));
            if (selectionVersion != _selectionVersion) return;
            foreach (var id in ids)
            {
                if (selected) _selectedTaskIds.Add(id); else _selectedTaskIds.Remove(id);
            }
            foreach (var task in Tasks) task.IsSelected = _selectedTaskIds.Contains(task.TaskId);
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            StatusText = "更新任务选择失败，请重试。";
        }
        finally { _isBulkSelecting = false; _isBulkSelectionBusy = false; OnPropertyChanged(nameof(CanUseContent)); OnPropertyChanged(nameof(CanGoPrevious)); OnPropertyChanged(nameof(CanGoNext)); RefreshCommands(); }
        OnSelectionChanged();
    }
    private void OnSelectionChanged()
    {
        if (_isBulkSelecting) return;
        foreach (var task in Tasks)
        {
            if (task.IsSelected) _selectedTaskIds.Add(task.TaskId); else _selectedTaskIds.Remove(task.TaskId);
        }
        OnPropertyChanged(nameof(SelectedCount)); RefreshCommands();
    }
    private async Task GoToPageAsync(int page)
    {
        if (!CanUseContent || page < 1 || page > TotalPages) return;
        _currentPage = page;
        OnPropertyChanged(nameof(CurrentPage)); OnPropertyChanged(nameof(PageSummary)); OnPropertyChanged(nameof(CanGoPrevious)); OnPropertyChanged(nameof(CanGoNext));
        await LoadAsync();
    }
    private void ResetSession() => ImportSession.CancelPreview();
    private void RemoveSelectedTaskIds(IEnumerable<long> taskIds)
    {
        _isBulkSelecting = true;
        try
        {
            foreach (var id in taskIds) _selectedTaskIds.Remove(id);
            foreach (var task in Tasks) task.IsSelected = _selectedTaskIds.Contains(task.TaskId);
        }
        finally { _isBulkSelecting = false; }
        OnSelectionChanged();
    }
    private void NotifyImportSessionChanged()
    {
        foreach (var name in new[] { nameof(IsActionBusy), nameof(IsBusy), nameof(IsReadingPlan), nameof(CanUseContent), nameof(CanGoPrevious), nameof(CanGoNext), nameof(PreviewRows), nameof(PreviewFilters), nameof(SelectedPreviewFilter), nameof(VisiblePreviewRows), nameof(AllPreviewFilterText), nameof(SubmittablePreviewFilterText), nameof(BlankPreviewFilterText), nameof(StalePreviewFilterText), nameof(InvalidPreviewFilterText), nameof(HasPreview), nameof(CanSaveDraft), nameof(IsFormValid), nameof(InspectorName), nameof(CheckDateText), nameof(CheckDateValue), nameof(InspectorNameError), nameof(CheckDateError), nameof(HasInspectorNameError), nameof(HasCheckDateError), nameof(PreviewSummaryText), nameof(DraftStatusText), nameof(CompleteTaskIds), nameof(HasPreviewIssues), nameof(PreviewIssueText), nameof(PreviewDetailText), nameof(OverStockText) }) OnPropertyChanged(name);
        if (!string.IsNullOrEmpty(ImportSession.StatusText)) StatusText = ImportSession.StatusText;
        RefreshCommands();
    }
    private void RefreshCommands() { SelectDayCommand.RaiseCanExecuteChanged(); ReloadCommand.RaiseCanExecuteChanged(); SelectAllCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged(); PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged(); SaveDraftCommand.RaiseCanExecuteChanged(); SubmitCommand.RaiseCanExecuteChanged(); }
}
