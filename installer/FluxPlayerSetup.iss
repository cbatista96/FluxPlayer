; FluxPlayer installer source. Build locally with Inno Setup 6 or 7.

#define MyAppName "FluxPlayer"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "FluxPlayer"
#define MyAppURL "https://github.com/cbatista96/FluxPlayer"
#define MyAppExeName "FluxPlayer.exe"
#define PublishDir "..\publish\FluxPlayer"

[Setup]
AppId={{B3F8A2D1-7C4E-4A9B-8D5F-1E2C3A4B5D6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\installer\output
OutputBaseFilename=FluxPlayerSetup-{#MyAppVersion}-x64
SetupIconFile=..\FluxPlayer\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
ChangesAssociations=yes
CloseApplications=yes
WizardStyle=modern
WizardSizePercent=120
; Use Inno Setup's standard wizard artwork; the application icon is set above.
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} installer
AppCopyright=Copyright (C) {#MyAppPublisher}
RestartApplications=no

[Languages]

Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Register FluxPlayer for media files"; GroupDescription: "File associations:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
#dim VideoExt[16] {".mkv", ".mp4", ".avi", ".wmv", ".webm", ".mov", ".flv", ".m4v", ".ts", ".mpg", ".mpeg", ".3gp", ".m2ts", ".mts", ".vob", ".ogv"}
#dim AudioExt[9] {".mp3", ".flac", ".ogg", ".wav", ".aac", ".m4a", ".wma", ".opus", ".mka"}
#define I
#sub RegVideo
Root: HKA; Subkey: "Software\Classes\{#VideoExt[I]}\OpenWithProgids"; ValueType: string; ValueName: "FluxPlayer.Video"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: "{#VideoExt[I]}"; ValueData: "FluxPlayer.Video"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: "{#VideoExt[I]}"; ValueData: ""; Tasks: fileassoc
#endsub
#sub RegAudio
Root: HKA; Subkey: "Software\Classes\{#AudioExt[I]}\OpenWithProgids"; ValueType: string; ValueName: "FluxPlayer.Audio"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: "{#AudioExt[I]}"; ValueData: "FluxPlayer.Audio"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: "{#AudioExt[I]}"; ValueData: ""; Tasks: fileassoc
#endsub
#for {I = 0; I < DimOf(VideoExt); I++} RegVideo
#for {I = 0; I < DimOf(AudioExt); I++} RegAudio
Root: HKA; Subkey: "Software\{#MyAppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Modern video and audio player"; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "Software\{#MyAppName}\Capabilities"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\FluxPlayer.Video";                  ValueType: string; ValueName: ""; ValueData: "Video file - FluxPlayer"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\FluxPlayer.Video\DefaultIcon";      ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\FluxPlayer.Video\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\FluxPlayer.Audio";                  ValueType: string; ValueName: ""; ValueData: "Audio file - FluxPlayer"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\FluxPlayer.Audio\DefaultIcon";      ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\FluxPlayer.Audio\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

[InstallDelete]
Type: files; Name: "{app}\libvlc\win-x64\plugins\plugins.dat"

[UninstallDelete]
Type: files; Name: "{app}\libvlc\win-x64\plugins\plugins.dat"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--build-plugin-cache"; StatusMsg: "Optimizing {#MyAppName} startup..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox('Also remove your FluxPlayer settings, bookmarks and playlists?' + #13#10 +
              'Choose No to keep them for a future installation.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\FluxPlayer'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\FluxPlayer'), True, True, True);
    end;
  end;
end;
