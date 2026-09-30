#define MyAppName "WideS"
#define MyAppVersion "1.7.1"
#define MyAppPublisher "WideS"
#define MyAppExeName "WideS.exe"
#define MyAppSource "WideS-Setup\app"

[Setup]
; Тот же AppId, что и раньше — иначе обновление не найдёт старую установку
AppId={{A7C4E2B1-9F3D-4A8E-B6C1-2D5E8F0A3B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\WideS
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; При обновлении не спрашивать папку заново — ставим поверх
DisableDirPage=auto
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
UsePreviousSetupType=yes
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
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
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\WideS.ico"; DestDir: "{app}"; DestName: "WideS.ico"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\WideS.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\WideS.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=Будет установлена программа [name/ver].%n%n.NET Runtime входит в состав установки — отдельно ничего ставить не нужно.%n%nДанные пользователя хранятся отдельно в %%AppData%%\WideS.%n%nЕсли WideS уже установлен, этот мастер просто обновит программу поверх — удалять ничего не нужно.

[Code]
function UninstallKey: String;
begin
  // Same AppId uninstall key as previous installs (HKCU for PrivilegesRequired=lowest)
  Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' +
            'A7C4E2B1-9F3D-4A8E-B6C1-2D5E8F0A3B7C}_is1';
end;

function IsUpgrade: Boolean;
var
  S: String;
begin
  Result :=
    RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', S) or
    RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', S) or
    RegQueryStringValue(HKLM,
      'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{' +
      'A7C4E2B1-9F3D-4A8E-B6C1-2D5E8F0A3B7C}_is1',
      'UninstallString', S);
end;

procedure InitializeWizard;
begin
  if IsUpgrade then
  begin
    WizardForm.WelcomeLabel1.Caption := 'Обновление WideS';
    WizardForm.WelcomeLabel2.Caption :=
      'Будет обновлена уже установленная программа WideS до версии {#MyAppVersion}.'#13#10#13#10 +
      'Удалять старую версию не нужно — установка идёт поверх.'#13#10#13#10 +
      'Ваши данные (%AppData%\WideS) сохранятся.'#13#10#13#10 +
      'Если WideS сейчас запущен, мастер предложит его закрыть.';
  end;
end;
