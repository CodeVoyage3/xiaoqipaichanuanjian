using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace StoreExpiryInspector.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ShellColumn.Width = new(e.NewSize.Width < 1280 ? 176 : 208);

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.NavigateTo(ShellPage.PendingTasks);
        }

        Dispatcher.BeginInvoke(() =>
        {
            TaskSearchBox.Focus();
            TaskSearchBox.SelectAll();
        }, DispatcherPriority.Input);
    }
}
