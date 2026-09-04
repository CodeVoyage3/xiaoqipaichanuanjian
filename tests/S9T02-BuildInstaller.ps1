param(
    [Parameter(Mandatory = $true)][string]$Compiler,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [switch]$TestMode
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'OutputRoot must not already exist.' }
$outputId = [guid]::Empty
if (-not [guid]::TryParse((Split-Path -Leaf $output), [ref]$outputId)) { throw 'OutputRoot must be a GUID directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$publish = Join-Path $output 'publish'
dotnet publish (Join-Path $root 'src\StoreExpiryInspector\StoreExpiryInspector.csproj') -c Release --no-restore -p:PublishProfile=WinX64 -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "publish failed: $LASTEXITCODE" }
$arguments = @("/DPayloadDir=$publish", "/DOutputDir=$output")
if ($TestMode) {
    $id = [guid]::NewGuid().ToString()
    $install = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
    $data = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
    $arguments += @('/DTestMode', '/DTestVersion=1.0.0', "/DTestAppIdKey=$id", "/DTestSuffix=$id", "/DTestInstallRoot=$install", "/DTestDataRoot=$data", "/DTestMutexName=$(Split-Path -Leaf $data)")
}
& $Compiler @arguments (Join-Path $root 'installer\StoreExpiryInspector.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
$installer = Get-ChildItem -LiteralPath $output -Filter '*Setup*.exe' | Select-Object -First 1
if ($TestMode) {
    [pscustomobject]@{ Installer = $installer.FullName; InstallRoot = $install; DataRoot = $data; AppId = $id; RunValueName = "StoreExpiryInspector-S9T02-$id"; ShortcutName = "StoreExpiryInspector S9-T02 $id" } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 's9-t02-test-identity.json') -Encoding utf8
}
$installer
