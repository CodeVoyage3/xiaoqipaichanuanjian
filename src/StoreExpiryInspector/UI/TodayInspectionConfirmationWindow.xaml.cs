using System.Windows;

namespace StoreExpiryInspector.UI;

public partial class TodayInspectionConfirmationWindow : Window
{
    private TodayInspectionViewModel? _viewModel;

    public TodayInspectionConfirmationWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.SubmissionBlocked -= ShowSubmissionBlocked;
            (DataContext as TodayInspectionViewModel)?.CancelPreview();
        };
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.SubmissionBlocked -= ShowSubmissionBlocked;
            _viewModel = DataContext as TodayInspectionViewModel;
            if (_viewModel is not null) _viewModel.SubmissionBlocked += ShowSubmissionBlocked;
        };
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TodayInspectionViewModel viewModel) return;
        await viewModel.SubmitAsync();
        if (!viewModel.HasPreview) Close();
    }

    private void ShowSubmissionBlocked(string reason) => WpfDialogService.Show(
        this, "暂时无法提交", reason, "知道了", WpfDialogKind.Warning,
        "请按提示补全或重新导出最新计划后再提交。", false);
}
