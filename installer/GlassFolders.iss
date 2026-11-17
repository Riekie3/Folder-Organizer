; GlassFolders Inno Setup Installer Script
; Targets Windows 10/11 64-bit (Per-User Installation, No Administrator Privileges Required)

#define MyAppName "GlassFolders"
#define MyAppVersion "1.2.2"
#define MyAppPublisher "Rie-Sources"
#define MyAppURL "https://rie-sources.com/"
#define MyAppExeName "GlassFolders.exe"
#define MyAppIcon "..\assets\app.ico"

[Setup]
AppId={{8F4B891A-2D85-4E7B-9E39-C462C09247E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=GlassFolders-Setup-v{#MyAppVersion}
SetupIconFile={#MyAppIcon}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startwithwindows"; Description: "&Start GlassFolders when Windows starts (system tray)"; GroupDescription: "Additional settings:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\src\GlassFolders.App\bin\Release\net10.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GlassFolders"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  AppMutexName = 'Local\GlassFolders.SingleInstance';
  AppExitEventName = 'Local\GlassFolders.Exit';
  EventModifyState = $0002;

function OpenEventW(DesiredAccess: Cardinal; InheritHandle: Boolean; EventName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(ObjectHandle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function GlassFoldersRunning(): Boolean;
begin
  Result := CheckForMutexes(AppMutexName);
end;

procedure WaitForGlassFoldersToExit(TimeoutMs: Integer);
var
  Waited: Integer;
begin
  Waited := 0;
  while (Waited < TimeoutMs) and GlassFoldersRunning() do
  begin
    Sleep(100);
    Waited := Waited + 100;
  end;
end;

// Closes the running app before files are replaced, so an update can be installed in place.
procedure CloseRunningGlassFolders();
var
  ExitEvent: THandle;
  ErrorCode: Integer;
begin
  if not GlassFoldersRunning() then
    Exit;

  // The app watches this event and exits through its normal tray path, which saves settings
  // and removes its desktop windows and tray icon before the setup replaces any files.
  ExitEvent := OpenEventW(EventModifyState, False, AppExitEventName);
  if ExitEvent <> 0 then
  begin
    SetEvent(ExitEvent);
    CloseHandle(ExitEvent);
    WaitForGlassFoldersToExit(5000);
  end;

  // A hung or older instance may not answer the event; end it and wait for it to go.
  if GlassFoldersRunning() then
  begin
    Exec('taskkill.exe', '/f /im GlassFolders.exe', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
    WaitForGlassFoldersToExit(5000);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  CloseRunningGlassFolders();
  if GlassFoldersRunning() then
    Result := 'GlassFolders could not be closed. Close it from the system tray, then click Install again.'
  else
    Result := '';
end;

// Asks user on uninstall if they also want to remove their folders and settings
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\GlassFolders');
    if DirExists(DataDir) then
    begin
      if MsgBox('Do you also want to remove all custom folders, layouts, and backup settings?' + #13#10 +
                '(Your actual applications and files will never be touched.)', mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
