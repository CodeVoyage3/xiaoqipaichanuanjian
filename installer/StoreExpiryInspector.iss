#ifndef PayloadDir
  #error PayloadDir must point to a fresh win-x64 publish directory.
#endif
#ifndef OutputDir
  #define OutputDir "installer-output"
#endif
#define AppId "{{8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}"
#define AppVersion "1.0.0"
#define AppName "门店效期排查软件"
#define InstallRoot "{localappdata}\Programs\StoreExpiryInspector"
#define DataRoot "{localappdata}\StoreExpiryInspector"
#define RunValueName "StoreExpiryInspector"
#define ShortcutName "门店效期排查软件"
#define OutputName "StoreExpiryInspector-Setup-1.0.0"

; Test builds replace every identity that could otherwise touch a real user install.
#ifdef TestMode
  #ifndef TestAppId
    #error TestAppId is required for TestMode.
  #endif
  #define AppId TestAppId
  #define AppName "StoreExpiryInspector S9-T02 Test"
  #define InstallRoot TestInstallRoot
  #define DataRoot TestDataRoot
  #define RunValueName "StoreExpiryInspector-S9T02-Test"
  #define ShortcutName "StoreExpiryInspector S9-T02 Test"
  #define OutputName "StoreExpiryInspector-S9T02-Test-Setup"
  #define AppMutexName "Local\StoreExpiryInspector.SingleInstance." + TestMutexName
#else
  #define AppMutexName "Local\StoreExpiryInspector.SingleInstance"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={#InstallRoot}
DefaultGroupName={#ShortcutName}
DisableProgramGroupPage=yes
DisableDirPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/ultra64
SolidCompression=yes
UninstallDisplayName={#AppName}
AppMutex={#AppMutexName}

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\StoreExpiryInspector-preflight"; Flags: dontcopy recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\app\StoreExpiryInspector.exe"; Parameters: "{code:RuntimeArguments}"; WorkingDir: "{app}\app"
Name: "{group}\{#ShortcutName}"; Filename: "{app}\app\StoreExpiryInspector.exe"; Parameters: "{code:RuntimeArguments}"; WorkingDir: "{app}\app"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#RunValueName}"; Flags: uninsdeletevalue

[Code]
var
  WasInstalled: Boolean;

function RuntimeArguments(Param: String): String;
begin
  #ifdef TestMode
    Result := '--data-root "{#DataRoot}" --allow-existing-isolated-data-root';
  #else
    Result := '';
  #endif
end;

function NextVersionPart(var Value: String): Integer;
var
  Separator: Integer;
  Part: String;
begin
  Separator := Pos('.', Value);
  if Separator = 0 then begin Part := Value; Value := ''; end
  else begin Part := Copy(Value, 1, Separator - 1); Delete(Value, 1, Separator); end;
  Result := StrToIntDef(Part, -1);
end;

function IsExistingVersionNewer(): Boolean;
var
  InstalledVersion: String;
  ExpectedVersion: String;
  Index, InstalledPart, ExpectedPart: Integer;
begin
  Result := False;
  if not GetVersionNumbersString(ExpandConstant('{app}\app\StoreExpiryInspector.exe'), InstalledVersion) then exit;
  ExpectedVersion := '{#AppVersion}';
  for Index := 0 to 3 do
  begin
    InstalledPart := NextVersionPart(InstalledVersion);
    ExpectedPart := NextVersionPart(ExpectedVersion);
    if InstalledPart > ExpectedPart then begin Result := True; exit; end;
    if InstalledPart < ExpectedPart then exit;
  end;
end;

function InitializeSetup(): Boolean;
begin
  if Pos('/DIR', Uppercase(GetCmdTail)) > 0 then
  begin
    MsgBox('安装目录已固定，不能通过命令行修改。', mbError, MB_OK);
    Result := False;
    exit;
  end;
  WasInstalled := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1');
  if WasInstalled and IsExistingVersionNewer() then
  begin
    MsgBox('已安装更高版本。为保护程序和数据，旧安装器已停止。', mbError, MB_OK);
    Result := False;
    exit;
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  PreflightExe: String;
begin
  ExtractTemporaryFiles('StoreExpiryInspector-preflight\*');
  PreflightExe := ExpandConstant('{tmp}\StoreExpiryInspector-preflight\StoreExpiryInspector.exe');
  if not Exec(PreflightExe, '--installer-preflight --data-root "' + ExpandConstant('{#DataRoot}') + '"', ExpandConstant('{tmp}\StoreExpiryInspector-preflight'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '无法执行数据安全检查。为保护原数据，安装已停止。';
    exit;
  end;
  if ResultCode <> 0 then
    Result := '现有数据未通过只读兼容性检查。为保护原数据，安装已停止。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExePath: String;
begin
  if (CurStep = ssPostInstall) and not WasInstalled then
  begin
    ExePath := ExpandConstant('{app}\app\StoreExpiryInspector.exe');
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValueName}', '"' + ExePath + '" ' + RuntimeArguments(''));
  end;
end;
