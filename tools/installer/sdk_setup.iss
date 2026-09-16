; =====================================================================
; OpenMedia SDK - Professional Inno Setup 6 Installer Script
; Modular SDK Runtime Architecture: C++23 Media Engine & Core Libraries
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\dist\sdk_staging"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "OpenMedia_SDK_Setup_v" + MyAppVersion
#endif

#define MyAppName "OpenMedia SDK"
#define MyAppPublisher "OpenMedia Project"
#define MyAppURL "https://github.com/openmedia/openmedia"
#define MyServerExeName "OpenMediaServer.exe"
#define MySdkGuid "{{4A35BD80-5C28-4BF2-824A-4C2E9A9D1234}}"

[Setup]
; App Identity
AppId={#MySdkGuid}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Destination Directories
DefaultDirName={autopf}\OpenMedia\SDK
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no

; Output Settings
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
UninstallDisplayIcon={app}\bin\{#MyServerExeName}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; Architecture - Strict 64-bit Native C++23
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Privileges - Admin required for Registry HKLM, Firewall rules & Program Files
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline

; Environment broadcast on install/uninstall
ChangesEnvironment=yes

; UI Styling
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenuicon"; Description: "Create Start Menu shortcuts"; GroupDescription: "Additional Icons"

[Files]
; 1. All SDK binaries, dependencies, wrappers, plugins and templates from staging
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. VC++ Redistributable temporary staging for silent installation
Source: "{#SourceDir}\prerequisites\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Check: NeedsVCRedist

[Icons]
; Server / Engine Shortcut in Start Menu
Name: "{group}\{#MyAppName} Server"; Filename: "{app}\bin\{#MyServerExeName}"; WorkingDir: "{app}\bin"; Tasks: startmenuicon

; Uninstaller Shortcut
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; 1. Primary Modular SDK Registry Key (Required for Application Pre-requisite checks)
Root: HKLM64; Subkey: "Software\OpenMedia\SDK"; ValueType: dword; ValueName: "Installed"; ValueData: "1"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\OpenMedia\SDK"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\OpenMedia\SDK"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

; 2. Compatibility Key for ServerDiscovery and legacy path queries
Root: HKLM64; Subkey: "Software\OpenMedia"; ValueType: string; ValueName: "ServerPath"; ValueData: "{app}\bin\{#MyServerExeName}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\OpenMedia"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\OpenMedia"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

; 3. System Environment Variable: OPENMEDIA_SDK_DIR (pointing to {app})
Root: HKLM64; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "OPENMEDIA_SDK_DIR"; ValueData: "{app}"; Flags: uninsdeletevalue

[Run]
; 1. Silent install Visual C++ 2015-2022 Redistributable if missing
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/passive /norestart"; Check: NeedsVCRedist; StatusMsg: "Installing Microsoft Visual C++ 2015-2022 Redistributable (x64)..."; Flags: waituntilterminated

; 2. Configure Windows Firewall rules for OpenMedia Server (Inbound SRT, WebRTC, NDI)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OpenMedia Server"" dir=in action=allow program=""{app}\bin\{#MyServerExeName}"" enable=yes profile=any description=""Allow incoming media streams for OpenMedia Server (SRT, WebRTC, NDI)"""; Flags: runhidden waituntilterminated; StatusMsg: "Configuring Windows Firewall rules for OpenMedia Server..."

; 3. Configure Windows Firewall rules for FFmpeg (Transcoding, preview, streaming)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OpenMedia FFmpeg"" dir=in action=allow program=""{app}\bin\ffmpeg.exe"" enable=yes profile=any description=""Allow media streams for FFmpeg in OpenMedia"""; Flags: runhidden waituntilterminated; StatusMsg: "Configuring Windows Firewall rules for FFmpeg..."

[UninstallRun]
; Clean up Windows Firewall rules on uninstall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OpenMedia Server"""; Flags: runhidden; RunOnceId: "DelFirewallRuleServer"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OpenMedia FFmpeg"""; Flags: runhidden; RunOnceId: "DelFirewallRuleFFmpeg"

[UninstallDelete]
; Clean up runtime logs and cache folders
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\crash_dumps"
Type: filesandordirs; Name: "{app}\cache"
Type: files; Name: "{app}\.env"
Type: files; Name: "{app}\bin\.env"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

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

// Thêm {app}\bin vào biến môi trường PATH của hệ thống
procedure AddToPath(PathToAdd: string);
var
  CurrentPath: string;
  NewPath: string;
begin
  if not RegQueryStringValue(HKLM64, EnvironmentKey, 'Path', CurrentPath) then
    CurrentPath := '';

  if Pos(';' + Uppercase(PathToAdd) + ';', ';' + Uppercase(CurrentPath) + ';') = 0 then
  begin
    if (CurrentPath <> '') and (CurrentPath[Length(CurrentPath)] <> ';') then
      NewPath := CurrentPath + ';' + PathToAdd
    else
      NewPath := CurrentPath + PathToAdd;

    RegWriteStringValue(HKLM64, EnvironmentKey, 'Path', NewPath);
  end;
end;

// Gỡ {app}\bin khỏi biến môi trường PATH khi gỡ cài đặt
procedure RemoveFromPath(PathToRemove: string);
var
  CurrentPath: string;
  P, L: Integer;
begin
  if RegQueryStringValue(HKLM64, EnvironmentKey, 'Path', CurrentPath) then
  begin
    P := Pos(';' + Uppercase(PathToRemove) + ';', ';' + Uppercase(CurrentPath) + ';');
    if P > 0 then
    begin
      L := Length(PathToRemove);
      if P = 1 then
      begin
        Delete(CurrentPath, 1, L);
        if (Length(CurrentPath) > 0) and (CurrentPath[1] = ';') then
          Delete(CurrentPath, 1, 1);
      end
      else
      begin
        Delete(CurrentPath, P - 1, L + 1);
      end;
      RegWriteStringValue(HKLM64, EnvironmentKey, 'Path', CurrentPath);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AddToPath(ExpandConstant('{app}\bin'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveFromPath(ExpandConstant('{app}\bin'));
  end;
end;
