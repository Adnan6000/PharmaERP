; PharmaERP Windows Installer Script (Inno Setup 6)
; Product: PharmaERP
; Version: 0.5.0-rc2
; Architecture: Windows x64 (Self-Contained .NET 10 WPF Application)

#define MyAppName "PharmaERP"
#define MyAppVersion "0.5.0-rc2"
#define MyAppPublisher "PharmaERP Team"
#define MyAppExeName "PharmaERP.Desktop.exe"
#define MySourceDir "..\..\artifacts\publish\win-x64"
#define MyOutputDir "..\..\artifacts\installer"
#define MyOutputBaseFilename "PharmaERP-Setup-0.5.0-rc2"

[Setup]
AppId={{8B84B48A-9A2E-4F3D-9457-3B21E885BFA2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained win-x64 release publish files
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; IMPORTANT: User data safety boundary.
; The installer only manages application binaries under {app} (Program Files).
; User configuration (%LocalAppData%\PharmaERP), databases, and business records
; are never deleted or modified during uninstallation or upgrades.
