; Інсталятор Rozkladka (Inno Setup 6). Встановлює для поточного користувача, без прав адміністратора.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8C3B6E2A-4F1D-4B7A-9E62-5A1C2D3E4F50}
AppName=Rozkladka
AppVersion={#AppVersion}
AppVerName=Rozkladka {#AppVersion}
AppPublisher=ostap49
AppPublisherURL=https://github.com/ostap49/Gfkzybwz
DefaultDirName={localappdata}\Programs\Rozkladka
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish
OutputBaseFilename=Rozkladka-Setup
SetupIconFile=..\src\Rozkladka\rozkladka.ico
UninstallDisplayIcon={app}\Rozkladka.exe
UninstallDisplayName=Rozkladka
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускати разом з Windows (рекомендовано)"
Name: "desktopicon"; Description: "Ярлик на робочому столі"; Flags: unchecked

[Files]
Source: "..\publish\Rozkladka.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Rozkladka"; Filename: "{app}\Rozkladka.exe"
Name: "{autodesktop}\Rozkladka"; Filename: "{app}\Rozkladka.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Rozkladka"; ValueData: """{app}\Rozkladka.exe"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\Rozkladka.exe"; Description: "Запустити Rozkladka"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Rozkladka.exe /F"; Flags: runhidden; RunOnceId: "StopRozkladka"

[Code]
// Перед оновленням зупиняємо запущену копію, щоб файл можна було замінити.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Rozkladka.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

// Під час видалення — запитати, чи стерти налаштування й винятки.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Видалити також налаштування й список винятків Rozkladka?', mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Rozkladka'), True, True, True);
end;
