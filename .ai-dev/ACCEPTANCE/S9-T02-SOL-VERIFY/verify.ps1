param([Parameter(Mandatory=$true)][string]$PayloadRoot,[Parameter(Mandatory=$true)][string]$Compiler)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo='D:\wendang\ChatGPT\门店效期排查软件'
$out=$PSScriptRoot
$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$id=[guid]::NewGuid().ToString()
$install=Join-Path $temp ([guid]::NewGuid().ToString())
$data=Join-Path $temp ([guid]::NewGuid().ToString())
$runName="StoreExpiryInspector-S9T02-$id"
$shortcutName="StoreExpiryInspector S9-T02 $id"
$runKey='Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey="Software\Microsoft\Windows\CurrentVersion\Uninstall\{$id}_is1"
$exe=Join-Path $install 'app\StoreExpiryInspector.exe'
$expectedArgs='--data-root "'+$data+'" --allow-existing-isolated-data-root'
$expectedRun='"'+$exe+'" '+$expectedArgs
$desktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($shortcutName+'.lnk')
$group=Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
$menu=Join-Path $group ($shortcutName+'.lnk')
$records=[Collections.Generic.List[object]]::new()
function Require([bool]$ok,[string]$why) { if(!$ok){throw $why} }
function CheckRoot([string]$path) {
 $full=[IO.Path]::GetFullPath($path); $parsed=[guid]::Empty
 Require ((Split-Path -Parent $full) -eq $temp -and [guid]::TryParse((Split-Path -Leaf $full),[ref]$parsed)) 'Unsafe test root'
 for($p=[IO.DirectoryInfo]$full;$null -ne $p;$p=$p.Parent){if($p.Exists){Require (($p.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0) 'Reparse test ancestor'}}
 if(Test-Path -LiteralPath $full){foreach($entry in Get-ChildItem -LiteralPath $full -Recurse -Force){Require (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0) 'Reparse test tree'}}
}
function Tree([string]$path) {
 if(!(Test-Path -LiteralPath $path)){return 'MISSING'}
 @(Get-ChildItem -LiteralPath $path -Recurse -File -Force | Sort-Object FullName | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($path,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}}) | ConvertTo-Json -Compress -Depth 4
}
function Proc([string]$file,[string]$arguments,[string]$label,[bool]$success=$true) {
 $p=Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(180000)){Stop-Process -Id $p.Id -Force;throw "$label timeout"}
 if($success){Require ($p.ExitCode -eq 0) "$label exit $($p.ExitCode)"}else{Require ($p.ExitCode -ne 0) "$label should block"}
 return $p.ExitCode
}
function RegValue([string]$key,[string]$name) {
 $k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
 if($null-eq $k){return $null};try{return $k.GetValue($name)}finally{$k.Dispose()}
}
function HasUninstall { $k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey);if($null-eq $k){return $false};$k.Dispose();return $true }
function CheckLinks {
 $shell=New-Object -ComObject WScript.Shell
 foreach($path in @($desktop,$menu)){
  Require (Test-Path -LiteralPath $path) "Missing shortcut $path"
  $link=$shell.CreateShortcut($path)
  Require ($link.TargetPath -eq $exe) 'Wrong shortcut target'
  Require ($link.Arguments -eq $expectedArgs) 'Unsafe shortcut arguments'
 }
 Require (HasUninstall) 'Missing uninstall registration'
}
function Probe([string]$label,[string]$mode='probe') {
 $json=& python -B (Join-Path $out 'probe.py') $mode $data
 Require ($LASTEXITCODE-eq 0) "$label DB probe failed"
 $json | Set-Content -LiteralPath (Join-Path $out "$label-db.json") -Encoding utf8
 $result=$json | ConvertFrom-Json
 Require ($result.integrity.Count-eq 1 -and $result.integrity[0][0]-eq 'ok') 'integrity failed'
 Require ($result.foreignKeys.Count-eq 0) 'FK failed'
 Require ($result.migrationIds.Count-eq 9 -and $result.migrationIds[-1]-eq '20260901155124_AddPolicyAndBaselineFoundation') 'migration mismatch'
 return $result
}
function Smoke([string]$label) {
 $before=Tree (Join-Path $install 'app')
 Proc $exe ($expectedArgs+' --s9-t01-smoke-exit') $label | Out-Null
 Require ($before-eq (Tree (Join-Path $install 'app'))) 'App mutated program tree'
 Require ([bool](Select-String -Path (Join-Path $data 'logs\*.log') -SimpleMatch 's9_t01_smoke_ready' -Quiet)) 'Missing Shell ready marker'
}
function Setup([string]$installer,[string]$label,[bool]$success=$true) {
 return Proc $installer ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="'+(Join-Path $out "$label-install.log")+'"') $label $success
}
function Uninstall([string]$label) {
 $before=Tree $data
 Proc (Join-Path $install 'unins000.exe') ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="'+(Join-Path $out "$label-uninstall.log")+'"') $label | Out-Null
 $deadline=[DateTime]::UtcNow.AddSeconds(15)
 while((Test-Path -LiteralPath $install)-and [DateTime]::UtcNow-lt $deadline){Start-Sleep -Milliseconds 250}
 Require (!(Test-Path -LiteralPath $install)) 'Program tree not removed'
 Require (!(Test-Path -LiteralPath $desktop)-and !(Test-Path -LiteralPath $menu)) 'Shortcut remains'
 Require ($null-eq (RegValue $runKey $runName)) 'Run remains'
 Require (!(HasUninstall)) 'Uninstall registration remains'
 Require ($before-eq (Tree $data)) 'Uninstall changed full data tree'
}
function Record([string]$case,[object]$details) {$records.Add([ordered]@{case=$case;result='PASS';details=$details});$records | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $out 'matrix-progress.json') -Encoding utf8;Write-Output "$case PASS"}
function Compile([string]$version) {
 $dir=Join-Path $out ('setup-'+$version);New-Item -ItemType Directory -Path $dir | Out-Null
 $args=@('/Q',"/DPayloadDir=$PayloadRoot","/DOutputDir=$dir",'/DTestMode',"/DTestAppIdKey=$id","/DTestSuffix=$id","/DTestInstallRoot=$install","/DTestDataRoot=$data","/DTestMutexName=$(Split-Path -Leaf $data)","/DTestVersion=$version")
 & $Compiler @args (Join-Path $repo 'installer\StoreExpiryInspector.iss') *> (Join-Path $out "compile-$version.log")
 Require ($LASTEXITCODE-eq 0) "Compile $version failed"
 return (Get-ChildItem -LiteralPath $dir -Filter '*.exe' | Select-Object -First 1).FullName
}
function Preserve([string]$name) {
 CheckRoot $data
 $dest=Join-Path $out $name
 Require (!(Test-Path -LiteralPath $dest)-and ([IO.Path]::GetDirectoryName($dest)-eq $out)) 'Unsafe preservation target'
 Move-Item -LiteralPath $data -Destination $dest
}
CheckRoot $out;CheckRoot $install;CheckRoot $data
Require (!(Test-Path -LiteralPath $install)-and !(Test-Path -LiteralPath $data)) 'Fresh roots required'
Require ($null-eq (RegValue $runKey $runName)-and !(HasUninstall)) 'Fresh test identity required'
[ordered]@{id=$id;install=$install;data=$data;runName=$runName;desktop=$desktop;menu=$menu;uninstallKey=$uninstallKey;payload=$PayloadRoot}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'identity.json') -Encoding utf8
$low=Compile '1.0.0';$high=Compile '1.0.1'
$errorText=$null
try {
 Setup $low 'A' | Out-Null
 Require (!(Test-Path -LiteralPath (Join-Path $data 'data\app.db'))) 'Installer must not create database'
 Require ((Tree $PayloadRoot)-eq (Tree (Join-Path $install 'app'))) 'Incomplete installed payload'
 CheckLinks
 Require ((RegValue $runKey $runName)-eq $expectedRun) 'First install Run wrong'
 Smoke 'A-smoke';$health=Probe 'A'
 Record 'A' $health
 foreach($relative in @('backups\backup-sentinel.bin','backups\pre-import\snapshot-sentinel.bin','logs\retention-sentinel.bin','other-business-data.bin')){
  $file=Join-Path $data $relative;New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force|Out-Null;[IO.File]::WriteAllBytes($file,[byte[]](1,7,19,37,255))
 }
 $seed=Probe 'seed' 'seed';Require ($seed.workbooks.Count-eq 1) 'Workbook sentinel absent'
 $before=Tree $data
 $repair=Join-Path $install 'app\DocumentFormat.OpenXml.dll';Require ($repair.StartsWith($install+'\')) 'Unsafe repair target';Remove-Item -LiteralPath $repair
 Setup $low 'B' | Out-Null
 Require ($before-eq (Tree $data)) 'B changed data bytes'
 Require ((Tree $PayloadRoot)-eq (Tree (Join-Path $install 'app'))) 'B failed file repair'
 CheckLinks;Require ((RegValue $runKey $runName)-eq $expectedRun) 'B changed enabled Run'
 $b=Probe 'B';Require ($seed.fullFingerprint-eq $b.fullFingerprint) 'B changed full DB fingerprint'
 Smoke 'B-smoke';Record 'B' $b
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKey,$true);try{$key.DeleteValue($runName,$true)}finally{$key.Dispose()}
 $before=Tree $data;Setup $low 'C' | Out-Null
 Require ($null-eq (RegValue $runKey $runName)) 'C reenabled autostart'
 Require ($before-eq (Tree $data)) 'C changed data'
 Record 'C' 'Deleted test HKCU Run; reinstall retained off'
 $d=Probe 'D-before';Uninstall 'D';$dAfter=Probe 'D-after'
 Require ($d.fullFingerprint-eq $dAfter.fullFingerprint) 'D DB fingerprint changed';Record 'D' $dAfter
 $before=Tree $data;Setup $low 'E' | Out-Null;Require ($before-eq (Tree $data)) 'E installation changed data'
 Smoke 'E-smoke';$e=Probe 'E';Require ($e.workbooks[0][0]-eq $seed.workbooks[0][0]) 'E workbook changed';Require ($e.settings[0][1]-eq 613) 'E settings lost';Record 'E' $e
 Setup $high 'F-high' | Out-Null
 Require ((RegValue $uninstallKey 'DisplayVersion')-eq '1.0.1') 'High fixture not installed'
 $programBefore=Tree $install;$dataBefore=Tree $data;$exit=Setup $low 'F-low' $false
 Require ($programBefore-eq (Tree $install)-and $dataBefore-eq (Tree $data)) 'Downgrade changed files';Record 'F' @{exitCode=$exit;version='1.0.1'}
 Uninstall 'F-cleanup';Preserve 'preserved-AF-data'
 foreach($fixture in @(@('G','migration8',10),@('H','unknown',11),@('I','corrupt',12))){
  $case=$fixture[0];$kind=$fixture[1]
  $env:S9T02_FIXTURE_ROOT=$data;$env:S9T02_FIXTURE_KIND=$kind
  try{& dotnet test (Join-Path $repo 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj') -c Release --no-build --no-restore --filter 'FullyQualifiedName~S9T02FixtureWorkerTests' *> (Join-Path $out "$case-fixture.log");Require ($LASTEXITCODE-eq 0) "$case fixture failed"}finally{Remove-Item Env:S9T02_FIXTURE_ROOT,Env:S9T02_FIXTURE_KIND -ErrorAction SilentlyContinue}
  $before=Tree $data
  $exit=Setup $low $case $false
  Require ($before-eq (Tree $data)) "$case changed source bytes"
  Require (!(Test-Path -LiteralPath $install)-and !(HasUninstall)-and $null-eq (RegValue $runKey $runName)) "$case installed despite block"
  $direct=Proc (Join-Path $PayloadRoot 'StoreExpiryInspector.exe') ('--installer-preflight --data-root "'+$data+'"') "$case-preflight" $false
  Require ($direct-eq $fixture[2]) "$case wrong preflight code"
  Record $case @{exitCode=$exit;preflightExit=$direct;tree=$before}
  Preserve "preserved-$case-data"
 }
} catch {$errorText=$_.ToString();throw} finally {
 if(Test-Path -LiteralPath (Join-Path $install 'unins000.exe')){CheckRoot $install;Uninstall 'final-cleanup'}
 [ordered]@{result= $(if($null-eq $errorText){'PASS'}else{'FAIL'});error=$errorText;cases=$records;identity=$id;install=$install;data=$data;cleanProgram=!(Test-Path -LiteralPath $install);cleanRun=$null-eq (RegValue $runKey $runName);cleanRegistration=!(HasUninstall);cleanShortcuts=(!(Test-Path -LiteralPath $desktop)-and !(Test-Path -LiteralPath $menu))}|ConvertTo-Json -Depth 14|Set-Content -LiteralPath (Join-Path $out 'matrix-final.json') -Encoding utf8
}
