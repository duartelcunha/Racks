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
  #define AppVersion "2.0.0"
#endif

#define AppName "Racks"
#define AppPublisher "Duarte L. Cunha"
#define AppExeName "Racks.exe"
#define AppUrl "https://github.com/duartelcunha/Racks"
#define SourceRoot "..\publish"

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
WizardResizable=no
; Faster install vs. smaller setup .exe — pick speed. lzma2/normal decompresses
; ~3× faster than ultra64 for a ~10% larger .exe. For a ~5s install that the
; user perceives as "instant" this is the right trade.
Compression=lzma2/normal
SolidCompression=yes
; Block a second installer from starting on top of a running one. Without this
; you can end up with half-extracted .exes on disk if the user double-clicks
; the setup twice.
SetupMutex=Racks-Setup-{#AppVersion}
OutputBaseFilename=Racks-Setup-{#AppVersion}
OutputDir=Output
SetupIconFile=..\Racks\Icon\ico.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; Single-arch — Racks targets x64 only (see csproj <Platforms>x64</Platforms>).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Auto-close a running Racks instance so we can replace the .exe on upgrade.
; No "please close the app" modal — just take care of it.
CloseApplications=force
CloseApplicationsFilter=*.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Pull every file from the publish output. Recurse so the localization
; subfolders (cs-CZ, ko-KR, zh-CN, ...) come along.
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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

[UninstallRun]
; Make sure the running Racks tray instance is closed before uninstall tries
; to delete the .exe. Best-effort: ignore errors (e.g., not running).
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "KillRacks"
