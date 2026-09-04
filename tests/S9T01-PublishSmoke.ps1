param(
    [string]$OutputRoot = (Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString()))
)

$ErrorActionPreference = "Stop"
function Get-RelativePath([string]$basePath, [string]$path) {
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $prefix = ([System.IO.Path]::GetFullPath($basePath).TrimEnd($separator) + $separator)
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    return $fullPath.Substring($prefix.Length)
}
function Assert-OrdinaryAncestors([string]$path) {
    for ($current = [IO.DirectoryInfo]([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))); $null -ne $current; $current = $current.Parent) {
        if (-not $current.Exists -or (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw "OutputRoot ancestor must be an ordinary local directory." }
    }
}
$fullOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$relative = Get-RelativePath $tempRoot $fullOutputRoot
$parsedOutputId = [guid]::Empty
if ($null -eq $relative -or [System.IO.Path]::IsPathRooted($relative) -or -not [guid]::TryParse($relative, [ref]$parsedOutputId)) {
    throw "OutputRoot must be a GUID directory directly below TEMP."
}

Assert-OrdinaryAncestors $fullOutputRoot
if (Test-Path -LiteralPath $fullOutputRoot) { throw "OutputRoot must not already exist." }
New-Item -ItemType Directory -Path $fullOutputRoot | Out-Null
if ((Get-Item -LiteralPath $fullOutputRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw "OutputRoot must be an ordinary local directory."
}

function Get-TreeHash([string]$path) {
    @(
        Get-ChildItem -LiteralPath $path -Recurse -File | Sort-Object FullName | ForEach-Object {
            $file = $_
            [pscustomobject]@{ Path = Get-RelativePath $path $file.FullName; Sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash; Length = $file.Length }
        }
    ) | ConvertTo-Json -Compress
}
function Invoke-IsolatedSmoke([string]$executable, [string]$dataRoot) {
    $process = Start-Process -FilePath $executable -ArgumentList ('--data-root "{0}" --s9-t01-smoke-exit' -f $dataRoot) -WorkingDirectory $smokeWorking -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw "Published WPF smoke timed out." }
    if ($process.ExitCode -ne 0) { throw "Published WPF smoke exited with $($process.ExitCode)." }
}

$firstPublish = Join-Path $fullOutputRoot "publish-a"
$secondPublish = Join-Path $fullOutputRoot "publish-b"
$firstData = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
$secondData = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
$smokeWorking = Join-Path $fullOutputRoot "working"
if ((Get-RelativePath $fullOutputRoot $firstPublish) -ne "publish-a" -or (Get-RelativePath $fullOutputRoot $secondPublish) -ne "publish-b" -or (Test-Path -LiteralPath $secondPublish)) { throw "Publish move paths are outside this run's output root or already exist." }
New-Item -ItemType Directory -Path $smokeWorking | Out-Null
dotnet publish src/StoreExpiryInspector/StoreExpiryInspector.csproj -c Release -p:PublishProfile=WinX64 -o $firstPublish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
$firstExe = Join-Path $firstPublish "StoreExpiryInspector.exe"
if (-not (Test-Path -LiteralPath $firstExe)) { throw "Published WPF executable was not produced." }
foreach ($required in @("coreclr.dll", "hostfxr.dll", "e_sqlite3.dll", "StoreExpiryInspector.runtimeconfig.json")) {
    if (-not (Test-Path -LiteralPath (Join-Path $firstPublish $required))) { throw "Published payload is missing $required." }
}

$before = Get-TreeHash $firstPublish
Invoke-IsolatedSmoke $firstExe $firstData
$firstLog = Join-Path $firstData "logs"
if (-not (Test-Path -LiteralPath (Join-Path $firstData "data/app.db")) -or -not (Select-String -Path (Join-Path $firstLog "*.log") -SimpleMatch 's9_t01_smoke_ready' -Quiet)) { throw "First isolated WPF smoke did not prove Shell initialization." }
if ($before -ne (Get-TreeHash $firstPublish)) { throw "Published install directory changed during the smoke run." }

Move-Item -LiteralPath $firstPublish -Destination $secondPublish
if ((Test-Path -LiteralPath $firstPublish) -or -not (Test-Path -LiteralPath $secondPublish) -or $null -eq (Get-RelativePath $fullOutputRoot $secondPublish)) { throw "Publish directory was not moved inside this run's output root." }
$secondExe = Join-Path $secondPublish "StoreExpiryInspector.exe"
$secondBefore = Get-TreeHash $secondPublish
Invoke-IsolatedSmoke $secondExe $secondData
if (-not (Test-Path -LiteralPath (Join-Path $secondData "data/app.db")) -or -not (Select-String -Path (Join-Path $secondData "logs/*.log") -SimpleMatch 's9_t01_smoke_ready' -Quiet)) { throw "Moved publish directory smoke did not prove Shell initialization." }
if ($secondBefore -ne (Get-TreeHash $secondPublish)) { throw "Moved publish install directory changed during the smoke run." }

$runtime = Get-Content -Raw (Join-Path $secondPublish "StoreExpiryInspector.runtimeconfig.json") | ConvertFrom-Json
$evidence = [pscustomobject]@{
    Result = "PASS"; PublishDirectory = $secondPublish; FirstDataRoot = $firstData; SecondDataRoot = $secondData
    PublishBytes = (Get-ChildItem -LiteralPath $secondPublish -Recurse -File | Measure-Object Length -Sum).Sum
    FileVersion = (Get-Item -LiteralPath $secondExe).VersionInfo.FileVersion
    ProductVersion = (Get-Item -LiteralPath $secondExe).VersionInfo.ProductVersion
    AssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $secondPublish "StoreExpiryInspector.dll")).Version.ToString()
    Framework = $runtime.runtimeOptions.framework; IncludedFrameworks = $runtime.runtimeOptions.includedFrameworks
    RuntimeFiles = @("coreclr.dll", "hostfxr.dll", "e_sqlite3.dll")
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fullOutputRoot "s9-t01-publish-smoke.json") -Encoding utf8
$evidence
