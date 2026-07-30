; Inno Setup 6 用のインストーラー定義。Release publish の出力を配布対象にする。
#define MyAppName "Overlay Browser"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Overlay Browser"
#define MyAppExeName "OverlayBrowser.exe"

[Setup]
AppId={{E4A9E4A7-23A9-4AFE-9D1D-FB794538D8A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=OverlayBrowserSetup
; セットアップ本体とアンインストール一覧は、アプリ本体と同じガラス窓ICOを使う。
SetupIconFile=..\OverlayBrowser\Assets\OverlayBrowser.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; CefSharpのネイティブファイルに合わせ、64bit Windowsだけを対象にする。
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\OverlayBrowser\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
