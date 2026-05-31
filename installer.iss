[Setup]
AppId={{B7E4F2A1-3C5D-4A8B-9F1E-6D2C8A7B5E3F}
AppName=PhotoVideoScreensaver
AppVersion=2.5.8
AppPublisher=PhotoVideoScreensaver
DefaultDirName={autopf}\PhotoVideoScreensaver
DefaultGroupName=PhotoVideoScreensaver
OutputDir=C:\Temp\pvss
OutputBaseFilename=PhotoVideoScreensaver_2.5.8_setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=PhotoVideoScreensaver\feather_with_shadow.ico
UninstallDisplayIcon={app}\PhotoVideoScreensaver.scr
DisableProgramGroupPage=yes
WizardStyle=modern

[Files]
Source: "PhotoVideoScreensaver\bin\Release\PhotoVideoScreensaver.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\PhotoVideoScreensaver.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\LibVLCSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\LibVLCSharp.WPF.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\System.Buffers.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\System.Memory.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\System.Numerics.Vectors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "PhotoVideoScreensaver\bin\Release\libvlc\win-x64\*"; DestDir: "{app}\libvlc\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "PhotoVideoScreensaver\bin\Release\libvlc\win-x86\*"; DestDir: "{app}\libvlc\win-x86"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Configure PhotoVideoScreensaver"; Filename: "{app}\PhotoVideoScreensaver.exe"; Parameters: "/c"
Name: "{group}\Uninstall PhotoVideoScreensaver"; Filename: "{uninstallexe}"

[Registry]
Root: HKU; Subkey: ".DEFAULT\Control Panel\Desktop"; ValueType: string; ValueName: "SCRNSAVE.EXE"; ValueData: "{app}\PhotoVideoScreensaver.scr"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Control Panel\Desktop"; ValueType: string; ValueName: "SCRNSAVE.EXE"; ValueData: "{app}\PhotoVideoScreensaver.scr"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\PhotoVideoScreensaver.exe"; Parameters: "/c"; Description: "Configure screensaver now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "reg.exe"; Parameters: "delete ""HKCU\Control Panel\Desktop"" /v SCRNSAVE.EXE /f"; Flags: runhidden; RunOnceId: "ClearScreensaver"
Filename: "reg.exe"; Parameters: "delete ""HKU\.DEFAULT\Control Panel\Desktop"" /v SCRNSAVE.EXE /f"; Flags: runhidden; RunOnceId: "ClearScreensaverDefault"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRegistry]
Root: HKCU; Subkey: "Software\VideoScreensaver"; Flags: deletekey

[Code]
const WS_EX_TOPMOST = $00000008;
  GWL_EXSTYLE = -20;

function GetWindowLong(hWnd: Integer; nIndex: Integer): Integer; external 'GetWindowLongW@user32.dll stdcall';
function SetWindowLong(hWnd: Integer; nIndex: Integer; dwNewLong: Integer): Integer; external 'SetWindowLongW@user32.dll stdcall';
function SetWindowPos(hWnd: Integer; hWndInsertAfter: Integer; X, Y, cx, cy: Integer; uFlags: Integer): Boolean; external 'SetWindowPos@user32.dll stdcall';

procedure InitializeWizard();
var
  exStyle: Integer;
begin
  exStyle := GetWindowLong(WizardForm.Handle, GWL_EXSTYLE);
  SetWindowLong(WizardForm.Handle, GWL_EXSTYLE, exStyle or WS_EX_TOPMOST);
  SetWindowPos(WizardForm.Handle, -1, 0, 0, 0, 0, 3);
end;
procedure CurStepChanged(CurStep: TSetupStep);
var
  ScrPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    ScrPath := ExpandConstant('{app}\PhotoVideoScreensaver.scr');
    CopyFile(ExpandConstant('{app}\PhotoVideoScreensaver.exe'), ScrPath, False);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SubkeyNames: TArrayOfString;
  I: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // 1. Delete registry settings from Current User (HKCU)
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\VideoScreensaver');

    // 2. Delete registry settings from all loaded profiles in HKEY_USERS (HKU)
    if RegGetSubkeyNames(HKEY_USERS, '', SubkeyNames) then
    begin
      for I := 0 to GetArrayLength(SubkeyNames) - 1 do
      begin
        if Pos('_Classes', SubkeyNames[I]) = 0 then
        begin
          RegDeleteKeyIncludingSubkeys(HKEY_USERS, SubkeyNames[I] + '\Software\VideoScreensaver');
        end;
      end;
    end;

    // 3. Delete error log from Documents folder
    DeleteFile(ExpandConstant('{userdocs}\PhotoVideoScreensaver_error.log'));
  end;
end;
