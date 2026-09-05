using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using StoreExpiryInspector;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        if (!Guid.TryParse(Path.GetRelativePath(Path.GetTempPath(), root), out _)) throw new InvalidOperationException("TEMP/GUID only");
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        RuntimeDataRoot.Configure(["--data-root", data]);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = XDocument.Load(Environment.GetEnvironmentVariable("S9_T06_SOURCE_APP_XAML") ?? throw new InvalidOperationException("Set source App.xaml path"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(p + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), xml.Root!.Element(p + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            var events = new List<object>();
            MainWindow? main = null;
            try
            {
                var shell = new ShellViewModel(dashboardLoader: () => new InspectionDashboardResult(0,0,0,0,0,[]), taskLoader: q => new InspectionTaskSearchResult([],0,q.Page,q.PageSize), categoryLoader: () => [], logException: _ => { });
                var ctor = typeof(MainWindow).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(ShellViewModel)], null)!;
                main = (MainWindow)ctor.Invoke([shell]); // Original constructor creates its own production handler.
                app.MainWindow = main;
                main.ShowActivated = false; main.ShowInTaskbar = false; main.Left = -15000; main.Top = -15000;
                main.Show();
                await shell.StartupLoadTask.WaitAsync(TimeSpan.FromSeconds(15));
                var result = await new GitHubReleaseUpdateChecker().CheckAsync(new Version(1,0,0), CancellationToken.None); // Separate original handler.
                if (result.Outcome != UpdateCheckOutcome.UpdateAvailable || result.LatestVersion != new Version(1,0,1)) throw new InvalidOperationException("Expected real latest101");
                typeof(MainWindow).GetMethod("ShowUpdateAvailable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [result]);
                await app.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
                var dialog = main.OwnedWindows.Cast<Window>().Single(w => w.Title == "发现新版本");
                var update = Children<Button>(dialog).Single(b => b.Content is TextBlock t && t.Text == "立即更新");
                update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var worker = (Task<UpdatePackageResult>?)typeof(MainWindow).GetField("_updateWorker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
                if (worker is null) throw new InvalidOperationException("Real button did not start original worker");
                var prepared = await worker.WaitAsync(TimeSpan.FromMinutes(12));
                await Task.Delay(250);
                var text = string.Join("\n", Children<TextBlock>(dialog).Select(t => t.Text));
                var source = (CancellationTokenSource)typeof(MainWindow).GetField("_updatePackageCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                var beforeDismissCancelled = source.IsCancellationRequested;
                Children<Button>(dialog).Single(b => Equals(b.Content, "稍后提醒")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                var afterDismissCancelled = source.IsCancellationRequested;
                main.Hide();
                var afterHideCancelled = source.IsCancellationRequested;
                events.Add(new { name="frozen100-real-gui-separate-handlers", passed=prepared.Outcome==UpdatePackageOutcome.Verified, outcome=prepared.Outcome.ToString(), text,
                    actualButton=true, originalMainWindowPrepareAsync=true, originalDefaultDownloaderConstructor=true, separateCheckerAndDownloaderHandlers=true,
                    noTransportWrapper=true, noTimeoutOverride=true, noCacheRootOverride=true, beforeDismissCancelled, afterDismissCancelled, afterHideCancelled,
                    host="Sol isolated WPF test host with synthetic shell loaders; not installed App lifecycle", dataRootSynthetic=true,
                    databaseCreated=File.Exists(Path.Combine(data,"data","app.db")), updaterConfigured=false, updaterStarted=false });
                main.Close();
                events.Add(new { name="main-closed-cancels", passed=source.IsCancellationRequested });
            }
            catch(Exception ex) { events.Add(new { name="execution-error", type=ex.GetType().FullName, hresult=ex.HResult }); Environment.ExitCode=1; }
            finally
            {
                File.WriteAllText(Path.Combine(root,"gui-original-result.json"),JsonSerializer.Serialize(events,new JsonSerializerOptions{WriteIndented=true}));
                main?.Close(); app.Shutdown();
            }
        }));
        app.Run();
    }
    static IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject
    {
        if(node is T item) yield return item;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) foreach(var child in Children<T>(VisualTreeHelper.GetChild(node,i))) yield return child;
    }
}
