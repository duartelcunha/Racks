; Racks installer — Inno Setup 6 script.
;
; Design brief: zero-choice, no-UAC, professional-looking installer.
; The user double-clicks Racks-Setup-x.y.z.exe and after a brief progress bar
; Racks is installed under %LocalAppData%\Programs\Racks and launched. No
; directory picker, no component selection, no "are you sure?" page. The
; "Run Racks" checkbox on the finished page is on by default; everything else
; is suppressed via Disable*Page directives.
;
; AppVersion is passed in by build-installer.ps1 via /DAppVersion=... so the
; csproj's <AssemblyVersion> stays the single source of truth. Defaults to a
; placeholder so the script can also be compiled by hand from the Inno Setup
; IDE during development.

#ifndef AppVersion
  #define AppVersion "0.8.0"
#endif

#define AppName "Racks"
#define AppPublisher "Duarte L. Cunha"
#ifndef AppExeName
  #define AppExeName "Racks.exe"
#endif
#define AppUrl "https://github.com/duartelcunha/Racks"
#ifndef SourceRoot
  #define SourceRoot "..\publish"
#endif

[Setup]
; Stable, unique-to-Racks GUID. Don't change this — it identifies the install
; in Windows Apps & Features. Changing it would create a duplicate entry on
; upgrade instead of replacing the previous version.
AppId={{F7C9A3B2-4D8E-4F0A-9C5E-7B3D6A1E8F2C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={userpf}\{#AppName}
DefaultGroupName={#AppName}
; Per-user install — no admin prompt, no UAC. Matches VS Code / Slack model.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Suppress every page that asks the user a question.
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableReadyMemo=yes
; Keep the finished page (so the "Run Racks" checkbox shows) but minimal.
DisableFinishedPage=no
ShowLanguageDialog=no
WizardStyle=modern
; Faster install vs. smaller setup .exe — pick speed. lzma2/normal decompresses
; ~3× faster than ultra64 for a ~10% larger .exe. For a ~5s install that the
; user perceives as "instant" this is the right trade.
Compression=lzma2/normal
SolidCompression=yes
; Block a second installer from starting on top of a running one. Without this
; you can end up with half-extracted .exes on disk if the user double-clicks
; the setup twice.
SetupMutex=Racks-Setup
OutputBaseFilename=Racks-Setup-{#AppVersion}
OutputDir=Output
SetupIconFile=..\Racks\Icon\ico.ico
WizardSmallImageFile=..\Racks\Icon\logo_small.bmp
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; Older releases recorded recursive AppData deletion and registry cleanup.
; Appending would preserve those destructive entries even after an upgrade.
; Reset that log; this full installer records every current application file.
UninstallLogMode=overwrite
; Single-arch — Racks targets x64 only (see csproj <Platforms>x64</Platforms>).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; An active file operation must never be terminated by an installer.
AppMutex=Racks-SingleInstance-2C9D
CloseApplications=no
CloseApplicationsFilter=*.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Pull every file from the publish output. Recurse so the localization
; subfolders (cs-CZ, ko-KR, zh-CN, ...) come along.
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#if AppExeName != "Racks.exe"
[InstallDelete]
; Exact obsolete files from the published WPF package. Never wildcard-delete
; the installation folder or traverse any user-data directory during upgrade.
Type: files; Name: "{app}\Racks.exe"
Type: files; Name: "{app}\Racks.pdb"
Type: files; Name: "{app}\Racks.dll"
Type: files; Name: "{app}\Racks.deps.json"
Type: files; Name: "{app}\Racks.runtimeconfig.json"
Type: files; Name: "{app}\wpfgfx_cor3.dll"
Type: files; Name: "{app}\vcruntime140_cor3.dll"
Type: files; Name: "{app}\PresentationNative_cor3.dll"
Type: files; Name: "{app}\PenImc_cor3.dll"
Type: files; Name: "{app}\LdaNative.dll"
Type: files; Name: "{app}\D3DCompiler_47_cor3.dll"
Type: files; Name: "{app}\Icon\WorkspaceFolder.ico"
#endif

[Icons]
; Start menu shortcut only by default. Desktop shortcut is intentionally
; omitted to keep the "no choices" promise — Racks lives in the tray, the
; user doesn't need a desktop icon for it.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
; Launch Racks on the finished page (checkbox on, no second click needed).
; nowait + skipifsilent + postinstall so an /SILENT install just runs it.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

; App files are removed by Inno's installation log. User files, settings and
; recovery records are deliberately retained. Never recursively remove AppData.
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Racks"; Flags: dontcreatekey uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DesktopRacks"; Flags: dontcreatekey uninsdeletevalue

#if AppExeName != "Racks.exe"
[Code]
procedure UpdateExistingStartup(const ValueName: String);
var
  Existing, OldExecutable, NewExecutable: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', ValueName, Existing) then
  begin
    OldExecutable := ExpandConstant('{app}\Racks.exe');
    NewExecutable := ExpandConstant('{app}\{#AppExeName}');
    if (CompareText(Existing, OldExecutable) = 0) or
       (CompareText(Existing, '"' + OldExecutable + '"') = 0) then
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', ValueName, '"' + NewExecutable + '"');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    UpdateExistingStartup('Racks');
    UpdateExistingStartup('DesktopRacks');
  end;
end;
#endif
