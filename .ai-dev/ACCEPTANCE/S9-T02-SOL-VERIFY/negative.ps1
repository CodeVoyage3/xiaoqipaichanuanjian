$ErrorActionPreference='Stop'
$out=$PSScriptRoot
$outId=[guid]::Empty
if((Split-Path -Parent $out)-ne ([IO.Path]::GetTempPath().TrimEnd('\')) -or ![guid]::TryParse((Split-Path -Leaf $out),[ref]$outId)){throw 'Copy this evidence script into a TEMP/GUID directory before running.'}
$app=Join-Path $out 'publish\StoreExpiryInspector.exe'
$source=Join-Path $out 'preserved-AF-data\data'
$results=[Collections.Generic.List[object]]::new()
function NewRoot {Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())}
function Tree([string]$root){if(!(Test-Path -LiteralPath $root)){return 'MISSING'};@(Get-ChildItem -LiteralPath $root -Recurse -File -Force|Sort-Object FullName|ForEach-Object{[ordered]@{path=$_.FullName;sha=(Get-FileHash -LiteralPath $_.FullName).Hash}})|ConvertTo-Json -Compress}
function CopyDb {
 $root=NewRoot;New-Item -ItemType Directory -Path (Join-Path $root 'data')|Out-Null
 foreach($name in @('app.db','app.db-wal','app.db-shm','app.db-journal')){$file=Join-Path $source $name;if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination (Join-Path $root "data\$name")}}
 return $root
}
function Check([string]$name,[string]$root,[int]$expected,[string]$code,[bool]$fingerprint=$true){
 if($fingerprint){$before=Tree $root}
 $stdout=Join-Path $out "$name-preflight.json"
 $p=Start-Process -FilePath $app -ArgumentList ('--installer-preflight --data-root "'+$root+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout
 if(!$p.WaitForExit(60000)){Stop-Process -Id $p.Id -Force;throw "$name timed out"}
 $json=Get-Content -LiteralPath $stdout -Raw -Encoding utf8|ConvertFrom-Json
 if($p.ExitCode-ne $expected-or $json.code-ne $code){throw "$name wrong result $($p.ExitCode) $($json.code)"}
 if($fingerprint-and $before-ne (Tree $root)){throw "$name mutated source files"}
 $results.Add([ordered]@{name=$name;root=$root;exit=$p.ExitCode;code=$json.code;sourceUnchanged=$fingerprint})
 Write-Output "$name PASS"
}
$root=NewRoot;Check 'no_database' $root 0 'no_database'
$root=NewRoot;New-Item -ItemType Directory -Path $root|Out-Null;Check 'empty_root' $root 0 'no_database'
$root=NewRoot;[IO.File]::WriteAllText($root,'synthetic root file');Check 'root_file' $root 13 'invalid_data_root'
$root=NewRoot;New-Item -ItemType Directory -Path $root|Out-Null;[IO.File]::WriteAllText((Join-Path $root 'data'),'synthetic data file');Check 'data_file' $root 13 'invalid_data_root'
$root=NewRoot;New-Item -ItemType Directory -Path (Join-Path $root 'data\app.db')|Out-Null;Check 'db_directory' $root 13 'invalid_data_root'
foreach($case in @(@('missing_history',12,'corrupt_or_unreadable'),@('blank_version',11,'newer_or_unknown_schema'),@('wal_current',0,'current_migration_9_healthy'),@('wal_unknown',11,'newer_or_unknown_schema'))){
 $root=CopyDb
 & python -B (Join-Path $out 'negative.py') $root $case[0]
 if($LASTEXITCODE-ne 0){throw 'Negative fixture mutation failed'}
 if($case[0]-like 'wal_*' -and (Get-Item -LiteralPath (Join-Path $root 'data\app.db-wal')).Length-le 0){throw 'Expected committed WAL'}
 Check $case[0] $root $case[1] $case[2]
}
$root=CopyDb
$busy=[IO.FileStream]::new((Join-Path $root 'data\app.db'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::ReadWrite)
try{Check 'busy_writer' $root 12 'corrupt_or_unreadable' $false}finally{$busy.Dispose()}
Check 'busy_writer_released' $root 0 'current_migration_9_healthy'
$target=CopyDb;$root=NewRoot;New-Item -ItemType Directory -Path $root|Out-Null
$link=Join-Path $root 'data';$targetBefore=Tree $target
New-Item -ItemType Junction -Path $link -Target (Join-Path $target 'data')|Out-Null
try{Check 'linked_data' $root 13 'invalid_data_root' $false;if($targetBefore-ne (Tree $target)){throw 'Link target changed'}}finally{[IO.Directory]::Delete($link)}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'negative-results.json') -Encoding utf8
