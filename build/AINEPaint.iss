; AINE Paint インストーラー定義（Inno Setup 6）
;
; 事前に build\publish.ps1 を実行して publish\AINEPaint.exe を作っておくこと。
; ビルド:  ISCC.exe build\AINEPaint.iss

#define AppName        "AINE Paint"
#define AppVersion      "0.4.0"
#define AppPublisher    "StudioAINE"
#define AppExeName      "AINEPaint.exe"

[Setup]
AppId={{8F2B5E41-3C7A-4D96-9B18-AINEPAINT0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://ainesoekakiland.github.io/StudioAINE/
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; 管理者権限を求めない。家庭のPCに入れやすくするため。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename=AINEPaint-v{#AppVersion}-Setup
SetupIconFile=..\src\AINEPaint\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作る"; GroupDescription: "追加のタスク:"

[Files]
Source: "..\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; .ainpaint をダブルクリックで開けるようにする
Root: HKCU; Subkey: "Software\Classes\.ainpaint"; ValueType: string; ValueName: ""; ValueData: "AINEPaint.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\AINEPaint.Project"; ValueType: string; ValueName: ""; ValueData: "AINE Paint プロジェクト"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AINEPaint.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\AINEPaint.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#AppExeName}"; Description: "AINE Paint を起動する"; Flags: nowait postinstall skipifsilent
