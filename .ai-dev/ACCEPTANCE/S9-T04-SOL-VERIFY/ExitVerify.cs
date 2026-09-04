using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StoreExpiryInspector;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.UI;

internal static partial class Program
{
    sealed class ExitHarnessApp:App{protected override void OnStartup(StartupEventArgs e){}}
    static async Task VerifyExitParent()
    {
        var nonce=Guid.NewGuid().ToString("N");
        var start=new ProcessStartInfo(Environment.ProcessPath!){WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--exit-child");start.ArgumentList.Add(nonce);
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
        try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));}
        catch(TimeoutException){child.Kill(true);await child.WaitForExitAsync();Record("actual_process_exit",false,"Owned child timed out");return;}
        var marker=Path.Combine(Root,"exit-child-"+nonce+".json");
        if(!File.Exists(marker)){Record("actual_process_exit",false,new{child.ExitCode,stdout=await stdout,stderr=await stderr});return;}
        using var json=JsonDocument.Parse(File.ReadAllText(marker));var cache=json.RootElement.GetProperty("cache").GetString()!;
        var files=Directory.Exists(cache)?Directory.GetFileSystemEntries(cache,"*",SearchOption.AllDirectories):[];
        Record("actual_process_exit",child.ExitCode==0&&files.Length==0&&json.RootElement.GetProperty("workerCompletedAtExit").GetBoolean()&&json.RootElement.GetProperty("cancellationDelayObserved").GetBoolean(),new{child.ExitCode,cache,remaining=files,details=json.RootElement.Clone(),stdout=await stdout,stderr=await stderr});
    }
    static async Task VerifyExitChild(string nonce)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async()=>{
                try{
                    RuntimeDataRoot.Configure(["--data-root",Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())]);
                    var app=new ExitHarnessApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                    var source=System.Xml.Linq.XDocument.Load(@"D:\wendang\ChatGPT\门店效期排查软件\src\StoreExpiryInspector\App.xaml");System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns+"ResourceDictionary",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),source.Root!.Element(ns+"Application.Resources")!.Elements()).ToString());
                    using var rsa=RSA.Create(2048);var zip=File.ReadAllBytes(Path.Combine(Root,"fixtures","valid.zip"));var raw=JsonSerializer.SerializeToUtf8Bytes(Manifest(zip));var signature=rsa.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
                    var release=JsonSerializer.SerializeToUtf8Bytes(new{id=314159,tag_name="v1.0.0",draft=false,prerelease=false,assets=Names.Select(n=>new{name=n,state="uploaded",browser_download_url=$"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.0/{n}"})});
                    await using var server=new LocalServer(path=>path.Contains("/repos/")?new Reply(200,release):path.Contains("update-manifest.json")?new Reply(200,raw):path.Contains("update-manifest.sig")?new Reply(200,signature):new Reply(200,zip,Delay:30));
                    using var transport=new ExitTransport(server.Port);var cache=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());var downloader=new SignedUpdatePackageDownloader(transport,new(rsa.ExportParameters(false),CacheRoot:cache));
                    var shell=new ShellViewModel(dashboardLoader:()=>new InspectionDashboardResult(0,0,0,0,0,[]),taskLoader:r=>new InspectionTaskSearchResult([],0,r.Page,r.PageSize),categoryLoader:()=>[],logException:_=>{});
                    var window=(MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,[typeof(ShellViewModel),typeof(SignedUpdatePackageDownloader)],null)!.Invoke([shell,downloader]);window.Left=-15000;window.Top=-15000;window.ShowActivated=false;window.ShowInTaskbar=false;app.MainWindow=window;window.Show();await shell.StartupLoadTask;
                    var result=new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable,Current,Target,"isolated exit",new(Target,314159,"v1.0.0",Names));typeof(MainWindow).GetMethod("ShowUpdateAvailable",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[result]);await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    var prompt=window.OwnedWindows.Cast<Window>().Single();var button=Descendants<Button>(prompt).Single(b=>b.Content is TextBlock t&&t.Text=="立即更新");if(button.Command!=null)button.Command.Execute(null);else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var deadline=DateTime.UtcNow.AddSeconds(10);while(DateTime.UtcNow<deadline&&(!Directory.Exists(cache)||!Directory.EnumerateFiles(cache,"package.download",SearchOption.AllDirectories).Any(p=>new FileInfo(p).Length>0)))await Task.Delay(10);
                    if(!Directory.Exists(cache)||!Directory.EnumerateFiles(cache,"package.download",SearchOption.AllDirectories).Any())throw new Exception("No in-flight partial package: "+string.Join(" | ",Descendants<TextBlock>(prompt).Select(t=>t.Text)));
                    app.Exit+=(_,_)=>{File.WriteAllText(Path.Combine(Root,"exit-child-"+nonce+".json"),JsonSerializer.Serialize(new{cache,cancellationDelayObserved=transport.CancellationDelayed,mainWindowPresentAtExit=app.MainWindow!=null,workerCompletedAtExit=((Task?)typeof(MainWindow).GetField("_updateWorker",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window))?.IsCompleted,remainingAtExit=Directory.Exists(cache)?Directory.GetFileSystemEntries(cache,"*",SearchOption.AllDirectories).Length:0,databaseUsed=false,invoked="App.ExitApplication and real App.OnExit"}));done.TrySetResult();};
                    typeof(App).GetMethod("ExitApplication",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,[]);
                }catch(Exception e){Console.Error.WriteLine(e);done.TrySetException(e);dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));Dispatcher.Run();
        }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    sealed class ExitTransport(int port):HttpMessageHandler
    {
        readonly HttpClient inner=new(new RouteHandler(port)){Timeout=Timeout.InfiniteTimeSpan};public bool CancellationDelayed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){using var cloned=new HttpRequestMessage(request.Method,request.RequestUri);foreach(var header in request.Headers)cloned.Headers.TryAddWithoutValidation(header.Key,header.Value);var response=await inner.SendAsync(cloned,HttpCompletionOption.ResponseHeadersRead,token);if(request.RequestUri!.AbsolutePath.EndsWith(Package)){var stream=await response.Content.ReadAsStreamAsync(token);response.Content=new StreamContent(new DelayedCancellationStream(stream,()=>CancellationDelayed=true));}return response;}
        protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
    }
    sealed class DelayedCancellationStream(Stream inner,Action observed):Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){try{return await inner.ReadAsync(buffer,token);}catch(OperationCanceledException){observed();await Task.Delay(2000);throw;}}
        public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
    }
}
