[CmdletBinding(DefaultParameterSetName = 'Production')]
param(
    [Parameter(ParameterSetName = 'Production')][string]$AppPath,
    [Parameter(Mandatory, ParameterSetName = 'Isolated')][string]$IsolatedInstallRoot,
    [Parameter(ParameterSetName = 'Isolated')][switch]$IsolatedSmoke
)

$ErrorActionPreference = 'Stop'
$expectedTreeHash = 'BE93548A81FCBF61DB2737C8BBEC9F9CE84DF2D226CB0456282469DF9835E0D8'
$expectedProductVersion = '1.0.0+7044a984ddca757d8ae9350fbc523800bd769796'

function Assert-OrdinaryDirectory([string]$Path) {
    for ($current = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path)); $null -ne $current; $current = $current.Parent) {
        if (-not $current.Exists -or ([IO.File]::GetAttributes($current.FullName) -band [IO.FileAttributes]::ReparsePoint)) { throw 'Application directory is not an ordinary local directory.' }
    }
}

function Get-OrdinaryTreeHash([string]$Root) {
    Assert-OrdinaryDirectory $Root
    $directories = [Collections.Generic.Stack[string]]::new(); $directories.Push($Root)
    $files = [Collections.Generic.List[string]]::new()
    while ($directories.Count) {
        $directory = $directories.Pop()
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            if ([IO.File]::GetAttributes($entry) -band [IO.FileAttributes]::ReparsePoint) { throw 'Application tree contains a reparse point.' }
            if ([IO.Directory]::Exists($entry)) { $directories.Push($entry) }
            elseif ([IO.File]::Exists($entry)) { $files.Add($entry) }
            else { throw 'Application tree contains an unsupported entry.' }
        }
    }
    $paths = $files.ToArray(); [Array]::Sort($paths, [StringComparer]::OrdinalIgnoreCase)
    $rows = foreach ($file in $paths) {
        if (-not $file.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Application tree path escaped its root.' }
        $relative = $file.Substring($Root.Length + 1)
        $stream = [IO.File]::OpenRead($file); $fileHash = [Security.Cryptography.SHA256]::Create()
        try { "$relative|$($stream.Length)|$(([BitConverter]::ToString($fileHash.ComputeHash($stream))).Replace('-', ''))" }
        finally { $fileHash.Dispose(); $stream.Dispose() }
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($rows -join "`n"))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '') }
    finally { $sha256.Dispose() }
}

$isolated = $PSCmdlet.ParameterSetName -eq 'Isolated'
$root = if ($isolated) {
    $value = [IO.Path]::GetFullPath($IsolatedInstallRoot).TrimEnd('\\')
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\\')
    $id = [guid]::Empty
    if (-not [guid]::TryParse((Split-Path -Leaf $value), [ref]$id) -or (Split-Path -Parent $value) -ine $temp) { throw 'IsolatedInstallRoot must be a direct TEMP GUID directory.' }
    $value
} else {
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\StoreExpiryInspector'
}
$appRoot = Join-Path $root 'app'
$expectedApp = Join-Path $appRoot 'StoreExpiryInspector.exe'
$app = if ($isolated) { $expectedApp } elseif ($AppPath) { [IO.Path]::GetFullPath($AppPath) } else { $expectedApp }
if ($app -ine $expectedApp -or -not [IO.File]::Exists($app)) { throw 'AppPath is not the fixed v1.0.0 application entry.' }
if ((Get-OrdinaryTreeHash $appRoot) -cne $expectedTreeHash) { throw 'Application tree is not the public v1.0.0 release tree.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app)
if ($version.FileVersion -cne '1.0.0.0' -or $version.ProductVersion.Trim() -cne $expectedProductVersion) { throw 'AppPath version identity is not public v1.0.0 source 7044a984.' }
if ([Diagnostics.Process]::GetProcessesByName('StoreExpiryInspector').Length) { throw 'StoreExpiryInspector is already running; close it before the bridge starts.' }
$workingDirectory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $workingDirectory | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new($app); $start.UseShellExecute = $false; $start.WorkingDirectory = $workingDirectory
$dataRoot = $null
if ($isolated) {
    $dataRoot = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
    $start.Arguments = '--data-root "' + $dataRoot + '"'
    if ($IsolatedSmoke) { $start.Arguments += ' --s9-t01-smoke-exit' }
}
$process = [Diagnostics.Process]::Start($start)
$exitCode = $null
if ($IsolatedSmoke) {
    if (-not $process.WaitForExit(60000)) { throw 'Isolated smoke timed out.' }
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) { throw "Isolated smoke failed with exit code $exitCode." }
}
[pscustomobject]@{ processId = $process.Id; exitCode = $exitCode; sourceVersion = '1.0.0'; sourceCommit = '7044a984ddca757d8ae9350fbc523800bd769796'; workingDirectory = $workingDirectory; dataRoot = $dataRoot; target = 'GitHub latest release, currently public v1.0.1; no v1.0.2 is published' }
