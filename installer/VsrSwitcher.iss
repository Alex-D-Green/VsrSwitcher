; VSR Switcher installer, for Inno Setup 6.
;
; Per user: installs to %LOCALAPPDATA%\Programs\VSR Switcher and needs no administrator rights of
; its own, matching the app, which writes only under HKCU. The app relies on the shared .NET 10
; Desktop Runtime; when that is missing the installer offers to download Microsoft's runtime
; installer, which does need administrator rights, once. Built by build\Package.ps1, which passes
;   /DAppVersion=1.2.3 /DFileVersion=1.2.3.0 /DPublishDir=<publish folder>
;   /DOutputDir=<folder> /DOutputName=<file name without .exe>
; The defaults below let the script also be compiled by hand after a publish into artifacts\.
;
; Keep this file ASCII: without a byte-order mark Inno Setup reads it in the system code page.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef OutputName
  #define OutputName "VsrSwitcher-" + AppVersion + "-setup"
#endif

; The runtime is looked for as a version folder matching RuntimeProbe. Override it with a version
; that does not exist (/DRuntimeProbe=99.*) to try the missing-runtime path on a machine that has it.
#ifndef RuntimeProbe
  #define RuntimeProbe "10.*"
#endif
#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define RuntimePage "https://dotnet.microsoft.com/download/dotnet/10.0"

#define AppName "VSR Switcher"
#define AppExe "VsrSwitcher.exe"
#define AppPublisher "Alex-D-Green"
#define AppUrl "https://github.com/Alex-D-Green"

[Setup]
; Identifies the app to Windows across all versions, for upgrades and uninstall. Never change it.
AppId={{D9D9D0B3-3584-4EDB-8E22-8FA59DB4A187}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppCopyright=Copyright (c) 2026 Alex-D-Green
VersionInfoVersion={#FileVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\VsrSwitcher.Tray\Resources\on.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#AppName} with &Windows"
Name: "addtopath"; Description: "Add the &vsrswitch command line to PATH"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; The same entry the tray's "Start with Windows" item writes and reads, so the two always agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VsrSwitcher"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath(ExpandConstant('{app}'))

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close the running tray cleanly first: its files are in use, and killing it would leave a stale icon.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitTray"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ApprovedKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';
  RunValue = 'VsrSwitcher';

var
  DownloadPage: TDownloadWizardPage;

{ The .NET 10 Desktop Runtime, looked for where Microsoft's installer puts it. }
function DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\{#RuntimeProbe}'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

{ Downloads Microsoft's runtime installer and runs it elevated. True when the runtime is then present. }
function InstallDesktopRuntime: Boolean;
var
  Downloaded: Boolean;
  ResultCode: Integer;
begin
  Result := False;
  Downloaded := False;
  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Downloaded := True;
    except
      if not DownloadPage.AbortedByUser then
        MsgBox('The .NET Desktop Runtime could not be downloaded:' + #13#10 + GetExceptionMessage,
          mbError, MB_OK);
    end;
  finally
    DownloadPage.Hide;
  end;

  if Downloaded then
  begin
    { .NET installs for all users, so Windows asks for administrator permission here. }
    if ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'),
         '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      Result := DesktopRuntimeInstalled;
  end;
end;

{ On the Ready page: offer the runtime if it is missing. Silent installs (winget, scripts) never
  download behind the caller's back; the app tells the user what is missing when it starts. }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Answer: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or WizardSilent then
    Exit;
  if DesktopRuntimeInstalled then
    Exit;

  Answer := MsgBox('VSR Switcher needs the .NET 10 Desktop Runtime, which is not installed.' + #13#10#13#10 +
    'Download it from Microsoft (about 60 MB) and install it now? Windows will ask for ' +
    'administrator permission, because .NET is installed for all users.' + #13#10#13#10 +
    'Choose No to install VSR Switcher without it: it will not start until .NET is installed.',
    mbConfirmation, MB_YESNOCANCEL);

  if Answer = IDCANCEL then
    Result := False
  else if Answer = IDYES then
  begin
    if not InstallDesktopRuntime then
      { No line may start with '#': the preprocessor would take it for a directive. }
      Result := MsgBox('The .NET Desktop Runtime was not installed. Install VSR Switcher anyway?' + #13#10#13#10 +
        'You can install .NET later from {#RuntimePage}', mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

{ VSR Switcher changes settings through NVIDIA App's own component, so warn when it is missing. }
function InitializeSetup: Boolean;
begin
  Result := True;
  if not WizardSilent and
     not RegValueExists(HKLM64, 'SOFTWARE\NVIDIA Corporation\Global\NvApp', 'FullPath') then
    Result := MsgBox('NVIDIA App does not seem to be installed.' + #13#10#13#10 +
      'VSR Switcher changes RTX Video Super Resolution through NVIDIA App, so it cannot work ' +
      'without it. Install anyway?', mbConfirmation, MB_YESNO) = IDYES;
end;

{ An upgrade: ask the running tray to close, so its files can be replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

function NeedsAddPath(Dir: String): Boolean;
var
  Path: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then
    Path := '';
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') = 0;
end;

procedure RemoveFromPath(Dir: String);
var
  Path, Padded: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then
    Exit;
  Padded := ';' + Path + ';';
  P := Pos(';' + Uppercase(Dir) + ';', Uppercase(Padded));
  if P = 0 then
    Exit;
  { Drop ';<dir>', keeping the separator that followed it, then the padding. }
  Delete(Padded, P, Length(Dir) + 1);
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Copy(Padded, 2, Length(Padded) - 2));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Also when "Start with Windows" was ticked in the tray menu rather than in the installer. }
    RegDeleteValue(HKCU, RunKey, RunValue);
    RegDeleteValue(HKCU, ApprovedKey, RunValue);
    RemoveFromPath(ExpandConstant('{app}'));
  end;
end;
