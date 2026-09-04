using StoreExpiryInspector;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;

internal static partial class Program
{
    static async Task VerifyUi()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async ()=>{
                try {
                    var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
                    RuntimeDataRoot.Configure(["--data-root",root]);
                    var app=new App {ShutdownMode=ShutdownMode.OnExplicitShutdown};
                    System.Windows.Application.LoadComponent(app,new Uri("/StoreExpiryInspector;component/App.xaml",UriKind.Relative));
                    var shell=new ShellViewModel(dashboardLoader:()=>new InspectionDashboardResult(0,0,0,0,0,[]),taskLoader:r=>new InspectionTaskSearchResult([],0,r.Page,r.PageSize),categoryLoader:()=>[],logException:_=>{});
                    var ctor=typeof(MainWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(ShellViewModel)],null)!;
                    var window=(MainWindow)ctor.Invoke([shell]);
                    window.WindowStartupLocation=WindowStartupLocation.Manual;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-15000;window.Top=-15000;app.MainWindow=window;window.Show();
                    var show=typeof(MainWindow).GetMethod("ShowUpdateAvailable",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    void Show(UpdateCheckResult r)=>show.Invoke(window,[r]);
                    await shell.StartupLoadTask.WaitAsync(TimeSpan.FromSeconds(5));await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Record("wpf_initial_core_success",window.IsLoaded&&!shell.Dashboard.IsLoading&&!shell.PendingTasks.IsLoading&&!shell.Dashboard.HasError&&!shell.PendingTasks.HasError,new {root,databaseUsed=false});
                    foreach(var outcome in Enum.GetValues<UpdateCheckOutcome>().Where(x=>x!=UpdateCheckOutcome.UpdateAvailable)) {
                        Show(new(outcome,Current));
                        await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        var popups=window.OwnedWindows.Cast<Window>().ToArray();Record("wpf_quiet_"+outcome,popups.Length==0);
                        foreach(var popup in popups)popup.Close();
                    }
                    var network=new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var received=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int requests=0;
                    using var runtime=new UpdateCheckRuntime(_=>{requests++;return network.Task;},r=>{Show(r);received.SetResult();});
                    runtime.StartAfter(shell.StartupLoadTask);runtime.StartAfter(shell.StartupLoadTask);
                    Record("wpf_core_does_not_wait_for_network",window.IsLoaded&&!shell.Dashboard.HasError&&!shell.PendingTasks.HasError&&!network.Task.IsCompleted&&requests==1);
                    var available=new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable,Current,new(1,0,1),"修复更新检查提示。\n<script>这段外部说明只作为文本显示</script>\n"+new string('示',900));
                    network.SetResult(available);await received.Task.WaitAsync(TimeSpan.FromSeconds(3));await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    var dialog=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="发现新版本");
                    var text=string.Join("\n",Descendants<TextBlock>(dialog).Select(x=>x.Text));
                    Record("wpf_update_text",text.Contains("发现新版本")&&text.Contains("当前版本：v1.0.0")&&text.Contains("最新版本：v1.0.1")&&text.Contains("<script>"));
                    SaveVisual((FrameworkElement)dialog.Content,"update-prompt.png");
                    var buttons=Descendants<Button>(dialog).ToArray();
                    var later=buttons.Single(b=>Equals(b.Content,"稍后提醒"));var update=buttons.Single(b=>(Equals(b.Content,"立即更新") || b.Content is TextBlock label && label.Text=="立即更新"));
                    Record("wpf_keyboard_buttons",later.Focusable&&update.Focusable&&later.IsDefault);
                    var primaryText=Descendants<TextBlock>(update).First(t=>t.Text=="立即更新");Record("wpf_primary_white_text",primaryText.Foreground is SolidColorBrush brush&&brush.Color==Colors.White);Show(available);Record("wpf_visible_duplicate_suppressed",window.OwnedWindows.Count==1);
                    var unavailableSeen=false;
                    _ = dispatcher.BeginInvoke(new Action(()=>{
                        var info=app.Windows.Cast<Window>().SingleOrDefault(w=>w.Title=="暂未启用更新");
                        if(info!=null){unavailableSeen=string.Join("\n",Descendants<TextBlock>(info).Select(x=>x.Text)).Contains("尚未启用");SaveVisual((FrameworkElement)info.Content,"update-not-enabled.png");info.Close();}
                    }),DispatcherPriority.ApplicationIdle);
                    update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Record("wpf_update_requested_explicit_not_enabled",unavailableSeen&&window.IsLoaded);
                    later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Show(available);await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Record("wpf_later_suppresses_repeat",window.OwnedWindows.Count==0&&requests==1);
                    runtime.Dispose();window.Close();Show(available);await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Record("wpf_closed_no_late_prompt",!app.Windows.Cast<Window>().Any(w=>w.Title=="发现新版本"));
                    app.Shutdown();done.TrySetResult();
                }catch(Exception e){Record("wpf_execution",false,e.ToString());done.TrySetResult();}
                finally {dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));
            Dispatcher.Run();
        }){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        try{await done.Task.WaitAsync(TimeSpan.FromSeconds(35));}catch(Exception e){Record("wpf_timeout",false,e.Message);}
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {
        if(root is T match)yield return match;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Descendants<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
    }
    static void SaveVisual(FrameworkElement element,string path) {
        element.UpdateLayout();var width=(int)Math.Ceiling(element.ActualWidth)+48;var height=(int)Math.Ceiling(element.ActualHeight)+48;var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(element),null,new Rect(24,24,element.ActualWidth,element.ActualHeight));}var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
}