; ============================================================
; AiMux 安装包脚本 (Inno Setup 6)
; 用法: iscc installer.iss [/DMyAppVersion=1.2.3]
; 版本号优先取命令行 /DMyAppVersion，否则用下方默认值。
; ============================================================

#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif

#define MyAppName "AiMux"
#define MyAppPublisher "AiMux"
#define MyAppURL "https://github.com/Ledgerbiggg/AIMux"
#define MyAppExeName "AiMux.exe"

[Setup]
; 安装包/卸载程序的唯一标识（请勿随意更改，否则会视为不同软件）
AppId={{8F3C9A1E-2B4D-4E7A-9C1F-3D5E6F7A8B9C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; 安装到 Program Files，作为“正规软件”集成到系统
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; 图标使用项目内的 128x128 ico
SetupIconFile=AiMux.Shell\Assets\app-icon_ico_128x128.ico
OutputDir=package
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 写入 Program Files 需要管理员权限
PrivilegesRequired=admin
; 64 位安装
ArchitecturesInstallIn64BitMode=x64os
; 卸载时清理整个安装目录
UninstallFilesDir={app}\Uninstall
; 不使用 Restart Manager 自动关闭应用：本程序托盘常驻，主窗口会拦截关闭消息
; （关闭按钮=隐藏到托盘），RM 关不掉它只会弹出「无法自动关闭应用程序」错误框。
; 改由下方 [Code] 的 PrepareToInstall 主动结束进程，行为可预期
CloseApplications=no

[Languages]
; 英文界面（Inno Setup Action 镜像默认只含 Default.isl；中文语言包后续补）
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; 将 dotnet publish 产物整体打包（_publish 为 Makefile dist 临时目录）
Source: "_publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "额外快捷方式:"

[Run]
; 安装完成后可选启动
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// 覆盖安装前先关掉正在运行的旧版本。
// 背景：本程序托盘常驻，"点 ×" 是隐藏到托盘而非退出，主窗口会拦截 WM_CLOSE，
// Inno 的 Restart Manager 因此关不掉它，会弹「Setup was unable to automatically
// close all applications」。
// 这里只做一件事：taskkill 结束进程树（/T 一并结束 WebView2 等子进程）。
// ⚠️ 严禁改成 Exec('{app}\应用.exe', '--exit-for-update', ..., ewWaitUntilTerminated)
// 之类的"先请应用优雅退出"：旧版本不认识该开关，会把这次启动当成正常启动并常驻
// 托盘永不退出，安装程序将永久卡在「Preparing to Install」页面（无响应且无取消按钮）。
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  NeedsRestart := False;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM "{#MyAppExeName}"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
