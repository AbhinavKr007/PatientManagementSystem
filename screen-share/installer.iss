; Inno Setup script for LAN Screen Share.
; The GitHub workflow "Build LAN Screen Share installer" stages the files
; (a copy of node.exe renamed to LanScreenShare.exe plus the app) and runs:
;   iscc /DStageDir=<staging folder> screen-share\installer.iss

#define MyAppName "LAN Screen Share"
#define MyAppVersion "1.0.0"
#define MyAppExeName "LanScreenShare.exe"
#ifndef StageDir
  #define StageDir "stage"
#endif

[Setup]
; Keep this GUID stable so newer versions upgrade the same installed app.
AppId={{3B9B2E0E-52C4-4B0B-9E55-7A1D5C0A4F21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=LanScreenShare-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
; Admin rights are needed once, to add the Windows Firewall rule below.
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "seconddisplay"; Description: "Install a &virtual second display (use a tablet as an extra screen)"; GroupDescription: "Second screen:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "server.js --open"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "server.js --open"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Let tablets and phones on the Wi-Fi reach this app (and only this app).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#MyAppName}"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#MyAppName}"" dir=in action=allow program=""{app}\{#MyAppExeName}"" enable=yes profile=any"; Flags: runhidden
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\driver\install-display.ps1"""; StatusMsg: "Installing the virtual second display..."; Flags: runhidden; Tasks: seconddisplay
Filename: "{app}\{#MyAppExeName}"; Parameters: "server.js --open"; WorkingDir: "{app}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\driver\install-display.ps1"" -Uninstall"; Flags: runhidden; RunOnceId: "DelVirtualDisplay"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#MyAppName}"""; Flags: runhidden; RunOnceId: "DelFirewallRule"
