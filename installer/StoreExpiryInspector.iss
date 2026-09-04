#ifndef PayloadDir
  #error PayloadDir must point to a fresh win-x64 publish directory.
#endif
#ifndef OutputDir
  #define OutputDir "installer-output"
#endif
#define AppIdKey "8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14"
#define AppId "{{" + AppIdKey + "}"
#define AppVersion "1.0.0"
#define AppName "门店效期排查软件"
#define InstallRoot "{localappdata}\Programs\StoreExpiryInspector"
#define DataRoot "{localappdata}\StoreExpiryInspector"
#define RunValueName "StoreExpiryInspector"
#define ShortcutName "门店效期排查软件"
#define OutputName "StoreExpiryInspector-Setup-1.0.0"

; Test builds replace every identity that could otherwise touch a real user install.
#ifdef TestMode
  #ifndef TestAppIdKey
    #error TestAppIdKey is required for TestMode.
  #endif
  #define AppIdKey TestAppIdKey
  #define AppId "{{" + AppIdKey + "}"
  #define AppVersion TestVersion
  #define AppName "StoreExpiryInspector S9-T02 Test"
  #define InstallRoot TestInstallRoot
  #define DataRoot TestDataRoot
  #define RunValueName "StoreExpiryInspector-S9T02-" + TestSuffix
  #define ShortcutName "StoreExpiryInspector S9-T02 " + TestSuffix
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
CloseApplications=no
RestartApplications=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/ultra64
SolidCompression=yes
UninstallDisplayName={#AppName}
AppMutex={#AppMutexName}
SetupMutex=StoreExpiryInspector.S9T02.Setup.{#AppIdKey}

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
  InstallMutex: THandle;

function GetFileAttributes(Path: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function CreateInstallMutex(Attributes: Integer; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
procedure CloseHandle(Handle: THandle);
  external 'CloseHandle@kernel32.dll stdcall';

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
  if Value = '' then begin Result := 0; exit; end;
  Separator := Pos('.', Value);
  if Separator = 0 then begin Part := Value; Value := ''; end
  else begin Part := Copy(Value, 1, Separator - 1); Delete(Value, 1, Separator); end;
  Result := StrToIntDef(Part, -1);
end;

function VersionIsNewer(InstalledVersion: String): Boolean;
var
  ExpectedVersion: String;
  Index, InstalledPart, ExpectedPart: Integer;
begin
  Result := False;
  ExpectedVersion := '{#AppVersion}';
  for Index := 0 to 3 do
  begin
    InstalledPart := NextVersionPart(InstalledVersion);
    ExpectedPart := NextVersionPart(ExpectedVersion);
    if InstalledPart > ExpectedPart then begin Result := True; exit; end;
    if InstalledPart < ExpectedPart then exit;
  end;
end;

function IsExistingVersionNewer(): Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppIdKey}}_is1';
  Result := RegQueryStringValue(HKCU, Key, 'DisplayVersion', Version) and VersionIsNewer(Version);
  if not Result and GetVersionNumbersString(ExpandConstant('{#InstallRoot}\app\StoreExpiryInspector.exe'), Version) then
    Result := VersionIsNewer(Version);
end;

function InitializeSetup(): Boolean;
begin
  if Pos('/DIR', Uppercase(GetCmdTail)) > 0 then
  begin
    SuppressibleMsgBox('安装目录已固定，不能通过命令行修改。', mbError, MB_OK, IDOK);
    Result := False;
    exit;
  end;
  WasInstalled := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppIdKey}}_is1');
  if IsExistingVersionNewer() then
  begin
    SuppressibleMsgBox('已安装更高版本。为保护程序和数据，旧安装器已停止。', mbError, MB_OK, IDOK);
    Result := False;
    exit;
  end;
  Result := True;
end;

function IsOrdinaryInstallTree(Path: String): Boolean; forward;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  PreflightExe: String;
begin
  if not IsOrdinaryInstallTree(ExpandConstant('{#InstallRoot}')) then
  begin
    Result := '安装目录不安全。为保护原数据，安装已停止。';
    exit;
  end;
  if InstallMutex = 0 then
  begin
    InstallMutex := CreateInstallMutex(0, True, '{#AppMutexName}');
    if (InstallMutex = 0) or (DLLGetLastError = 183) then
    begin
      if InstallMutex <> 0 then begin CloseHandle(InstallMutex); InstallMutex := 0; end;
      Result := '无法建立安装保护。安装已停止。';
      exit;
    end;
  end;
  if CompareText(WizardDirValue, ExpandConstant('{#InstallRoot}')) <> 0 then
  begin
    Result := '安装目录已固定，不能修改。';
    exit;
  end;
  ExtractTemporaryFiles('*');
  PreflightExe := ExpandConstant('{tmp}\StoreExpiryInspector-preflight\StoreExpiryInspector.exe');
  if not Exec(PreflightExe, '--installer-preflight --data-root "' + ExpandConstant('{#DataRoot}') + '"', ExpandConstant('{tmp}\StoreExpiryInspector-preflight'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '无法执行数据安全检查。为保护原数据，安装已停止。';
    exit;
  end;
  if ResultCode = 10 then Result := '检测到旧版数据库。为保护原数据，安装已停止。'
  else if ResultCode = 11 then Result := '检测到未知或更高版本数据库。为保护原数据，安装已停止。'
  else if ResultCode = 12 then Result := '数据库或 WAL 状态不可安全验证。为保护原数据，安装已停止。'
  else if ResultCode = 13 then Result := '数据目录不安全。为保护原数据，安装已停止。'
  else if ResultCode <> 0 then Result := '现有数据未通过只读兼容性检查。为保护原数据，安装已停止。';
end;

function IsOrdinaryInstallTree(Path: String): Boolean;
var
  Current: String;
  Attributes: Cardinal;
  FindRec: TFindRec;
  FindError: Integer;
begin
  Result := True;
  Current := RemoveBackslashUnlessRoot(Path);
  while Current <> '' do
  begin
    Attributes := GetFileAttributes(Current);
    if (Attributes = $FFFFFFFF) and (DLLGetLastError <> 2) and (DLLGetLastError <> 3) then begin Result := False; exit; end;
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then begin Result := False; exit; end;
    if (Attributes <> $FFFFFFFF) and ((Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0) then begin Result := False; exit; end;
    if ExtractFileDir(Current) = Current then break;
    Current := ExtractFileDir(Current);
  end;
  if not DirExists(Path) then exit;
  if FindFirst(AddBackslash(Path) + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if (GetFileAttributes(AddBackslash(Path) + FindRec.Name) and $400) <> 0 then begin Result := False; exit; end;
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
          if not IsOrdinaryInstallTree(AddBackslash(Path) + FindRec.Name) then begin Result := False; exit; end;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end
  else begin
    FindError := DLLGetLastError;
    if (FindError <> 2) and (FindError <> 18) then Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExePath: String;
  Command: String;
  Arguments: String;
begin
  if (CurStep = ssPostInstall) and not WasInstalled then
  begin
    ExePath := ExpandConstant('{app}\app\StoreExpiryInspector.exe');
    Command := '"' + ExePath + '"';
    Arguments := RuntimeArguments('');
    if Arguments <> '' then Command := Command + ' ' + Arguments;
    if not RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValueName}', Command) then
      RaiseException('无法写入当前用户开机启动设置。');
  end;
end;
