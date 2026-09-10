# 参与贡献

感谢你愿意改进 DesktopBoxes。

## 开发环境

- Windows 10 或 Windows 11
- .NET 8 SDK（含 Windows Desktop SDK）
- 可选：Inno Setup 6，用于生成安装包

## 本地验证

提交更改前请运行：

```powershell
dotnet restore DesktopBoxes.sln --locked-mode
dotnet format DesktopBoxes.sln --no-restore --verify-no-changes
dotnet build DesktopBoxes.sln -c Release --no-restore
dotnet test DesktopBoxes.sln -c Release --no-build --no-restore
```

涉及 WorkerW、拖放、Win+D、多显示器或 DPI 的更改还需要在真实 Windows 桌面会话中验证。不要在自动测试中隐藏系统桌面图标。

UI 更改还可运行独立的 WPF 检查工具（Windows 桌面会话）：

```powershell
dotnet run --project tools/DesktopBoxes.UiChecks -c Release
```

工具使用演示数据和不可见的测试宿主，不读取用户配置、不挂接 Explorer；检查搜索、批量锁定、新建/删除回调、外观保存/取消、折叠以及名称校验，并在 `artifacts/ui-preview/` 生成真实 WPF 渲染图。它不替代 WorkerW、拖放、键盘焦点和多显示器的真机验证。

盒子 UI 检查还覆盖独立锁按钮的双向切换、保存事件、锁定后禁止移动 / 缩放入口、折叠态解锁，以及嵌入 / 兼容模式在缩放、折叠和旧直角配置下的原生圆角区域（四角裁剪、边缘保留）。真机回归请另外确认 100% / 150% / 200% 缩放、Win+D 和 Explorer 重启后的圆角与锁定状态。

文件移动测试会在临时目录和 `artifacts/ui-preview/` 下创建隔离文件，通过真实 Explorer `IDataObject` / `IDropTarget` 与 `IFileOperation` 检查往返移动；若两处位于不同磁盘，还会验证跨盘文件和文件夹。受限沙箱可能阻止 Windows 原生文件夹操作，出现该情况应在获准的正常 Windows 会话重跑，不能把失败当作通过。恢复测试覆盖保存失败、部分移动、旧引用、同名文件与迁移。

原生拖放实现遵循 [Microsoft 的优化移动协议](https://learn.microsoft.com/windows/win32/shell/datascenarios#handling-optimized-move-operations)，目标移动完成后不再要求源端删除文件；不要仅凭 `DROPEFFECT_MOVE` 递归删除源路径。

## 提交与 Pull Request

1. 一个 Pull Request 聚焦一个问题。
2. 行为变更应补充或更新测试。
3. 用户可见变化应更新 README；版本变化应更新 CHANGELOG。
4. 不要提交 `bin/`、`obj/`、`artifacts/`、安装包、ZIP、日志或个人运行数据。
5. 提交信息建议使用简洁的祈使句，例如 `Fix WorkerW reattachment`。

提交 Pull Request 即表示你同意按本项目的 MIT License 授权所提交的内容。
