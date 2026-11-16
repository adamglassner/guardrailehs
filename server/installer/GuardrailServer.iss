; Guardrail Server for Windows: installs the server as a Windows service (starts with Windows),
; opens port 5080 on private networks and opens the web panel. Uninstall from Settings → Apps (asks whether to
; delete the data too).
;   iscc /DMyAppVersion=0.1.0 /DSourceDir=..\..\dist-server\win-x64 server\installer\GuardrailServer.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\dist-server\win-x64"
#endif
#define MyAppName "Guardrail Server"
#define MyExe "GuardrailServer.exe"
#define Service "GuardrailServer"

[Setup]
AppId={{8E1D4C72-3B5A-4F09-9C6E-2A7B1D0F5E93}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Bytehawk Studio
AppPublisherURL=https://github.com/adamglassner/guardrailehs
DefaultDirName={autopf}\Guardrail Server
DefaultGroupName=Guardrail Server
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist-server
OutputBaseFilename=GuardrailServer-{#MyAppVersion}-Setup
SetupIconFile=..\..\src\Guardrail.Desktop\Assets\guardrail.ico
UninstallDisplayIcon={app}\{#MyExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "firewall"; Description: "Let other computers and tablets on my network connect (open port 5080 on private networks)"

[Dirs]
Name: "{commonappdata}\Guardrail Server"

[Files]
Source: "{#SourceDir}\{#MyExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
Filename: "{group}\Guardrail Server panel.url"; Section: "InternetShortcut"; Key: "URL"; String: "http://localhost:5080"
Filename: "{autodesktop}\Guardrail Server panel.url"; Section: "InternetShortcut"; Key: "URL"; String: "http://localhost:5080"

[UninstallDelete]
Type: files; Name: "{group}\Guardrail Server panel.url"
Type: files; Name: "{autodesktop}\Guardrail Server panel.url"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#Service} binPath= ""\""{app}\{#MyExe}\"""" start= delayed-auto DisplayName= ""{#MyAppName}"""; Flags: runhidden; Check: not ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "description {#Service} ""Sync server for Guardrail EHS. Web panel: http://localhost:5080"""; Flags: runhidden
; The service's environment: where to keep data (set after the service exists)
Filename: "{sys}\reg.exe"; Parameters: "add ""HKLM\SYSTEM\CurrentControlSet\Services\{#Service}"" /v Environment /t REG_MULTI_SZ /d ""GR_DATA={commonappdata}\Guardrail Server"" /f"; Flags: runhidden
; Restart after a crash or a "Restart" from the web panel (exit code 75); "Shut down" in the panel stays stopped
Filename: "{sys}\sc.exe"; Parameters: "failure {#Service} reset= 86400 actions= restart/3000/restart/3000/restart/10000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failureflag {#Service} 1"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Guardrail Server"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Guardrail Server"" dir=in action=allow protocol=TCP localport=5080 profile=private,domain"; Flags: runhidden; Tasks: firewall
Filename: "{sys}\sc.exe"; Parameters: "start {#Service}"; Flags: runhidden
Filename: "http://localhost:5080"; Description: "Open the Guardrail Server web panel"; Flags: postinstall shellexec nowait skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#Service}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\timeout.exe"; Parameters: "/t 3 /nobreak"; Flags: runhidden; RunOnceId: "Wait"
Filename: "{sys}\sc.exe"; Parameters: "delete {#Service}"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Guardrail Server"""; Flags: runhidden; RunOnceId: "Firewall"

[Messages]
FinishedLabel=Guardrail Server is installed and running as a Windows service. It starts with Windows, even before anyone signs in.%n%nYour data is kept in C:\ProgramData\Guardrail Server.

[Code]
function ServiceExists: Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#Service}', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  // Upgrading: stop the running service so its files can be replaced
  if ServiceExists then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#Service}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(3000);
  end;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Data: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{commonappdata}\Guardrail Server');
    if DirExists(Data) and (not UninstallSilent) then
      if MsgBox('Also delete Guardrail Server''s data (accounts, synced records and files) in ' + Data + '?' + #13#10#13#10 +
                'Choose No to keep it, for example to reinstall later.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Data, True, True, True);
  end;
end;
