; Inno Setup script for GuardHawk
; Build: run ..\build.ps1, or compile this file with Inno Setup 6 (ISCC.exe) after publishing.
;
; Installs per-user by default (no administrator prompt). The first page lets people choose
; "all users" instead, which installs under Program Files.
;
; The Bytehawk Studio Launcher runs this setup silently to install and update GuardHawk (formerly Guardrail EHS):
;   GuardHawk-<version>-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /DIR="<folder>"

#define MyAppName "GuardHawk"
#ifndef MyAppVersion
  #define MyAppVersion "0.2.0"
#endif
#define MyAppPublisher "Bytehawk Studio"
#define MyAppExeName "GuardHawk.exe"
#define MyAppUrl "https://github.com/adamglassner/guardrailehs"
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

[Setup]
AppId={{8C4E2A71-3B9F-4D56-A1E8-6F2B0C9D7E43}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppCopyright=Free and open source (MIT)
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf}\GuardHawk
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=GuardHawk-{#MyAppVersion}-Setup
SetupIconFile=..\src\Guardrail.Desktop\Assets\guardrail.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
LicenseFile=..\LICENSE
WizardStyle=modern
WizardImageFile=wizard-large-1.bmp,wizard-large-2.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp
Compression=lzma2/max
SolidCompression=yes
; Ask to close GuardHawk if it's running (Windows Restart Manager)
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[InstallDelete]
; Renamed from Guardrail EHS in 0.15.0: remove the old program file and shortcuts when updating.
Type: files; Name: "{app}\Guardrail.exe"
Type: filesandordirs; Name: "{autoprograms}\Guardrail EHS"
Type: files; Name: "{autoprograms}\Guardrail EHS.lnk"
Type: files; Name: "{autodesktop}\Guardrail EHS.lnk"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; NOTE: There is deliberately no [UninstallDelete] section.
; Uninstalling removes the program and its shortcuts only. Records and settings in
; %APPDATA%\Guardrail\ are left alone so a reinstall picks up where you left off.
