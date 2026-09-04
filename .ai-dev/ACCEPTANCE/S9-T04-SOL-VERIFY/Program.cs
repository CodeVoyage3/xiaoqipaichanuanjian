using System.IO;

using System.Net;

using System.Net.Http;

using System.Net.Sockets;

using System.Security.Cryptography;

using System.Text;

using System.Text.Json;

using System.Text.Json.Nodes;

using System.Collections.Concurrent;

using StoreExpiryInspector.Application.Updates;



internal static partial class Program

{

    static readonly List<object> Records=[];

    static int Failed;

    static readonly string Root=Directory.GetCurrentDirectory();

    static readonly Version Target=new(1,0,0), Current=new(0,9,9);

    const string Package="StoreExpiryInspector-1.0.0-win-x64.zip";

    static readonly string[] Names=["update-manifest.json","update-manifest.sig",Package];

    static readonly string[] Migrations=["20260826123739_InitialCreate","20260826130822_AddTasksAndDrafts","20260826135612_AddInspectionHistory","20260826142429_AddInventoryAdjustments","20260826152131_AddImportPersistence","20260826155455_AddBackupMetadata","20260826162033_AddSettingsAndAppState","20260826170403_AddLifecycleEvents","20260901155124_AddPolicyAndBaselineFoundation"];

    static void Record(string name,bool pass,object? details=null){Records.Add(new{name,pass,details});if(!pass)Failed++;Console.WriteLine($"{name}: {(pass?"PASS":"FAIL")}");}

    static JsonObject Manifest(byte[] zip)=>JsonSerializer.SerializeToNode(new{schemaVersion=1,version="1.0.0",releaseTag="v1.0.0",repository="CodeVoyage3/xiaoqipaichanuanjian",channel="stable",rid="win-x64",minimumProtocolVersion=1,package=new{fileName=Package,bytes=zip.Length,sha256=Convert.ToHexString(SHA256.HashData(zip))},targetMigrations=Migrations,source=new{minVersion="0.0.0",maxVersion="1.0.0",minMigration=Migrations[0],maxMigration=Migrations[^1]}})!.AsObject();

    sealed class Scenario

    {

        public byte[] Zip=File.ReadAllBytes(Path.Combine(Root,"fixtures","valid.zip"));

        public Action<JsonObject>? Change;

        public Func<byte[],byte[]>? RawChange;

        public bool BadSignature, WrongKey, Pkcs1, NoKey, Concurrent, Cancel, UnknownLength, MalformedSignature, ResignRaw, InvalidKey, CancelAudit, DetectFirst, CancelInsideAudit;

        public int PackageStatus=200, ManifestStatus=200, SignatureStatus=200, Delay, Truncate, ManifestDelay, PackageHeaderDelay;

        public TimeSpan? Timeout;

        public string? Redirect;

        public string[] Assets=Names;

        public Version SourceVersion=Current;

        public Action<JsonObject>? ReleaseChange;

    }

    static async Task Probe(string name,Scenario scenario,params UpdatePackageOutcome[] expected)

    {

        var cache=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());

        try{

            using var rsa=RSA.Create(2048);using var wrong=RSA.Create(2048);

            var manifest=Manifest(scenario.Zip);scenario.Change?.Invoke(manifest);

            var raw=JsonSerializer.SerializeToUtf8Bytes(manifest);

            var signature=rsa.SignData(raw,HashAlgorithmName.SHA256,scenario.Pkcs1?RSASignaturePadding.Pkcs1:RSASignaturePadding.Pss);

            if(scenario.BadSignature)signature[0]^=1;

            if(scenario.MalformedSignature)signature=[1,2,3];

            if(scenario.RawChange!=null)raw=scenario.RawChange(raw);

            if(scenario.ResignRaw)signature=rsa.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);

            var release=JsonSerializer.SerializeToNode(new{id=314159,tag_name="v1.0.0",draft=false,prerelease=false,assets=scenario.Assets.Select((n,i)=>new{id=i+1,name=n,state="uploaded",size=n==Package?scenario.Zip.Length:n.EndsWith(".sig")?signature.Length:raw.Length,browser_download_url=$"https://github.com/CodeVoyage3/xiaoqipaichanuanjian/releases/download/v1.0.0/{n}",url=$"https://api.github.com/repos/CodeVoyage3/xiaoqipaichanuanjian/releases/assets/{i+1}"})})!.AsObject();

            scenario.ReleaseChange?.Invoke(release);

            await using var server=new LocalServer((path)=>{

                if(path.Contains("/repos/"))return new Reply(200,JsonSerializer.SerializeToUtf8Bytes(release));

                if(scenario.Redirect!=null&&!path.Contains("/github-production-release-asset/"))return new Reply(302,[],scenario.Redirect+"?asset="+Uri.EscapeDataString(path.Split('/')[^1]));

                if(path.Contains("update-manifest.json"))return new Reply(scenario.ManifestStatus,raw,Delay:scenario.ManifestDelay);

                if(path.Contains("update-manifest.sig"))return new Reply(scenario.SignatureStatus,signature);

                return new Reply(scenario.PackageStatus,scenario.Zip,null,scenario.UnknownLength,scenario.Delay,scenario.Truncate,scenario.PackageHeaderDelay);

            });

            using var transport=new RouteHandler(server.Port);

            var service=new SignedUpdatePackageDownloader(transport,new UpdatePackageOptions(scenario.NoKey?null:scenario.InvalidKey?new RSAParameters{Modulus=[1],Exponent=[0]}:(scenario.WrongKey?wrong:rsa).ExportParameters(false),scenario.Timeout??TimeSpan.FromSeconds(2),scenario.Timeout??TimeSpan.FromSeconds(5),cache));

            using var cancel=new CancellationTokenSource();if(scenario.Cancel)cancel.CancelAfter(80);

            var progress=new ConcurrentQueue<UpdatePackageProgress>();

            var checkedRelease=new CheckedRelease(Target,314159,"v1.0.0",scenario.Assets);

            if(scenario.DetectFirst){var detected=await new GitHubReleaseUpdateChecker(transport).CheckAsync(scenario.SourceVersion,cancel.Token);if(detected.Outcome!=UpdateCheckOutcome.UpdateAvailable||detected.Release is null)throw new Exception("Detection did not preserve release identity");checkedRelease=detected.Release;}

            void Report(UpdatePackageProgress value){progress.Enqueue(value);if(scenario.CancelAudit&&value.Stage.Contains("校验"))cancel.Cancel();if(scenario.CancelInsideAudit&&value.Stage.Contains("校验"))cancel.CancelAfter(40);}

            var first=service.PrepareAsync(checkedRelease,scenario.SourceVersion,Report,cancel.Token);

            Task<UpdatePackageResult>[] others=scenario.Concurrent?Enumerable.Range(0,12).Select(_=>service.PrepareAsync(checkedRelease,scenario.SourceVersion,progress.Enqueue,cancel.Token)).ToArray():[];

            var result=await first.WaitAsync(TimeSpan.FromSeconds(15));if(others.Length>0)await Task.WhenAll(others).WaitAsync(TimeSpan.FromSeconds(15));

            var cacheClean=!Directory.Exists(cache)||!Directory.EnumerateFileSystemEntries(cache).Any();

            var allNoAuth=transport.Requests.All(r=>!r.Auth);

            var packageRequests=transport.Requests.Count(r=>r.Url.EndsWith(Package,StringComparison.Ordinal));

            var verifiedIdentity=result.Outcome!=UpdatePackageOutcome.Verified||result.Package!=null&&File.Exists(result.Package.PackagePath)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Package.PackagePath)))==Convert.ToHexString(SHA256.HashData(scenario.Zip));

            var pass=expected.Contains(result.Outcome)&&allNoAuth&&verifiedIdentity&&(result.Outcome==UpdatePackageOutcome.Verified||cacheClean)&&(!scenario.Concurrent||packageRequests==1)&&(scenario.NoKey?transport.Requests.Count==0:true);

            Record(name,pass,new{actual=result.Outcome.ToString(),expected=expected.Select(x=>x.ToString()),cache,cacheClean,allNoAuth,packageRequests,requestCount=transport.Requests.Count,verifiedIdentity,progressCount=progress.Count,result.Message,packagePath=result.Package?.PackagePath});

        }catch(Exception e){Record(name,false,new{exception=e.ToString(),cache});}

    }

    static async Task<int> Main(string[] args)

    {

        if(args.Contains("--exit-child")){await VerifyExitChild(args[^1]);return 0;}
        if(args.Contains("--exit-parent")){await VerifyExitParent();File.WriteAllText("exit-independent.json",JsonSerializer.Serialize(new{failed=Failed,records=Records},new JsonSerializerOptions{WriteIndented=true}));return Failed==0?0:1;}
        if(args.Contains("--real")){var version=typeof(GitHubReleaseUpdateChecker).Assembly.GetName().Version!;var current=new Version(version.Major,version.Minor,version.Build);var result=await new GitHubReleaseUpdateChecker().CheckAsync(current,CancellationToken.None);Record("real_anonymous_no_release",result.Outcome==UpdateCheckOutcome.NoPublishedRelease,new{at=DateTimeOffset.Now,current=current.ToString(3),outcome=result.Outcome.ToString(),releaseCreated=false});File.WriteAllText("real-client.json",JsonSerializer.Serialize(new{failed=Failed,records=Records},new JsonSerializerOptions{WriteIndented=true}));return Failed==0?0:1;}
        if(args.Contains("--ui")){await VerifyUi();File.WriteAllText("ui-independent.json",JsonSerializer.Serialize(new{failed=Failed,records=Records},new JsonSerializerOptions{WriteIndented=true}));return Failed==0?0:1;}

        await Probe("valid_http_stream",new(),UpdatePackageOutcome.Verified);

        await Probe("detect_to_verified_http",new(){DetectFirst=true},UpdatePackageOutcome.Verified);

        await Probe("full_self_contained_publish",new(){Zip=File.ReadAllBytes(Path.Combine(Root,"fixtures","full_publish.zip")),Timeout=TimeSpan.FromMinutes(2)},UpdatePackageOutcome.Verified);

        await Probe("unknown_content_length",new(){UnknownLength=true},UpdatePackageOutcome.Verified);

        await Probe("no_production_anchor",new(){NoKey=true},UpdatePackageOutcome.SigningNotConfigured);

        await Probe("invalid_anchor",new(){InvalidKey=true},UpdatePackageOutcome.SigningNotConfigured);

        await Probe("cancel_audit",new(){CancelAudit=true},UpdatePackageOutcome.Cancelled);

        await Probe("cancel_inside_large_audit",new(){CancelInsideAudit=true,Zip=File.ReadAllBytes(Path.Combine(Root,"fixtures","full_publish.zip")),Timeout=TimeSpan.FromMinutes(2)},UpdatePackageOutcome.Cancelled);

        await Probe("manifest_body_timeout",new(){ManifestDelay=500,Timeout=TimeSpan.FromMilliseconds(120)},UpdatePackageOutcome.NetworkUnavailable);

        await Probe("package_header_timeout",new(){PackageHeaderDelay=500,Timeout=TimeSpan.FromMilliseconds(120)},UpdatePackageOutcome.NetworkUnavailable);

        await Probe("release_metadata_cap",new(){ReleaseChange=m=>m["oversize"]=new string('x',1100000)},UpdatePackageOutcome.InvalidManifest,UpdatePackageOutcome.AssetTooLarge);

        await Probe("same_current_version",new(){SourceVersion=Target},UpdatePackageOutcome.VersionMismatch);

        await Probe("manifest_byte_changed",new(){RawChange=b=>b.Concat(new byte[]{32}).ToArray()},UpdatePackageOutcome.InvalidManifestSignature);

        await Probe("signature_byte_changed",new(){BadSignature=true},UpdatePackageOutcome.InvalidManifestSignature);

        await Probe("wrong_public_key",new(){WrongKey=true},UpdatePackageOutcome.InvalidManifestSignature);

        await Probe("wrong_padding",new(){Pkcs1=true},UpdatePackageOutcome.InvalidManifestSignature);

        await Probe("malformed_signature",new(){MalformedSignature=true},UpdatePackageOutcome.InvalidManifestSignature);

        await Probe("valid_cdn_redirect",new(){Redirect="https://release-assets.githubusercontent.com/github-production-release-asset/123/11111111-1111-1111-1111-111111111111"},UpdatePackageOutcome.Verified);

        await Probe("signature_missing",new(){Assets=[Names[0],Package]},UpdatePackageOutcome.SignatureMissing);

        await Probe("manifest_missing",new(){Assets=[Names[1],Package]},UpdatePackageOutcome.ManifestMissing);

        await Probe("hash_wrong",new(){Change=m=>m["package"]!["sha256"]=new string('0',64)},UpdatePackageOutcome.HashMismatch);

        await Probe("size_larger_than_actual",new(){Change=m=>m["package"]!["bytes"]=m["package"]!["bytes"]!.GetValue<int>()+1},UpdatePackageOutcome.SizeMismatch);

        await Probe("size_smaller_than_actual",new(){Change=m=>m["package"]!["bytes"]=m["package"]!["bytes"]!.GetValue<int>()-1},UpdatePackageOutcome.SizeMismatch);

        await Probe("package_cap",new(){Change=m=>m["package"]!["bytes"]=268435457L},UpdatePackageOutcome.AssetTooLarge);

        await Probe("arm64",new(){Change=m=>m["rid"]="win-arm64"},UpdatePackageOutcome.UnsupportedPlatform);

        await Probe("version_mismatch",new(){Change=m=>m["version"]="1.0.1"},UpdatePackageOutcome.VersionMismatch);

        await Probe("source_outside_range",new(){Change=m=>m["source"]!["minVersion"]="0.9.10"},UpdatePackageOutcome.SourceNotSupported);

        await Probe("unsupported_protocol",new(){Change=m=>m["minimumProtocolVersion"]=2},UpdatePackageOutcome.UnsupportedProtocol);

        await Probe("unsupported_schema",new(){Change=m=>m["schemaVersion"]=2},UpdatePackageOutcome.UnsupportedProtocol);

        await Probe("duplicate_json_property",new(){RawChange=b=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(b).Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"schemaVersion\":1"))},UpdatePackageOutcome.InvalidManifestSignature);

        await Probe("signed_array_manifest",new(){RawChange=_=>Encoding.UTF8.GetBytes("[]"),ResignRaw=true},UpdatePackageOutcome.InvalidManifest);

        await Probe("signed_string_schema",new(){Change=m=>m["schemaVersion"]="1"},UpdatePackageOutcome.InvalidManifest);

        await Probe("signed_null_schema",new(){Change=m=>m["schemaVersion"]=null},UpdatePackageOutcome.InvalidManifest);

        await Probe("signed_duplicate_json_property",new(){RawChange=b=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(b).Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"schemaVersion\":1")),ResignRaw=true},UpdatePackageOutcome.InvalidManifest);

        await Probe("migration_target_mismatch",new(){Change=m=>m["targetMigrations"]!.AsArray().Add("20260906000000_SyntheticFuture")},UpdatePackageOutcome.PackageVersionMismatch);

        await Probe("migration_target_reordered",new(){Change=m=>m["targetMigrations"]=JsonSerializer.SerializeToNode(Migrations.Reverse())},UpdatePackageOutcome.InvalidManifest);

        await Probe("migration_duplicate",new(){Change=m=>m["targetMigrations"]!.AsArray().Add(Migrations[^1])},UpdatePackageOutcome.InvalidManifest);

        await Probe("migration_invalid_id",new(){Change=m=>m["targetMigrations"]![0]="20260826123739_../escape"},UpdatePackageOutcome.InvalidManifest);

        await Probe("source_migration_future",new(){Change=m=>{m["source"]!["minMigration"]="20260906000000_Next";m["source"]!["maxMigration"]="20260907000000_Last";}},UpdatePackageOutcome.SourceNotSupported);

        await Probe("release_id_changed",new(){ReleaseChange=m=>m["id"]=271828},UpdatePackageOutcome.AssetMissing,UpdatePackageOutcome.VersionMismatch);

        await Probe("release_tag_changed",new(){ReleaseChange=m=>m["tag_name"]="v1.0.1"},UpdatePackageOutcome.AssetMissing,UpdatePackageOutcome.VersionMismatch);

        await Probe("release_draft",new(){ReleaseChange=m=>m["draft"]=true},UpdatePackageOutcome.AssetMissing,UpdatePackageOutcome.InvalidManifest);

        await Probe("release_prerelease",new(){ReleaseChange=m=>m["prerelease"]=true},UpdatePackageOutcome.AssetMissing,UpdatePackageOutcome.InvalidManifest);

        await Probe("asset_foreign_url",new(){ReleaseChange=m=>m["assets"]![0]!["browser_download_url"]="https://evil.test/update-manifest.json"},UpdatePackageOutcome.AssetMissing);

        await Probe("asset_not_uploaded",new(){ReleaseChange=m=>m["assets"]![0]!["state"]="open"},UpdatePackageOutcome.AssetMissing);

        await Probe("duplicate_manifest_asset",new(){Assets=[Names[0],Names[0],Names[1],Package]},UpdatePackageOutcome.AssetMissing,UpdatePackageOutcome.ManifestMissing);

        await Probe("duplicate_package_asset",new(){Assets=[Names[0],Names[1],Package,Package]},UpdatePackageOutcome.AssetMissing);

        await Probe("manifest_large",new(){Change=m=>m["padding"]=new string('x',70000)},UpdatePackageOutcome.InvalidManifest,UpdatePackageOutcome.AssetTooLarge);

        await Probe("invalid_semver",new(){Change=m=>m["version"]="01.0.0"},UpdatePackageOutcome.InvalidManifest);

        await Probe("package_missing",new(){Assets=[Names[0],Names[1]]},UpdatePackageOutcome.AssetMissing);

        await Probe("package_404",new(){PackageStatus=404},UpdatePackageOutcome.AssetMissing);

        await Probe("package_403",new(){PackageStatus=403},UpdatePackageOutcome.RateLimited);

        await Probe("package_429",new(){PackageStatus=429},UpdatePackageOutcome.RateLimited);

        await Probe("package_503",new(){PackageStatus=503},UpdatePackageOutcome.NetworkUnavailable);

        await Probe("manifest_404",new(){ManifestStatus=404},UpdatePackageOutcome.ManifestMissing);

        await Probe("signature_404",new(){SignatureStatus=404},UpdatePackageOutcome.SignatureMissing);

        await Probe("manifest_429",new(){ManifestStatus=429},UpdatePackageOutcome.RateLimited);

        await Probe("interrupted_body",new(){Truncate=1024},UpdatePackageOutcome.NetworkUnavailable,UpdatePackageOutcome.IoFailure,UpdatePackageOutcome.SizeMismatch);

        await Probe("body_timeout",new(){Delay=20,Timeout=TimeSpan.FromMilliseconds(120)},UpdatePackageOutcome.NetworkUnavailable);

        await Probe("cancel_body",new(){Delay=20,Cancel=true},UpdatePackageOutcome.Cancelled);

        await Probe("same_version_singleflight",new(){Delay=2,Concurrent=true},UpdatePackageOutcome.Verified);

        foreach(var path in Directory.EnumerateFiles(Path.Combine(Root,"fixtures"),"*.zip").Where(p=>Path.GetFileNameWithoutExtension(p) is not ("valid" or "full_publish")))

            await Probe("zip_"+Path.GetFileNameWithoutExtension(path),new(){Zip=File.ReadAllBytes(path)},UpdatePackageOutcome.UnsafeArchive,UpdatePackageOutcome.PackageVersionMismatch);

        foreach(var url in new[]{"http://release-assets.githubusercontent.com/github-production-release-asset/1/11111111-1111-1111-1111-111111111111","https://localhost/a","file:///C:/test","https://release-assets.githubusercontent.com.evil.test/a","https://evil.test/a","https://release-assets.githubusercontent.com:444/github-production-release-asset/1/11111111-1111-1111-1111-111111111111","https://user@release-assets.githubusercontent.com/github-production-release-asset/1/11111111-1111-1111-1111-111111111111","https://release-assets.githubusercontent.com/wrong-path"})

            await Probe("redirect_"+url,new(){Redirect=url},UpdatePackageOutcome.NetworkUnavailable,UpdatePackageOutcome.InvalidManifest);

        File.WriteAllText("independent-results.json",JsonSerializer.Serialize(new{failed=Failed,records=Records},new JsonSerializerOptions{WriteIndented=true}));

        return Failed==0?0:1;

    }

    sealed record Reply(int Status,byte[] Body,string? Location=null,bool UnknownLength=false,int Delay=0,int Truncate=0,int HeaderDelay=0);

    sealed class RouteHandler(int port):HttpMessageHandler

    {

        readonly HttpClient client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};

        public readonly ConcurrentQueue<(string Url,bool Auth)> Requests=new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)

        {

            Requests.Enqueue((request.RequestUri!.AbsoluteUri,request.Headers.Authorization!=null));

            var local=new HttpRequestMessage(request.Method,$"http://127.0.0.1:{port}{request.RequestUri.PathAndQuery}");

            foreach(var h in request.Headers)local.Headers.TryAddWithoutValidation(h.Key,h.Value);

            return await client.SendAsync(local,HttpCompletionOption.ResponseHeadersRead,ct);

        }

        protected override void Dispose(bool disposing){if(disposing)client.Dispose();base.Dispose(disposing);}

    }

    sealed class LocalServer:IAsyncDisposable

    {

        readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource cancellation=new();readonly Func<string,Reply> route;readonly Task loop;

        public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;

        public LocalServer(Func<string,Reply> route){this.route=route;listener.Start();loop=Run();}

        async Task Run(){while(!cancellation.IsCancellationRequested){try{var c=await listener.AcceptTcpClientAsync(cancellation.Token);_ = Serve(c);}catch(OperationCanceledException){break;}catch(SocketException){break;}}}

        async Task Serve(TcpClient client)

        {

            using(client)try{await using var stream=client.GetStream();var data=new List<byte>();var single=new byte[1];while(data.Count<16384&&await stream.ReadAsync(single,cancellation.Token)>0){data.Add(single[0]);if(data.Count>=4&&data[^4]==13&&data[^3]==10&&data[^2]==13&&data[^1]==10)break;}var request=Encoding.ASCII.GetString(data.ToArray()).Split('\r')[0];var path=request.Split(' ')[1];var reply=route(path);var headers=$"HTTP/1.1 {reply.Status} Response\r\nConnection: close\r\n"+(reply.UnknownLength?"":$"Content-Length: {reply.Body.Length}\r\n")+(reply.Location==null?"":$"Location: {reply.Location}\r\n")+"\r\n";if(reply.HeaderDelay>0)await Task.Delay(reply.HeaderDelay,cancellation.Token);await stream.WriteAsync(Encoding.ASCII.GetBytes(headers),cancellation.Token);var limit=reply.Truncate>0?Math.Min(reply.Truncate,reply.Body.Length):reply.Body.Length;for(int pos=0;pos<limit;pos+=4096){if(reply.Delay>0)await Task.Delay(reply.Delay,cancellation.Token);await stream.WriteAsync(reply.Body.AsMemory(pos,Math.Min(4096,limit-pos)),cancellation.Token);}}catch(Exception e)when(e is IOException or OperationCanceledException or SocketException or ObjectDisposedException){}

        }

        public async ValueTask DisposeAsync(){cancellation.Cancel();listener.Stop();await loop;cancellation.Dispose();}

    }

}
