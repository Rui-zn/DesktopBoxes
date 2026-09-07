# 更新日志

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 的结构，并使用语义化版本号。

## [Unreleased]

### Added

- GitHub Actions 构建、测试和发布流程。
- 贡献指南、安全策略、编辑器配置和 MIT License。

## [1.1.0] - 2026-09-07

### Added

- WorkerW/Progman 桌面嵌入及 Explorer 重启后自动重挂。
- Per-Monitor V2 DPI 支持和基础坐标换算。
- 配置原子保存、备份恢复和损坏文件保留。
- 数据目录迁移、主题预设和文件属性入口。
- 自包含单文件发布和正确的便携模式标记。

### Changed

- 保留 Windows 系统桌面图标，不再默认接管桌面。

### Fixed

- 第二实例退出可能改变桌面图标状态的问题。
- 配置损坏后被空配置覆盖的问题。
- 图标轻微移动即触发拖放及背景图片取消后遗留文件的问题。
