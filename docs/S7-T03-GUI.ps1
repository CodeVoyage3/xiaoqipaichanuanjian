param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Prepare', 'Start', 'VerifyRestore', 'Finish')]
    [string]$Action,
    [string]$BackupFileName
)

# User-run acceptance helper. Never run against a live application.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $repo 'obj\S7T03GuiAcceptance'
$manifestPath = Join-Path $evidence 'environment.json'
$runtime = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StoreExpiryInspector'
$protected = $runtime + '.s7t03-original'
$completed = $runtime + '.s7t03-isolated'
$markerName = '.s7t03-isolated.json'
$expectedHash = 'F3D423DF14B882D7BFE87780A81CF5879F074AF4880601CBEDB6B475A964F522'
$exe = Join-Path $repo 'src\StoreExpiryInspector\bin\Release\net10.0-windows\StoreExpiryInspector.exe'

function Assert-Stopped {
    if (@(Get-Process -Name StoreExpiryInspector -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'Exit the application from its tray menu first. No process will be killed.'
    }
}

function Read-AutoStart {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        if ($null -eq $key -or 'StoreExpiryInspector' -notin $key.GetValueNames()) {
            return [pscustomobject]@{ Exists = $false; Kind = 'String'; Value = $null }
        }
        $kind = $key.GetValueKind('StoreExpiryInspector').ToString()
        if ($kind -notin @('String', 'ExpandString')) { throw 'Unexpected autostart value type; manual review required.' }
        return [pscustomobject]@{
            Exists = $true; Kind = $kind
            Value = $key.GetValue('StoreExpiryInspector', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Restore-AutoStart($Original) {
    $current = Read-AutoStart
    if ($current.Exists -eq $Original.Exists -and $current.Kind -eq $Original.Kind -and
        $current.Value -ceq $Original.Value) { return }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        if ($Original.Exists) {
            $key.SetValue('StoreExpiryInspector', $Original.Value, [Microsoft.Win32.RegistryValueKind]$Original.Kind)
        }
        else { $key.DeleteValue('StoreExpiryInspector', $false) }
    }
    finally { $key.Dispose() }
    $current = Read-AutoStart
    if ($current.Exists -ne $Original.Exists -or $current.Kind -ne $Original.Kind -or
        $current.Value -cne $Original.Value) { throw 'Autostart restoration could not be verified.' }
}

function Assert-ManagedDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notin @($runtime, $protected, $completed)) { throw "Unexpected directory: $full" }
    if (Test-Path -LiteralPath $full) {
        $item = Get-Item -LiteralPath $full -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Directory is not a normal local directory: $full"
        }
        $links = @(Get-ChildItem -LiteralPath $full -Force -Recurse |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count) { throw "Reparse point found under $full; manual review required." }
    }
}

function Assert-Database([string]$Directory, [string]$Hash, [long]$Length) {
    $db = Join-Path $Directory 'data\app.db'
    if ((Get-Item -LiteralPath $db).Length -ne $Length -or
        (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash -ne $Hash) {
        throw "Database identity mismatch: $db"
    }
    foreach ($suffix in @('-wal', '-shm', '-journal')) {
        if (Test-Path -LiteralPath ($db + $suffix)) { throw "Unexpected sidecar: $db$suffix" }
    }
    if (@(Get-ChildItem -LiteralPath (Split-Path $db) -Force -Filter '*.restore-*').Count) {
        throw 'Restore artifacts remain. Keep the app stopped and request review.'
    }
}

function Read-Manifest {
    $value = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($value.Runtime -ne $runtime -or $value.Protected -ne $protected -or
        $value.Completed -ne $completed -or $value.Hash -ne $expectedHash -or $value.Length -ne 299008) {
        throw 'Acceptance manifest does not match this Windows user or approved baseline.'
    }
    return $value
}

function Assert-Isolated([string]$Directory, $Manifest) {
    Assert-ManagedDirectory $Directory
    $marker = Get-Content -LiteralPath (Join-Path $Directory $markerName) -Raw | ConvertFrom-Json
    if ($marker.Token -ne $Manifest.Token) { throw 'Isolation marker mismatch.' }
}

Assert-Stopped
foreach ($directory in @($runtime, $protected, $completed)) { Assert-ManagedDirectory $directory }

if ($Action -eq 'Prepare') {
    if ((Test-Path -LiteralPath $manifestPath) -or (Test-Path -LiteralPath $protected) -or
        (Test-Path -LiteralPath $completed)) { throw 'An acceptance session already exists. Do not overwrite it.' }
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Release EXE missing. Complete the technical build first.' }
    Assert-Database $runtime $expectedHash 299008
    $originalAutoStart = Read-AutoStart
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $manifest = [ordered]@{
        Runtime = $runtime; Protected = $protected; Completed = $completed
        Hash = $expectedHash; Length = 299008; Token = [guid]::NewGuid().ToString('N')
        PreparedAtUtc = [DateTime]::UtcNow.ToString('o')
        AutoStart = $originalAutoStart
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    # Preserve the whole original runtime, including its existing backups and logs.
    Move-Item -LiteralPath $runtime -Destination $protected
    New-Item -ItemType Directory -Path $runtime | Out-Null
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime $markerName) -Encoding UTF8
    Assert-Database $protected $expectedHash 299008
    Write-Output 'PREPARE_PASS: original runtime protected; isolated runtime is empty. App not started.'
    return
}

$manifest = Read-Manifest
if ($Action -eq 'Finish') {
    if (Test-Path -LiteralPath $protected) {
        Assert-Database $protected $manifest.Hash $manifest.Length
        if (Test-Path -LiteralPath $runtime) {
            Assert-Isolated $runtime $manifest
            if (Test-Path -LiteralPath $completed) { throw 'Completed directory already exists; manual review required.' }
            Move-Item -LiteralPath $runtime -Destination $completed
        }
        Move-Item -LiteralPath $protected -Destination $runtime
    }
    Assert-Database $runtime $manifest.Hash $manifest.Length
    if (Test-Path -LiteralPath (Join-Path $runtime $markerName)) { throw 'Formal runtime still has an isolation marker.' }
    Restore-AutoStart $manifest.AutoStart
    if (Test-Path -LiteralPath $completed) {
        Assert-Isolated $completed $manifest
        # Exact sibling path and marker verified above; never delete the original runtime.
        Remove-Item -LiteralPath $completed -Recurse -Force
    }
    Assert-Stopped
    $receipt = [ordered]@{
        Result = 'RESTORE_PASS'; TimeUtc = [DateTime]::UtcNow.ToString('o')
        Runtime = $runtime; Length = (Get-Item -LiteralPath (Join-Path $runtime 'data\app.db')).Length
        SHA256 = (Get-FileHash -LiteralPath (Join-Path $runtime 'data\app.db') -Algorithm SHA256).Hash
        ProcessCount = 0; IsolatedRuntimeRemoved = -not (Test-Path -LiteralPath $completed)
        ProtectedStagingRemoved = -not (Test-Path -LiteralPath $protected); ApplicationStartedByScript = $false
        AutoStartRestored = $true
    }
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'formal-restore-result.json') -Encoding UTF8
    $receipt | ConvertTo-Json
    return
}

Assert-Isolated $runtime $manifest
Assert-Database $protected $manifest.Hash $manifest.Length
if ($Action -eq 'Start') {
    # Intentional interactive WPF launch, only when the user invokes this action.
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) | Out-Null
    Write-Output 'ISOLATED_APP_STARTED: do not enable Windows autostart during this test.'
    return
}

if ([string]::IsNullOrWhiteSpace($BackupFileName) -or [IO.Path]::GetFileName($BackupFileName) -ne $BackupFileName -or
    $BackupFileName -notlike 'backup-*.db' -or $BackupFileName.Contains(':')) {
    throw 'Supply only the selected manual backup filename: -BackupFileName backup-....db'
}
$backup = Join-Path (Join-Path $runtime 'backups') $BackupFileName
$metadata = Get-Content -LiteralPath ($backup + '.metadata.json') -Raw | ConvertFrom-Json
if ($metadata.ValidationResult -ne 'verified' -or $metadata.FileName -ne $BackupFileName -or
    (Get-Item -LiteralPath $backup).Length -ne $metadata.FileSize -or
    (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $metadata.Sha256) { throw 'Backup identity mismatch.' }
Assert-Database $runtime $metadata.Sha256 $metadata.FileSize
$protection = @(Get-ChildItem -LiteralPath (Join-Path $runtime 'backups') -Filter 'pre-restore-*.db')
if (-not $protection.Count) { throw 'No pre-restore protection backup found.' }
foreach ($file in $protection) {
    $meta = Get-Content -LiteralPath ($file.FullName + '.metadata.json') -Raw | ConvertFrom-Json
    if ($meta.ValidationResult -ne 'verified' -or $meta.FileName -ne $file.Name -or
        $meta.FileSize -ne $file.Length -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $meta.Sha256) {
        throw 'Pre-restore backup identity mismatch.'
    }
}
[ordered]@{
    Result = 'GUI_RESTORE_BYTES_PASS'; Backup = $BackupFileName; SHA256 = $metadata.Sha256
    ProtectionBackupCount = $protection.Count; TimeUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $evidence 'gui-restore-bytes-result.json')
