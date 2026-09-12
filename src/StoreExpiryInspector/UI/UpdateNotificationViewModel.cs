using System.Windows.Input;
using StoreExpiryInspector.Application.Updates;

namespace StoreExpiryInspector.UI;

public sealed class UpdateNotificationViewModel : ViewModelBase
{
    private readonly Action? _dialogClosed;
    private readonly RelayCommand _update;
    private readonly RelayCommand _cancel;
    private readonly bool _isManualDownload;
    private bool _isBusy;
    private bool _isDownloading;
    private bool _isUpdating;
    private bool _isInstalling;
    private int _downloadPercent;
    private bool _hasFailure;
    private bool _isDomesticFallback;
    private Action? _manualDownload;

    public UpdateNotificationViewModel(UpdateCheckResult result, Action dismiss, Action requestUpdate, Action? dialogClosed = null)
    {
        CurrentVersionText = $"当前版本：v{result.CurrentVersion.ToString(3)}";
        LatestVersionText = $"最新版本：v{result.LatestVersion?.ToString(3)}";
        ReleaseNotes = result.ReleaseNotes;
        _isManualDownload = result.ManualDownloadUrl is not null;
        _dialogClosed = dialogClosed;
        DismissCommand = new RelayCommand(_ => dismiss());
        _update = new RelayCommand(_ => requestUpdate(), _ => !IsBusy);
        _cancel = new RelayCommand(_ => CancelRequested?.Invoke(), _ => CanCancel);
        ManualDownloadCommand = new RelayCommand(_ => _manualDownload?.Invoke(), _ => IsDomesticFallback);
        UpdateRequestedCommand = _update;
        CancelCommand = _cancel;
    }

    public string CurrentVersionText { get; }
    public string LatestVersionText { get; }
    public string? ReleaseNotes { get; }
    public bool IsManualDownload => _isManualDownload;
    public string PrimaryActionText => IsManualDownload ? "下载最新版" : "立即更新";
    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);
    public ICommand DismissCommand { get; }
    public ICommand UpdateRequestedCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ManualDownloadCommand { get; }
    public event Action? CancelRequested;
    public void DialogClosed() => _dialogClosed?.Invoke();
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInitial)); _update.RaiseCanExecuteChanged(); } }
    public bool IsInitial => !IsBusy && !HasFailure && !IsDomesticFallback;
    public bool IsDomesticFallback { get => _isDomesticFallback; private set { _isDomesticFallback = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInitial)); OnPropertyChanged(nameof(StatusText)); ((RelayCommand)ManualDownloadCommand).RaiseCanExecuteChanged(); } }
    public bool IsDownloading { get => _isDownloading; private set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCancel)); OnPropertyChanged(nameof(StatusText)); _cancel.RaiseCanExecuteChanged(); } }
    public bool IsUpdating { get => _isUpdating; private set { _isUpdating = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsProgressIndeterminate)); } }
    public bool IsInstalling { get => _isInstalling; private set { _isInstalling = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public bool CanCancel => IsDownloading;
    public bool IsProgressIndeterminate => IsUpdating;
    public int DownloadPercent { get => _downloadPercent; private set { _downloadPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public bool HasFailure { get => _hasFailure; private set { _hasFailure = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInitial)); OnPropertyChanged(nameof(StatusText)); } }
    public string StatusText => IsDownloading ? $"下载中 {DownloadPercent}%" : IsUpdating ? "更新中…" : IsInstalling ? "安装中…" : IsDomesticFallback ? "当前网络访问更新服务器较慢或连接失败。\n可以重试在线更新，或通过网盘下载最新版。" : HasFailure ? "更新失败，请稍后重试。" : string.Empty;

    public void Begin() { HasFailure = false; IsDomesticFallback = false; _manualDownload = null; IsBusy = true; BeginUpdating(); }
    public void ShowDomesticFallback(Action manualDownload) { _manualDownload = manualDownload; IsDownloading = false; IsUpdating = false; IsInstalling = false; IsBusy = false; IsDomesticFallback = true; }
    public void Report(UpdatePackageProgress progress)
    {
        if (progress.Stage == "正在下载更新包" && progress.TotalBytes > 0)
        {
            IsDownloading = true; IsUpdating = false; IsInstalling = false;
            DownloadPercent = (int)Math.Clamp(progress.BytesReceived * 100 / progress.TotalBytes, 0, 100);
        }
        else BeginUpdating();
    }
    public void BeginUpdating() { IsDownloading = false; IsUpdating = true; IsInstalling = false; }
    public void BeginInstalling() { IsDownloading = false; IsUpdating = false; IsInstalling = true; }
    public void Complete(UpdatePackageResult result) { HasFailure = result.Outcome is not UpdatePackageOutcome.Verified and not UpdatePackageOutcome.Cancelled; IsDownloading = false; IsUpdating = false; IsInstalling = false; IsBusy = false; }
}
