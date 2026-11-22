; Perch installer (Inno Setup 6.3 or newer).
; Build it with tools\build-installer.ps1, which publishes the app first and passes Source and OutDir.

#ifndef Source
  #define Source AddBackslash(SourcePath) + "..\obj\installer\app"
#endif
#ifndef OutDir
  #define OutDir AddBackslash(SourcePath) + "..\publish"
#endif

; The version comes from the published exe, so it cannot drift from the app.
#define Major
#define Minor
#define Rev
#define Build
#expr GetVersionComponents(AddBackslash(Source) + "Perch.exe", Major, Minor, Rev, Build)
#if Major == 0 && Minor == 0 && Rev == 0
  #error Perch.exe was not found in Source, or it has no version. Run tools\build-installer.ps1.
#endif
#define AppVersion Str(Major) + "." + Str(Minor) + "." + Str(Rev)

#define AppName "Perch"
#define AppExe "Perch.exe"
#define AppUrl "https://github.com/haych-dev/Perch"

[Setup]
; Never change AppId: it is how a newer installer finds and upgrades an older install.
AppId={{77837950-47B7-4FCF-B085-C3DE75543FF2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=haych-dev
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

; Perch hides the taskbar for the person signed in, so it installs per user, without an administrator prompt.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; A running Perch is asked to quit (and put the taskbar back) in [Code], so Restart Manager isn't needed.
CloseApplications=no

SetupIconFile=..\Assets\perch.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern dynamic
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutDir}
OutputBaseFilename={#AppName}-{#AppVersion}-win-x64-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; The same setting as "Start with Windows" in Perch's Settings. A dock is most useful from sign-in, so it is on by default.
Name: "autostart"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Start-up:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{#AppName} - restore the taskbar"; Filename: "{app}\{#AppExe}"; Parameters: "--restore-taskbar"; Comment: "Puts the Windows taskbar back if it stays hidden after Perch closes"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Written exactly as Perch writes it. Removal is handled in [Code], because the switch in Settings can also create it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

{ Asks a running Perch to quit and waits for it, so files can be replaced and the taskbar is back. }
procedure QuitPerch(Exe: String);
var
  Code: Integer;
begin
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  QuitPerch(ExpandConstant('{app}\{#AppExe}'));
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  QuitPerch(ExpandConstant('{app}\{#AppExe}'));
  Result := True;
end;

{ Removes "Start with Windows" only when it points at the copy being uninstalled. }
procedure RemoveAutoStart;
var
  Value: String;
begin
  if RegQueryStringValue(HKCU, RunKey, '{#AppName}', Value) and
     (Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0) then
    RegDeleteValue(HKCU, RunKey, '{#AppName}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Belt and braces: make sure the taskbar is visible before the exe is removed. }
    Exec(ExpandConstant('{app}\{#AppExe}'), '--restore-taskbar', '', SW_HIDE, ewWaitUntilTerminated, Code);
    RemoveAutoStart;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Settings, pins, to-dos and notes are kept unless the user asks otherwise. A silent uninstall always keeps them. }
    Data := ExpandConstant('{userappdata}\{#AppName}');
    if DirExists(Data) and not UninstallSilent then
      if MsgBox('Also delete your {#AppName} settings, pinned apps, to-dos and notes?' + #13#10#13#10 +
                'Choose No to keep them for a later install.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Data, True, True, True);
  end;
end;
