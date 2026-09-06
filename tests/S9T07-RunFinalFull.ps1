param(
    [Parameter(Mandatory)][string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (Test-Path $ResultDirectory) { throw 'fresh result directory required' }
New-Item -ItemType Directory -Path $ResultDirectory | Out-Null
$oldPublish = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())

Push-Location $root
try {
    $buildStartUtc = [DateTimeOffset]::UtcNow
    & dotnet build 'src\StoreExpiryInspector\StoreExpiryInspector.csproj' -c Release --no-restore --no-incremental -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'production old build failed' }
    & dotnet publish 'src\StoreExpiryInspector\StoreExpiryInspector.csproj' -c Release --no-restore --no-build -p:NuGetAudit=false -o $oldPublish
    if ($LASTEXITCODE -ne 0 -or !(Test-Path (Join-Path $oldPublish 'StoreExpiryInspector.exe'))) { throw 'production old publish failed' }

    & dotnet build 'src\StoreExpiryInspector\StoreExpiryInspector.csproj' -c Release --no-restore --no-incremental -p:S9T07TestMode=true -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'S9T07 test App build failed' }
    & dotnet build 'src\StoreExpiryInspector.Updater\StoreExpiryInspector.Updater.csproj' -c Release --no-restore --no-incremental -p:S9T05TestMode=true -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'S9T05 test Updater build failed' }
    & dotnet build 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj' -c Release --no-restore --no-incremental -p:S9T05TestMode=true -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'test assembly build failed' }
    $hardKillSafety = Join-Path $root 'src\StoreExpiryInspector.UpdateSafety\bin\Release\net10.0\s9t07hardkilltest\net10.0\StoreExpiryInspector.UpdateSafety.dll'
    $testSafety = Join-Path $root 'tests\StoreExpiryInspector.Tests\bin\Release\net10.0-windows\StoreExpiryInspector.UpdateSafety.dll'
    if (!(Test-Path $hardKillSafety) -or !(Test-Path $testSafety) -or (Get-FileHash -LiteralPath $hardKillSafety -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $testSafety -Algorithm SHA256).Hash) { throw 'test assembly did not bind the S9-T07 hard-kill UpdateSafety artifact' }
    $artifacts = @(
        [pscustomobject]@{ path = (Join-Path $oldPublish 'StoreExpiryInspector.exe'); requireFresh = $false },
        [pscustomobject]@{ path = (Join-Path $root 'src\StoreExpiryInspector\bin\Release\net10.0-windows\s9t07test\net10.0-windows\StoreExpiryInspector.exe'); requireFresh = $true },
        [pscustomobject]@{ path = (Join-Path $root 'src\StoreExpiryInspector.Updater\bin\Release\net10.0\win-x64\s9t05test\net10.0\win-x64\StoreExpiryInspector.Updater.exe'); requireFresh = $true },
        [pscustomobject]@{ path = (Join-Path $root 'tests\StoreExpiryInspector.Tests\bin\Release\net10.0-windows\StoreExpiryInspector.Tests.dll'); requireFresh = $true }
    )
    $evidence = foreach ($artifact in $artifacts) {
        if (!(Test-Path $artifact.path)) { throw "required fresh artifact missing: $($artifact.path)" }
        $item = Get-Item -LiteralPath $artifact.path
        if ($artifact.requireFresh -and $item.LastWriteTimeUtc -lt $buildStartUtc.UtcDateTime) { throw "test artifact predates this harness build: $($item.FullName)" }
        [ordered]@{ path = $item.FullName; lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('O'); sha256 = (Get-FileHash -LiteralPath $artifact.path -Algorithm SHA256).Hash }
    }
    [IO.File]::WriteAllText((Join-Path $ResultDirectory 'build-artifacts.json'), ([ordered]@{ buildStartUtc = $buildStartUtc.ToString('O'); artifacts = $evidence } | ConvertTo-Json))

    $env:S9_T07_REAL_OLD_PUBLISH = $oldPublish
    & dotnet test 'StoreExpiryInspector.slnx' -c Release --no-build --no-restore -p:NuGetAudit=false --logger 'trx;LogFileName=S9T07-final-full.trx' --results-directory $ResultDirectory
    exit $LASTEXITCODE
}
finally { Pop-Location }
