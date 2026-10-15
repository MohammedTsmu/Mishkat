; Mishkat — Inno Setup installer script
; Build: install Inno Setup 6 (https://jrsoftware.org/isinfo.php), then run:
;   ISCC.exe installer\Mishkat.iss
; The produced setup installs the widget, registers a Start Menu shortcut and
; a clean uninstaller.

#define AppName "Mishkat"
#define AppVersion "2.10.12"
#define AppPublisher "Mishkat"
#define AppExe "Mishkat.exe"

[Setup]
AppId={{7C6E8B1A-52D4-4B3E-9C2F-0A8F7B5D3E11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Mishkat
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\QuranWidget\Assets\app.ico
OutputDir=..
OutputBaseFilename=MishkatSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
; the app was renamed from QuranWidget to Mishkat: always install into the
; new folder instead of reusing the previous (old-named) install directory
UsePreviousAppDir=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "autostart"; Description: "Start {#AppName} with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\dist\Mishkat.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; per-user autostart (only when the task is selected; removed on uninstall)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Mishkat"; \
    ValueData: """{app}\{#AppExe}"" --hidden"; \
    Flags: uninsdeletevalue; Tasks: autostart
; remove the autostart entry written by the pre-rename (QuranWidget) installer
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "QuranWidget"; \
    Flags: deletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// The app was renamed from QuranWidget to Mishkat. If a previous install still
// lives in the old "...\Programs\QuranWidget" folder, uninstall it silently
// first so its files and Start Menu entries do not linger under the old name.
function InitializeSetup(): Boolean;
var
  Uninst: String;
  Rc: Integer;
begin
  Result := True;
  if RegQueryStringValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C6E8B1A-52D4-4B3E-9C2F-0A8F7B5D3E11}_is1',
      'UninstallString', Uninst) then
  begin
    if Pos('QuranWidget', Uninst) > 0 then
      Exec(RemoveQuotes(Uninst), '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '',
           SW_SHOW, ewWaitUntilTerminated, Rc);
  end;
end;

