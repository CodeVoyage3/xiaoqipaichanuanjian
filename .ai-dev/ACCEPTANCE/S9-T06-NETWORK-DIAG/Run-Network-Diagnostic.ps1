param([switch]$SelfTest,[string]$PacketRoot=$PSScriptRoot,[string]$AppDirectory=(Join-Path $env:LOCALAPPDATA 'Programs\StoreExpiryInspector\app'))
$ErrorActionPreference='Stop'
function Ordinary([string]$path) {
    for($d=[IO.DirectoryInfo][IO.Path]::GetFullPath($path);$null -ne $d;$d=$d.Parent){if($d.Exists -and ($d.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked paths are not accepted.'}}
}
Ordinary $AppDirectory; Ordinary $PacketRoot
$source=Join-Path $AppDirectory 'StoreExpiryInspector.dll'
if(-not (Test-Path -LiteralPath $source -PathType Leaf)){throw 'Official installed v1.0.0 was not found. Stop and report.'}
if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked app DLL.'}
if((Get-FileHash -LiteralPath $source).Hash -ne '09E1E4B7DF08D48602D76684AC9F6D39BA0B6F02066973AD5B7A8E25090CF444'){throw 'Requires unchanged official v1.0.0 app DLL. Nothing was modified.'}
$run=Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString());Ordinary $run
$runtime=Join-Path $run 'runtime';New-Item -ItemType Directory -Path $runtime|Out-Null
# Copies only top-level program/runtime binaries and their JSON configuration. Never opens a data directory.
foreach($f in Get-ChildItem -LiteralPath $AppDirectory -File){
    if($f.Extension -notin '.dll','.exe','.json'){continue}
    if($f.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked runtime file.'}
    Copy-Item -LiteralPath $f.FullName -Destination $runtime
}
foreach($name in 'NetworkProbe.exe','NetworkProbe.dll','NetworkProbe.deps.json','NetworkProbe.runtimeconfig.json'){
    $p=Join-Path $PacketRoot $name
    if((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked probe file.'}
    Copy-Item -LiteralPath $p -Destination $runtime
}
if((Get-FileHash -LiteralPath (Join-Path $runtime 'StoreExpiryInspector.dll')).Hash -ne (Get-FileHash -LiteralPath $source).Hash){throw 'Copied app DLL mismatch.'}
Write-Host 'Diagnostic only: no app update, no database access, no security or proxy settings changed.'
Write-Host 'Allow up to 12 minutes if a network timeout occurs. Do not close the window.'
Push-Location $run
try {
    $arguments=@();if($SelfTest){$arguments+='--self-test'}
    & (Join-Path $runtime 'NetworkProbe.exe') @arguments
    $exitCode=$LASTEXITCODE
}finally{Pop-Location}
$log=Join-Path $run 'network-diagnostic.jsonl'
if(Test-Path -LiteralPath $log){
    $output=Join-Path $PacketRoot ('network-diagnostic-'+[IO.Path]::GetFileName($run)+'.jsonl')
    if(Test-Path -LiteralPath $output){throw 'Evidence already exists. Do not overwrite.'}
    Copy-Item -LiteralPath $log -Destination $output
    Write-Host ('Return this file in the current task: '+$output)
}
if($exitCode -ne 0){throw 'Diagnostic host exited unsuccessfully; retain and return its partial JSONL. Do not reset app data.'}
