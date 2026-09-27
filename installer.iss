; Inno Setup script for Patient Management System
; Build the app first, then compile this script with Inno Setup (iscc.exe):
;
;   dotnet publish PatientManagementSystem.csproj -c Release -r win-x64 ^
;       --self-contained true -p:PublishSingleFile=true
;   iscc installer.iss
;
; This produces a real Windows installer (Output\PatientManagementSystem-Setup.exe)
; that registers the app in "Apps & features" / "Programs and Features" and
; installs a Start Menu shortcut plus a clean uninstaller.

#define MyAppName "Patient Management System"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Healthcare Solutions Ltd."
#define MyAppExeName "PatientManagementSystem.exe"
#define MyPublishDir "bin\Release\net6.0-windows\win-x64\publish"

[Setup]
; Keep this GUID stable across releases so upgrades replace the same entry
; instead of creating a duplicate "installed app".
AppId={{6F2C6C55-6E5A-4A6D-9C7F-6C7B1F7D9E11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=PatientManagementSystem-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "{#MyAppExeName}"
Source: "medical.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\medical.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\medical.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Logs"
Type: filesandordirs; Name: "{app}\Backups"
