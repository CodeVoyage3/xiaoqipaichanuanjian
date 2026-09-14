using System.Collections.ObjectModel;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.UI;

public sealed class ProductCatalogViewModel : ViewModelBase
{
    private readonly Func<ProductCatalogRequest, ProductCatalogPage> _search;
    private readonly Func<long, ProductCatalogDetail?> _detail;
    private readonly Action<Exception>? _log;
    private string _searchText = string.Empty, _category = string.Empty, _stage = string.Empty, _taskStatus = string.Empty, _error = string.Empty;
    private int _page = 1, _total; private bool _loading, _hasError; private ProductCatalogDetail? _selected;
    public ProductCatalogViewModel(Func<ProductCatalogRequest, ProductCatalogPage> search, Func<long, ProductCatalogDetail?> detail, Action<Exception>? log = null)
    { _search = search; _detail = detail; _log = log; RefreshCommand = new RelayCommand(_ => { _page = 1; _ = LoadAsync(); }); ClearCommand = new RelayCommand(_ => { SearchText = Category = Stage = TaskStatus = string.Empty; _page = 1; _ = LoadAsync(); }); PreviousPageCommand = new RelayCommand(_ => { if (_page > 1) { _page--; _ = LoadAsync(); } }); NextPageCommand = new RelayCommand(_ => { if (_page * 50 < Total) { _page++; _ = LoadAsync(); } }); }
    public ObservableCollection<ProductCatalogItem> Items { get; } = [];
    public IReadOnlyList<string> Categories { get; } = ["", "食品", "宠物", "日用", "美妆", "家居", "香氛香水", "文具", "潮流玩具", "应季搭配", "赠品小样"];
    public IReadOnlyList<string> Stages { get; } = ["", ExpiryStageCalculator.None, ExpiryStageCalculator.Discount50, ExpiryStageCalculator.Discount20, ExpiryStageCalculator.Withdraw, ExpiryStageCalculator.Expired];
    public IReadOnlyList<string> TaskStatuses { get; } = ["", "open", "none"];
    public RelayCommand RefreshCommand { get; } public RelayCommand ClearCommand { get; } public RelayCommand PreviousPageCommand { get; } public RelayCommand NextPageCommand { get; }
    public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); } }
    public string Category { get => _category; set { if (_category == value) return; _category = value; OnPropertyChanged(); } }
    public string Stage { get => _stage; set { if (_stage == value) return; _stage = value; OnPropertyChanged(); } }
    public string TaskStatus { get => _taskStatus; set { if (_taskStatus == value) return; _taskStatus = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _loading; private set { _loading = value; OnPropertyChanged(); } } public bool HasError { get => _hasError; private set { _hasError = value; OnPropertyChanged(); } } public string Error { get => _error; private set { _error = value; OnPropertyChanged(); } }
    public int Total { get => _total; private set { _total = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageText)); } } public string PageText => $"第 {_page} 页 · 共 {Total} 个商品";
    public ProductCatalogDetail? Selected { get => _selected; private set { _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOpenTask)); } } public bool HasOpenTask => Selected?.Product.OpenTaskId is not null;
    public async Task LoadAsync() { IsLoading = true; HasError = false; try { var result = await Task.Run(() => _search(new(SearchText, Category, Stage, TaskStatus, _page))); Items.Clear(); foreach (var item in result.Items) Items.Add(item); Total = result.TotalCount; } catch (Exception ex) { _log?.Invoke(ex); HasError = true; Error = "商品明细加载失败"; } finally { IsLoading = false; } }
    public async Task OpenAsync(long productId) { IsLoading = true; try { Selected = await Task.Run(() => _detail(productId)); } catch (Exception ex) { _log?.Invoke(ex); HasError = true; Error = "商品详情加载失败"; } finally { IsLoading = false; } }
    public void ClearDetail() => Selected = null;
}
