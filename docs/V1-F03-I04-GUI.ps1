param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Check', 'Enter', 'Start', 'Finish')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $repo 'obj\V1F03I04GuiAcceptance'
$manifestPath = Join-Path $evidence 'environment.json'
$runtime = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StoreExpiryInspector'
$protected = $runtime + '.v1f03i04-original'
$completed = $runtime + '.v1f03i04-isolated'
$markerName = '.v1f03i04-isolated.json'
$exe = Join-Path $repo 'src\StoreExpiryInspector\bin\Release\net10.0-windows\StoreExpiryInspector.exe'

if ($Action -eq 'Check') {
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Release EXE missing.' }
    Write-Output 'ENTRY_CHECK_PASS'
    return
}

function Assert-Stopped {
    if (@(Get-Process -Name StoreExpiryInspector -ErrorAction SilentlyContinue).Count -ne 0) {
        throw '请先从托盘退出门店效期排查软件；脚本不会强制结束进程。'
    }
}

function Assert-ManagedDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notin @($runtime, $protected, $completed)) { throw "拒绝未授权目录：$full" }
    if (-not (Test-Path -LiteralPath $full)) { return }
    $item = Get-Item -LiteralPath $full -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "目录不是普通本地目录，请停止并反馈：$full"
    }
    if (@(Get-ChildItem -LiteralPath $full -Force -Recurse |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) {
        throw "目录内存在重解析点，请停止并反馈：$full"
    }
}

function Get-DatabaseIdentity([string]$Directory) {
    $db = Join-Path $Directory 'data\app.db'
    if (-not (Test-Path -LiteralPath $db)) {
        return [pscustomobject]@{ Exists = $false; Length = 0; SHA256 = $null }
    }
    foreach ($suffix in @('-wal', '-shm', '-journal')) {
        if (Test-Path -LiteralPath ($db + $suffix)) { throw "数据库存在未收口 sidecar：$db$suffix" }
    }
    return [pscustomobject]@{
        Exists = $true
        Length = (Get-Item -LiteralPath $db).Length
        SHA256 = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash
    }
}

function Read-AutoStart {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        if ($null -eq $key -or 'StoreExpiryInspector' -notin $key.GetValueNames()) {
            return [pscustomobject]@{ Exists = $false; Kind = 'String'; Value = $null }
        }
        $kind = $key.GetValueKind('StoreExpiryInspector').ToString()
        if ($kind -notin @('String', 'ExpandString')) { throw '检测到无法安全恢复的自启动值类型。' }
        return [pscustomobject]@{
            Exists = $true
            Kind = $kind
            Value = $key.GetValue('StoreExpiryInspector', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Restore-AutoStart($Original) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        if ($Original.Exists) {
            $key.SetValue('StoreExpiryInspector', $Original.Value, [Microsoft.Win32.RegistryValueKind]$Original.Kind)
        }
        else { $key.DeleteValue('StoreExpiryInspector', $false) }
    }
    finally { $key.Dispose() }
}

function Read-Manifest {
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw '没有正在进行的 V1-F03-I04 隔离验收。' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.Runtime -ne $runtime -or $manifest.Protected -ne $protected -or $manifest.Completed -ne $completed) {
        throw '验收清单与当前 Windows 用户不匹配。'
    }
    return $manifest
}

function Assert-Isolated([string]$Directory, $Manifest) {
    Assert-ManagedDirectory $Directory
    $markerPath = Join-Path $Directory $markerName
    if (-not (Test-Path -LiteralPath $markerPath)) { throw '隔离运行目录缺少安全标记。' }
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    if ($marker.Token -ne $Manifest.Token) { throw '隔离运行目录标记不匹配。' }
}

function Assert-OriginalIdentity([string]$Directory, $Manifest) {
    $identity = Get-DatabaseIdentity $Directory
    if ($identity.Exists -ne $Manifest.Database.Exists -or
        ($identity.Exists -and ($identity.Length -ne $Manifest.Database.Length -or $identity.SHA256 -ne $Manifest.Database.SHA256))) {
        throw '原运行数据库身份校验失败；保留现场并反馈，不要手工覆盖。'
    }
}

Assert-Stopped
foreach ($directory in @($runtime, $protected, $completed)) { Assert-ManagedDirectory $directory }

if ($Action -eq 'Enter') {
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Read-Manifest
        Assert-Isolated $runtime $manifest
        if ($manifest.RuntimeExisted) { Assert-OriginalIdentity $protected $manifest }
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null
        Write-Output 'ISOLATED_GUI_STARTED：已重新启动当前隔离验收。'
        return
    }
    if ((Test-Path -LiteralPath $protected) -or (Test-Path -LiteralPath $completed)) {
        throw '发现旧验收旁置目录；请停止并反馈，不会覆盖。'
    }
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Release EXE 不存在，请先完成 Release build。' }

    $runtimeExisted = Test-Path -LiteralPath $runtime
    $database = if ($runtimeExisted) { Get-DatabaseIdentity $runtime } else { [pscustomobject]@{ Exists = $false; Length = 0; SHA256 = $null } }
    $manifest = [ordered]@{
        Runtime = $runtime
        Protected = $protected
        Completed = $completed
        RuntimeExisted = $runtimeExisted
        Database = $database
        Token = [guid]::NewGuid().ToString('N')
        PreparedAtUtc = [DateTime]::UtcNow.ToString('o')
        AutoStart = Read-AutoStart
    }
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    if ($runtimeExisted) { Move-Item -LiteralPath $runtime -Destination $protected }
    New-Item -ItemType Directory -Path $runtime | Out-Null
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runtime $markerName) -Encoding UTF8
    if ($runtimeExisted) { Assert-OriginalIdentity $protected $manifest }
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null
    Write-Output 'ISOLATED_GUI_STARTED：已保护原运行目录并启动 I04 隔离验收。'
    return
}

$manifest = Read-Manifest
if ($Action -eq 'Start') {
    Assert-Isolated $runtime $manifest
    if ($manifest.RuntimeExisted) { Assert-OriginalIdentity $protected $manifest }
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null
    Write-Output 'ISOLATED_GUI_STARTED：已重新启动当前隔离验收。'
    return
}

if (Test-Path -LiteralPath $runtime) {
    Assert-Isolated $runtime $manifest
    if (Test-Path -LiteralPath $completed) { throw '结束目录已存在；保留现场并反馈。' }
    Move-Item -LiteralPath $runtime -Destination $completed
}
if ($manifest.RuntimeExisted) {
    if (-not (Test-Path -LiteralPath $protected)) { throw '原运行目录保护副本缺失。' }
    Assert-OriginalIdentity $protected $manifest
    Move-Item -LiteralPath $protected -Destination $runtime
    Assert-OriginalIdentity $runtime $manifest
}
Restore-AutoStart $manifest.AutoStart
if (Test-Path -LiteralPath $completed) {
    Assert-Isolated $completed $manifest
    Remove-Item -LiteralPath $completed -Recurse -Force
}
$receipt = [ordered]@{
    Result = 'RESTORE_PASS'
    TimeUtc = [DateTime]::UtcNow.ToString('o')
    OriginalRuntimeRestored = [bool]$manifest.RuntimeExisted
    IsolatedRuntimeRemoved = -not (Test-Path -LiteralPath $completed)
    ProtectedDirectoryRemoved = -not (Test-Path -LiteralPath $protected)
    ProcessCount = 0
    ApplicationStartedByFinish = $false
    AutoStartRestored = $true
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'restore-result.json') -Encoding UTF8
$receipt | ConvertTo-Json
