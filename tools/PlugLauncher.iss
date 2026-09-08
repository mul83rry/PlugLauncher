; Inno Setup script for PlugLauncher.
;
; Built by tools/build-release.ps1, which passes the version and the publish folder in:
;   ISCC.exe /DAppVersion=1.2.0 /DSourceDir=...\publish\stage\PlugLauncher /DOutputDir=...\publish tools\PlugLauncher.iss
;
; The installer is an alternative to the zip, not a replacement: the release carries both.
; It installs per user, so it never needs administrator rights — the same promise the app
; itself makes.

#ifndef AppVersion
  #error AppVersion is required (pass /DAppVersion=x.y.z)
#endif
#ifndef SourceDir
  #error SourceDir is required (pass /DSourceDir=path)
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

#define AppName "PlugLauncher"
#define AppExe "PlugLauncher.exe"
#define AppUrl "https://github.com/mul83rry/PlugLauncher"
#define RuntimeUrl "https://dotnet.microsoft.com/download/dotnet/10.0"

[Setup]
; Never change AppId: it is what lets a new version upgrade an old one in place
; instead of installing beside it.
AppId={{A7D911B9-FFF6-4D62-BD0E-0E01C15D3B99}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Hosein Asadi
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; {autopf} under PrivilegesRequired=lowest is %LOCALAPPDATA%\Programs, so the whole install
; happens without UAC. PlugLauncher writes nothing outside the user profile anyway.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}

; The app holds this mutex while running. Without it, setup would overwrite a running exe
; and the user would keep using the old one until they noticed.
AppMutex=PlugLauncher.SingleInstance

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

LicenseFile=..\LICENSE
SetupIconFile=..\src\PlugLauncher.App\assets\pluglauncher.ico
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Run {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]

{ The app is framework-dependent, so without the runtime it starts and dies with a Windows
  dialog. Better to say so during setup than to let the shortcut look broken.

  The plain runtime, not the Desktop bundle: the interface is drawn by Avalonia now, and that
  needs nothing beyond Microsoft.NETCore.App. Anyone who has the Desktop Runtime has this one
  too, so nobody who could run the old version is turned away. }
function RuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\10.*'), FindRec) then
  try
    repeat
      if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ A warning, not a gate: the runtime can be installed after PlugLauncher just as well. }
function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if RuntimeInstalled then
    Exit;

  if MsgBox('PlugLauncher needs the .NET 10 Runtime (x64), which does not look like it '
          + 'is installed. The app will not start without it.' + #13#10#13#10
          + 'Open the download page? Setup will carry on either way, and you can install the '
          + 'runtime afterwards.', mbConfirmation, MB_YESNO) = IDYES then
    ShellExec('open', '{#RuntimeUrl}', '', '', SW_SHOW, ewNoWait, ErrorCode);
end;

{ The app writes its own Run value when "Start with Windows" is ticked. Only remove it if it
  points into the folder being uninstalled — a copy running from somewhere else keeps working. }
procedure RemoveStartupEntry;
var
  Value: String;
begin
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PlugLauncher', Value) then
    Exit;

  if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) = 0 then
    Exit;

  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PlugLauncher');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  RemoveStartupEntry;

  { Settings, usage stats and every plugin installed from the store live here. Removing it is
    not part of uninstalling the program, so it is asked rather than assumed. }
  DataDir := ExpandConstant('{userappdata}\PlugLauncher');
  if not DirExists(DataDir) then
    Exit;

  if MsgBox('Also delete your PlugLauncher settings, installed plugins and logs?' + #13#10#13#10
          + DataDir, mbConfirmation, MB_YESNO) = IDYES then
    DelTree(DataDir, True, True, True);
end;
