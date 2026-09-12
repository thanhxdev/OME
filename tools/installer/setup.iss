; =====================================================================
; OpenMedia SDK - Professional Inno Setup 6 Installer Script
; C++23 High-Performance Media Engine & .NET 10 WPF Client Applications
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\dist\OpenMedia-v" + MyAppVersion + "-Production"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#define MyAppName "OpenMedia SDK"
#define MyAppPublisher "OpenMedia Project"
#define MyAppURL "https://github.com/openmedia/openmedia"
#define MyAppExeName "WpfDemo.exe"
#define MyServerExeName "OpenMediaServer.exe"

[Setup]
; App Identity
AppId={{9F82B8A1-4638-4C82-99B7-D8B31E9F1234}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Destination Directories
DefaultDirName={autopf}\OpenMedia
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no

; Output Settings
OutputDir={#OutputDir}
OutputBaseFilename=OpenMedia_Setup_v{#MyAppVersion}
UninstallDisplayIcon={app}\app\{#MyAppExeName}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; Architecture - Strict 64-bit Native C++23 & .NET 10
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Privileges - Admin required for Firewall registration, VC++ Redist & Program Files
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline

; UI Styling
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "Create Start Menu shortcuts"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 1. All binaries and assets from Staging directory
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. VC++ Redistributable temporary staging for silent installation
Source: "{#SourceDir}\prerequisites\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Check: NeedsVCRedist

[Icons]
; Client App Shortcuts
Name: "{group}\{#MyAppName} Demo"; Filename: "{app}\app\{#MyAppExeName}"; WorkingDir: "{app}\app"; IconFilename: "{app}\app\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName} Demo"; Filename: "{app}\app\{#MyAppExeName}"; WorkingDir: "{app}\app"; IconFilename: "{app}\app\{#MyAppExeName}"; Tasks: desktopicon

; Server / Engine Shortcut
Name: "{group}\{#MyAppName} Server"; Filename: "{app}\bin\{#MyServerExeName}"; WorkingDir: "{app}\bin"; Tasks: startmenuicon

; Uninstaller Shortcut
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Register OpenMedia Server and Installation path in 64-bit Registry
; (Matches OpenMedia.Platform.Internal.ServerDiscovery chain)
Root: HKLM64; Subkey: "Software\OpenMedia"; ValueType: string; ValueName: "ServerPath"; ValueData: "{app}\bin\{#MyServerExeName}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\OpenMedia"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\OpenMedia"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
; 1. Silent install Visual C++ 2015-2022 Redistributable if missing
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/passive /norestart"; Check: NeedsVCRedist; StatusMsg: "Installing Microsoft Visual C++ 2015-2022 Redistributable (x64)..."; Flags: waituntilterminated

; 2. Configure Windows Firewall rules for OpenMedia Server (Inbound SRT, WebRTC, NDI, RTMP)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OpenMedia Server"" dir=in action=allow program=""{app}\bin\{#MyServerExeName}"" enable=yes profile=any description=""Allow incoming media streams for OpenMedia Server (SRT, WebRTC, NDI, RTMP)"""; Flags: runhidden waituntilterminated; StatusMsg: "Configuring Windows Firewall rules for OpenMedia Server..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OpenMedia Server (Client Co-located)"" dir=in action=allow program=""{app}\app\{#MyServerExeName}"" enable=yes profile=any description=""Allow incoming media streams for OpenMedia Server"""; Flags: runhidden waituntilterminated; StatusMsg: "Configuring Windows Firewall rules..."

; 3. Launch Demo Application option on finish
Filename: "{app}\app\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Clean up Windows Firewall rules on uninstall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OpenMedia Server"""; Flags: runhidden; RunOnceId: "DelFirewallRuleServer"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OpenMedia Server (Client Co-located)"""; Flags: runhidden; RunOnceId: "DelFirewallRuleClient"

[UninstallDelete]
; Clean up runtime logs and cache folders
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\crash_dumps"
Type: filesandordirs; Name: "{app}\cache"
Type: files; Name: "{app}\.env"
Type: files; Name: "{app}\bin\.env"
Type: files; Name: "{app}\app\.env"

[Code]
// Kiểm tra xem Visual C++ 2015-2022 Redistributable (x64) đã được cài đặt chưa
function NeedsVCRedist(): Boolean;
var
  Installed: Cardinal;
begin
  Result := True;
  
  // 1. Kiểm tra trong registry 64-bit (Visual Studio 14.0 = VC 2015-2022 runtimes)
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64', 'Installed', Installed) then
  begin
    if Installed = 1 then
    begin
      Result := False;
      Exit;
    end;
  end;
  
  // 2. Fallback kiểm tra trong Wow6432Node
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64', 'Installed', Installed) then
  begin
    if Installed = 1 then
    begin
      Result := False;
      Exit;
    end;
  end;
end;
