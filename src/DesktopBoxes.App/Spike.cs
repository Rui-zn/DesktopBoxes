using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopBoxes.Core;
using DesktopBoxes.UI;
using DesktopBoxes.Win32;

namespace DesktopBoxes.App;

/// <summary>M0 技术验证：HwndSource 嵌入桌面层 / 图标提取 / .lnk 解析 / 拖放管线。</summary>
public static class Spike
{
    public static int Run()
    {
        var sb = new StringBuilder();
        void Log(string s) => sb.AppendLine(s);
        string logPath = Path.Combine(AppContext.BaseDirectory, "spike-log.txt");

        try
        {
            Log("[DesktopStructure]");
            Log(DesktopAttacher.DescribeDesktopStructure());

            // 正确贴桌面：HwndSource 创建时指定 ParentWindow（嵌入 WorkerW 桌面层）
            IntPtr defViewWorker = DesktopAttacher.FindWorkerHostingDefView();
            Log($"[Embed] DefViewWorker=0x{defViewWorker.ToInt64():X}");

            var p = new HwndSourceParameters("box-embed")
            {
                ParentWindow = defViewWorker,
                WindowStyle = 0x40000000 | 0x10000000 | 0x04000000, // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS
                Width = 320,
                Height = 200,
                PositionX = 80,
                PositionY = 80,
            };
            var src = new HwndSource(p);
            src.RootVisual = new Border
            {
                Width = 320,
                Height = 200,
                Background = System.Windows.Media.Brushes.Orange,
                CornerRadius = new CornerRadius(8),
            };
            Log($"[Embed] hwnd=0x{src.Handle.ToInt64():X}");
            Log($"[Embed] parent=0x{DesktopAttacher.GetParentOf(src.Handle).ToInt64():X}");
            Log($"[Embed] visible={DesktopAttacher.IsVisible(src.Handle)}");
            Log($"[Embed] parentMatch={(DesktopAttacher.GetParentOf(src.Handle) == defViewWorker ? "YES" : "NO")}");

            // 圆角透明验证：嵌入子窗口 + LWA_COLORKEY
            {
                const uint magenta = 0x00FF00FF; // R=255,G=0,B=255
                var p2 = new HwndSourceParameters("layered-test")
                {
                    ParentWindow = defViewWorker,
                    WindowStyle = DesktopAttacher.WS_CHILD | DesktopAttacher.WS_VISIBLE | DesktopAttacher.WS_CLIPSIBLINGS,
                    Width = 220,
                    Height = 160,
                    PositionX = 420,
                    PositionY = 80,
                    UsesPerPixelOpacity = false,
                };
                var src2 = new HwndSource(p2);
                var grid = new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 255)) };
                grid.Children.Add(new Border
                {
                    Background = System.Windows.Media.Brushes.DarkSlateBlue,
                    CornerRadius = new CornerRadius(12),
                });
                src2.RootVisual = grid;

                int exBefore = GetWindowLongPtr64(src2.Handle, GWL_EXSTYLE).ToInt32();
                SetWindowLongPtr64(src2.Handle, GWL_EXSTYLE, new IntPtr(exBefore | WS_EX_LAYERED));
                int exAfter = GetWindowLongPtr64(src2.Handle, GWL_EXSTYLE).ToInt32();
                bool setOk = SetLayeredWindowAttributes(src2.Handle, magenta, 255, LWA_COLORKEY);
                bool getOk = GetLayeredWindowAttributes(src2.Handle, out uint gotKey, out byte gotAlpha, out uint gotFlags);

                Log($"[Layered] EXSTYLE before=0x{exBefore:X} after=0x{exAfter:X} layered={(exAfter & WS_EX_LAYERED) != 0}");
                Log($"[Layered] SetLayeredWindowAttributes={setOk} Get={getOk} key=0x{gotKey:X} alpha={gotAlpha} flags=0x{gotFlags:X}");
                Log("[Layered] 判定：Set=true 且 key 被写入 → 嵌入子窗口可做圆角(colorkey)；半透明/逐像素仍须顶层窗口");

                src2.Dispose();
            }

            // 图标提取
            var icon = ShellIconProvider.GetIconBitmap(@"C:\Windows\System32\notepad.exe", 48);
            Log($"[ShellIcon] notepad.exe => {(icon == null ? "NULL" : $"{icon.Width}x{icon.Height}")}");
            icon?.Dispose();

            // .lnk 解析
            string lnkPath = Path.Combine(Path.GetTempPath(), "desktopboxes-spike.lnk");
            CreateTestShortcut(lnkPath);
            string? target = ShellLinkResolver.ResolveTarget(lnkPath);
            Log($"[ShellLink] target={target}");
            Log($"[ShellLink] match={(target != null && target.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase) ? "YES" : "NO")}");
            try { File.Delete(lnkPath); } catch { }

            // 拖放管线
            var dropData = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { @"C:\Windows\System32\notepad.exe", @"C:\Windows\System32\calc.exe" });
            var files = dropData.GetData(System.Windows.DataFormats.FileDrop) as string[];
            Log($"[DragDrop] parse => {(files == null ? "NULL" : string.Join(" | ", files))}");

            // 停留片刻便于人工目视确认
            System.Threading.Thread.Sleep(1500);
            src.Dispose();
        }
        catch (Exception ex)
        {
            Log($"[EXCEPTION] {ex}");
        }

        File.WriteAllText(logPath, sb.ToString());
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const uint LWA_COLORKEY = 0x1;

    /// <summary>验证 Win+D（显示桌面）后盒子是否仍可见（带对照组 + 正确泵消息）。</summary>
    public static int TestWinD()
    {
        var sb = new StringBuilder();
        void Log(string s) => sb.AppendLine(s);
        string logPath = Path.Combine(AppContext.BaseDirectory, "wind-log.txt");

        try
        {
            IntPtr desktopParent = DesktopAttacher.GetDesktopParent();
            var box = new Box { Name = "WinD与透明度测试", X = 200, Y = 200, Width = 280, Height = 200 };
            using var w = new BoxWindow(box, Path.GetTempPath(), desktopParent);

            var plain = new Window
            {
                Title = "对照组",
                Width = 220,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 520,
                Top = 200,
            };
            plain.Show();
            var plainHwnd = new WindowInteropHelper(plain).Handle;

            Log($"box=0x{w.Handle.ToInt64():X} plain=0x{plainHwnd.ToInt64():X}");
            Log($"desktopParent=0x{desktopParent.ToInt64():X} actualOwner=0x{DesktopAttacher.GetParentOf(w.Handle).ToInt64():X} attached={w.IsDesktopAttached}");
            Log($"before: box.IsIconic={IsIconic(w.Handle)} plain.IsIconic={IsIconic(plainHwnd)}");

            var t = Type.GetTypeFromProgID("Shell.Application")!;
            dynamic shell = Activator.CreateInstance(t)!;
            shell.ToggleDesktop();
            Log("ToggleDesktop() called");

            PumpDispatcher(TimeSpan.FromMilliseconds(800));

            Log($"after: box.IsIconic={IsIconic(w.Handle)} plain.IsIconic={IsIconic(plainHwnd)}");
            Log($"after: box.IsVisible={DesktopAttacher.IsVisible(w.Handle)} plain.IsVisible={DesktopAttacher.IsVisible(plainHwnd)}");

            plain.Close();
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex}");
        }

        File.WriteAllText(logPath, sb.ToString());
        return 0;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(
            duration,
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => { frame.Continue = false; },
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void CreateTestShortcut(string lnkPath)
    {
        try { if (File.Exists(lnkPath)) File.Delete(lnkPath); } catch { }
        var t = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = @"C:\Windows\System32\notepad.exe";
        sc.Save();
    }
}
