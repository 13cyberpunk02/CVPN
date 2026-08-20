#define AppName "CVPN"
#define AppPublisher "CVPN"
#define AppUrl "https://github.com/OWNER/CVPN"
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
Source: "payload\*"; DestDir: "{app}"; Excludes: "service\*"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; Components: app
Source: "payload\service\*"; DestDir: "{app}\service"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; Components: service

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
#ifdef NeedsRuntime
Filename: "{tmp}\windowsdesktop-runtime.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Установка среды .NET…"; Check: not DesktopRuntimeInstalled; Flags: waituntilterminated
#endif

Filename: "{sys}\sc.exe"; Parameters: "create CVPNTunnel binPath= ""{app}\service\CVPN.Service.exe"" start= auto DisplayName= ""CVPN Tunnel"""; \
    Flags: runhidden; Components: service
Filename: "{sys}\sc.exe"; Parameters: "description CVPNTunnel ""Туннель CVPN: запускает sing-box и поднимает TUN"""; \
    Flags: runhidden; Components: service
Filename: "{sys}\sc.exe"; Parameters: "start CVPNTunnel"; Flags: runhidden; Components: service

Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop CVPNTunnel"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete CVPNTunnel"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""CVPN Tunnel"" /f"; Flags: runhidden; RunOnceId: "DeleteTask"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\CVPN"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function GetSubDirsFromDir(const Dir: string; var Names: TArrayOfString): Boolean;
var
  Rec: TFindRec;
  Count: Integer;
begin
  Count := 0;
  SetArrayLength(Names, 0);
  Result := False;

  if FindFirst(AddBackslash(Dir) + '*', Rec) then
  try
    repeat
      if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0)
         and (Rec.Name <> '.') and (Rec.Name <> '..') then
      begin
        SetArrayLength(Names, Count + 1);
        Names[Count] := Rec.Name;
        Count := Count + 1;
      end;
    until not FindNext(Rec);

    Result := Count > 0;
  finally
    FindClose(Rec);
  end;
end;

function DesktopRuntimeInstalled: Boolean;
var
  Names: TArrayOfString;
  Base: string;
  I: Integer;
begin
  Result := False;
  Base := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if not DirExists(Base) then Exit;

  if GetSubDirsFromDir(Base, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('10.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

#ifdef NeedsRuntime
  if (CurPageID = wpReady) and not DesktopRuntimeInstalled then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(
      'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe',
      'windowsdesktop-runtime.exe', '');
    DownloadPage.Show;

    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
#endif
end;

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
