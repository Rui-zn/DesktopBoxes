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

## 提交与 Pull Request

1. 一个 Pull Request 聚焦一个问题。
2. 行为变更应补充或更新测试。
3. 用户可见变化应更新 README；版本变化应更新 CHANGELOG。
4. 不要提交 `bin/`、`obj/`、`artifacts/`、安装包、ZIP、日志或个人运行数据。
5. 提交信息建议使用简洁的祈使句，例如 `Fix WorkerW reattachment`。

提交 Pull Request 即表示你同意按本项目的 MIT License 授权所提交的内容。
