using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopBoxes.Core;
using DesktopBoxes.Win32;

namespace DesktopBoxes.UI;

/// <summary>
/// 一个盒子窗口：基于 HwndSource 嵌入桌面层，标题栏 + 图标网格，
/// 支持移动/缩放/折叠动画/拖入拖出/双击打开/右键菜单/外观。
/// </summary>
public class BoxWindow : IDisposable
{
    private const int TitleBarHeight = 28;
    private const int AnimDurationMs = 160;

    private int IconSize => _box.IconSize is >= 24 and <= 96 ? _box.IconSize : 32;

    private readonly HwndSource _source;
    private readonly Box _box;
    private string _dataDir;
    private readonly IntPtr _desktopParent;

    private readonly Border _root;
    private readonly Border _titleBar;
    private readonly TextBlock _title;
    private readonly Button _collapseBtn;
    private readonly ScrollViewer _scroll;
    private readonly WrapPanel _itemsWrap;
    private readonly System.Windows.Threading.DispatcherTimer _animTimer;

    private Brush _textBrush = Brushes.White;

    private bool _dragging;
    private Point _dragScreenStart;
    private double _dragStartX;
    private double _dragStartY;

    private bool _resizing;
    private double _resizeStartW;
    private double _resizeStartH;

    private int _animFrom;
    private int _animTo;
    private DateTime _animStart;
    private Point? _itemDragStart;

    public Box Box => _box;
    public IntPtr Handle => _source.Handle;
    public bool IsEmbedded => _desktopParent != IntPtr.Zero;

    public void UpdateDataDirectory(string dataDir) => _dataDir = dataDir;

    /// <summary>任意变更后触发，供上层持久化。</summary>
    public event Action<BoxWindow>? Changed;
    public event Action<BoxWindow>? DeleteRequested;
    public event Action? NewBoxRequested;

    public BoxWindow(Box box, string dataDir, IntPtr desktopParent = default)
    {
        _box = box;
        _dataDir = dataDir;
        _desktopParent = desktopParent;

        _root = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 76, 90)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            AllowDrop = true,
        };

        _title = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _collapseBtn = new Button
        {
            Width = 22,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
        };
        _collapseBtn.Click += (_, _) => ToggleCollapse();

        _titleBar = new Border
        {
            Height = TitleBarHeight,
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Child = new DockPanel(),
        };
        var dock = (DockPanel)_titleBar.Child;
        DockPanel.SetDock(_collapseBtn, Dock.Left);
        dock.Children.Add(_collapseBtn);
        dock.Children.Add(_title);

        _itemsWrap = new WrapPanel { Margin = new Thickness(2) };
        _scroll = new ScrollViewer
        {
            Content = _itemsWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var resizeGrip = new Border
        {
            Width = 14,
            Height = 14,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 0 12 L 12 0"),
                Stroke = new SolidColorBrush(Color.FromRgb(120, 128, 144)),
                StrokeThickness = 1.5,
            },
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_titleBar, 0);
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(resizeGrip, 1);
        layout.Children.Add(_titleBar);
        layout.Children.Add(_scroll);
        layout.Children.Add(resizeGrip);

        _root.Child = layout;

        _animTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        _animTimer.Tick += (_, _) => AnimTick();

        var p = new HwndSourceParameters("DesktopBoxes.Box." + box.Id)
        {
            ParentWindow = desktopParent,
            WindowStyle = desktopParent != IntPtr.Zero
                ? DesktopAttacher.WS_CHILD | DesktopAttacher.WS_VISIBLE | DesktopAttacher.WS_CLIPSIBLINGS
                : DesktopAttacher.WS_POPUP | DesktopAttacher.WS_VISIBLE,
            Width = Math.Max(120, (int)box.Width),
            Height = Math.Max(60, (int)box.Height),
            PositionX = 0,
            PositionY = 0,
            // WorkerW 子窗口不能可靠使用逐像素透明；降级顶层窗口仍可使用。
            UsesPerPixelOpacity = desktopParent == IntPtr.Zero,
            ExtendedWindowStyle = DesktopAttacher.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(p) { RootVisual = _root };
        _source.AddHook(WndProc);
        if (desktopParent != IntPtr.Zero)
        {
            DesktopAttacher.EnsureTopInParent(_source.Handle);
        }
        else
        {
            DesktopAttacher.PinToBottom(_source.Handle);
        }

        WireEvents(_titleBar, resizeGrip);
        ApplyAppearance();
        ApplySize();
    }

    private void WireEvents(Border titleBar, Border resizeGrip)
    {
        _root.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        _root.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                foreach (string path in paths)
                {
                    AddItem(path);
                }
            }
        };

        _root.MouseRightButtonUp += (_, e) => ShowBoxMenu(_root);
        titleBar.MouseRightButtonUp += (_, e) => { ShowBoxMenu(titleBar); e.Handled = true; };

        titleBar.MouseLeftButtonDown += (_, e) => BeginMove(titleBar, e);
        titleBar.MouseMove += (_, e) => OnMove(titleBar, e);
        titleBar.MouseLeftButtonUp += (_, e) => EndMove(titleBar);

        resizeGrip.MouseLeftButtonDown += (_, e) => BeginResize(resizeGrip, e);
        resizeGrip.MouseMove += (_, e) => OnResize(resizeGrip, e);
        resizeGrip.MouseLeftButtonUp += (_, e) => EndResize(resizeGrip);
    }

    // ---------- 外观 ----------

    private void ApplyAppearance()
    {
        _textBrush = TryParseColor(_box.TextColor, out var tc) ? new SolidColorBrush(tc) : Brushes.White;

        string? backgroundPath = ResolveDataPath(_box.BackgroundImagePath);
        if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
        {
            var brush = new ImageBrush();
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(backgroundPath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                brush.ImageSource = bmp;
            }
            catch
            {
                brush.ImageSource = null;
            }
            ApplyImageMode(brush, _box.BackgroundImageMode);
            _root.Background = brush;
        }
        else if (TryParseColor(_box.BackgroundColor, out var bc))
        {
            _root.Background = new SolidColorBrush(bc);
        }
        else
        {
            _root.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
        }

        _titleBar.Background = TryParseColor(_box.TitleBarColor, out var tbc)
            ? new SolidColorBrush(tbc)
            : new SolidColorBrush(Color.FromRgb(52, 57, 68));

        _title.Foreground = _textBrush;
        _collapseBtn.Foreground = _textBrush;

        int r = IsEmbedded ? 0 : Math.Max(0, _box.CornerRadius);
        _root.CornerRadius = new CornerRadius(r);
        _titleBar.CornerRadius = new CornerRadius(r, r, 0, 0);
        _root.Background.Opacity = IsEmbedded ? 1.0 : Math.Clamp(_box.Opacity, 0, 100) / 100.0;

        Refresh();
    }

    private static void ApplyImageMode(ImageBrush brush, string? mode)
    {
        switch (mode)
        {
            case "Fill":
                brush.Stretch = Stretch.Fill;
                break;
            case "Uniform":
                brush.Stretch = Stretch.Uniform;
                break;
            case "Tile":
                brush.Stretch = Stretch.Fill;
                brush.TileMode = TileMode.Tile;
                brush.ViewportUnits = BrushMappingMode.Absolute;
                brush.Viewport = new Rect(0, 0, 128, 128);
                break;
            case "Center":
                brush.Stretch = Stretch.None;
                brush.AlignmentX = AlignmentX.Center;
                brush.AlignmentY = AlignmentY.Center;
                break;
            default:
                brush.Stretch = Stretch.UniformToFill;
                break;
        }
    }

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        string s = hex.Trim().TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return false;
        try
        {
            byte r = Convert.ToByte(s.Substring(0, 2), 16);
            byte g = Convert.ToByte(s.Substring(2, 2), 16);
            byte b = Convert.ToByte(s.Substring(4, 2), 16);
            byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- 渲染 ----------

    private void Refresh()
    {
        _title.Text = (_box.Locked ? "🔒 " : "") + $"{_box.Name}  ({_box.Items.Count})";
        _collapseBtn.Content = _box.Collapsed ? "▸" : "▾";
        _itemsWrap.Children.Clear();
        foreach (var item in _box.Items)
        {
            _itemsWrap.Children.Add(BuildItemElement(item));
        }
    }

    private FrameworkElement BuildItemElement(BoxItem item)
    {
        int size = IconSize;
        var image = new Image
        {
            Width = size,
            Height = size,
            Source = IconCache.Get(item.Path, size) ?? IconCache.Fallback,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 4, 0, 2),
        };
        var label = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 11,
            Foreground = _textBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = size + 36,
            TextAlignment = TextAlignment.Center,
        };
        var stack = new StackPanel { Width = size + 44, Margin = new Thickness(3) };
        stack.Children.Add(image);
        stack.Children.Add(label);

        var border = new Border { Background = Brushes.Transparent, Child = stack, Cursor = Cursors.Hand };

        border.MouseLeftButtonDown += (_, e) =>
        {
            _itemDragStart = e.GetPosition(border);
            if (e.ClickCount == 2)
            {
                OpenItem(item);
                e.Handled = true;
            }
        };
        border.MouseLeftButtonUp += (_, _) => _itemDragStart = null;
        border.MouseRightButtonUp += (_, e) => { ShowItemMenu(item); e.Handled = true; };
        border.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _itemDragStart is Point start)
            {
                Point current = e.GetPosition(border);
                if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _itemDragStart = null;
                var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
                DragDrop.DoDragDrop(border, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
        };

        return border;
    }

    // ---------- 内容变更 ----------

    private void AddItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        ItemKind kind = BoxItem.DetectKind(path);
        var item = new BoxItem
        {
            Path = path,
            Kind = kind,
            DisplayName = DefaultDisplayName(path),
            ResolvedTarget = kind == ItemKind.Shortcut ? ShellLinkResolver.ResolveTarget(path) : null,
        };
        _box.Items.Add(item);
        Refresh();
        RaiseChanged();
    }

    private static string DefaultDisplayName(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).Name;
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private void ToggleCollapse()
    {
        _box.Collapsed = !_box.Collapsed;
        _scroll.Visibility = _box.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        Refresh();
        AnimateHeight(_box.Collapsed ? TitleBarHeight : Math.Max(60, (int)_box.Height));
        RaiseChanged();
    }

    private void ApplySize()
    {
        SetHeight(_box.Collapsed ? TitleBarHeight : Math.Max(60, (int)_box.Height));
        _scroll.Visibility = _box.Collapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetHeight(int h)
    {
        var toDevice = _source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        Point screen = toDevice.Transform(new Point(_box.X, _box.Y));
        Point size = toDevice.Transform(new Point(Math.Max(120, _box.Width), h));
        var position = DesktopAttacher.ScreenToParentClient(
            _desktopParent,
            (int)Math.Round(screen.X),
            (int)Math.Round(screen.Y));

        DesktopAttacher.SetWindowPosition(
            _source.Handle,
            position.X,
            position.Y,
            Math.Max(1, (int)Math.Round(size.X)),
            Math.Max(1, (int)Math.Round(size.Y)));

        if (_desktopParent != IntPtr.Zero)
        {
            DesktopAttacher.EnsureTopInParent(_source.Handle);
        }
    }

    /// <summary>把盒子位置约束到虚拟屏幕内（拖动时实时调用，保证标题栏始终可见可抓取）。</summary>
    private void ClampToScreen()
    {
        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        LayoutHelper.ClampToVisible(
            _box,
            left,
            top,
            left + SystemParameters.VirtualScreenWidth,
            top + SystemParameters.VirtualScreenHeight);
    }

    private void AnimateHeight(int to)
    {
        int from = _box.Collapsed ? Math.Max(60, (int)_box.Height) : TitleBarHeight;
        if (from == to)
        {
            return;
        }
        _animFrom = from;
        _animTo = to;
        _animStart = DateTime.UtcNow;
        _animTimer.Stop();
        _animTimer.Start();
    }

    private void AnimTick()
    {
        double t = (DateTime.UtcNow - _animStart).TotalMilliseconds / AnimDurationMs;
        if (t >= 1.0)
        {
            t = 1.0;
            _animTimer.Stop();
        }
        SetHeight(_animFrom + (int)((_animTo - _animFrom) * t));
    }

    // ---------- 移动 / 缩放 ----------

    private void BeginMove(UIElement el, MouseButtonEventArgs e)
    {
        if (_box.Locked) return;
        _dragging = true;
        _dragScreenStart = _root.PointToScreen(e.GetPosition(_root));
        _dragStartX = _box.X;
        _dragStartY = _box.Y;
        el.CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(UIElement el, MouseEventArgs e)
    {
        if (!_dragging) return;
        Point now = _root.PointToScreen(e.GetPosition(_root));
        Vector deviceDelta = now - _dragScreenStart;
        Vector logicalDelta = (_source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(deviceDelta);
        _box.X = _dragStartX + logicalDelta.X;
        _box.Y = _dragStartY + logicalDelta.Y;
        ClampToScreen();
        SetHeight(_box.Collapsed ? TitleBarHeight : Math.Max(60, (int)_box.Height));
    }

    private void EndMove(UIElement el)
    {
        if (!_dragging) return;
        _dragging = false;
        el.ReleaseMouseCapture();
        RaiseChanged();
    }

    private void BeginResize(UIElement el, MouseButtonEventArgs e)
    {
        if (_box.Locked) return;
        _resizing = true;
        _dragScreenStart = _root.PointToScreen(e.GetPosition(_root));
        _resizeStartW = _box.Width;
        _resizeStartH = _box.Height;
        el.CaptureMouse();
        e.Handled = true;
    }

    private void OnResize(UIElement el, MouseEventArgs e)
    {
        if (!_resizing) return;
        Point now = _root.PointToScreen(e.GetPosition(_root));
        Vector deviceDelta = now - _dragScreenStart;
        Vector logicalDelta = (_source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(deviceDelta);
        _box.Width = Math.Max(120, _resizeStartW + logicalDelta.X);
        _box.Height = Math.Max(60, _resizeStartH + logicalDelta.Y);
        SetHeight(_box.Collapsed ? TitleBarHeight : Math.Max(60, (int)_box.Height));
    }

    private void EndResize(UIElement el)
    {
        if (!_resizing) return;
        _resizing = false;
        el.ReleaseMouseCapture();
        RaiseChanged();
    }

    // ---------- 右键菜单 ----------

    private void ShowItemMenu(BoxItem item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("打开", () => OpenItem(item)));
        menu.Items.Add(MenuItem("打开所在位置", () => OpenLocation(item)));
        menu.Items.Add(MenuItem("属性", () => ShowItemProperties(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("重命名", () => RenameItem(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("从盒子移除", () =>
        {
            _box.Items.Remove(item);
            IconCache.Clear();
            Refresh();
            RaiseChanged();
        }));
        menu.IsOpen = true;
    }

    private void ShowBoxMenu(FrameworkElement placement)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(_box.Collapsed ? "展开" : "折叠", ToggleCollapse));
        menu.Items.Add(MenuItem("重命名盒子", RenameBox));
        menu.Items.Add(MenuItem(_box.Locked ? "解锁" : "锁定", ToggleLock));
        menu.Items.Add(MenuItem("盒子外观…", ShowAppearance));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("新建盒子", () => NewBoxRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("删除盒子", () => DeleteRequested?.Invoke(this)));
        menu.PlacementTarget = placement;
        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string header, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    private void RenameItem(BoxItem item)
    {
        var dlg = new RenameDialog("重命名图标", item.DisplayName);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
        {
            item.DisplayName = dlg.Value.Trim();
            Refresh();
            RaiseChanged();
        }
    }

    private void RenameBox()
    {
        var dlg = new RenameDialog("重命名盒子", _box.Name);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
        {
            _box.Name = dlg.Value.Trim();
            Refresh();
            RaiseChanged();
        }
    }

    private void ToggleLock()
    {
        _box.Locked = !_box.Locked;
        Refresh();
        RaiseChanged();
    }

    private void ShowAppearance()
    {
        var dlg = new AppearanceDialog(_box, _dataDir, supportsTransparency: !IsEmbedded);
        if (dlg.ShowDialog() == true)
        {
            ApplyAppearance();
            RaiseChanged();
        }
    }

    private static void OpenLocation(BoxItem item)
    {
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            MessageBox.Show("项目指向的文件或文件夹已不存在。", "桌面盒子", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
    }

    private static void OpenItem(BoxItem item)
    {
        try
        {
            Launcher.Open(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ShowItemProperties(BoxItem item)
    {
        try
        {
            Launcher.ShowProperties(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法显示属性", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? ResolveDataPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        return Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, _dataDir);
    }

    private void RaiseChanged() => Changed?.Invoke(this);

    private const int WM_SIZE = 0x0005;
    private const int SIZE_MINIMIZED = 1;
    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 抵抗「显示桌面 / Win+D」的最小化：延迟恢复 + 重新置底，避免与最小化竞争
        if (msg == WM_SIZE && wParam.ToInt64() == SIZE_MINIMIZED)
        {
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowWindow(hwnd, SW_RESTORE);
                if (_desktopParent == IntPtr.Zero)
                {
                    DesktopAttacher.PinToBottom(hwnd);
                }
            }));
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>重新应用外观并触发保存。</summary>
    public void RefreshAppearance()
    {
        ApplyAppearance();
        RaiseChanged();
    }

    /// <summary>仅重绘视图（标题/图标/锁定态）并触发保存。</summary>
    public void RefreshView()
    {
        Refresh();
        RaiseChanged();
    }

    public void Dispose()
    {
        _animTimer.Stop();
        _source.Dispose();
    }
}
