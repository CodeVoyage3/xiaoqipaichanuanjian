using System.Collections.ObjectModel;
using System.Globalization;
using StoreExpiryInspector.Application.Tasks;

namespace StoreExpiryInspector.UI;

public sealed class InspectionHistoryViewModel : ViewModelBase
{
    private enum ListLoadState { Initial, Loading, Loaded, Error }

    private enum DetailLoadState { None, Loading, Found, NotFound, Error }

    private enum RevisionLoadState { None, Loading, Found, Empty, NotFound, Error }

    private readonly Func<IReadOnlyList<InspectionHistoryListItem>> _loadList;
    private readonly Func<long, InspectionHistoryDetailResult> _loadDetail;
    private readonly Func<long, long, InspectionItemRevisionHistoryResult> _loadRevisions;
    private readonly Func<InspectionHistoryEditRequest, InspectionHistoryEditResult> _editHistory;
    private readonly Func<InspectionHistoryEditRequest, bool> _confirmEdit;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateTime> _utcNow;
    private ListLoadState _listState;
    private DetailLoadState _detailState;
    private RevisionLoadState _revisionState;
    private int _loadVersion;
    private int _detailLoadVersion;
    private int _revisionLoadVersion;
    private string _errorMessage = string.Empty;
    private InspectionHistoryListItem? _selectedRecord;
    private bool _isDetailVisible;
    private string _detailErrorMessage = string.Empty;
    private InspectionHistoryDetail? _detail;
    private InspectionHistoryItemDetail? _selectedDetailItem;
    private string _revisionErrorMessage = string.Empty;
    private InspectionItemRevisionHistory? _revisionHistory;
    private bool _isEditing;
    private bool _isEditSubmitting;
    private string _editCheckedQtyText = string.Empty;
    private string _editValidationMessage = string.Empty;
    private string _editErrorMessage = string.Empty;
    private string _editFeedbackMessage = string.Empty;
    private long? _editInspectionId;
    private long? _editInspectionItemId;

    public InspectionHistoryViewModel(
        Func<IReadOnlyList<InspectionHistoryListItem>> loadList,
        Func<long, InspectionHistoryDetailResult> loadDetail,
        Func<long, long, InspectionItemRevisionHistoryResult> loadRevisions,
        Action<Exception>? logException = null,
        Func<InspectionHistoryEditRequest, InspectionHistoryEditResult>? editHistory = null,
        Func<InspectionHistoryEditRequest, bool>? confirmEdit = null,
        Func<DateTime>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(loadList);
        ArgumentNullException.ThrowIfNull(loadDetail);
        ArgumentNullException.ThrowIfNull(loadRevisions);
        _loadList = loadList;
        _loadDetail = loadDetail;
        _loadRevisions = loadRevisions;
        _editHistory = editHistory ?? (_ => throw new InvalidOperationException("History edit is not configured."));
        _confirmEdit = confirmEdit ?? (_ => false);
        _logException = logException;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        RefreshCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !IsEditBusy);
        RetryCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !IsEditBusy);
        BackCommand = new RelayCommand(_ => BackToList(), _ => !IsEditBusy);
        OpenDetailCommand = new RelayCommand(parameter =>
        {
            if (parameter is InspectionHistoryListItem record)
            {
                OpenDetail(record);
            }
        }, parameter => parameter is InspectionHistoryListItem && !IsEditBusy);
        RetryDetailCommand = new RelayCommand(_ => { _ = LoadDetailAsync(); }, _ => !IsEditBusy);
        RetryRevisionCommand = new RelayCommand(_ => { _ = LoadRevisionsAsync(); }, _ => !IsEditBusy);
        BeginEditCommand = new RelayCommand(_ => BeginEdit(), _ => CanBeginEdit);
        CancelEditCommand = new RelayCommand(_ => CancelEdit(), _ => CanCancelEdit);
        SaveEditCommand = new RelayCommand(_ => { _ = SaveEditAsync(); }, _ => CanSaveEdit);
    }

    public ObservableCollection<InspectionHistoryListItem> Items { get; } = [];

    public ObservableCollection<InspectionHistoryItemDetail> DetailItems { get; } = [];

    public ObservableCollection<InspectionItemRevisionDetail> Revisions { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand RetryCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand OpenDetailCommand { get; }

    public RelayCommand RetryDetailCommand { get; }

    public RelayCommand RetryRevisionCommand { get; }

    public RelayCommand BeginEditCommand { get; }

    public RelayCommand CancelEditCommand { get; }

    public RelayCommand SaveEditCommand { get; }

    public bool IsLoading => _listState == ListLoadState.Loading;

    public bool HasError => _listState == ListLoadState.Error;

    public bool HasLoadedResult => _listState == ListLoadState.Loaded;

    public bool HasEmptyResult => HasLoadedResult && Items.Count == 0;

    public string ErrorMessage => _errorMessage;

    public InspectionHistoryListItem? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (IsEditBusy || ReferenceEquals(_selectedRecord, value))
            {
                return;
            }

            _selectedRecord = value;
            Notify(nameof(SelectedRecord));
            if (value is null)
            {
                _isDetailVisible = false;
                Notify(nameof(IsDetailVisible));
                Interlocked.Increment(ref _detailLoadVersion);
                Interlocked.Increment(ref _revisionLoadVersion);
                ClearDetailState();
            }
        }
    }

    public bool IsDetailVisible => _isDetailVisible;

    public bool IsDetailLoading => _detailState == DetailLoadState.Loading;

    public bool HasDetail => _detailState == DetailLoadState.Found;

    public bool HasDetailError => _detailState == DetailLoadState.Error;

    public bool IsDetailNotFound => _detailState == DetailLoadState.NotFound;

    public string DetailErrorMessage => _detailErrorMessage;

    public InspectionHistoryDetail? Detail => _detail;

    public InspectionHistoryItemDetail? SelectedDetailItem
    {
        get => _selectedDetailItem;
        set
        {
            if (IsEditBusy)
            {
                return;
            }

            SetSelectedDetailItem(value, loadRevisions: true, clearEditMessages: true);
        }
    }

    public bool HasSelectedDetailItem => SelectedDetailItem is not null;

    public bool IsRevisionLoading => _revisionState == RevisionLoadState.Loading;

    public bool HasRevisionError => _revisionState == RevisionLoadState.Error;

    public bool IsRevisionNotFound => _revisionState == RevisionLoadState.NotFound;

    public bool HasRevisionHistory => _revisionState == RevisionLoadState.Found && Revisions.Count > 0;

    public bool HasNoRevisions => _revisionState == RevisionLoadState.Empty;

    public string RevisionErrorMessage => _revisionErrorMessage;

    public InspectionItemRevisionHistory? RevisionHistory => _revisionHistory;

    public bool IsEditing => _isEditing;

    public bool IsEditSubmitting => _isEditSubmitting;

    public bool IsEditBusy => _isEditing || _isEditSubmitting;

    public bool CanChangeHistorySelection => !IsEditBusy;

    public bool CanBeginEdit => HasDetail && SelectedDetailItem is not null && !IsEditBusy;

    public bool CanCancelEdit => IsEditing && !IsEditSubmitting;

    public bool CanSaveEdit => IsEditing && !IsEditSubmitting && !HasEditInputError;

    public string EditCheckedQtyText
    {
        get => _editCheckedQtyText;
        set
        {
            value ??= string.Empty;
            if (!IsEditing || IsEditSubmitting || string.Equals(_editCheckedQtyText, value, StringComparison.Ordinal))
            {
                return;
            }

            _editCheckedQtyText = value;
            UpdateEditValidation();
            _editErrorMessage = string.Empty;
            _editFeedbackMessage = string.Empty;
            NotifyEditMessages();
            SaveEditCommand.RaiseCanExecuteChanged();
        }
    }

    public int? EditCheckedQty => TryParseCheckedQty(_editCheckedQtyText, out var value) ? value : null;

    public string EditValidationMessage => _editValidationMessage;

    public bool HasEditInputError => !string.IsNullOrEmpty(EditValidationMessage);

    public string EditErrorMessage => _editErrorMessage;

    public bool HasEditError => !string.IsNullOrEmpty(EditErrorMessage);

    public string EditFeedbackMessage => _editFeedbackMessage;

    public bool HasEditFeedback => !string.IsNullOrEmpty(EditFeedbackMessage);

    public void BeginEdit()
    {
        if (!CanBeginEdit || Detail is null || SelectedDetailItem is null)
        {
            return;
        }

        _editInspectionId = Detail.InspectionId;
        _editInspectionItemId = SelectedDetailItem.InspectionItemId;
        _editCheckedQtyText = SelectedDetailItem.CheckedQty.ToString(CultureInfo.InvariantCulture);
        _editValidationMessage = string.Empty;
        _editErrorMessage = string.Empty;
        _editFeedbackMessage = string.Empty;
        _isEditing = true;
        NotifyEditState();
        NotifyEditMessages();
    }

    public void CancelEdit()
    {
        if (!CanCancelEdit)
        {
            return;
        }

        EndEditSession();
        _editErrorMessage = string.Empty;
        _editFeedbackMessage = "已取消修改，未写入";
        NotifyEditMessages();
    }

    public async Task SaveEditAsync()
    {
        if (!CanSaveEdit
            || !TryParseCheckedQty(_editCheckedQtyText, out var newCheckedQty)
            || !_editInspectionId.HasValue
            || !_editInspectionItemId.HasValue)
        {
            return;
        }

        var request = new InspectionHistoryEditRequest(
            _editInspectionId.Value,
            _editInspectionItemId.Value,
            newCheckedQty,
            AsUtc(_utcNow()));
        _isEditSubmitting = true;
        _editErrorMessage = string.Empty;
        _editFeedbackMessage = string.Empty;
        NotifyEditState();
        NotifyEditMessages();

        bool confirmed;
        try
        {
            confirmed = _confirmEdit(request);
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            _editErrorMessage = "数量修改确认失败，请重试";
            _isEditSubmitting = false;
            NotifyEditState();
            NotifyEditMessages();
            return;
        }

        if (!confirmed)
        {
            _editFeedbackMessage = "已取消修改，未写入";
            _isEditSubmitting = false;
            NotifyEditState();
            NotifyEditMessages();
            return;
        }

        if (!IsCurrentEditTarget(request))
        {
            _editErrorMessage = "编辑目标已变化，请重新选择后重试";
            _isEditSubmitting = false;
            NotifyEditState();
            NotifyEditMessages();
            return;
        }

        var revisionWasLoading = IsRevisionLoading;
        InvalidateDetailAndRevisionLoads();
        try
        {
            var result = await Task.Run(() => _editHistory(request));
            switch (result.Status)
            {
                case "changed":
                    await HandleChangedAsync(request);
                    break;
                case "no_change":
                    await HandleNoChangeAsync(request);
                    break;
                case "not_found":
                    await HandleNotFoundAsync(request);
                    break;
                default:
                    throw new InvalidOperationException("历史数量修改返回未知状态。");
            }
        }
        catch (Exception exception)
        {
            _logException?.Invoke(exception);
            if (revisionWasLoading)
            {
                SetRevisionRefreshError();
            }

            _editErrorMessage = "数量修改失败，请重试；当前正式数量未更新";
            NotifyEditMessages();
        }
        finally
        {
            _isEditSubmitting = false;
            NotifyEditState();
            NotifyEditMessages();
        }
    }

    public async Task LoadAsync()
    {
        if (IsEditBusy)
        {
            return;
        }

        var version = Interlocked.Increment(ref _loadVersion);
        Interlocked.Increment(ref _detailLoadVersion);
        Interlocked.Increment(ref _revisionLoadVersion);
        _selectedRecord = null;
        Notify(nameof(SelectedRecord));
        _isDetailVisible = false;
        Notify(nameof(IsDetailVisible));
        ClearDetailState();
        Items.Clear();
        Notify(nameof(HasEmptyResult));
        _listState = ListLoadState.Loading;
        _errorMessage = string.Empty;
        Notify(nameof(IsLoading), nameof(HasError), nameof(HasLoadedResult), nameof(HasEmptyResult), nameof(ErrorMessage));

        try
        {
            var records = await Task.Run(_loadList);
            if (version != _loadVersion)
            {
                return;
            }

            foreach (var record in records)
            {
                Items.Add(record);
            }

            _listState = ListLoadState.Loaded;
            Notify(nameof(IsLoading), nameof(HasError), nameof(HasLoadedResult), nameof(HasEmptyResult));
        }
        catch (Exception exception)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logException?.Invoke(exception);
            Items.Clear();
            _listState = ListLoadState.Error;
            _errorMessage = "排查历史加载失败";
            Notify(nameof(IsLoading), nameof(HasError), nameof(HasLoadedResult), nameof(HasEmptyResult), nameof(ErrorMessage));
        }
    }

    public async Task LoadDetailAsync()
    {
        if (!IsEditBusy && SelectedRecord is not null)
        {
            await LoadDetailAsync(SelectedRecord);
        }
    }

    public async Task LoadRevisionsAsync()
    {
        if (!IsEditBusy && SelectedDetailItem is not null && Detail is not null)
        {
            await LoadRevisionsAsync(SelectedDetailItem, Detail.InspectionId);
        }
    }

    public void OpenDetail(InspectionHistoryListItem record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (IsEditBusy)
        {
            return;
        }

        if (!ReferenceEquals(_selectedRecord, record))
        {
            _selectedRecord = record;
            Notify(nameof(SelectedRecord));
        }

        _isDetailVisible = true;
        Notify(nameof(IsDetailVisible));
        _ = LoadDetailAsync(record);
    }

    private async Task HandleChangedAsync(InspectionHistoryEditRequest request)
    {
        var refreshed = await ReloadAfterEditAsync(request.InspectionId, request.InspectionItemId);
        EndEditSession();
        if (!refreshed)
        {
            _editFeedbackMessage = "数量已保存，但详情或修改历史刷新失败，请重试";
            NotifyEditMessages();
            return;
        }

        _editFeedbackMessage = "数量修改成功";
        NotifyEditMessages();
    }

    private async Task HandleNoChangeAsync(InspectionHistoryEditRequest request)
    {
        var refreshed = await ReloadAfterEditAsync(request.InspectionId, request.InspectionItemId);
        EndEditSession();
        _editFeedbackMessage = refreshed
            ? "数量未变化"
            : "数量未变化，但详情或修改历史刷新失败，请重试";
        NotifyEditMessages();
    }

    private async Task HandleNotFoundAsync(InspectionHistoryEditRequest request)
    {
        var record = _selectedRecord;
        if (record is not null && record.InspectionId == request.InspectionId)
        {
            await LoadDetailAsync(record);
        }

        EndEditSession();
        _editFeedbackMessage = "正式排查明细不存在或已失效";
        NotifyEditMessages();
    }

    private async Task<bool> ReloadAfterEditAsync(long inspectionId, long inspectionItemId)
    {
        var record = _selectedRecord;
        if (record is null || record.InspectionId != inspectionId)
        {
            return false;
        }

        await LoadDetailAsync(record);
        if (!HasDetail || Detail?.InspectionId != inspectionId)
        {
            return false;
        }

        var refreshedItem = DetailItems.FirstOrDefault(item => item.InspectionItemId == inspectionItemId);
        if (refreshedItem is null)
        {
            return false;
        }

        SetSelectedDetailItem(refreshedItem, loadRevisions: false, clearEditMessages: false);
        await LoadRevisionsAsync(refreshedItem, inspectionId);
        return _revisionState is RevisionLoadState.Found or RevisionLoadState.Empty;
    }

    private bool IsCurrentEditTarget(InspectionHistoryEditRequest request) =>
        _isEditing
        && _editInspectionId == request.InspectionId
        && _editInspectionItemId == request.InspectionItemId
        && Detail?.InspectionId == request.InspectionId
        && SelectedDetailItem?.InspectionItemId == request.InspectionItemId;

    private void InvalidateDetailAndRevisionLoads()
    {
        Interlocked.Increment(ref _detailLoadVersion);
        Interlocked.Increment(ref _revisionLoadVersion);
    }

    private void EndEditSession()
    {
        _isEditing = false;
        _editInspectionId = null;
        _editInspectionItemId = null;
        _editCheckedQtyText = string.Empty;
        _editValidationMessage = string.Empty;
        NotifyEditState();
    }

    private void SetSelectedDetailItem(
        InspectionHistoryItemDetail? value,
        bool loadRevisions,
        bool clearEditMessages)
    {
        if (ReferenceEquals(_selectedDetailItem, value))
        {
            return;
        }

        _selectedDetailItem = value;
        Notify(nameof(SelectedDetailItem), nameof(HasSelectedDetailItem));
        Interlocked.Increment(ref _revisionLoadVersion);
        ClearRevisionState();
        if (clearEditMessages)
        {
            _editErrorMessage = string.Empty;
            _editFeedbackMessage = string.Empty;
            NotifyEditMessages();
        }

        NotifyEditState();
        if (loadRevisions && value is not null && Detail is not null)
        {
            _ = LoadRevisionsAsync(value, Detail.InspectionId);
        }
    }

    private void UpdateEditValidation()
    {
        _editValidationMessage = TryParseCheckedQty(_editCheckedQtyText, out _)
            ? string.Empty
            : "请输入非负整数";
    }

    private void SetRevisionRefreshError()
    {
        _revisionState = RevisionLoadState.Error;
        _revisionErrorMessage = "修改历史刷新失败，请重试";
        Revisions.Clear();
        _revisionHistory = null;
        Notify(nameof(RevisionHistory), nameof(RevisionErrorMessage), nameof(IsRevisionLoading), nameof(HasRevisionError), nameof(IsRevisionNotFound), nameof(HasRevisionHistory), nameof(HasNoRevisions));
    }

    private void NotifyEditState()
    {
        Notify(
            nameof(IsEditing),
            nameof(IsEditSubmitting),
            nameof(IsEditBusy),
            nameof(CanChangeHistorySelection),
            nameof(CanBeginEdit),
            nameof(CanCancelEdit),
            nameof(CanSaveEdit));
        BeginEditCommand.RaiseCanExecuteChanged();
        CancelEditCommand.RaiseCanExecuteChanged();
        SaveEditCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        OpenDetailCommand.RaiseCanExecuteChanged();
        RetryDetailCommand.RaiseCanExecuteChanged();
        RetryRevisionCommand.RaiseCanExecuteChanged();
    }

    private void NotifyEditMessages() => Notify(
        nameof(EditCheckedQtyText),
        nameof(EditCheckedQty),
        nameof(EditValidationMessage),
        nameof(HasEditInputError),
        nameof(EditErrorMessage),
        nameof(HasEditError),
        nameof(EditFeedbackMessage),
        nameof(HasEditFeedback));

    private static bool TryParseCheckedQty(string value, out int checkedQty) =>
        int.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out checkedQty)
        && checkedQty >= 0;

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private async Task LoadDetailAsync(InspectionHistoryListItem record)
    {
        var version = Interlocked.Increment(ref _detailLoadVersion);
        Interlocked.Increment(ref _revisionLoadVersion);
        ClearDetailState();
        _detailState = DetailLoadState.Loading;
        Notify(nameof(IsDetailLoading), nameof(HasDetail), nameof(HasDetailError), nameof(IsDetailNotFound));

        try
        {
            var result = await Task.Run(() => _loadDetail(record.InspectionId));
            if (version != _detailLoadVersion || SelectedRecord?.InspectionId != record.InspectionId)
            {
                return;
            }

            if (string.Equals(result.Status, "not_found", StringComparison.Ordinal))
            {
                _detailState = DetailLoadState.NotFound;
                Notify(nameof(IsDetailLoading), nameof(HasDetail), nameof(HasDetailError), nameof(IsDetailNotFound));
                return;
            }

            if (!string.Equals(result.Status, "found", StringComparison.Ordinal) || result.Detail is null)
            {
                throw new InvalidOperationException("排查详情查询返回未知状态。");
            }

            _detail = result.Detail;
            DetailItems.Clear();
            foreach (var item in result.Detail.Items)
            {
                DetailItems.Add(item);
            }

            _detailState = DetailLoadState.Found;
            Notify(nameof(Detail), nameof(IsDetailLoading), nameof(HasDetail), nameof(HasDetailError), nameof(IsDetailNotFound));
        }
        catch (Exception exception)
        {
            if (version != _detailLoadVersion || SelectedRecord?.InspectionId != record.InspectionId)
            {
                return;
            }

            _logException?.Invoke(exception);
            _detailErrorMessage = "排查详情加载失败";
            _detailState = DetailLoadState.Error;
            Notify(nameof(DetailErrorMessage), nameof(IsDetailLoading), nameof(HasDetail), nameof(HasDetailError), nameof(IsDetailNotFound));
        }
    }

    private async Task LoadRevisionsAsync(InspectionHistoryItemDetail item, long inspectionId)
    {
        var version = Interlocked.Increment(ref _revisionLoadVersion);
        ClearRevisionState();
        _revisionState = RevisionLoadState.Loading;
        Notify(nameof(IsRevisionLoading), nameof(HasRevisionError), nameof(IsRevisionNotFound), nameof(HasRevisionHistory), nameof(HasNoRevisions));

        try
        {
            var result = await Task.Run(() => _loadRevisions(inspectionId, item.InspectionItemId));
            if (version != _revisionLoadVersion
                || Detail?.InspectionId != inspectionId
                || SelectedDetailItem?.InspectionItemId != item.InspectionItemId)
            {
                return;
            }

            if (string.Equals(result.Status, "not_found", StringComparison.Ordinal))
            {
                _revisionState = RevisionLoadState.NotFound;
                Notify(nameof(IsRevisionLoading), nameof(HasRevisionError), nameof(IsRevisionNotFound), nameof(HasRevisionHistory), nameof(HasNoRevisions));
                return;
            }

            if (!string.Equals(result.Status, "found", StringComparison.Ordinal) || result.History is null)
            {
                throw new InvalidOperationException("修改历史查询返回未知状态。");
            }

            _revisionHistory = result.History;
            Revisions.Clear();
            foreach (var revision in result.History.Revisions)
            {
                Revisions.Add(revision);
            }

            _revisionState = Revisions.Count == 0 ? RevisionLoadState.Empty : RevisionLoadState.Found;
            Notify(nameof(RevisionHistory), nameof(IsRevisionLoading), nameof(HasRevisionError), nameof(IsRevisionNotFound), nameof(HasRevisionHistory), nameof(HasNoRevisions));
        }
        catch (Exception exception)
        {
            if (version != _revisionLoadVersion
                || Detail?.InspectionId != inspectionId
                || SelectedDetailItem?.InspectionItemId != item.InspectionItemId)
            {
                return;
            }

            _logException?.Invoke(exception);
            _revisionErrorMessage = "修改历史加载失败";
            _revisionState = RevisionLoadState.Error;
            Notify(nameof(RevisionErrorMessage), nameof(IsRevisionLoading), nameof(HasRevisionError), nameof(IsRevisionNotFound), nameof(HasRevisionHistory), nameof(HasNoRevisions));
        }
    }

    private void BackToList()
    {
        if (IsEditBusy)
        {
            return;
        }

        Interlocked.Increment(ref _detailLoadVersion);
        Interlocked.Increment(ref _revisionLoadVersion);
        _selectedRecord = null;
        Notify(nameof(SelectedRecord));
        _isDetailVisible = false;
        Notify(nameof(IsDetailVisible));
        ClearDetailState();
    }

    private void ClearDetailState()
    {
        _detailState = DetailLoadState.None;
        _detailErrorMessage = string.Empty;
        _detail = null;
        DetailItems.Clear();
        _selectedDetailItem = null;
        Notify(nameof(Detail), nameof(DetailErrorMessage), nameof(SelectedDetailItem), nameof(HasSelectedDetailItem));
        ClearRevisionState();
        Notify(nameof(IsDetailLoading), nameof(HasDetail), nameof(HasDetailError), nameof(IsDetailNotFound));
        if (!IsEditBusy)
        {
            _editErrorMessage = string.Empty;
            _editFeedbackMessage = string.Empty;
            NotifyEditMessages();
        }

        NotifyEditState();
    }

    private void ClearRevisionState()
    {
        _revisionState = RevisionLoadState.None;
        _revisionErrorMessage = string.Empty;
        _revisionHistory = null;
        Revisions.Clear();
        Notify(nameof(RevisionHistory), nameof(RevisionErrorMessage), nameof(IsRevisionLoading), nameof(HasRevisionError), nameof(IsRevisionNotFound), nameof(HasRevisionHistory), nameof(HasNoRevisions));
    }

    private void Notify(params string[] names)
    {
        foreach (var name in names)
        {
            OnPropertyChanged(name);
        }
    }
}
