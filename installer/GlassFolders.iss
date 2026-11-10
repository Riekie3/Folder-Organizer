; GlassFolders Inno Setup Installer Script
; Targets Windows 10/11 64-bit (Per-User Installation, No Administrator Privileges Required)

#define MyAppName "GlassFolders"
#define MyAppVersion "1.1.2"
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
DisableProgramGroupPage=yes
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
// Closes running instance before installation
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if CheckForMutexes('Local\GlassFolders.SingleInstance') then
  begin
    if MsgBox('GlassFolders is currently running. Would you like to close it to continue the installation?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // Terminate running process via taskkill
      Exec('taskkill.exe', '/f /im GlassFolders.exe', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
      Sleep(500);
    end
    else
    begin
      Result := False;
    end;
  end;
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
