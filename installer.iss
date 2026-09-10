; 桌面盒子 (DesktopBoxes) Inno Setup 安装脚本
; 使用步骤：
;   1) 先执行 .\build-release.ps1 生成 artifacts\installer\（见 README）
;   2) 用 Inno Setup (ISCC.exe) 编译本脚本，输出安装包到项目根目录
#define MyAppName "桌面盒子 (DesktopBoxes)"
#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif
#define MyAppExeName "DesktopBoxes.App.exe"

[Setup]
AppId={{D2A6F0E3-4B5C-4A7E-9D8E-1F2A3B4C5D6E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\DesktopBoxes
DefaultGroupName=桌面盒子
OutputDir=.
OutputBaseFilename=DesktopBoxes-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "artifacts\installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\桌面盒子"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\桌面盒子"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行桌面盒子"; Flags: nowait postinstall skipifsilent
