using System.Collections.ObjectModel;
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
    private readonly Action<Exception>? _logException;
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

    public InspectionHistoryViewModel(
        Func<IReadOnlyList<InspectionHistoryListItem>> loadList,
        Func<long, InspectionHistoryDetailResult> loadDetail,
        Func<long, long, InspectionItemRevisionHistoryResult> loadRevisions,
        Action<Exception>? logException = null)
    {
        ArgumentNullException.ThrowIfNull(loadList);
        ArgumentNullException.ThrowIfNull(loadDetail);
        ArgumentNullException.ThrowIfNull(loadRevisions);
        _loadList = loadList;
        _loadDetail = loadDetail;
        _loadRevisions = loadRevisions;
        _logException = logException;
        RefreshCommand = new RelayCommand(_ => { _ = LoadAsync(); });
        RetryCommand = new RelayCommand(_ => { _ = LoadAsync(); });
        BackCommand = new RelayCommand(_ => BackToList());
        OpenDetailCommand = new RelayCommand(parameter =>
        {
            if (parameter is InspectionHistoryListItem record)
            {
                OpenDetail(record);
            }
        });
        RetryDetailCommand = new RelayCommand(_ => { _ = LoadDetailAsync(); });
        RetryRevisionCommand = new RelayCommand(_ => { _ = LoadRevisionsAsync(); });
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
            if (ReferenceEquals(_selectedRecord, value))
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
            if (ReferenceEquals(_selectedDetailItem, value))
            {
                return;
            }

            _selectedDetailItem = value;
            Notify(nameof(SelectedDetailItem), nameof(HasSelectedDetailItem));
            Interlocked.Increment(ref _revisionLoadVersion);
            ClearRevisionState();
            if (value is not null && Detail is not null)
            {
                _ = LoadRevisionsAsync(value, Detail.InspectionId);
            }
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

    public async Task LoadAsync()
    {
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
        if (SelectedRecord is not null)
        {
            await LoadDetailAsync(SelectedRecord);
        }
    }

    public async Task LoadRevisionsAsync()
    {
        if (SelectedDetailItem is not null && Detail is not null)
        {
            await LoadRevisionsAsync(SelectedDetailItem, Detail.InspectionId);
        }
    }

    public void OpenDetail(InspectionHistoryListItem record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!ReferenceEquals(_selectedRecord, record))
        {
            _selectedRecord = record;
            Notify(nameof(SelectedRecord));
        }

        _isDetailVisible = true;
        Notify(nameof(IsDetailVisible));
        _ = LoadDetailAsync(record);
    }

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

