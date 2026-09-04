using System.Windows.Input;
using StoreExpiryInspector.Application.Updates;

namespace StoreExpiryInspector.UI;

public sealed class UpdateNotificationViewModel : ViewModelBase
{
    private readonly RelayCommand _update;
    private readonly RelayCommand _cancel;
    private string _statusText = "准备更新包";
    private long _receivedBytes;
    private long _totalBytes;
    private bool _isBusy;
    public UpdateNotificationViewModel(UpdateCheckResult result, Action dismiss, Action requestUpdate)
    {
        CurrentVersionText = $"当前版本：v{result.CurrentVersion.ToString(3)}";
        LatestVersionText = $"最新版本：v{result.LatestVersion?.ToString(3)}";
        ReleaseNotes = result.ReleaseNotes;
        DismissCommand = new RelayCommand(_ => dismiss());
        _update = new RelayCommand(_ => requestUpdate(), _ => !IsBusy);
        _cancel = new RelayCommand(_ => CancelRequested?.Invoke(), _ => IsBusy);
        UpdateRequestedCommand = _update;
        CancelCommand = _cancel;
    }

    public string CurrentVersionText { get; }
    public string LatestVersionText { get; }
    public string? ReleaseNotes { get; }
    public ICommand DismissCommand { get; }
    public ICommand UpdateRequestedCommand { get; }
    public ICommand CancelCommand { get; }
    public event Action? CancelRequested;
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); } }
    public long ReceivedBytes { get => _receivedBytes; private set { _receivedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); } }
    public long TotalBytes { get => _totalBytes; private set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); _update.RaiseCanExecuteChanged(); _cancel.RaiseCanExecuteChanged(); } }
    public string ProgressText => TotalBytes > 0 ? $"{ReceivedBytes:N0} / {TotalBytes:N0} 字节（{ReceivedBytes * 100 / TotalBytes}%）" : string.Empty;
    public void Begin() { IsBusy = true; StatusText = "正在准备更新包"; }
    public void Report(UpdatePackageProgress progress) { StatusText = progress.Stage; ReceivedBytes = progress.BytesReceived; TotalBytes = progress.TotalBytes; }
    public void Complete(UpdatePackageResult result) { IsBusy = false; StatusText = result.Message; }
}
