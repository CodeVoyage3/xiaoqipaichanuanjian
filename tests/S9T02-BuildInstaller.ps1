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
    $id = '{{' + [guid]::NewGuid().ToString() + '}'
    $install = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
    $data = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
    $arguments += @('/DTestMode', "/DTestAppId=$id", "/DTestInstallRoot=$install", "/DTestDataRoot=$data", "/DTestMutexName=$(Split-Path -Leaf $data)")
}
& $Compiler @arguments (Join-Path $root 'installer\StoreExpiryInspector.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
Get-ChildItem -LiteralPath $output -Filter '*Setup*.exe' | Select-Object -First 1
