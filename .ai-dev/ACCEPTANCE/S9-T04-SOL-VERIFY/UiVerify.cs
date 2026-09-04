using StoreExpiryInspector;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;
using StoreExpiryInspector.Application.Updates;

internal static partial class Program
{
    static async Task VerifyUi()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async ()=>{
                try{
                    var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
                    RuntimeDataRoot.Configure(["--data-root",root]);
                    var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                    var resourceSource=System.Xml.Linq.XDocument.Load(@"D:\wendang\ChatGPT\门店效期排查软件\src\StoreExpiryInspector\App.xaml");
                    System.Xml.Linq.XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    var dictionary=new System.Xml.Linq.XElement(presentation+"ResourceDictionary",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),resourceSource.Root!.Element(presentation+"Application.Resources")!.Elements());
                    app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
                    async Task<MainWindow> Window(SignedUpdatePackageDownloader downloader){
                        var shell=new ShellViewModel(dashboardLoader:()=>new InspectionDashboardResult(0,0,0,0,0,[]),taskLoader:r=>new InspectionTaskSearchResult([],0,r.Page,r.PageSize),categoryLoader:()=>[],logException:_=>{});
                        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(ShellViewModel),typeof(SignedUpdatePackageDownloader)],null)!;
                        var window=(MainWindow)ctor.Invoke([shell,downloader]);
                        window.WindowStartupLocation=WindowStartupLocation.Manual;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-15000;window.Top=-15000;app.MainWindow=window;window.Show();
                        await shell.StartupLoadTask.WaitAsync(TimeSpan.FromSeconds(5));await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        Record("wpf_core_ready",window.IsLoaded&&!shell.Dashboard.HasError&&!shell.PendingTasks.HasError,new{root,databaseUsed=false});return window;
                    }
                    async Task<Window> Prompt(MainWindow window){
                        var result=new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable,Current,Target,"合成发布说明，仅用于下载验收。",new CheckedRelease(Target,314159,"v1.0.0",Names));
                        typeof(MainWindow).GetMethod("ShowUpdateAvailable",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[result]);
                        await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        return window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="发现新版本");
                    }
                    static string Text(Window w)=>string.Join("\n",Descendants<TextBlock>(w).Select(t=>t.Text));
                    static Button Update(Window w)=>Descendants<Button>(w).Single(b=>Equals(b.Content,"立即更新")||b.Content is TextBlock t&&t.Text=="立即更新");
                    static void Press(Button button){if(button.Command is not null)button.Command.Execute(null);else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    async Task WaitUntil(Func<bool> condition){var stop=DateTime.UtcNow.AddSeconds(12);while(!condition()&&DateTime.UtcNow<stop){await Task.Delay(25);}if(!condition())throw new TimeoutException("WPF expected state missing");}
                    using var rsa=RSA.Create(2048);
                    var zip=File.ReadAllBytes(Path.Combine(Root,"fixtures","valid.zip"));var raw=JsonSerializer.SerializeToUtf8Bytes(Manifest(zip));var sig=rsa.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
                    var release=JsonSerializer.SerializeToUtf8Bytes(new{id=314159,tag_name="v1.0.0",draft=false,prerelease=false,assets=Names.Select((n,i)=>new{id=i+1,name=n,state="uploaded",size=n==Package?zip.Length:n.EndsWith(".sig")?sig.Length:raw.Length,browser_download_url=$"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.0/{n}"})});
                    await using var server=new LocalServer(path=>path.Contains("/repos/")?new Reply(200,release):path.Contains("update-manifest.json")?new Reply(200,raw):path.Contains("update-manifest.sig")?new Reply(200,sig):new Reply(200,zip,Delay:4));
                    using var transport=new RouteHandler(server.Port);
                    var cache=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
                    var window=await Window(new(transport,new(rsa.ExportParameters(false),CacheRoot:cache)));
                    var prompt=await Prompt(window);var update=Update(prompt);SaveVisual((FrameworkElement)prompt.Content,"s9t04-ready.png");
                    Record("wpf_primary_keyboard",update.Focusable&&Descendants<TextBlock>(update).Any(t=>t.Text=="立即更新"&&t.Foreground is SolidColorBrush b&&b.Color==Colors.White));
                    Press(update);
                    await WaitUntil(()=>Text(prompt).Contains("下载")||Text(prompt).Contains("校验"));
                    await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Record("wpf_busy_disabled",!update.IsEnabled,new{text=Text(prompt)});SaveVisual((FrameworkElement)prompt.Content,"s9t04-downloading.png");
                    update.Command?.Execute(null);Press(update);
                    await WaitUntil(()=>Text(prompt).Contains("安装更新功能"));
                    SaveVisual((FrameworkElement)prompt.Content,"s9t04-verified.png");
                    Record("wpf_verified_stops_without_exit",window.IsLoaded&&Text(prompt).Contains("准备完成")&&!Text(prompt).Contains("已经更新完成"),new{text=Text(prompt)});
                    Record("wpf_same_version_one_download",transport.Requests.Count(r=>r.Url.EndsWith(Package))==1,new{requests=transport.Requests.Count});
                    Record("wpf_progress_bytes",Descendants<ProgressBar>(prompt).Any()||Text(prompt).Contains("%")||Text(prompt).Contains("字节"),new{text=Text(prompt)});
                    prompt.Close();window.Close();
                    var production=await Window(new());var blocked=await Prompt(production);Press(Update(blocked));
                    await WaitUntil(()=>Text(blocked).Contains("配置"));
                    Record("wpf_empty_anchor_explicit",production.IsLoaded&&!Text(blocked).Contains("校验成功"),new{text=Text(blocked)});SaveVisual((FrameworkElement)blocked.Content,"s9t04-no-anchor.png");blocked.Close();production.Close();
                    await using var slowServer=new LocalServer(path=>path.Contains("/repos/")?new Reply(200,release):path.Contains("update-manifest.json")?new Reply(200,raw):path.Contains("update-manifest.sig")?new Reply(200,sig):new Reply(200,zip,Delay:25));
                    using var slowTransport=new RouteHandler(slowServer.Port);var cancelCache=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
                    var closing=await Window(new(slowTransport,new(rsa.ExportParameters(false),CacheRoot:cancelCache)));var closingPrompt=await Prompt(closing);Press(Update(closingPrompt));
                    await WaitUntil(()=>slowTransport.Requests.Any(r=>r.Url.EndsWith(Package)));
                    for(int attempt=1;attempt<=2;attempt++){
                        await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        var cancelButton=Descendants<Button>(closingPrompt).Single(b=>Equals(b.Content,"取消准备"));
                        Press(cancelButton);
                        await WaitUntil(()=>Text(closingPrompt).Contains("已取消")&&Update(closingPrompt).IsEnabled);
                        await WaitUntil(()=>!Directory.Exists(cancelCache)||!Directory.EnumerateFileSystemEntries(cancelCache).Any());
                        Record("wpf_cancel_retry_"+attempt,closing.IsLoaded,new{text=Text(closingPrompt),attempt});
                        Press(Update(closingPrompt));
                        var expectedRequests=attempt+1;
                        await WaitUntil(()=>slowTransport.Requests.Count(r=>r.Url.EndsWith(Package))>=expectedRequests);
                    }
                    closing.Close();await WaitUntil(()=>!Directory.Exists(cancelCache)||!Directory.EnumerateFileSystemEntries(cancelCache).Any());await Task.Delay(150);
                    Record("wpf_close_cancels_cleans_no_late_ui",!app.Windows.Cast<Window>().Any(),new{cancelCache,databaseUsed=false,remaining=app.Windows.Cast<Window>().Select(w=>w.Title).ToArray()});
                    app.Shutdown();done.TrySetResult();
                }catch(Exception e){Record("wpf_execution",false,e.ToString());done.TrySetResult();}
                finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));Dispatcher.Run();
        }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();
        try{await done.Task.WaitAsync(TimeSpan.FromSeconds(55));}catch(Exception e){Record("wpf_timeout",false,e.Message);}
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{if(root is T match)yield return match;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void SaveVisual(FrameworkElement element,string path){element.UpdateLayout();var width=(int)Math.Ceiling(element.ActualWidth)+48;var height=(int)Math.Ceiling(element.ActualHeight)+48;var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(element),null,new Rect(24,24,element.ActualWidth,element.ActualHeight));}var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
}
