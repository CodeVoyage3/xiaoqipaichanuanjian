param([Parameter(Mandatory=$true)][string]$Candidate, [Parameter(Mandatory=$true)][string]$EvidenceDirectory, [int]$ExistingPid=0, [string]$ExistingDataRoot, [switch]$CancelDownload)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GuiCandidateNative {
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
'@
$dataRoot=if($ExistingPid){$ExistingDataRoot}else{Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())}
if(![guid]::TryParse([IO.Path]::GetRelativePath([IO.Path]::GetTempPath(),$dataRoot),[ref]([guid]::Empty))){throw 'TEMP/GUID only'}
$log=Join-Path $dataRoot 's9-t06-network-diagnostic.jsonl'
$arguments='--data-root "{0}" --s9-t06-network-diagnostic "{1}" --s9-t06-prepare-only --s9-t06-simulated-source 1.0.0' -f $dataRoot,$log
$owned=if($ExistingPid){Get-Process -Id $ExistingPid}else{Start-Process -FilePath $Candidate -ArgumentList $arguments -PassThru}
if($owned.Path -ne [IO.Path]::GetFullPath($Candidate)){throw 'Unexpected process path'}
$condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$owned.Id)
function Windows {
 foreach($rootWindow in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)){
  $rootWindow
  $windowType=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window)
  $rootWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,$windowType)
 }
}
function Button($window,$name) {
 $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)
 $el=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
 if($null -eq $el){throw ('Button missing: '+$name)}
 $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
$until=[DateTime]::UtcNow.AddSeconds(40); $popup=$null
while([DateTime]::UtcNow -lt $until -and !$owned.HasExited){
 foreach($w in (Windows)){ if($w.Current.Name -eq '发现新版本'){$popup=$w;break} }
 if($null -ne $popup){break}; Start-Sleep -Milliseconds 300
}
if($null -eq $popup){Write-Output ('NO_POPUP PID='+$owned.Id+' LOG='+$log); exit 2}
$main=@(Windows)|Where-Object {$_.Current.Name -ne '发现新版本'}|Select-Object -First 1
$mainHandle=$main.Current.NativeWindowHandle
Button $popup '立即更新'
if($CancelDownload){Start-Sleep -Milliseconds 300; Button $popup '取消准备'}
$until=[DateTime]::UtcNow.AddMinutes(12); $result=$null
while([DateTime]::UtcNow -lt $until -and !$owned.HasExited){
 if(Test-Path -LiteralPath $log){
  $events=@(Get-Content -LiteralPath $log | ForEach-Object {try{$_|ConvertFrom-Json}catch{}})
  $result=$events|Where-Object {$_.kind -in @('gui-prepare-result','gui-prepare-error','gui-prepare-cancelled')}|Select-Object -Last 1
  if($null -ne $result){break}
 }
 Start-Sleep -Milliseconds 500
}
Button $popup '稍后提醒'
$main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Milliseconds 300
[GuiCandidateNative]::PostMessage([IntPtr]$mainHandle,0x8001,[IntPtr]::Zero,[IntPtr]0x205)|Out-Null
Start-Sleep -Milliseconds 300
$exitInvoked=$false
foreach($w in (Windows)){
 $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'退出应用')
 $el=$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
 if($null -ne $el){$el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();$exitInvoked=$true;break}
}
$exited=$owned.WaitForExit(30000)
Copy-Item -LiteralPath $log -Destination (Join-Path $EvidenceDirectory 'candidate-gui-trace.jsonl')
$summary=[ordered]@{actualAppGui=$true;sourceSimulation='1.0.0';actualCandidate='1.0.2';target='1.0.1';actualButton=$true;result=$result;exitInvoked=$exitInvoked;exited=$exited;dataRootSynthetic=$true;installationConfigured=$false;updaterStarted=$false}
$summary|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'candidate-gui-result.json') -Encoding UTF8
Write-Output ('PID='+$owned.Id+' DATA='+$dataRoot)
$summary|ConvertTo-Json -Depth 10
$expectedOutcome=if($CancelDownload){'Cancelled'}else{'Verified'}
if(!$exited -or $result.detail.outcome -ne $expectedOutcome){exit 3}
