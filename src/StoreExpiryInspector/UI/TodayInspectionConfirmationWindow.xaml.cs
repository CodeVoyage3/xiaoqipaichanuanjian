using System.Windows;

namespace StoreExpiryInspector.UI;

public partial class TodayInspectionConfirmationWindow : Window
{
    public TodayInspectionConfirmationWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as TodayInspectionViewModel)?.CancelPreview();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TodayInspectionViewModel viewModel) return;
        await viewModel.SubmitAsync();
        if (!viewModel.HasPreview) Close();
    }
}
