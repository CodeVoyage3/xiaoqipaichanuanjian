using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Application.Backups;
using StoreExpiryInspector.Application.Imports;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Logging;

namespace StoreExpiryInspector.UI;

public enum ShellPage
{
    Dashboard,
    PendingTasks,
    History,
    Import,
    TodayInspection,
    BackupRestore,
    InspectionDetail
}

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record StageFilterOption(string Label, string? CanonicalStage)
{
    public static IReadOnlyList<StageFilterOption> All { get; } = Array.AsReadOnly(new StageFilterOption[]
    {
        new("全部阶段", null),
        new("过期", "expired"),
        new("收仓", "withdraw"),
        new("2折", "discount_20"),
        new("5折", "discount_50")
    });
}

public sealed record CategoryFilterOption(string Label, string? CategoryName);

public static class StageLabels
{
    public static string ToDisplay(string? stage) => ExpiryStageCalculator.ToDisplay(stage);
}

public sealed class StageLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        StageLabels.ToDisplay(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class DateOnlyDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateOnly date
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class HumanReadableFileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IConvertible convertible)
        {
            return "—";
        }

        long bytes;
        try
        {
            bytes = convertible.ToInt64(CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is not long)
        {
            return "—";
        }

        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        var kibibytes = bytes / 1024d;
        if (kibibytes < 1024)
        {
            return $"{kibibytes:0.#} KB";
        }

        var mebibytes = kibibytes / 1024d;
        return $"{mebibytes:0.#} MB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class UtcDateTimeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime date
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class Stage4BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class Stage4InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class DashboardViewModel : ViewModelBase
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    private readonly Func<InspectionDashboardResult> _loadDashboard;
    private readonly Func<InspectionTaskSearchRequest, InspectionTaskSearchResult>? _searchTasks;
    private readonly Action<Exception>? _logException;
    private readonly Func<DateTime> _utcNow;
    private readonly Action? _openTasks;
    private int _loadVersion;
    private bool _isLoading;
    private bool _hasError;
    private bool _hasLoadedResult;
    private string _errorMessage = string.Empty;
    private int _openTaskCount;
    private int _expiredCount;
    private int _withdrawCount;
    private int _discount20Count;
    private int _discount50Count;
    private int _productCount;
    private int _batchCount;
    private DateTime? _lastSuccessfulImportAtUtc;
    private bool _isSearchActive;
    private int _searchResultCount;

    public DashboardViewModel(
        Func<InspectionDashboardResult> loadDashboard,
        Action<Exception>? logException = null,
        Func<DateTime>? utcNow = null,
        Action? openTasks = null,
        Func<InspectionTaskSearchRequest, InspectionTaskSearchResult>? searchTasks = null)
    {
        ArgumentNullException.ThrowIfNull(loadDashboard);
        _loadDashboard = loadDashboard;
        _logException = logException;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _openTasks = openTasks;
        _searchTasks = searchTasks;
        RefreshCommand = new RelayCommand(parameter => { _ = LoadAsync(); });
        ViewAllTasksCommand = new RelayCommand(_ => _openTasks?.Invoke());
    }

    public ObservableCollection<InspectionTaskListItem> UrgentTasks { get; } = [];

    public bool IsSearchActive
    {
        get => _isSearchActive;
        private set
        {
            if (_isSearchActive == value)
            {
                return;
            }

            _isSearchActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(SearchResultText));
        }
    }

    public int SearchResultCount
    {
        get => _searchResultCount;
        private set
        {
            if (_searchResultCount == value)
            {
                return;
            }

            _searchResultCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(SearchResultText));
        }
    }

    public bool HasNoSearchResults => IsSearchActive
        && !IsLoading
        && !HasError
        && SearchResultCount == 0;

    public string SearchResultText => IsSearchActive
        ? $"搜索结果：{SearchResultCount} 个商品"
        : string.Empty;

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ViewAllTasksCommand { get; }

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
            OnPropertyChanged(nameof(HasNoSearchResults));
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError == value)
            {
                return;
            }

            _hasError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLoadedResult));
            OnPropertyChanged(nameof(HasNoImportData));
            OnPropertyChanged(nameof(HasNoOpenTasks));
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(IsStale));
        }
    }

    public bool HasLoadedResult
    {
        get => _hasLoadedResult;
        private set
        {
            if (_hasLoadedResult == value)
            {
                return;
            }

            _hasLoadedResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNoImportData));
            OnPropertyChanged(nameof(HasNoOpenTasks));
            OnPropertyChanged(nameof(IsStale));
            OnPropertyChanged(nameof(LastImportText));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public int OpenTaskCount
    {
        get => _openTaskCount;
        private set
        {
            if (_openTaskCount == value)
            {
                return;
            }

            _openTaskCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNoOpenTasks));
        }
    }

    public int ExpiredCount
    {
        get => _expiredCount;
        private set
        {
            if (_expiredCount == value)
            {
                return;
            }

            _expiredCount = value;
            OnPropertyChanged();
        }
    }

    public int WithdrawCount
    {
        get => _withdrawCount;
        private set
        {
            if (_withdrawCount == value)
            {
                return;
            }

            _withdrawCount = value;
            OnPropertyChanged();
        }
    }

    public int Discount20Count
    {
        get => _discount20Count;
        private set
        {
            if (_discount20Count == value)
            {
                return;
            }

            _discount20Count = value;
            OnPropertyChanged();
        }
    }

    public int Discount50Count
    {
        get => _discount50Count;
        private set
        {
            if (_discount50Count == value)
            {
                return;
            }

            _discount50Count = value;
            OnPropertyChanged();
        }
    }

    public int ProductCount
    {
        get => _productCount;
        private set
        {
            if (_productCount == value)
            {
                return;
            }

            _productCount = value;
            OnPropertyChanged();
        }
    }

    public int BatchCount
    {
        get => _batchCount;
        private set
        {
            if (_batchCount == value)
            {
                return;
            }

            _batchCount = value;
            OnPropertyChanged();
        }
    }

    public DateTime? LastSuccessfulImportAtUtc
    {
        get => _lastSuccessfulImportAtUtc;
        private set
        {
            if (_lastSuccessfulImportAtUtc == value)
            {
                return;
            }

            _lastSuccessfulImportAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastImportText));
            OnPropertyChanged(nameof(HasNoImportData));
            OnPropertyChanged(nameof(HasNoOpenTasks));
            OnPropertyChanged(nameof(IsStale));
            OnPropertyChanged(nameof(FreshnessWarningText));
        }
    }

    public bool HasNoImportData => HasLoadedResult && !HasError && !LastSuccessfulImportAtUtc.HasValue;

    public bool HasNoOpenTasks => HasLoadedResult
        && !HasError
        && LastSuccessfulImportAtUtc.HasValue
        && OpenTaskCount == 0;

    public bool IsStale => HasLoadedResult
        && !HasError
        && IsOlderThanSevenDays(LastSuccessfulImportAtUtc, _utcNow());

    public string LastImportText => !HasLoadedResult
        ? (HasError ? "数据加载失败" : "正在加载…")
        : LastSuccessfulImportAtUtc.HasValue
            ? $"最近一次成功导入：{FormatLastSuccessfulImport(LastSuccessfulImportAtUtc.Value)}"
            : "暂无导入数据";

    public string FreshnessWarningText => IsStale ? "数据已超过7天未更新" : string.Empty;

    public async Task SearchAsync(string? searchText)
    {
        var normalizedSearchText = searchText?.Trim() ?? string.Empty;
        if (normalizedSearchText.Length == 0)
        {
            await LoadAsync();
            return;
        }

        if (_searchTasks is null)
        {
            _openTasks?.Invoke();
            return;
        }

        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        IsSearchActive = true;

        try
        {
            var result = await Task.Run(() => DatabaseRuntimeGate.Run(() => _searchTasks(
                new InspectionTaskSearchRequest(normalizedSearchText, null, 1, 20))));
            if (version != _loadVersion)
            {
                return;
            }

            UrgentTasks.Clear();
            foreach (var item in result.Items)
            {
                UrgentTasks.Add(item);
            }

            SearchResultCount = result.TotalCount;
            HasLoadedResult = true;
        }
        catch (Exception exception)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logException?.Invoke(exception);
            UrgentTasks.Clear();
            SearchResultCount = 0;
            HasLoadedResult = false;
            HasError = true;
            ErrorMessage = "首页搜索失败";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    public async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        IsSearchActive = false;
        SearchResultCount = 0;
        OnPropertyChanged(nameof(LastImportText));

        try
        {
            var result = await Task.Run(() => DatabaseRuntimeGate.Run(_loadDashboard));
            if (version != _loadVersion)
            {
                return;
            }

            OpenTaskCount = result.OpenTaskCount;
            ExpiredCount = result.ExpiredCount;
            WithdrawCount = result.WithdrawCount;
            Discount20Count = result.Discount20Count;
            Discount50Count = result.Discount50Count;
            ProductCount = result.ProductCount;
            BatchCount = result.BatchCount;
            LastSuccessfulImportAtUtc = result.LastSuccessfulImportAtUtc;
            UrgentTasks.Clear();
            foreach (var task in result.UrgentTasks)
            {
                UrgentTasks.Add(task);
            }

            HasLoadedResult = true;
        }
        catch (Exception exception)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logException?.Invoke(exception);
            UrgentTasks.Clear();
            HasLoadedResult = false;
            HasError = true;
            ErrorMessage = "数据加载失败";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(LastImportText));
                OnPropertyChanged(nameof(FreshnessWarningText));
            }
        }
    }

    public static bool IsOlderThanSevenDays(DateTime? importedAtUtc, DateTime nowUtc)
    {
        if (!importedAtUtc.HasValue)
        {
            return false;
        }

        var imported = AsUtc(importedAtUtc.Value);
        var now = AsUtc(nowUtc);
        return now >= imported && now - imported > StaleAfter;
    }

    public static string FormatLastSuccessfulImport(DateTime importedAtUtc) =>
        AsUtc(importedAtUtc)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed class PendingTasksViewModel : ViewModelBase
{
    public const int FixedPageSize = 50;

    private readonly Func<InspectionTaskSearchRequest, InspectionTaskSearchResult> _searchTasks;
    private readonly Func<IReadOnlyList<string>>? _loadCategories;
    private readonly Action<Exception>? _logException;
    private int _loadVersion;
    private bool _isLoading;
    private bool _hasError;
    private bool _hasLoadedResult;
    private string _errorMessage = string.Empty;
    private string _searchText = string.Empty;
    private string? _selectedStage;
    private string? _selectedCategory;
    private int _currentPage = 1;
    private int _totalCount;
    private int _totalPages = 1;

    public PendingTasksViewModel(
        Func<InspectionTaskSearchRequest, InspectionTaskSearchResult> searchTasks,
        Action<Exception>? logException = null,
        Func<IReadOnlyList<string>>? loadCategories = null)
    {
        ArgumentNullException.ThrowIfNull(searchTasks);
        _searchTasks = searchTasks;
        _loadCategories = loadCategories;
        _logException = logException;
        SearchCommand = new RelayCommand(parameter => { _ = SearchAsync(); });
        ClearFiltersCommand = new RelayCommand(parameter => { _ = ClearFiltersAsync(); });
        RetryCommand = new RelayCommand(parameter => { _ = LoadAsync(); });
        PreviousPageCommand = new RelayCommand(parameter => { _ = GoToPreviousPageAsync(); });
        NextPageCommand = new RelayCommand(parameter => { _ = GoToNextPageAsync(); });
    }

    public ObservableCollection<InspectionTaskListItem> Items { get; } = [];

    public IReadOnlyList<StageFilterOption> StageFilters => StageFilterOption.All;

    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = [];

    public RelayCommand SearchCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand RetryCommand { get; }

    public RelayCommand PreviousPageCommand { get; }

    public RelayCommand NextPageCommand { get; }

    public int PageSize => FixedPageSize;

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
            OnPropertyChanged(nameof(HasEmptyResult));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError == value)
            {
                return;
            }

            _hasError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEmptyResult));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
        }
    }

    public bool HasLoadedResult
    {
        get => _hasLoadedResult;
        private set
        {
            if (_hasLoadedResult == value)
            {
                return;
            }

            _hasLoadedResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEmptyResult));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchText));
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
        }
    }

    public string? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (string.Equals(_selectedStage, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedStage = value;
            _currentPage = 1;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
            _ = LoadAsync();
        }
    }

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (string.Equals(_selectedCategory, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedCategory = value;
            CurrentPage = 1;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
            _ = LoadAsync();
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
            {
                return;
            }

            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (_totalCount == value)
            {
                return;
            }

            _totalCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(HasEmptyResult));
            OnPropertyChanged(nameof(IsFilterEmpty));
            OnPropertyChanged(nameof(IsDatabaseEmpty));
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (_totalPages == value)
            {
                return;
            }

            _totalPages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public bool CanGoPrevious => CurrentPage > 1;

    public bool CanGoNext => CurrentPage < TotalPages;

    public string PageSummary => $"第 {CurrentPage} / {TotalPages} 页 · 共 {TotalCount} 个商品";

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public bool IsFilterActive => !string.IsNullOrWhiteSpace(SearchText)
        || !string.IsNullOrWhiteSpace(SelectedStage)
        || !string.IsNullOrWhiteSpace(SelectedCategory);

    public bool HasEmptyResult => HasLoadedResult && !HasError && !IsLoading && TotalCount == 0;

    public bool IsFilterEmpty => HasEmptyResult && IsFilterActive;

    public bool IsDatabaseEmpty => HasEmptyResult && !IsFilterActive;

    public async Task SearchAsync()
    {
        CurrentPage = 1;
        await LoadAsync();
    }

    public async Task ClearFiltersAsync()
    {
        var changed = !string.IsNullOrEmpty(_searchText) || _selectedStage is not null || _selectedCategory is not null || CurrentPage != 1;
        _searchText = string.Empty;
        _selectedStage = null;
        _selectedCategory = null;
        CurrentPage = 1;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(SelectedStage));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(IsFilterActive));
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(IsDatabaseEmpty));
        if (changed || !HasLoadedResult)
        {
            await LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var result = await Task.Run(() => DatabaseRuntimeGate.Run(() =>
            {
                var allTasks = _searchTasks(new InspectionTaskSearchRequest(PageSize: int.MaxValue));
                var filteredTasks = string.IsNullOrWhiteSpace(SearchText) && string.IsNullOrEmpty(SelectedStage)
                    ? allTasks
                    : _searchTasks(new InspectionTaskSearchRequest(SearchText, SelectedStage, PageSize: int.MaxValue));
                return (allTasks.Items, filteredTasks.Items);
            }));
            if (version != _loadVersion)
            {
                return;
            }

            if (_loadCategories is not null)
            {
                var categories = await Task.Run(() => DatabaseRuntimeGate.Run(_loadCategories));
                if (version != _loadVersion)
                {
                    return;
                }

                UpdateCategoryFilters(categories);
            }
            else
            {
                UpdateCategoryFilters(result.Item1);
            }
            var filteredItems = string.IsNullOrEmpty(SelectedCategory)
                ? result.Item2
                : result.Item2.Where(item => string.Equals(item.CategoryName, SelectedCategory, StringComparison.Ordinal)).ToArray();
            TotalCount = filteredItems.Count;
            TotalPages = Math.Max(1, (TotalCount + FixedPageSize - 1) / FixedPageSize);
            if (CurrentPage > TotalPages)
            {
                CurrentPage = TotalPages;
            }

            Items.Clear();
            foreach (var item in filteredItems.Skip((CurrentPage - 1) * FixedPageSize).Take(FixedPageSize))
            {
                Items.Add(item);
            }

            HasLoadedResult = true;
        }
        catch (Exception exception)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logException?.Invoke(exception);
            Items.Clear();
            HasLoadedResult = false;
            HasError = true;
            ErrorMessage = "待排查任务加载失败";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    public async Task GoToPreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        CurrentPage--;
        await LoadAsync();
    }

    public async Task GoToNextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        CurrentPage++;
        await LoadAsync();
    }

    private void UpdateCategoryFilters(IReadOnlyList<InspectionTaskListItem> allTasks)
    {
        UpdateCategoryFilters(allTasks
            .Select(item => item.CategoryName)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray());
    }

    private void UpdateCategoryFilters(IReadOnlyList<string> categories)
    {
        if (CategoryFilters.Select(option => option.CategoryName).SequenceEqual(categories.Prepend<string?>(null)))
        {
            return;
        }

        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryFilterOption("全部大类", null));
        foreach (var category in categories)
        {
            CategoryFilters.Add(new CategoryFilterOption(category, category));
        }

        OnPropertyChanged(nameof(CategoryFilters));
        if (_selectedCategory is not null && !categories.Contains(_selectedCategory, StringComparer.Ordinal))
        {
            _selectedCategory = null;
            OnPropertyChanged(nameof(SelectedCategory));
        }
    }
}

public sealed class ShellViewModel : ViewModelBase
{
    private readonly LocalFileLogger _logger;
    private ShellPage _currentPage;
    private ShellPage _detailReturnPage = ShellPage.Dashboard;

    public ShellViewModel(
        Func<InspectionDashboardResult>? dashboardLoader = null,
        Func<InspectionTaskSearchRequest, InspectionTaskSearchResult>? taskLoader = null,
        Action<Exception>? logException = null,
        Func<DateTime>? utcNow = null,
        Func<long, InspectionTaskDetailResult>? detailLoader = null,
        Func<bool>? confirmClearDraft = null,
        Func<bool>? confirmZeroInventory = null,
        Func<InspectionSubmissionRequest, InspectionSubmissionResult>? submit = null,
        Func<IReadOnlyList<InspectionHistoryListItem>>? historyListLoader = null,
        Func<long, InspectionHistoryDetailResult>? historyDetailLoader = null,
        Func<long, long, InspectionItemRevisionHistoryResult>? historyRevisionLoader = null,
        Func<InspectionHistoryEditRequest, InspectionHistoryEditResult>? historyEdit = null,
        Func<InspectionHistoryEditRequest, bool>? confirmHistoryEdit = null,
        Func<LocalDatabaseBackupListItem, bool>? confirmRestore = null,
        Func<IReadOnlyList<LocalDatabaseBackupListItem>>? backupLoader = null,
        Func<LocalDatabaseBackupResult>? backupCreator = null,
        Func<string, DatabaseRestoreResult>? backupRestorer = null,
        Func<string, ImportPreviewLoadResult>? importParser = null,
        Func<SaveDraftRequest, SaveDraftResult>? saveDraft = null,
        Func<ReconfirmItemRequest, ReconfirmItemResult>? reconfirmItem = null,
        Func<ClearDraftRequest, ClearDraftResult>? clearDraft = null,
        Func<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult>? adjustInventory = null,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirmTodayOverStock = null,
        Func<ExpiredInventoryWarning, bool>? confirmTodayExpiredInventory = null,
        Func<bool>? confirmTodaySubmission = null,
        Func<IReadOnlyList<string>>? categoryLoader = null,
        Func<StoreDbContext>? defaultContextFactory = null,
        Func<ImportPreviewIdentity, ImportConfirmationResult>? importPreviewConfirmation = null,
        Func<ImportConfirmationContract, DateTime, ConfirmedImportResult>? importExecutor = null)
    {
        _logger = new LocalFileLogger(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoreExpiryInspector",
            "logs"));
        var logger = logException ?? LogException;
        var hasInjectedReadDependency = dashboardLoader is not null || taskLoader is not null || categoryLoader is not null
            || historyListLoader is not null || historyDetailLoader is not null || historyRevisionLoader is not null || historyEdit is not null
            || backupLoader is not null || backupCreator is not null || backupRestorer is not null || importParser is not null || importPreviewConfirmation is not null || importExecutor is not null
            || detailLoader is not null || saveDraft is not null || reconfirmItem is not null || clearDraft is not null || adjustInventory is not null || submit is not null;
        var contextFactory = defaultContextFactory ?? (() => DatabaseInitializer.CreateContext());
        var loadDashboard = dashboardLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<InspectionDashboardResult>("dashboardLoader")
            : CreateDashboardLoader(contextFactory));
        var searchTasks = taskLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<InspectionTaskSearchRequest, InspectionTaskSearchResult>("taskLoader")
            : CreateTaskLoader(contextFactory));
        var loadCategories = categoryLoader ?? (hasInjectedReadDependency
            ? null
            : CreateCategoryLoader(contextFactory));
        var loadHistory = historyListLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<IReadOnlyList<InspectionHistoryListItem>>("historyListLoader")
            : CreateHistoryLoader(contextFactory));
        var loadHistoryDetail = historyDetailLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<long, InspectionHistoryDetailResult>("historyDetailLoader")
            : CreateHistoryDetailLoader(contextFactory));
        var loadHistoryRevisions = historyRevisionLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<long, long, InspectionItemRevisionHistoryResult>("historyRevisionLoader")
            : CreateHistoryRevisionLoader(contextFactory));
        var editHistory = historyEdit ?? (hasInjectedReadDependency
            ? FailClosedLoader<InspectionHistoryEditRequest, InspectionHistoryEditResult>("historyEdit")
            : CreateHistoryEditLoader(contextFactory));
        var loadDetail = detailLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<long, InspectionTaskDetailResult>("detailLoader")
            : CreateDetailLoader(contextFactory));
        var saveDraftAction = saveDraft ?? (hasInjectedReadDependency
            ? FailClosedLoader<SaveDraftRequest, SaveDraftResult>("saveDraft")
            : CreateSaveDraftLoader(contextFactory));
        var reconfirmItemAction = reconfirmItem ?? (hasInjectedReadDependency
            ? FailClosedLoader<ReconfirmItemRequest, ReconfirmItemResult>("reconfirmItem")
            : CreateReconfirmItemLoader(contextFactory));
        var clearDraftAction = clearDraft ?? (hasInjectedReadDependency
            ? FailClosedLoader<ClearDraftRequest, ClearDraftResult>("clearDraft")
            : CreateClearDraftLoader(contextFactory));
        var adjustInventoryAction = adjustInventory ?? (hasInjectedReadDependency
            ? FailClosedLoader<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult>("adjustInventory")
            : CreateAdjustInventoryLoader(contextFactory));
        var submitInspection = submit ?? (hasInjectedReadDependency
            ? FailClosedLoader<InspectionSubmissionRequest, InspectionSubmissionResult>("submit")
            : CreateSubmitInspectionLoader(contextFactory));
        var exportToday = hasInjectedReadDependency
            ? FailClosedLoader<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult>("todayExport")
            : CreateTodayExportLoader(contextFactory);
        var previewToday = hasInjectedReadDependency
            ? FailClosedLoader<string, InspectionPlanPreview>("todayPreview")
            : CreateTodayPreviewLoader(contextFactory);
        var applyToday = hasInjectedReadDependency
            ? FailClosedLoader<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult>("todayApply")
            : CreateTodayApplyLoader(contextFactory);
        var submitToday = hasInjectedReadDependency
            ? FailClosedLoader<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult>("todaySubmit")
            : CreateTodaySubmitLoader(contextFactory);
        var loadBackups = backupLoader ?? (hasInjectedReadDependency
            ? FailClosedLoader<IReadOnlyList<LocalDatabaseBackupListItem>>("backupLoader")
            : LoadBackups);
        var createBackup = backupCreator ?? (hasInjectedReadDependency
            ? FailClosedLoader<LocalDatabaseBackupResult>("backupCreator")
            : CreateBackup);
        var restoreBackup = backupRestorer ?? (hasInjectedReadDependency
            ? FailClosedLoader<string, DatabaseRestoreResult>("backupRestorer")
            : RestoreBackup);
        Dashboard = new DashboardViewModel(
            loadDashboard,
            logger,
            utcNow,
            () => NavigateTo(ShellPage.PendingTasks),
            searchTasks);
        PendingTasks = new PendingTasksViewModel(searchTasks, logger, loadCategories);
        History = new InspectionHistoryViewModel(
            loadHistory,
            loadHistoryDetail,
            loadHistoryRevisions,
            logger,
            editHistory,
            confirmHistoryEdit,
            utcNow);
        Import = new ImportViewModel(
            parsePreview: importParser ?? (hasInjectedReadDependency
                ? FailClosedLoader<string, ImportPreviewLoadResult>("importParser")
                : null),
            confirmPreview: importPreviewConfirmation ?? (hasInjectedReadDependency
                ? FailClosedLoader<ImportPreviewIdentity, ImportConfirmationResult>("importPreviewConfirmation")
                : null),
            executeImport: importExecutor ?? (hasInjectedReadDependency
                ? FailClosedLoader<ImportConfirmationContract, DateTime, ConfirmedImportResult>("importExecutor")
                : null),
            refreshDashboard: Dashboard.LoadAsync,
            refreshPendingTasks: PendingTasks.LoadAsync,
            logException: logException ?? LogImportException,
            utcNow: utcNow);
        TodayInspection = new TodayInspectionViewModel(
            loadTasks: () => searchTasks(new InspectionTaskSearchRequest(PageSize: int.MaxValue)),
            export: exportToday,
            preview: previewToday,
            apply: applyToday,
            submit: submitToday,
            refreshAfterSubmit: RefreshAfterTodayInspectionSubmitAsync,
            confirmOverStock: confirmTodayOverStock,
            confirmExpiredInventory: confirmTodayExpiredInventory,
            confirmSubmission: confirmTodaySubmission,
            logException: logger,
            businessToday: () => DateOnly.FromDateTime(DateTime.Today));
        BackupRestore = new DatabaseBackupRestoreViewModel(
            loadBackups: loadBackups,
            createBackup: createBackup,
            restore: restoreBackup,
            confirmRestore: confirmRestore,
            logException: logger);
        Detail = new InspectionDetailViewModel(
            loadDetail: loadDetail,
            saveDraft: saveDraftAction,
            reconfirmItem: reconfirmItemAction,
            clearDraft: clearDraftAction,
            adjustInventory: adjustInventoryAction,
            refreshDashboard: Dashboard.LoadAsync,
            refreshPendingTasks: PendingTasks.LoadAsync,
            logException: logger,
            utcNow: utcNow,
            confirmClearDraft: confirmClearDraft,
            confirmZeroInventory: confirmZeroInventory,
            goBack: ReturnFromDetailAsync,
            submit: submitInspection);
        NavigateHomeCommand = new RelayCommand(_ => NavigateTo(ShellPage.Dashboard), _ => CanNavigate);
        NavigateTasksCommand = new RelayCommand(_ => NavigateTo(ShellPage.PendingTasks), _ => CanNavigate);
        SearchTasksCommand = new RelayCommand(_ => { _ = SearchDashboardAsync(); }, _ => CanNavigate);
        ClearDashboardSearchCommand = new RelayCommand(_ => { _ = ClearDashboardSearchAsync(); }, _ => CanNavigate);
        NavigateHistoryCommand = new RelayCommand(_ => NavigateTo(ShellPage.History), _ => CanNavigate);
        NavigateImportCommand = new RelayCommand(_ => NavigateTo(ShellPage.Import), _ => CanNavigate);
        NavigateTodayInspectionCommand = new RelayCommand(_ => NavigateTo(ShellPage.TodayInspection), _ => CanNavigate);
        NavigateBackupRestoreCommand = new RelayCommand(_ => NavigateTo(ShellPage.BackupRestore), _ => CanNavigate);
        NavigateSettingsCommand = new RelayCommand(_ => { }, _ => false);
        OpenDetailCommand = new RelayCommand(parameter =>
        {
            if (parameter is InspectionTaskListItem item)
            {
                OpenDetail(item.TaskId);
            }
        }, _ => CanNavigate);
        History.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(History.IsEditBusy))
            {
                NotifyNavigationState();
            }
        };
        BackupRestore.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DatabaseBackupRestoreViewModel.IsBusy)
                or nameof(DatabaseBackupRestoreViewModel.IsLocked)
                or nameof(DatabaseBackupRestoreViewModel.IsRestartRequired)
                or nameof(DatabaseBackupRestoreViewModel.IsCriticalFailure))
            {
                NotifyDatabaseProtectionState();
            }
        };
        Import.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ImportViewModel.IsLoading))
            {
                NotifyNavigationState();
            }
        };
        TodayInspection.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TodayInspectionViewModel.IsActionBusy)) NotifyNavigationState();
        };
        Detail.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InspectionDetailViewModel.IsActionBusy))
            {
                NotifyNavigationState();
            }
        };
        _ = Dashboard.LoadAsync();
        _ = PendingTasks.LoadAsync();
    }

    public DashboardViewModel Dashboard { get; }

    public PendingTasksViewModel PendingTasks { get; }

    public InspectionHistoryViewModel History { get; }

    public ImportViewModel Import { get; }

    public TodayInspectionViewModel TodayInspection { get; }

    public DatabaseBackupRestoreViewModel BackupRestore { get; }

    public InspectionDetailViewModel Detail { get; }

    public RelayCommand NavigateHomeCommand { get; }

    public RelayCommand NavigateTasksCommand { get; }

    public RelayCommand SearchTasksCommand { get; }

    public RelayCommand ClearDashboardSearchCommand { get; }

    public RelayCommand NavigateHistoryCommand { get; }

    public RelayCommand NavigateImportCommand { get; }

    public RelayCommand NavigateTodayInspectionCommand { get; }

    public RelayCommand NavigateBackupRestoreCommand { get; }

    public RelayCommand NavigateSettingsCommand { get; }

    public RelayCommand OpenDetailCommand { get; }

    public ShellPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
            {
                return;
            }

            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDashboardVisible));
            OnPropertyChanged(nameof(IsPendingTasksVisible));
            OnPropertyChanged(nameof(IsHistoryVisible));
            OnPropertyChanged(nameof(IsImportVisible));
            OnPropertyChanged(nameof(IsTodayInspectionVisible));
            OnPropertyChanged(nameof(IsBackupRestoreVisible));
            OnPropertyChanged(nameof(IsInspectionDetailVisible));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public bool IsDashboardVisible => CurrentPage == ShellPage.Dashboard;

    public bool IsPendingTasksVisible => CurrentPage == ShellPage.PendingTasks;

    public bool IsHistoryVisible => CurrentPage == ShellPage.History;

    public bool IsImportVisible => CurrentPage == ShellPage.Import;

    public bool IsTodayInspectionVisible => CurrentPage == ShellPage.TodayInspection;

    public bool IsBackupRestoreVisible => CurrentPage == ShellPage.BackupRestore;

    public bool IsInspectionDetailVisible => CurrentPage == ShellPage.InspectionDetail;

    public string PageTitle => CurrentPage switch
    {
        ShellPage.Dashboard => "效期排查",
        ShellPage.PendingTasks => "待排查任务",
        ShellPage.History => "排查历史",
        ShellPage.Import => "数据导入",
        ShellPage.TodayInspection => "今日排查",
        ShellPage.BackupRestore => "数据备份与恢复",
        ShellPage.InspectionDetail => "排查详情",
        _ => "效期排查"
    };

    public string PageSubtitle => CurrentPage switch
    {
        ShellPage.Dashboard => "今日需要处理的效期任务与数据状态",
        ShellPage.PendingTasks => "查看当前需要完成效期排查的商品",
        ShellPage.History => "查看已完成的正式排查记录及修改留痕",
        ShellPage.Import => "导入最新的商品效期 Excel，更新商品与批次数据",
        ShellPage.TodayInspection => "导出今日计划、回导结果并集中提交已完成排查",
        ShellPage.BackupRestore => "创建经过验证的本地备份，或从应用备份安全恢复",
        ShellPage.InspectionDetail => "检查信息自动保存，提交前请确认数量",
        _ => "查看当前数据状态"
    };

    public void NavigateTo(ShellPage page) => _ = NavigateToAsync(page);

    public async Task NavigateToAsync(ShellPage page)
    {
        if (History.IsEditBusy ||
            Import.IsLoading ||
            TodayInspection.IsActionBusy ||
            Detail.IsActionBusy ||
            (BackupRestore.IsLocked && page != ShellPage.BackupRestore) ||
            (BackupRestore.IsBusy && page != ShellPage.BackupRestore))
        {
            return;
        }

        if (CurrentPage == ShellPage.InspectionDetail && page != ShellPage.InspectionDetail)
        {
            if (Detail.IsActionBusy)
            {
                return;
            }

            Detail.CancelPendingSubmissionConfirmation();
            await NavigateAwayFromDetailAsync(page);
            if (page == ShellPage.History && CurrentPage == ShellPage.History)
            {
                _ = History.LoadAsync();
            }
            if (page == ShellPage.BackupRestore && CurrentPage == ShellPage.BackupRestore)
            {
                _ = BackupRestore.LoadAsync();
            }

            return;
        }

        var enteredHistory = page == ShellPage.History && CurrentPage != ShellPage.History;
        var enteredTodayInspection = page == ShellPage.TodayInspection && CurrentPage != ShellPage.TodayInspection;
        var enteredBackupRestore = page == ShellPage.BackupRestore && CurrentPage != ShellPage.BackupRestore;
        CurrentPage = page;
        if (enteredHistory)
        {
            _ = History.LoadAsync();
        }
        if (enteredTodayInspection && !TodayInspection.HasLoadedTasks)
        {
            _ = TodayInspection.LoadAsync();
        }
        if (enteredBackupRestore)
        {
            _ = BackupRestore.LoadAsync();
        }
    }

    public void OpenDetail(long taskId)
    {
        if (taskId <= 0 || CurrentPage == ShellPage.InspectionDetail || !CanNavigate)
        {
            return;
        }

        _detailReturnPage = CurrentPage == ShellPage.PendingTasks
            ? ShellPage.PendingTasks
            : ShellPage.Dashboard;
        CurrentPage = ShellPage.InspectionDetail;
        _ = Detail.LoadAsync(taskId);
    }

    private async Task ReturnFromDetailAsync() =>
        await NavigateAwayFromDetailAsync(_detailReturnPage);

    private async Task SearchDashboardAsync() =>
        await Dashboard.SearchAsync(PendingTasks.SearchText);

    private async Task ClearDashboardSearchAsync()
    {
        PendingTasks.SearchText = string.Empty;
        await Dashboard.LoadAsync();
    }

    private async Task NavigateAwayFromDetailAsync(ShellPage page)
    {
        if (IsDatabaseProtectionBlocking)
        {
            return;
        }

        if (!await Detail.WaitForStableSaveAsync())
        {
            return;
        }

        if (IsDatabaseProtectionBlocking)
        {
            return;
        }

        CurrentPage = page;
    }

    private static Func<InspectionDashboardResult> CreateDashboardLoader(Func<StoreDbContext> contextFactory) => () =>
    {
        using var context = contextFactory();
        return new InspectionTaskQuery().Dashboard(context);
    };

    private static Func<InspectionTaskSearchRequest, InspectionTaskSearchResult> CreateTaskLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionTaskQuery().SearchOpenTasks(context, request);
    };

    private static Func<IReadOnlyList<string>> CreateCategoryLoader(Func<StoreDbContext> contextFactory) => () =>
    {
        using var context = contextFactory();
        return new InspectionTaskQuery().GetOpenTaskCategoryNames(context);
    };

    private static Func<T> FailClosedLoader<T>(string dependency) =>
        () => throw new InvalidOperationException($"{dependency} must be injected with the Shell read dependencies.");

    private static Func<TRequest, TResult> FailClosedLoader<TRequest, TResult>(string dependency) =>
        _ => throw new InvalidOperationException($"{dependency} must be injected with the Shell read dependencies.");

    private static Func<TFirst, TSecond, TResult> FailClosedLoader<TFirst, TSecond, TResult>(string dependency) =>
        (_, _) => throw new InvalidOperationException($"{dependency} must be injected with the Shell read dependencies.");

    private static Func<IReadOnlyList<InspectionHistoryListItem>> CreateHistoryLoader(Func<StoreDbContext> contextFactory) => () =>
    {
        using var context = contextFactory();
        return new InspectionHistoryQuery().List(context);
    };

    private static Func<long, InspectionHistoryDetailResult> CreateHistoryDetailLoader(Func<StoreDbContext> contextFactory) => inspectionId =>
    {
        using var context = contextFactory();
        return new InspectionHistoryQuery().GetDetail(context, inspectionId);
    };

    private static Func<long, long, InspectionItemRevisionHistoryResult> CreateHistoryRevisionLoader(Func<StoreDbContext> contextFactory) => (inspectionId, inspectionItemId) =>
    {
        using var context = contextFactory();
        return new InspectionHistoryQuery().GetItemRevisions(context, inspectionId, inspectionItemId);
    };

    private static Func<InspectionHistoryEditRequest, InspectionHistoryEditResult> CreateHistoryEditLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionHistoryEditUseCase().Execute(context, request);
    };

    private static Func<long, InspectionTaskDetailResult> CreateDetailLoader(Func<StoreDbContext> contextFactory) => taskId =>
    {
        using var context = contextFactory();
        return new InspectionTaskQuery().GetDetail(context, taskId);
    };

    private static Func<SaveDraftRequest, SaveDraftResult> CreateSaveDraftLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionDraftUseCase().SaveDraft(context, request);
    };

    private static Func<ReconfirmItemRequest, ReconfirmItemResult> CreateReconfirmItemLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionDraftUseCase().ReconfirmItem(context, request);
    };

    private static Func<ClearDraftRequest, ClearDraftResult> CreateClearDraftLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionDraftUseCase().ClearDraft(context, request);
    };

    private static Func<ManualInventoryAdjustmentRequest, ManualInventoryAdjustmentResult> CreateAdjustInventoryLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new ManualInventoryAdjustmentUseCase().Execute(context, request);
    };

    private static Func<InspectionSubmissionRequest, InspectionSubmissionResult> CreateSubmitInspectionLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionSubmissionUseCase().Submit(context, request);
    };

    private static Func<string, IReadOnlyCollection<long>, TodayInspectionPlanExportResult> CreateTodayExportLoader(Func<StoreDbContext> contextFactory) => (path, taskIds) =>
    {
        using var context = contextFactory();
        return new TodayInspectionPlanExportUseCase().Execute(context, new(path, taskIds));
    };

    private static Func<string, InspectionPlanPreview> CreateTodayPreviewLoader(Func<StoreDbContext> contextFactory) => path =>
    {
        using var context = contextFactory();
        return new InspectionPlanDraftApplyUseCase().Preview(context, path);
    };

    private static Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> CreateTodayApplyLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new InspectionPlanDraftApplyUseCase().Apply(context, request);
    };

    private static Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> CreateTodaySubmitLoader(Func<StoreDbContext> contextFactory) => request =>
    {
        using var context = contextFactory();
        return new BulkInspectionSubmissionUseCase().Submit(context, request);
    };

    private async Task RefreshAfterTodayInspectionSubmitAsync(IReadOnlyCollection<long> taskIds)
    {
        var refreshes = new List<Task> { Dashboard.LoadAsync(), PendingTasks.LoadAsync(), History.LoadAsync() };
        if (Detail.TaskId is long taskId && taskIds.Contains(taskId)) refreshes.Add(Detail.LoadAsync(taskId));
        await Task.WhenAll(refreshes);
        if (Dashboard.HasError || PendingTasks.HasError || History.HasError || (Detail.TaskId is long id && taskIds.Contains(id) && Detail.HasError))
        {
            throw new InvalidOperationException("One or more post-submission views did not refresh.");
        }
    }

    private static IReadOnlyList<LocalDatabaseBackupListItem> LoadBackups() =>
        new LocalDatabaseBackupQuery().List();

    private static LocalDatabaseBackupResult CreateBackup() =>
        new LocalDatabaseBackupUseCase().Create();

    private static DatabaseRestoreResult RestoreBackup(string backupPath) =>
        new DatabaseRestoreUseCase().Restore(backupPath, databaseRuntimeStopped: true);

    public bool IsDatabaseProtectionBusy => BackupRestore.IsBusy;

    public bool IsDatabaseProtectionLocked => BackupRestore.IsLocked;

    public bool IsDatabaseProtectionBlocking => IsDatabaseProtectionBusy || IsDatabaseProtectionLocked;

    public bool CanNavigate => !History.IsEditBusy
        && !Import.IsLoading
        && !TodayInspection.IsActionBusy
        && !Detail.IsActionBusy
        && !IsDatabaseProtectionBlocking;

    public bool CanOpenSettings => CanNavigate;

    public void ConfigureDatabaseProtectionRuntime(
        Func<Task<bool>> enterMaintenance,
        Action<bool> leaveMaintenance,
        Action requestExit) => BackupRestore.ConfigureRuntime(
            enterMaintenance,
            leaveMaintenance,
            requestExit);

    private void NotifyDatabaseProtectionState()
    {
        OnPropertyChanged(nameof(IsDatabaseProtectionBusy));
        OnPropertyChanged(nameof(IsDatabaseProtectionLocked));
        OnPropertyChanged(nameof(IsDatabaseProtectionBlocking));
        NotifyNavigationState();
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(CanOpenSettings));
        NavigateHomeCommand.RaiseCanExecuteChanged();
        NavigateTasksCommand.RaiseCanExecuteChanged();
        SearchTasksCommand.RaiseCanExecuteChanged();
        ClearDashboardSearchCommand.RaiseCanExecuteChanged();
        NavigateHistoryCommand.RaiseCanExecuteChanged();
        NavigateImportCommand.RaiseCanExecuteChanged();
        NavigateTodayInspectionCommand.RaiseCanExecuteChanged();
        NavigateBackupRestoreCommand.RaiseCanExecuteChanged();
        OpenDetailCommand.RaiseCanExecuteChanged();
    }

    private void LogException(Exception exception) => _logger.TryWrite(
        "error",
        "ui_query_failed",
        "WPF 页面查询失败。",
        exception.ToString());

    private void LogImportException(Exception exception) => _logger.TryWrite(
        "error",
        "ui_import_failed",
        "WPF 数据导入页面失败。",
        exception.ToString());
}
