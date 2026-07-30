; Inno Setup — автономный установщик L-Phone (с вшитым рантаймом liblinphone).
; Скомпилировать: установить Inno Setup 6.3+ и выполнить  ISCC.exe installer.iss
; (или открыть файл в Inno Setup Compiler и нажать Build). Результат: installer\L-Phone-Setup-1.0.exe

#define MyApp "L-Phone"
; Версию передаёт build-installer.ps1 (/DMyVer=…), беря её из собранного exe — иначе она
; разъезжается с <Version> в csproj, а автообновление сравнивает именно версию сборки.
#ifndef MyVer
  #define MyVer "1.0"
#endif
#define MyExe "L-Phone.exe"

[Setup]
AppId={{B7E9F2A1-3C4D-4E5F-9A8B-1C2D3E4F5A6B}
AppName={#MyApp}
AppVersion={#MyVer}
AppPublisher=L-Phone
DefaultDirName={autopf}\{#MyApp}
DefaultGroupName={#MyApp}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=L-Phone-Setup-{#MyVer}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile=LinphoneWinUI\Assets\lphone.ico
UninstallDisplayIcon={app}\{#MyExe}
WizardStyle=modern

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Dirs]
; Каталог нативных данных liblinphone (логи/конфиг/sqlite-базы) — ASCII-путь, доступный
; на запись всем пользователям (нужно из-за кириллических профилей в %LOCALAPPDATA%).
Name: "{commonappdata}\L-Phone"; Permissions: users-modify

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"

[Files]
Source: "dist\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyApp}"; Filename: "{app}\{#MyExe}"
Name: "{group}\Удалить {#MyApp}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyApp}"; Filename: "{app}\{#MyExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyExe}"; Description: "Запустить {#MyApp}"; Flags: nowait postinstall skipifsilent
