using System.Windows.Input;
using StoreExpiryInspector.Application.Updates;

namespace StoreExpiryInspector.UI;

public sealed class UpdateNotificationViewModel
{
    public UpdateNotificationViewModel(UpdateCheckResult result, Action dismiss, Action requestUpdate)
    {
        CurrentVersionText = $"当前版本：v{result.CurrentVersion.ToString(3)}";
        LatestVersionText = $"最新版本：v{result.LatestVersion?.ToString(3)}";
        ReleaseNotes = result.ReleaseNotes;
        DismissCommand = new RelayCommand(_ => dismiss());
        UpdateRequestedCommand = new RelayCommand(_ => requestUpdate());
    }

    public string CurrentVersionText { get; }
    public string LatestVersionText { get; }
    public string? ReleaseNotes { get; }
    public ICommand DismissCommand { get; }
    public ICommand UpdateRequestedCommand { get; }
}
