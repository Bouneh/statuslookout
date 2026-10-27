; Official per-user installer. Application data lives separately and is never removed.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef Runtime
  #error Runtime is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef NoticeDir
  #error NoticeDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{750FBCDF-6BB7-4485-AF93-3F6A1BB2DD59}
AppName=StatusLookout
AppVersion={#AppVersion}
AppPublisher=LAMOUREUX Fabien (Bouneh)
AppPublisherURL=https://github.com/Bouneh/statuslookout
AppSupportURL=https://github.com/Bouneh/statuslookout/issues
AppUpdatesURL=https://github.com/Bouneh/statuslookout/releases/latest
DefaultDirName={localappdata}\Programs\StatusLookout
DefaultGroupName=StatusLookout
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
MinVersion=10.0.19041
AppMutex=Local\StatusLookout.Instance
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\StatusLookout.exe
SetupIconFile=..\src\StatusLookout.Windows\Assets\app.ico
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=StatusLookout-Setup-{#Runtime}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=none
#if Runtime == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Runtime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif Runtime == "win-x86"
ArchitecturesAllowed=x86compatible
#else
  #error Unsupported runtime
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
french.DesktopShortcut=Créer un raccourci sur le Bureau
english.StartupTask=Start StatusLookout when I sign in to Windows
french.StartupTask=Lancer StatusLookout à l’ouverture de session Windows
english.LaunchApp=Launch StatusLookout
french.LaunchApp=Lancer StatusLookout

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\StatusLookout.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#NoticeDir}\LICENSE*"; DestDir: "{app}\Licenses"; Flags: ignoreversion
Source: "{#NoticeDir}\THIRD-PARTY-NOTICES*"; DestDir: "{app}\Licenses"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\StatusLookout"; Filename: "{app}\StatusLookout.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\StatusLookout"; Filename: "{app}\StatusLookout.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StatusLookout"; ValueData: """{app}\StatusLookout.exe"" --background"; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "StatusLookout"; Flags: deletevalue; Tasks: not startup

[Run]
Filename: "{app}\StatusLookout.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function HasTaskOverride: Boolean;
var
  I: Integer;
  Argument: String;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    Argument := Uppercase(ParamStr(I));
    if (Pos('/TASKS=', Argument) = 1) or (Pos('/MERGETASKS=', Argument) = 1) then
      Result := True;
  end;
end;

procedure InitializeWizard;
var
  Command: String;
begin
  { The app can change startup after installation. Its current setting takes precedence
    over a stale task selection saved by an older installer. Silent overrides still work. }
  if not HasTaskOverride then
  begin
    if RegQueryStringValue(HKCU, RunKey, 'StatusLookout', Command) and (Command <> '') then
      WizardSelectTasks('startup')
    else
      WizardSelectTasks('!startup');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, 'StatusLookout', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\StatusLookout.exe') + '" --background') = 0 then
        RegDeleteValue(HKCU, RunKey, 'StatusLookout');
  { Preferences, monitoring history, custom services and sounds are deliberately retained. }
end;
