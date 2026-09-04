using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using StoreExpiryInspector.Application.Updates;
using StoreExpiryInspector.UI;

internal static partial class Program
{
    static readonly List<object> Records = new();
    static int failures;
    static readonly Version ProductAssemblyVersion = typeof(GitHubReleaseUpdateChecker).Assembly.GetName().Version!;
    static readonly Version Current = new(ProductAssemblyVersion.Major,ProductAssemblyVersion.Minor,ProductAssemblyVersion.Build);
    static string Body(string tag="v1.0.1", string? notes=null) => JsonSerializer.Serialize(new { tag_name=tag, draft=false, prerelease=false, body=notes });
    static void Record(string name, bool pass, object? details=null) { Records.Add(new {name,pass,details}); if(!pass) failures++; Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}"); }
    static async Task Probe(string name, Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> response, UpdateCheckOutcome expected, Version? current=null, CancellationToken ct=default, TimeSpan? timeout=null)
    {
        try {
            using var handler=new Handler(response);
            var checker=new GitHubReleaseUpdateChecker(handler,timeout);
            var result=await checker.CheckAsync(current??Current,ct);
            Record(name,result.Outcome==expected,new {expected=expected.ToString(),actual=result.Outcome.ToString(),notesLength=result.ReleaseNotes?.Length});
        } catch(Exception e) { Record(name,false,new {exception=e.GetType().FullName,message=e.Message}); }
    }
    static Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply(int status,string body) => (req,ct)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status){RequestMessage=req,Content=new StringContent(body,Encoding.UTF8,"application/json")});
    static async Task<int> Main(string[] args)
    {
        if(args.Contains("--ui")) { await VerifyUi(); } else if(args.Contains("--real")) {
            var at=DateTimeOffset.Now;
            var checker=new GitHubReleaseUpdateChecker();
            var result=await checker.CheckAsync(Current,CancellationToken.None);
            Record("production_client_real_github",result.Outcome==UpdateCheckOutcome.NoPublishedRelease,new {time=at,outcome=result.Outcome.ToString(),current=Current.ToString(),anonymous=true,authorization=false});
        } else {
            await Probe("new",Reply(200,Body()),UpdateCheckOutcome.UpdateAvailable);
            await Probe("same",Reply(200,Body("v1.0.0")),UpdateCheckOutcome.UpToDate);
            await Probe("old",Reply(200,Body("v0.9.9")),UpdateCheckOutcome.RemoteOlder);
            await Probe("patch_numeric",Reply(200,Body("v1.0.10")),UpdateCheckOutcome.UpdateAvailable,new(1,0,9));
            await Probe("minor_numeric",Reply(200,Body("v1.10.0")),UpdateCheckOutcome.UpdateAvailable,new(1,9,9));
            foreach(var (status,expected) in new[]{(404,UpdateCheckOutcome.NoPublishedRelease),(403,UpdateCheckOutcome.RateLimited),(429,UpdateCheckOutcome.RateLimited),(500,UpdateCheckOutcome.NetworkUnavailable),(503,UpdateCheckOutcome.NetworkUnavailable)}) await Probe("http_"+status,Reply(status,"{}"),expected);
            foreach(var tag in new[]{"1.0.1","v01.0.1","v1.0.1-alpha","v1.0.1+build","v1.0.1\n","v1.0.1 ","v1.0.1.0","v2147483648.0.0"}) await Probe("tag_"+JsonSerializer.Serialize(tag),Reply(200,Body(tag)),UpdateCheckOutcome.InvalidRemoteMetadata);
            foreach(var invalid in new[]{"{","[]","null","{}","{\"tag_name\":4,\"draft\":false,\"prerelease\":false}","{\"tag_name\":\"v1.0.1\",\"draft\":false}","{\"tag_name\":\"v1.0.1\",\"draft\":\"false\",\"prerelease\":false}","{\"tag_name\":\"v1.0.1\",\"draft\":true,\"prerelease\":false}","{\"tag_name\":\"v1.0.1\",\"draft\":false,\"prerelease\":true}"}) await Probe("metadata_"+invalid,Reply(200,invalid),UpdateCheckOutcome.InvalidRemoteMetadata);
            await Probe("dns",(_,_)=>throw new HttpRequestException(HttpRequestError.NameResolutionError,"synthetic DNS"),UpdateCheckOutcome.NetworkUnavailable);
            await Probe("tls",(_,_)=>throw new HttpRequestException(HttpRequestError.SecureConnectionError,"synthetic TLS"),UpdateCheckOutcome.NetworkUnavailable);
            await Probe("operation_cancel_timeout",(_,_)=>throw new OperationCanceledException("synthetic timeout"),UpdateCheckOutcome.NetworkUnavailable);
            await Probe("body_io_failure",(req,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=req,Content=new StreamContent(new FaultStream())}),UpdateCheckOutcome.NetworkUnavailable);
            await Probe("body_total_timeout",(req,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=req,Content=new StreamContent(new SlowStream())}),UpdateCheckOutcome.NetworkUnavailable,timeout:TimeSpan.FromMilliseconds(50));
            using(var cts=new CancellationTokenSource()) {cts.Cancel(); await Probe("cancelled",Reply(200,Body()),UpdateCheckOutcome.Cancelled,ct:cts.Token);}
            await Probe("bounded_unknown_length",(req,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=req,Content=new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes(Body(notes:new string('x',300000)))))}),UpdateCheckOutcome.InvalidRemoteMetadata);
            var requestChecked=false;
            using(var handler=new Handler((req,ct)=>{requestChecked=req.RequestUri?.AbsoluteUri=="https://api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/latest" && req.Headers.Authorization is null && req.Headers.UserAgent.Count>0;return Reply(200,Body(notes:"<script>alert(1)</script>\u0001"+new string('字',1500)))(req,ct);})) {
                var r=await new GitHubReleaseUpdateChecker(handler).CheckAsync(Current,CancellationToken.None);
                Record("fixed_anonymous_metadata",requestChecked);
                Record("notes_bounded_text",r.Outcome==UpdateCheckOutcome.UpdateAvailable && r.ReleaseNotes?.Length<=1000 && !r.ReleaseNotes.Contains('\u0001'),new {length=r.ReleaseNotes?.Length});
                int dismissed=0,requested=0;
                var vm=new UpdateNotificationViewModel(r,()=>dismissed++,()=>requested++);
                vm.DismissCommand.Execute(null);vm.UpdateRequestedCommand.Execute(null);
                Record("vm_versions_and_commands",vm.CurrentVersionText.Contains("v1.0.0")&&vm.LatestVersionText.Contains("v1.0.1")&&dismissed==1&&requested==1);
            }
            var calls=0;var completed=0;var released=new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime=new UpdateCheckRuntime(ct=>{Interlocked.Increment(ref calls);ct.Register(()=>cancelled.TrySetResult());return released.Task;},_=>Interlocked.Increment(ref completed));
            await Task.WhenAll(Enumerable.Range(0,32).Select(_=>Task.Run(runtime.Start)));
            Record("single_flight_nonblocking",calls==1&&!released.Task.IsCompleted,new {calls});
            runtime.Dispose();await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));released.SetResult(new(UpdateCheckOutcome.UpdateAvailable,Current,new(1,0,1)));await Task.Delay(100);
            Record("cancel_late_no_callback",completed==0,new {completed});
            try{runtime.Dispose();Record("dispose_idempotent",true);}catch(Exception e){Record("dispose_idempotent",false,e.GetType().Name);}
        }
        await File.WriteAllTextAsync(args.Contains("--ui")?"ui-independent.json":args.Contains("--real")?"real-client.json":"protocol-independent.json",JsonSerializer.Serialize(new {failures,records=Records},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>response(request,token);}
    class FaultStream:Stream {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new IOException("synthetic body interrupted");
        public override ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default)=>ValueTask.FromException<int>(new IOException("synthetic body interrupted"));
        public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
    sealed class SlowStream:FaultStream {public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default){await Task.Delay(Timeout.Infinite,ct);return 0;}}
    sealed class UnseekableStream(byte[] bytes):FaultStream {int offset;public override int Read(byte[] b,int o,int c){var n=Math.Min(c,bytes.Length-offset);bytes.AsSpan(offset,n).CopyTo(b.AsSpan(o));offset+=n;return n;}public override ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken ct=default){var n=Math.Min(b.Length,bytes.Length-offset);bytes.AsSpan(offset,n).CopyTo(b.Span);offset+=n;return ValueTask.FromResult(n);}}
}