#define MyAppName "WideS"
#define MyAppVersion "1.5.6"
#define MyAppPublisher "WideS"
#define MyAppExeName "WideS.exe"
#define MyAppSource "WideS-Setup\app"

[Setup]
AppId={{A7C4E2B1-9F3D-4A8E-B6C1-2D5E8F0A3B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\WideS
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=WideS-Setup
SetupIconFile=..\Assets\WideS.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\WideS.ico"; DestDir: "{app}"; DestName: "WideS-{#MyAppVersion}.ico"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\WideS-{#MyAppVersion}.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\WideS-{#MyAppVersion}.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=Будет установлена программа [name/ver].%n%n.NET Runtime входит в состав установки — отдельно ничего ставить не нужно.%n%nДанные пользователя хранятся отдельно в %%AppData%%\WideS.
