; Inno Setup script for NightLights.
; Builds a small, per-user (no admin rights required) installer.
;
; Build locally:
;   1. Install Inno Setup: https://jrsoftware.org/isinfo.php
;   2. Build the app first (Release, from NightLights.sln)
;   3. Open this file in the Inno Setup Compiler and click Compile,
;      or run:  iscc installer\setup.iss
;
; The GitHub Actions release workflow (.github/workflows/release.yml) builds
; this automatically for every tagged release - you shouldn't normally need
; to run this by hand.

#define MyAppName "NightLights"
#define MyAppPublisher "NightLights contributors"
#define MyAppURL "https://github.com/nonamegsm/NightLights"
#define MyAppExeName "NightLights.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceBinDir
  #define SourceBinDir "..\NightLights\NightLights\bin\Release"
#endif

[Setup]
AppId={{8C52BEE1-A68E-4566-9FDE-81E662D159A0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
; Per-user install - no admin/UAC prompt needed, matching the app's own
; "no admin rights required" design.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
SetupIconFile=..\NightLights\NightLights\App.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceBinDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBinDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceBinDir}\{#MyAppName}.exe.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; MysticLight_SDK.dll is MSI's proprietary DLL and is never bundled here (see
; README) - if the user has already dropped it next to a previous install,
; leave it alone; otherwise the app just skips motherboard RGB control.

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort: make sure a running instance isn't holding the exe locked during uninstall.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillNightLights"

; Note: {app} is fully removed on uninstall, but %AppData%\NightLights
; (settings, logs, cached lighting snapshots) is intentionally left behind,
; the same way most apps leave user data unless asked to wipe it.
