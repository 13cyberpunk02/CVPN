; Установщик CVPN. Собирается Inno Setup 6: iscc installer\CVPN.iss
; Предполагается, что publish уже выполнен и файлы лежат в installer\payload\

#define AppName "CVPN"
#define AppPublisher "CVPN"
#define AppUrl "https://github.com/13CyberPunk02/CVPN"
#define AppExe "CVPN.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{7C1E4C2A-9F3B-4E28-9C1D-2B5A6F0D8E11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=CVPN-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Program Files обязателен: под SYSTEM запускается sing-box, и каталог
; не должен быть доступен обычному пользователю на запись
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Полная установка"
Name: "compact"; Description: "Только приложение"
Name: "custom"; Description: "Выборочная установка"; Flags: iscustom

[Components]
Name: "app"; Description: "Приложение CVPN"; Types: full compact custom; Flags: fixed
Name: "service"; Description: "Служба туннеля (TUN без запроса прав при каждом запуске)"; Types: full

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app
Source: "payload\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: service

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"
Name: "autostart"; Description: "Запускать вместе с Windows"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "CVPN"; ValueData: """{app}\{#AppExe}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Служба ставится и стартует сразу — установщик уже работает с правами администратора
Filename: "{sys}\sc.exe"; Parameters: "create CVPNTunnel binPath= ""{app}\service\CVPN.Service.exe"" start= auto DisplayName= ""CVPN Tunnel"""; \
    Flags: runhidden; Components: service
Filename: "{sys}\sc.exe"; Parameters: "description CVPNTunnel ""Туннель CVPN: запускает sing-box и поднимает TUN"""; \
    Flags: runhidden; Components: service
Filename: "{sys}\sc.exe"; Parameters: "start CVPNTunnel"; Flags: runhidden; Components: service

Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Порядок важен: сначала остановить, потом удалять файлы
Filename: "{sys}\sc.exe"; Parameters: "stop CVPNTunnel"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete CVPNTunnel"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""CVPN Tunnel"" /f"; Flags: runhidden; RunOnceId: "DeleteTask"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\CVPN"

[Code]
{ Данные пользователя лежат в APPDATA и при удалении сохраняются:
  профили и правила — не мусор, а результат работы. Спрашиваем явно. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\CVPN');

    if DirExists(DataDir) then
      if MsgBox('Удалить профили, правила и настройки CVPN?' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
