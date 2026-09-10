using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopBoxes.Core;
using DesktopBoxes.UI;
using DesktopBoxes.Win32;

namespace DesktopBoxes.UiChecks;

/// <summary>Runs UI smoke checks against disposable fixtures, never the user's desktop/config.</summary>
internal static class Program
{
    private static string _output = "";
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        _output = Path.GetFullPath(args.Length == 0 ? "artifacts/ui-preview" : args[0]);
        Directory.CreateDirectory(_output);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var host = new HwndSource(new HwndSourceParameters("DesktopBoxes UI test host")
        {
            WindowStyle = unchecked((int)0x80000000), // Hidden popup, not Explorer/WorkerW.
            PositionX = -20000,
            PositionY = -20000,
            Width = 1000,
            Height = 1000,
        });
        var data = new Box
        {
            Name = "工作与灵感",
            X = -20000,
            Y = -20000,
            Width = 320,
            Height = 280,
            IconSize = 48,
            Items = new List<BoxItem>
            {
                new() { DisplayName = "文件资源管理器", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") },
                new() { DisplayName = "记事本", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe") },
                new() { DisplayName = "项目资料", Path = _output, Kind = ItemKind.Folder },
            },
        };
        var windows = new List<BoxWindow>
        {
            new(data, _output, host.Handle),
            new(new Box { Name = "日常工具", X = -20000, Y = -20000, Locked = true }, _output, host.Handle),
            new(new Box { Name = "待读与收藏", X = -20000, Y = -20000 }, _output, host.Handle),
        };
        Assert(data.Opacity == 65, "new boxes default to 65 percent opacity");
        Assert((GetWindowLong(windows[0].Handle, -20) & 0x00080000) != 0, "desktop-owned boxes enable per-pixel opacity");
        bool autoStartEnabled = false;
        var master = new MasterWindow(windows, _output, () => { },
            () => windows.Add(new BoxWindow(new Box { Name = "新建盒子", X = -20000, Y = -20000 }, _output, host.Handle)),
            w => { w.Dispose(); windows.Remove(w); }, () => autoStartEnabled, enabled => autoStartEnabled = enabled, () => true);
        try
        {
            RenderWindow(master, "dashboard.png", 1000, 650);
            RenderWindow(master, "dashboard-compact.png", 824, 530);
            var list = Descendants<ListBox>((DependencyObject)master.Content).Single();
            var search = Descendants<TextBox>((DependencyObject)master.Content).Single();
            Assert(list.Items.Count == 3, "dashboard lists all boxes");
            search.Text = "工作";
            Assert(list.Items.Count == 1, "search filters by box name");
            search.Text = "does not exist";
            Assert(list.Items.Count == 0, "search empty state");
            RenderWindow(master, "search-empty.png", 1000, 650);
            search.Clear();
            Click(master, "锁定全部位置");
            Assert(windows.All(w => w.Box.Locked), "mixed lock state locks all boxes");
            Click(master, "解锁全部位置");
            Assert(windows.All(w => !w.Box.Locked), "unlock all boxes");
            Click(master, "＋  新建盒子");
            Assert(windows.Count == 4 && list.SelectedItem == windows[3], "new box selected");
            Click(master, "删除盒子");
            Assert(windows.Count == 3, "delete selected box callback");
            var dialog = new AppearanceDialog(data, _output, true);
            RenderWindow(dialog, "appearance.png", 844, 665);
            var theme = Descendants<Button>((DependencyObject)dialog.Content)
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "云白主题");
            theme.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Click(dialog, "大图 · 64");
            Click(dialog, "大 · 24");
            Assert(data.BackgroundColor == null && data.IconSize == 48, "draft changes do not mutate box");
            RenderWindow(dialog, "appearance-light.png", 844, 665);
            CloseModal(dialog, "取消");
            Assert(data.BackgroundColor == null && data.IconSize == 48, "cancel preserves appearance");
            var saved = new AppearanceDialog(data, _output, true);
            RenderWindow(saved, "appearance-compact.png", 744, 490);
            Click(saved, "舒适 · 48");
            Click(saved, "大 · 24");
            var forest = Descendants<Button>((DependencyObject)saved.Content)
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "松林主题");
            forest.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            CloseModal(saved, "保存外观");
            Assert(data.BackgroundColor == "#203631" && data.TitleBarColor == "#2C4840", "save applies appearance");
            Assert(data.CornerRadius == 24, "embedded appearance editor saves rounded corners");
            data.CornerRadius = 16;
            data.BackgroundColor = data.TitleBarColor = data.TextColor = null;
            windows[0].RefreshAppearance();
            var boxVisual = (FrameworkElement)HwndSource.FromHwnd(windows[0].Handle).RootVisual;
            data.BackgroundImagePath = "missing-image.png";
            data.BackgroundColor = "#12345680";
            windows[0].RefreshAppearance();
            var fallback = (SolidColorBrush)((Border)boxVisual).Background;
            Assert(fallback.Color == Color.FromArgb(128, 0x12, 0x34, 0x56), "missing image fallback preserves legacy RGBA colors");
            data.BackgroundImagePath = data.BackgroundColor = null;
            windows[0].RefreshAppearance();
            Render(boxVisual, "desktop-box.png", 320, 280, Brushes.Transparent);
            var lockButton = Descendants<Button>(boxVisual).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "BoxLockToggle");
            int changes = 0;
            windows[0].Changed += _ => changes++;
            lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(data.Locked && !windows[1].Box.Locked && !windows[2].Box.Locked, "header lock affects only this box");
            Assert(lockButton.IsVisible && System.Windows.Automation.AutomationProperties.GetName(lockButton) == "解锁盒子", "locked button remains available to unlock");
            Render(boxVisual, "desktop-locked.png", 320, 280, Brushes.Transparent);
            var titleBar = Descendants<Border>(boxVisual).Single(b => b.Child is DockPanel);
            var mouseDown = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            };
            titleBar.RaiseEvent(mouseDown);
            Assert(!titleBar.IsMouseCaptured && data.X == -20000 && data.Y == -20000, "locked title cannot begin moving");
            Assert(Descendants<Border>(boxVisual).Single(b => Equals(b.ToolTip, "拖动调整大小")).Visibility == Visibility.Collapsed, "locked box hides resize grip");
            lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(!data.Locked && changes == 2 && System.Windows.Automation.AutomationProperties.GetName(lockButton) == "锁定盒子", "second click unlocks and both clicks request persistence");
            Assert(Descendants<Border>(boxVisual).Single(b => Equals(b.ToolTip, "拖动调整大小")).Visibility == Visibility.Visible, "unlock restores resize grip");
            CheckRoundedRegion(windows[0], "expanded embedded box");
            data.CornerRadius = 0;
            windows[0].RefreshAppearance();
            Assert(((Border)boxVisual).CornerRadius.TopLeft == 16, "legacy square configuration becomes rounded");
            CheckRoundedRegion(windows[0], "legacy embedded box");
            data.CornerRadius = 16;
            windows[0].RefreshAppearance();
            DesktopAttacher.SetWindowPosition(windows[0].Handle, 0, 0, 260, 180);
            Pump();
            CheckRoundedRegion(windows[0], "resized embedded box");
            var collapse = Descendants<Button>(boxVisual).Single(b => Equals(b.ToolTip, "折叠盒子"));
            lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            collapse.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Assert(data.Collapsed, "collapse button changes model");
            CheckRoundedRegion(windows[0], "collapsed locked box");
            Render(boxVisual, "desktop-collapsed.png", 320, 42, Brushes.Transparent);
            Assert(data.Locked && lockButton.IsVisible, "collapsed box keeps lock control");
            lockButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(!data.Locked, "collapsed box can be unlocked");
            collapse.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Assert(!data.Collapsed, "expand button changes model");
            CheckRoundedRegion(windows[0], "re-expanded box");
            Assert(boxVisual.Clip is RectangleGeometry { RadiusX: > 0 }, "WPF clips children to rounded frame");
            using (var fallbackWindow = new BoxWindow(new Box { Name = "兼容模式", X = -20000, Y = -20000, CornerRadius = 0 }, _output))
            {
                Pump();
                CheckRoundedRegion(fallbackWindow, "fallback top-level box");
            }
            Render((FrameworkElement)HwndSource.FromHwnd(windows[2].Handle).RootVisual, "desktop-empty.png", 280, 220, Brushes.Transparent);
            var rename = new RenameDialog("重命名盒子", "工作与灵感");
            RenderWindow(rename, "rename.png", 404, 220);
            Descendants<TextBox>((DependencyObject)rename.Content).Single().Text = "  ";
            Assert(!FindButton(rename, "保存名称").IsEnabled, "blank names cannot be saved");
            rename.Close();
            var autoStart = Descendants<CheckBox>((DependencyObject)master.Content).Single(control => Equals(control.Content, "开机自动启动"));
            Assert(autoStart.IsChecked == false, "dashboard shows disabled auto-start state");
            autoStart.IsChecked = true;
            autoStart.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(autoStartEnabled && autoStart.IsChecked == true, "dashboard enables auto-start");
            autoStart.IsChecked = false;
            autoStart.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(!autoStartEnabled && autoStart.IsChecked == false, "dashboard disables auto-start");
            var empty = new MasterWindow(Array.Empty<BoxWindow>(), _output, () => { }, () => { }, _ => { }, () => false, _ => { }, () => true);
            RenderWindow(empty, "dashboard-empty.png", 1000, 650);
            Assert(!FindButton(empty, "统一外观").IsEnabled, "empty dashboard disables bulk operations");
            empty.Close();
            RenderBoxGallery();
            FileMoveChecks.Run(host.Handle, _output, Assert);
            Console.WriteLine($"PASS: {_checks} UI checks; previews: {_output}");
        }
        finally
        {
            master.Close();
            foreach (var window in windows) window.Dispose();
            application.Shutdown();
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine($"PASS: {message}");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private static void RenderBoxGallery()
    {
        var board = new StackPanel { Margin = new Thickness(28), Width = 680, UseLayoutRounding = true, SnapsToDevicePixels = true };
        board.Children.Add(new TextBlock { Text = "一个盒子，一处有序", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        board.Children.Add(new TextBlock { Text = "圆角卡片  /  独立锁定  /  一键解锁", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(193, 202, 223)), Margin = new Thickness(0, 8, 0, 24) });
        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (file, label) in new[] { ("desktop-box.png", "可移动 · 拖动标题栏调整位置"), ("desktop-locked.png", "已锁定 · 点击锁按钮随时解锁") })
        {
            var column = new StackPanel { Width = 320, Margin = new Thickness(0, 0, 24, 0) };
            column.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.Combine(_output, file));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            column.Children.Add(new Image { Source = bitmap, Width = 320, Height = 280 });
            columns.Children.Add(column);
        }
        board.Children.Add(columns);
        board.Children.Add(new TextBlock { Text = "锁定只固定位置与大小，打开文件和折叠仍可使用。", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(193, 202, 223)), Margin = new Thickness(0, 20, 0, 0) });
        var surface = new Grid { Background = new LinearGradientBrush(Color.FromRgb(52, 66, 93), Color.FromRgb(30, 39, 60), 35) };
        surface.Children.Add(board);
        Render(surface, "boxes.png", 736, 488, surface.Background);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(240) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CheckRoundedRegion(BoxWindow window, string state)
    {
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Assert(GetWindowRgn(window.Handle, region) == 3, $"{state}: native rounded region");
            GetWindowRect(window.Handle, out var rect);
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            Assert(!PtInRegion(region, 0, 0) && !PtInRegion(region, width - 1, 0) &&
                !PtInRegion(region, 0, height - 1) && !PtInRegion(region, width - 1, height - 1), $"{state}: all four corners excluded");
            Assert(PtInRegion(region, width / 2, height / 2) && PtInRegion(region, width - 1, height / 2) &&
                PtInRegion(region, width / 2, height - 1), $"{state}: body and outer edges retained");
        }
        finally { DeleteObject(region); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static Button FindButton(Window window, string text) => Descendants<Button>((DependencyObject)window.Content)
        .Single(b => b.Content is string content && content == text);

    private static void Click(Window window, string text) => FindButton(window, text).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void CloseModal(Window window, string button)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (button == "取消") window.DialogResult = false;
            else Click(window, button);
        }));
        window.ShowDialog();
    }

    private static void RenderWindow(Window window, string name, double width, double height) =>
        Render((FrameworkElement)window.Content, name, width, height, window.Background);

    private static void Render(FrameworkElement element, string name, double width, double height, Brush background)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var canvas = new DrawingVisual();
        using (var context = canvas.RenderOpen()) context.DrawRectangle(background, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        bitmap.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(_output, name));
        png.Save(stream);
    }
}
