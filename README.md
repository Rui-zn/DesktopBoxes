# 桌面盒子（DesktopBoxes）

一个类似「酷呆桌面 / Stardock Fences」的 Windows 桌面整理工具：把应用、快捷方式、文件夹、文件拖进「盒子」，盒子贴桌面、可折叠成标题栏。

支持 Windows 10 / 11 x64。当前版本处于早期可用阶段，欢迎通过 Issue 反馈不同 Windows 版本、多显示器和混合 DPI 场景的问题。

## 功能

- 保留 Windows 系统桌面图标；盒子嵌入 WorkerW/Progman 桌面层，位于桌面图标之上、普通窗口之下，`Win+D` 后仍在
- 新建 / 删除 / 重命名盒子；拖动移动、拖右下角缩放
- 拖入 `.exe` / `.lnk` / 文件夹 / 文件；双击启动 / 打开
- 折叠成标题栏（带 160ms 动画）；图标项右键：打开 / 打开所在位置 / 重命名 / 移除
- 换肤：主题预设 / 背景色 / 标题栏色 / 文字色 / 背景图片（适应·拉伸·平铺·居中）/ 图标大小（小·中·大）
- 托盘常驻、开机自启、单实例
- 位置 / 大小 / 折叠 / 内容 / 外观持久化到 JSON，并采用原子保存、上一版备份和损坏配置恢复

## 运行

- 推荐：从仓库的 **Releases** 页面下载最新的单文件 EXE 或便携 ZIP
- 便携版：解压带 `portable` 标记文件的便携包，双击 `DesktopBoxes.App.exe`
- 源码运行：`dotnet run --project src\DesktopBoxes.App`

启动后桌面左上角出现「我的盒子」，右上角托盘有图标（右键菜单）。

## 数据位置

默认 `%AppData%\DesktopBoxes\`（`boxes.json`、`boxes.json.bak`、`backgrounds\`、`startup.log`、`error.log`）。

- 便携模式：在 exe 同目录放一个空文件 `portable`，数据存到 exe 旁 `data\`
- 自定义目录：在默认位置的 `storage.location` 文件里写目标目录绝对路径
- 也可以从托盘菜单选择“数据存储位置…”；程序会复制配置使用的背景资源、切换保存位置，并保留原目录供手动确认后清理

## 构建

```powershell
# 依赖：.NET 8 SDK（含 WindowsDesktop）
dotnet restore DesktopBoxes.sln --locked-mode
dotnet format DesktopBoxes.sln --no-restore --verify-no-changes
dotnet build DesktopBoxes.sln -c Release --no-restore
dotnet test DesktopBoxes.sln -c Release --no-build --no-restore

# 同时生成安装器输入目录与带 portable 标记的便携包
.\build-release.ps1 -Version 1.1.0
```

`artifacts\installer\` 中生成一个自包含的 `DesktopBoxes.App.exe`，可用作 Inno Setup 输入；`artifacts\DesktopBoxes-portable-win-x64-<版本>.zip` 是带便携模式标记的压缩包。项目根目录现存的旧安装包、旧 ZIP 和 `publish\` 不会被自动更新，发布时不要继续使用。

## 项目结构

```
src/DesktopBoxes.Core/    数据模型、JSON 持久化、数据目录、启动器
src/DesktopBoxes.Win32/   贴桌面(WorkerW/SetParent)、Shell 图标、.lnk 解析
src/DesktopBoxes.UI/      盒子窗口(HwndSource)、图标缓存、外观/重命名对话框
src/DesktopBoxes.App/     入口、托盘、单实例、开机自启、异常日志
tests/DesktopBoxes.Tests/ xunit 单元测试
```

技术栈：.NET 8 + WPF（+ WinForms NotifyIcon）+ Vanara.PInvoke/Windows.Shell。

## 已知限制

- **WorkerW 模式使用不透明直角窗口**：这是保留系统桌面图标并获得稳定桌面层级的技术取舍；透明度和圆角只在找不到桌面宿主时的顶层兼容模式生效。
- 单文件自包含 exe 约 150MB（含 .NET 运行时 + WPF 原生库，WPF 不支持裁剪）。
- 已启用 Per-Monitor V2 DPI 并处理基础坐标换算；混合缩放多显示器仍需真机回归。
- 外观目前按「每盒独立」存储；总控中的“统一设置”会把当前外观复制到全部盒子。

## 参与贡献

提交 Issue 或 Pull Request 前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 和 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。安全漏洞请按照 [SECURITY.md](SECURITY.md) 私密报告。版本变化记录在 [CHANGELOG.md](CHANGELOG.md)。

## License

本项目采用 [MIT License](LICENSE)。
