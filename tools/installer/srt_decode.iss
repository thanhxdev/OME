; =====================================================================
; OpenMedia - SRT_DECODE Inno Setup 6 Script
; Modular Client Application (.NET 10 Self-Contained win-x64)
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "SRT_DECODE"
#define MyAppExeName "SRT_DECODE.exe"
#define MyAppPublisher "OpenMedia Project"
#define MyAppURL "https://github.com/openmedia/openmedia"
#define AppGuid "{{E7D91370-7AA2-4E7D-92B2-32C92E456789}}"

#ifndef SourceDir
  #define SourceDir "..\..\dist\apps\SRT_DECODE"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\dist\installers"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "SRT_DECODE_Setup"
#endif

[Setup]
; App Identity
AppId={#AppGuid}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Destination Directories
DefaultDirName={autopf}\OpenMedia\Apps\{#MyAppName}
DefaultGroupName=OpenMedia\{#MyAppName}
DisableProgramGroupPage=no

; Output Settings
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
UninstallDisplayIcon={app}\{#MyAppExeName}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; Architecture - Strict 64-bit Native .NET 10
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Privileges - Admin required for Program Files
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline

; UI Styling
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Create Start Menu shortcuts"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 1. All binaries and assets from published application staging directory
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Client Application Shortcuts
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

; Uninstaller Shortcut
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
; Launch Application option upon setup completion
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up application logs and caches on uninstall (leaves shared SDK intact)
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\crash_dumps"
Type: filesandordirs; Name: "{app}\cache"

[Code]
// =====================================================================
// Pre-requisite Check: Đảm bảo OpenMedia SDK đã được cài đặt trước
// =====================================================================
function InitializeSetup(): Boolean;
var
  Installed: Cardinal;
  SdkFound: Boolean;
  SdkPath: String;
begin
  SdkFound := False;
  
  // 1. Kiểm tra trong Registry 64-bit: HKLM64\Software\OpenMedia\SDK
  if RegQueryDWordValue(HKLM64, 'Software\OpenMedia\SDK', 'Installed', Installed) then
  begin
    if Installed = 1 then
      SdkFound := True;
  end;

  // 2. Fallback kiểm tra HKLM (32-bit registry)
  if (not SdkFound) and RegQueryDWordValue(HKLM, 'Software\OpenMedia\SDK', 'Installed', Installed) then
  begin
    if Installed = 1 then
      SdkFound := True;
  end;

  // 3. Fallback kiểm tra HKCU\Software\OpenMedia\SDK (nếu cài đặt dưới quyền user)
  if (not SdkFound) and RegQueryDWordValue(HKCU, 'Software\OpenMedia\SDK', 'Installed', Installed) then
  begin
    if Installed = 1 then
      SdkFound := True;
  end;

  // Nếu chưa cài đặt OpenMedia SDK: Cảnh báo người dùng và hủy ngay quá trình cài đặt
  if not SdkFound then
  begin
    SuppressibleMsgBox('Ứng dụng yêu cầu OpenMedia SDK phải được cài đặt trước trên máy tính này.' + #13#10#13#10 +
                      'Vui lòng cài đặt gói OpenMedia_SDK_Setup.exe trước khi tiến hành cài đặt ứng dụng!',
                      mbCriticalError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  // Lấy đường dẫn cài đặt SDK (nếu có) để ghi nhận
  if RegQueryStringValue(HKLM64, 'Software\OpenMedia\SDK', 'Path', SdkPath) then
  begin
    Log('Detected OpenMedia SDK Path: ' + SdkPath);
  end;

  Result := True;
end;
