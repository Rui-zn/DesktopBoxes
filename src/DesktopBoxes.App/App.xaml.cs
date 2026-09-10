using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using DesktopBoxes.Core;
using DesktopBoxes.UI;
using DesktopBoxes.Win32;

namespace DesktopBoxes.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DesktopBoxes";

    private System.Threading.Mutex? _mutex;
    private System.Windows.Forms.NotifyIcon? _tray;
    private BoxStore? _store;
    private BoxFileTransfers? _fileTransfers;
    private readonly List<BoxWindow> _windows = new();
    private IntPtr _desktopParent;
    private string _dataDir = "";
    private System.Windows.Threading.DispatcherTimer? _saveTimer;
    private MasterWindow? _master;
    private bool _exiting;
    private System.Windows.Threading.DispatcherTimer? _keepAliveTimer;
    private System.Windows.Threading.DispatcherTimer? _desktopMonitorTimer;
    private bool _usingDesktopFallback;
    private int _exitExportStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        if (e.Args.Contains("--spike"))
        {
            Shutdown(Spike.Run());
            return;
        }

        if (e.Args.Contains("--wind"))
        {
            Shutdown(Spike.TestWinD());
            return;
        }

        _mutex = new System.Threading.Mutex(true, @"Local\DesktopBoxes.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("桌面盒子已经在运行。", "桌面盒子");
            Shutdown();
            return;
        }

        _dataDir = DataDirectory.GetDefaultDirectory();
        SetupErrorHandling();
        _dataDir = DataDirectory.Resolve();
        CleanupDesktopExports();
        _store = new BoxStore(_dataDir);
        var boxes = _store.Load();
        try
        {
            var recoveryMessages = BoxFileTransfers.Recover(_dataDir, boxes, () => _store.Save(boxes));
            if (recoveryMessages.Count > 0)
                System.Windows.MessageBox.Show(string.Join("\n\n", recoveryMessages), "文件移动恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            LogError("file-recovery", ex);
            System.Windows.MessageBox.Show($"文件移动记录未清理，原文件仍保留。\n{ex.Message}", "文件移动恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _fileTransfers = CreateFileTransfers();

        if (_store.PreservedCorruptFile != null)
        {
            string detail = _store.RecoveredFromBackup
                ? "已从备份恢复配置。"
                : "没有可用备份，已创建一个新的空配置。";
            System.Windows.MessageBox.Show(
                $"检测到损坏的配置文件，原文件已保留在：\n{_store.PreservedCorruptFile}\n\n{detail}",
                "桌面盒子 - 配置恢复",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (boxes.Count == 0)
        {
            boxes.Add(new Box { Name = "我的盒子", X = 120, Y = 120, Width = 300, Height = 240 });
        }

        ClampBoxesToVisible(boxes);

        _desktopParent = DesktopAttacher.GetDesktopParent();
        _usingDesktopFallback = _desktopParent == IntPtr.Zero;

        foreach (var box in boxes)
        {
            CreateBoxWindow(box);
        }

        SetupTray();
        SetupMaster();

        // 找不到桌面宿主时才启用普通顶层置底窗口的 Win+D 兜底。
        if (_usingDesktopFallback)
        {
            SetupKeepAlive();
        }
        SetupDesktopMonitor();

        Save();
        LogStartup();
    }

    private void CreateBoxWindow(Box box)
    {
        var w = new BoxWindow(box, _dataDir, _desktopParent, _fileTransfers);
        w.Changed += _ => ScheduleSave();
        w.DeleteRequested += OnDeleteRequested;
        w.NewBoxRequested += OnNewBoxRequested;
        _windows.Add(w);
        _master?.RefreshList();
    }

    private void ClampBoxesToVisible(List<Box> boxes)
    {
        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double right = left + SystemParameters.VirtualScreenWidth;
        double bottom = top + SystemParameters.VirtualScreenHeight;
        foreach (var box in boxes)
        {
            LayoutHelper.ClampToVisible(box, left, top, right, bottom);
        }
    }

    private void Save()
    {
        try
        {
            SaveOrThrow();
        }
        catch (Exception ex)
        {
            LogError("save", ex);
            _tray?.ShowBalloonTip(
                5000,
                "桌面盒子",
                "配置保存失败，详细信息已写入 error.log。",
                System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private void SaveOrThrow()
    {
        _store?.Save(_windows.Select(x => x.Box).ToList());
        CleanupUnusedBackgrounds();
    }

    private BoxFileTransfers CreateFileTransfers() => new(_dataDir,
        () => _windows.Select(w => w.Box).ToList(),
        () =>
        {
            // File moves commit synchronously; never depend on the layout debounce timer.
            _store!.Save(_windows.Select(w => w.Box).ToList());
            foreach (var window in _windows) window.RefreshContents();
            _master?.RefreshList();
        }, ShellFileMover.Move);

    private void CleanupUnusedBackgrounds()
    {
        string backgrounds = Path.Combine(_dataDir, "backgrounds");
        if (!Directory.Exists(backgrounds)) return;

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in _windows)
        {
            string? path = window.Box.BackgroundImagePath;
            if (string.IsNullOrWhiteSpace(path)) continue;
            string fullPath = Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, _dataDir);
            referenced.Add(fullPath);
        }

        foreach (string file in Directory.EnumerateFiles(backgrounds, "*", SearchOption.AllDirectories))
        {
            if (!referenced.Contains(Path.GetFullPath(file)))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    /// <summary>防抖保存（300ms），避免拖动/连续操作时频繁写盘。</summary>
    private void ScheduleSave()
    {
        _saveTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick -= OnSaveTick;
        _saveTimer.Tick += OnSaveTick;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        Save();
    }

    private void OnDeleteRequested(BoxWindow w)
    {
        if (FileOperationInProgress()) return;
        if (System.Windows.MessageBox.Show(
                $"确定删除盒子“{w.Box.Name}”吗？\n托管文件将移回桌面（同名自动改名），不会从磁盘删除。\n旧版的外部引用只移除引用，原文件不动。",
                "删除盒子",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            foreach (var item in w.Box.Items.Where(i => i.StoragePath != null).ToList())
                _fileTransfers!.ReturnToDirectory(w.Box, item, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"盒子未删除，未完成的项目仍保留。\n{ex.Message}", "删除盒子未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        w.Dispose();
        _windows.Remove(w);
        _master?.RefreshList();
        Save();
    }

    private void OnNewBoxRequested()
    {
        int n = _windows.Count;
        var box = new Box
        {
            Name = "新建盒子",
            X = 160 + (n % 6) * 40,
            Y = 160 + (n % 6) * 40,
            Width = 280,
            Height = 220,
        };
        if (_windows.Count > 0)
        {
            CopyAppearance(_windows[0].Box, box);
        }
        CreateBoxWindow(box);
        Save();
    }

    private static void CopyAppearance(Box source, Box target)
    {
        target.BackgroundColor = source.BackgroundColor;
        target.TitleBarColor = source.TitleBarColor;
        target.TextColor = source.TextColor;
        target.BackgroundImagePath = source.BackgroundImagePath;
        target.BackgroundImageMode = source.BackgroundImageMode;
        target.IconSize = source.IconSize;
        target.CornerRadius = source.CornerRadius;
        target.Opacity = source.Opacity;
    }

    private void SetupKeepAlive()
    {
        _keepAliveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _keepAliveTimer.Tick += (_, _) =>
        {
            foreach (var w in _windows)
            {
                if (DesktopAttacher.IsMinimized(w.Handle))
                {
                    DesktopAttacher.RestoreAndPinToBottom(w.Handle);
                }
            }
        };
        _keepAliveTimer.Start();
    }

    private void SetupDesktopMonitor()
    {
        _desktopMonitorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _desktopMonitorTimer.Tick += (_, _) =>
        {
            IntPtr currentParent = DesktopAttacher.GetDesktopParent();
            bool anyInvalidBox = _windows.Any(w => !DesktopAttacher.IsValidWindow(w.Handle));

            if (currentParent != IntPtr.Zero && (currentParent != _desktopParent || anyInvalidBox))
            {
                RecreateBoxWindows(currentParent);
            }
        };
        _desktopMonitorTimer.Start();
    }

    private void RecreateBoxWindows(IntPtr desktopParent)
    {
        var boxes = _windows.Select(w => w.Box).ToList();
        foreach (var window in _windows)
        {
            window.Dispose();
        }
        _windows.Clear();

        _desktopParent = desktopParent;
        _usingDesktopFallback = false;
        _keepAliveTimer?.Stop();
        foreach (var box in boxes)
        {
            CreateBoxWindow(box);
        }
        _master?.RefreshList();
    }

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = GetAppIcon(),
            Text = "桌面盒子 (DesktopBoxes)",
            Visible = true,
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开总控", null, (_, _) => ShowMaster());
        menu.Items.Add("新建盒子", null, (_, _) => OnNewBoxRequested());
        menu.Items.Add("数据存储位置…", null, (_, _) => ChangeDataDirectory());

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => { if (FileOperationInProgress()) return; _exiting = true; Shutdown(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMaster();

        if (_usingDesktopFallback)
        {
            _tray.ShowBalloonTip(
                5000,
                "桌面盒子",
                "未找到 Windows 桌面宿主，当前使用置底窗口兼容模式。",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private void SetupMaster()
    {
        _master = new MasterWindow(
            _windows,
            _dataDir,
            onChanged: Save,
            onNewBox: OnNewBoxRequested,
            onDeleteBox: OnDeleteRequested,
            isAutoStartEnabled: IsAutoStartEnabled,
            setAutoStart: SetAutoStart,
            isExiting: () => _exiting);
        _master.Show();
    }

    private void ShowMaster()
    {
        if (_master == null)
        {
            return;
        }
        _master.RefreshList();
        _master.Show();
        _master.WindowState = WindowState.Normal;
        _master.Activate();
    }

    private static System.Drawing.Icon GetAppIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon != null)
                {
                    return icon;
                }
            }
        }
        catch
        {
        }
        return System.Drawing.SystemIcons.Application;
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) != null;
    }

    private void ChangeDataDirectory()
    {
        if (FileOperationInProgress()) return;
        string pendingTransfers = Path.Combine(_dataDir, "transfers");
        if (Directory.Exists(pendingTransfers) && Directory.EnumerateFiles(pendingTransfers, "*.json").Any())
        {
            System.Windows.MessageBox.Show("存在未完成的文件移动记录，请先重启恢复并处理提示的文件，再迁移数据目录。", "数据存储位置");
            return;
        }
        if (DataDirectory.IsPortable())
        {
            System.Windows.MessageBox.Show(
                $"当前处于便携模式，数据固定存储在：\n{_dataDir}",
                "数据存储位置",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择桌面盒子的数据存储目录",
            InitialDirectory = Directory.Exists(_dataDir) ? _dataDir : DataDirectory.GetDefaultDirectory(),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        string target = Path.GetFullPath(dialog.SelectedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string source = Path.GetFullPath(_dataDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsNestedPath(source, target) || IsNestedPath(target, source))
        {
            System.Windows.MessageBox.Show(
                "新旧数据目录不能互相包含，请选择另一个目录。",
                "数据存储位置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string targetConfig = Path.Combine(target, "boxes.json");
        if (File.Exists(targetConfig) &&
            System.Windows.MessageBox.Show(
                "目标目录已有 boxes.json。继续将用当前配置替换它，并保留目标目录的上一版备份。",
                "数据存储位置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var originalBackgroundPaths = _windows.ToDictionary(w => w.Box, w => w.Box.BackgroundImagePath);
        var originalItemPaths = _windows.SelectMany(w => w.Box.Items).ToDictionary(i => i, i => i.Path);
        try
        {
            SaveOrThrow();
            CopyDataDirectory(source, target);

            foreach (var item in _windows.SelectMany(w => w.Box.Items).Where(i => i.StoragePath != null))
                item.Path = BoxFileTransfers.ResolveStoredPath(target, item.StoragePath!);

            foreach (var window in _windows)
            {
                string? imagePath = window.Box.BackgroundImagePath;
                if (!string.IsNullOrWhiteSpace(imagePath) && Path.IsPathFullyQualified(imagePath) && IsNestedPath(source, imagePath))
                {
                    window.Box.BackgroundImagePath = Path.GetRelativePath(source, imagePath);
                }
            }

            var targetStore = new BoxStore(target);
            targetStore.Save(_windows.Select(w => w.Box).ToList());
            DataDirectory.SetCustomLocation(target);

            _dataDir = target;
            _store = targetStore;
            _fileTransfers = CreateFileTransfers();
            foreach (var window in _windows) window.UpdateDataDirectory(target, _fileTransfers);
            _master?.UpdateDataDirectory(target);

            System.Windows.MessageBox.Show(
                $"数据已经迁移到：\n{target}\n\n原目录未删除，可确认运行正常后手动清理。",
                "数据迁移完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            foreach (var pair in originalBackgroundPaths) pair.Key.BackgroundImagePath = pair.Value;
            foreach (var pair in originalItemPaths) pair.Key.Path = pair.Value;
            LogError("storage-migration", ex);
            System.Windows.MessageBox.Show(
                $"数据迁移失败：{ex.Message}\n\n原目录未删除。",
                "数据迁移失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static bool IsNestedPath(string parent, string child)
    {
        string normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedChild = Path.GetFullPath(child);
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDataDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        BoxFileTransfers.CopyManagedFiles(source, target);
        foreach (string directoryName in new[] { "backgrounds", "icon-cache" })
        {
            string sourceDirectory = Path.Combine(source, directoryName);
            if (!Directory.Exists(sourceDirectory)) continue;

            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, sourceFile);
                string targetFile = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(sourceFile, targetFile, overwrite: true);
            }
        }
    }

    private bool FileOperationInProgress()
    {
        if (_fileTransfers?.IsBusy != true && !BoxWindow.IsFileDragActive) return false;
        System.Windows.MessageBox.Show("正在移动或拖动文件，请完成后再操作。", "桌面盒子");
        return true;
    }

    private static void SetAutoStart(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled)
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                key.SetValue(RunValueName, $"\"{exe}\"");
            }
        }
        else
        {
            key.DeleteValue(RunValueName, false);
        }
    }

    private void SetupErrorHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogError("dispatcher", args.Exception);
            System.Windows.MessageBox.Show(
                $"操作失败：{args.Exception.Message}\n\n详细信息已写入 error.log。",
                "桌面盒子",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogError("unhandled", ex);
            }
            ExportBoxesForExit("unhandled");
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ExportBoxesForExit("process-exit");
    }

    private void CleanupDesktopExports()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) throw new DirectoryNotFoundException("无法确定桌面文件夹位置。");

            CleanupResult result = BoxExitExporter.Cleanup(desktop, _dataDir);
            if (result.Errors.Count > 0)
            {
                LogError(
                    "startup-export-cleanup",
                    new IOException($"启动清理有 {result.Errors.Count} 个项目未完成：{Environment.NewLine}{string.Join(Environment.NewLine, result.Errors)}"));
            }
        }
        catch (Exception ex)
        {
            LogError("startup-export-cleanup", ex);
        }
    }

    private void ExportBoxesForExit(string reason, IReadOnlyList<Box>? currentBoxes = null)
    {
        if (System.Threading.Interlocked.Exchange(ref _exitExportStarted, 1) != 0 || _store == null) return;

        try
        {
            IReadOnlyList<Box> boxes = currentBoxes ?? _store.Load();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) throw new DirectoryNotFoundException("无法确定桌面文件夹位置。");

            ExportResult result = BoxExitExporter.Export(desktop, _dataDir, boxes);
            if (result.Errors.Count > 0)
            {
                LogError(
                    $"exit-export-{reason}",
                    new IOException($"退出备份有 {result.Errors.Count} 个项目未完成：{Environment.NewLine}{string.Join(Environment.NewLine, result.Errors)}"));
            }
        }
        catch (Exception ex)
        {
            LogError($"exit-export-{reason}", ex);
        }
    }

    private void LogStartup()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            File.AppendAllText(
                Path.Combine(_dataDir, "startup.log"),
                $"[{DateTime.Now:O}] desktopParent=0x{_desktopParent.ToInt64():X} fallback={_usingDesktopFallback} boxes={_windows.Count} dataDir={_dataDir}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void LogError(string kind, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            File.AppendAllText(
                Path.Combine(_dataDir, "error.log"),
                $"[{DateTime.Now:O}] {kind}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _saveTimer?.Stop();
        Save();
        ExportBoxesForExit("normal", _windows.Select(window => window.Box).ToList());
        _desktopMonitorTimer?.Stop();
        _keepAliveTimer?.Stop();
        foreach (var w in _windows)
        {
            w.Dispose();
        }
        _windows.Clear();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
