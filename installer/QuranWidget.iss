; Quran Widget — Inno Setup installer script
; Build: install Inno Setup 6 (https://jrsoftware.org/isinfo.php), then run:
;   ISCC.exe installer\QuranWidget.iss
; The produced setup installs the widget, registers a Start Menu shortcut and
; a clean uninstaller.

#define AppName "Quran Widget"
#define AppVersion "1.0.1"
#define AppPublisher "Quran Widget"
#define AppExe "QuranWidget.exe"

[Setup]
AppId={{7C6E8B1A-52D4-4B3E-9C2F-0A8F7B5D3E11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\QuranWidget
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..
OutputBaseFilename=QuranWidgetSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "autostart"; Description: "Start {#AppName} with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\dist\QuranWidget.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; per-user autostart (only when the task is selected; removed on uninstall)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "QuranWidget"; \
    ValueData: """{app}\{#AppExe}"" --hidden"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"
