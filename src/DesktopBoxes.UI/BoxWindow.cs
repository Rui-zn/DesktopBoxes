using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopBoxes.Core;
using DesktopBoxes.Win32;

namespace DesktopBoxes.UI;

/// <summary>
/// 一个盒子窗口：基于 HwndSource 嵌入桌面层，标题栏 + 图标网格，
/// 支持移动/缩放/折叠动画/拖入拖出/双击打开/右键菜单/外观。
/// </summary>
public class BoxWindow : IDisposable
{
    private const int TitleBarHeight = (int)LayoutHelper.TitleBarHeight;
    private const int AnimDurationMs = 160;
    private const int MinimumWidth = 220;

    private int IconSize => _box.IconSize is >= 24 and <= 96 ? _box.IconSize : 32;

    private readonly HwndSource _source;
    private readonly Box _box;
    private string _dataDir;
    private BoxFileTransfers? _fileTransfers;
    private static DragSession? _fileDrag;
    private const string BoxDragFormat = "DesktopBoxes.FileMove.Session";
    public static bool IsFileDragActive => _fileDrag != null;
    private sealed record DragSession(string Token, Box Box, BoxItem Item);
    private readonly IntPtr _desktopParent;

    private readonly Border _root;
    private readonly Border _titleBar;
    private readonly TextBlock _title;
    private readonly TextBlock _count;
    private readonly Button _lockBtn;
    private readonly Border _countBadge;
    private readonly TextBlock _emptyState;
    private readonly Button _collapseBtn;
    private readonly Button _menuBtn;
    private readonly Border _resizeGrip;
    private readonly ScrollViewer _scroll;
    private readonly WrapPanel _itemsWrap;
    private readonly System.Windows.Threading.DispatcherTimer _animTimer;
    private readonly RoundedWindowRegion _windowRegion = new();
    private bool _regionUpdatePending;
    private bool _disposed;
    private int _currentHeight;

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
    public bool IsDesktopAttached => _desktopParent != IntPtr.Zero;

    public void UpdateDataDirectory(string dataDir, BoxFileTransfers? fileTransfers = null)
    {
        _dataDir = dataDir;
        _fileTransfers = fileTransfers;
        RefreshContents();
    }

    /// <summary>任意变更后触发，供上层持久化。</summary>
    public event Action<BoxWindow>? Changed;
    public event Action<BoxWindow>? DeleteRequested;
    public event Action? NewBoxRequested;

    public BoxWindow(Box box, string dataDir, IntPtr desktopParent = default, BoxFileTransfers? fileTransfers = null)
    {
        _box = box;
        _dataDir = dataDir;
        _fileTransfers = fileTransfers;
        _desktopParent = desktopParent;
        _box.Width = Math.Max(MinimumWidth, _box.Width);

        _root = new Border
        {
            BorderBrush = UiTheme.Brush("#525B73"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(UiTheme.BoxRadius(box.CornerRadius)),
            AllowDrop = true,
        };
        UiTheme.Install(_root);
        TextElement.SetFontFamily(_root, new FontFamily("Segoe UI, Microsoft YaHei UI"));

        _title = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _collapseBtn = new Button
        {
            Width = 28,
            Height = 28,
            MinHeight = 28,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
        };
        _collapseBtn.Click += (_, _) => ToggleCollapse();
        _collapseBtn.SetResourceReference(FrameworkElement.StyleProperty, "BoxToolbarButton");
        _menuBtn = new Button
        {
            Content = "\uE712",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 28,
            Height = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 6, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = "盒子选项",
        };
        System.Windows.Automation.AutomationProperties.SetName(_menuBtn, "盒子选项");
        _menuBtn.SetResourceReference(FrameworkElement.StyleProperty, "BoxToolbarButton");
        _menuBtn.Click += (_, _) => ShowBoxMenu(_menuBtn);
        _count = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        _countBadge = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _count,
        };
        _lockBtn = new Button();
        _lockBtn.SetResourceReference(FrameworkElement.StyleProperty, "BoxToolbarButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_lockBtn, "BoxLockToggle");
        _lockBtn.Click += (_, _) => ToggleLock();

        _titleBar = new Border
        {
            Height = TitleBarHeight - 2,
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Child = new DockPanel(),
        };
        var dock = (DockPanel)_titleBar.Child;
        DockPanel.SetDock(_menuBtn, Dock.Right);
        dock.Children.Add(_menuBtn);
        DockPanel.SetDock(_lockBtn, Dock.Right);
        dock.Children.Add(_lockBtn);
        DockPanel.SetDock(_countBadge, Dock.Right);
        dock.Children.Add(_countBadge);
        DockPanel.SetDock(_collapseBtn, Dock.Left);
        dock.Children.Add(_collapseBtn);
        dock.Children.Add(_title);

        _itemsWrap = new WrapPanel { Margin = new Thickness(10, 10, 10, 16) };
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
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "拖动调整大小",
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 3 11 L 11 3 M 8 11 L 11 8"),
                Stroke = new SolidColorBrush(Color.FromRgb(120, 128, 144)),
                StrokeThickness = 1.5,
            },
        };
        _resizeGrip = resizeGrip;
        _emptyState = new TextBlock
        {
            Text = "＋\n拖入文件，收好常用\n应用 · 文件夹 · 快捷方式",
            FontSize = 12,
            LineHeight = 26,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14),
            Opacity = 0.7,
            IsHitTestVisible = false,
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight - 2) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_titleBar, 0);
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(resizeGrip, 1);
        layout.Children.Add(_titleBar);
        layout.Children.Add(_scroll);
        Grid.SetRow(_emptyState, 1);
        layout.Children.Add(_emptyState);
        layout.Children.Add(resizeGrip);

        _root.Child = layout;

        _animTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        _animTimer.Tick += (_, _) => AnimTick();

        var p = new HwndSourceParameters("DesktopBoxes.Box." + box.Id)
        {
            // 桌面模式使用 WorkerW/Progman 作为所有者，同时保持顶层窗口，
            // 以便 WPF 提供背景逐像素透明；找不到桌面宿主时仍为置底顶层窗口。
            ParentWindow = desktopParent,
            WindowStyle = DesktopAttacher.WS_POPUP | DesktopAttacher.WS_VISIBLE,
            Width = Math.Max(MinimumWidth, (int)box.Width),
            Height = Math.Max(60, (int)box.Height),
            PositionX = 0,
            PositionY = 0,
            UsesPerPixelOpacity = true,
            ExtendedWindowStyle = DesktopAttacher.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(p) { RootVisual = _root };
        _source.AddHook(WndProc);
        _root.SizeChanged += (_, _) => UpdateRoundedCorners();
        // WorkerW 所有权负责桌面层级；无桌面宿主时才钉到普通窗口底层。
        if (desktopParent == IntPtr.Zero)
        {
            DesktopAttacher.PinToBottom(_source.Handle);
        }

        WireEvents(_titleBar, resizeGrip);
        ApplyAppearance();
        ApplySize();
    }

    private void WireEvents(Border titleBar, Border resizeGrip)
    {
        void DragFeedback(object sender, DragEventArgs e)
        {
            e.Effects = DropEffect(e.Data, e.AllowedEffects, e.KeyStates);
            _root.BorderBrush = e.Effects == DragDropEffects.Move ? UiTheme.Brush("#9C94FF") : FrameBrush();
            e.Handled = true;
        }
        _root.PreviewDragEnter += DragFeedback;
        _root.PreviewDragOver += DragFeedback;
        _root.DragLeave += (_, _) => _root.BorderBrush = FrameBrush();
        _root.PreviewDrop += (_, e) => ReceiveDrop(e);

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
        _textBrush = BoxPreview.Parse(_box.TextColor, UiTheme.BoxText);

        _root.Background = BoxPreview.BackgroundBrush(_box, _dataDir);
        _titleBar.Background = BoxPreview.Parse(_box.TitleBarColor, UiTheme.BoxHeader);

        _title.Foreground = _textBrush;
        _collapseBtn.Foreground = _textBrush;
        _menuBtn.Foreground = _textBrush;
        _count.Foreground = _textBrush;
        _lockBtn.Foreground = _textBrush;
        _emptyState.Foreground = _textBrush;

        var badgeBrush = _textBrush.Clone();
        badgeBrush.Opacity = 0.08;
        _countBadge.Background = badgeBrush;
        _root.BorderBrush = FrameBrush();
        int r = UiTheme.BoxRadius(_box.CornerRadius);
        _root.CornerRadius = new CornerRadius(r);
        _titleBar.CornerRadius = new CornerRadius(r, r, 0, 0);
        double opacity = Math.Clamp(_box.Opacity, 0, 100) / 100.0;
        _root.Background.Opacity = opacity;
        _titleBar.Background.Opacity = opacity;

        Refresh();
        UpdateRoundedCorners();
    }

    private Brush FrameBrush()
    {
        var brush = _textBrush.Clone();
        brush.Opacity = 0.22;
        return brush;
    }

    private void UpdateRoundedCorners()
    {
        if (_disposed) return;
        double radius = UiTheme.BoxRadius(_box.CornerRadius);
        if (_root.ActualWidth > 0 && _root.ActualHeight > 0)
            _root.Clip = new RectangleGeometry(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), radius, radius);
        // WPF clipping alone leaves a rectangular opaque child HWND behind it.
        _windowRegion.Update(_source.Handle, radius);
    }

    // ---------- 渲染 ----------

    private void Refresh()
    {
        _titleBar.CornerRadius = _box.Collapsed ? _root.CornerRadius
            : new CornerRadius(_root.CornerRadius.TopLeft, _root.CornerRadius.TopRight, 0, 0);
        _title.Text = _box.Name;
        _title.ToolTip = _box.Name;
        _count.Text = _box.Items.Count > 99 ? "99+" : _box.Items.Count.ToString();
        _countBadge.ToolTip = $"{_box.Items.Count} 个项目";
        _collapseBtn.Content = _box.Collapsed ? "\uE76C" : "\uE70D";
        _collapseBtn.ToolTip = _box.Collapsed ? "展开盒子" : "折叠盒子";
        System.Windows.Automation.AutomationProperties.SetName(_collapseBtn, (string)_collapseBtn.ToolTip);
        _lockBtn.Content = _box.Locked ? "\uE72E" : "\uE785";
        _lockBtn.ToolTip = _box.Locked ? "已锁定位置和大小 · 点击解锁" : "可移动和缩放 · 点击锁定";
        System.Windows.Automation.AutomationProperties.SetName(_lockBtn, _box.Locked ? "解锁盒子" : "锁定盒子");
        System.Windows.Automation.AutomationProperties.SetHelpText(_lockBtn, (string)_lockBtn.ToolTip);
        var lockBrush = _textBrush.Clone();
        lockBrush.Opacity = _box.Locked ? 0.16 : 0;
        _lockBtn.Background = lockBrush;
        _titleBar.Cursor = _box.Locked ? Cursors.Arrow : Cursors.SizeAll;
        if (_box.Locked)
        {
            _dragging = _resizing = false;
            if (_titleBar.IsMouseCaptured) _titleBar.ReleaseMouseCapture();
            if (_resizeGrip.IsMouseCaptured) _resizeGrip.ReleaseMouseCapture();
        }
        _emptyState.Visibility = !_box.Collapsed && _box.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _resizeGrip.Visibility = _box.Locked || _box.Collapsed ? Visibility.Collapsed : Visibility.Visible;
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
            Margin = new Thickness(0, 6, 0, 8),
        };
        var label = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 12,
            Foreground = _textBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = size + 36,
            TextAlignment = TextAlignment.Center,
        };
        var stack = new StackPanel { Width = size + 38, Margin = new Thickness(2, 2, 2, 7) };
        stack.Children.Add(image);
        stack.Children.Add(label);

        var border = new Border
        {
            Background = ItemBackground(false),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(2),
            Child = stack,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = $"{item.DisplayName}\n{item.Path}",
        };
        System.Windows.Automation.AutomationProperties.SetName(border, item.DisplayName);
        void Highlight(bool active)
        {
            border.Background = ItemBackground(active);
            border.BorderBrush = border.IsKeyboardFocused ? UiTheme.Brush("#9C94FF") : Brushes.Transparent;
        }
        border.MouseEnter += (_, _) => Highlight(true);
        border.MouseLeave += (_, _) => Highlight(border.IsKeyboardFocused);
        border.GotKeyboardFocus += (_, _) => Highlight(true);
        border.LostKeyboardFocus += (_, _) => Highlight(border.IsMouseOver);
        border.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenItem(item); e.Handled = true; }
            else if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
            {
                ShowItemMenu(item);
                e.Handled = true;
            }
        };

        border.MouseLeftButtonDown += (_, e) =>
        {
            border.Focus();
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
                DragItem(border, item);
            }
        };

        return border;
    }

    private Brush ItemBackground(bool active)
    {
        var brush = _textBrush.Clone();
        brush.Opacity = active ? 0.11 : 0.035;
        return brush;
    }

    // ---------- 内容变更 ----------

    private DragDropEffects DropEffect(IDataObject data, DragDropEffects allowed, DragDropKeyStates keys)
    {
        if (_fileTransfers == null || _fileTransfers.IsBusy || (allowed & DragDropEffects.Move) == 0 ||
            (keys & (DragDropKeyStates.ControlKey | DragDropKeyStates.AltKey)) != 0) return DragDropEffects.None;
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
            if (_fileDrag != null && Equals(data.GetData(BoxDragFormat), _fileDrag.Token) && _fileDrag.Box == _box)
                return DragDropEffects.None;
            return DragDropEffects.Move;
        }
        catch { return DragDropEffects.None; }
    }

    private void ReceiveDrop(DragEventArgs e)
    {
        bool accept = DropEffect(e.Data, e.AllowedEffects, e.KeyStates) == DragDropEffects.Move;
        e.Handled = true;
        // Optimized move: we move the file, so the source must NOT delete it again.
        // https://learn.microsoft.com/windows/win32/shell/datascenarios#handling-optimized-move-operations
        e.Effects = DragDropEffects.None;
        _root.BorderBrush = FrameBrush();
        if (!accept) return;
        var failures = new List<string>();
        bool completed = false;
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var session = _fileDrag != null && Equals(e.Data.GetData(BoxDragFormat), _fileDrag.Token) ? _fileDrag : null;
                    _fileTransfers!.Import(_box, path, session?.Box, session?.Item);
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(path)}：{ex.Message}");
                    if (ex is OperationCanceledException || ex.InnerException is OperationCanceledException) break;
                }
            }
            completed = failures.Count == 0 && paths.Length > 0;
        }
        catch (Exception ex) { failures.Add(ex.Message); }
        finally
        {
            // Notify only AFTER extracting/moving files: a real Shell data object may
            // release its file list as soon as it receives performed-effect feedback.
            try { SetDropEffect(e.Data, "Performed DropEffect", DragDropEffects.None); }
            catch { /* e.Effects is already None, so no source deletion is requested. */ }
            if (completed)
                try { SetDropEffect(e.Data, "Logical Performed DropEffect", DragDropEffects.Move); }
                catch { /* Logical effect is advisory; the optimized move has completed. */ }
            RefreshContents();
            RaiseChanged();
        }
        if (failures.Count > 0) MessageBox.Show(string.Join("\n\n", failures), "部分项目未完成移动", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void SetDropEffect(IDataObject data, string format, DragDropEffects effect) =>
        data.SetData(format, new MemoryStream(BitConverter.GetBytes((int)effect)), false);

    private void DragItem(FrameworkElement source, BoxItem item)
    {
        if (_fileTransfers == null || _fileTransfers.IsBusy || _fileDrag != null) return;
        try
        {
            _fileTransfers.ValidateForExport(item);
            _fileDrag = new DragSession(Guid.NewGuid().ToString("N"), _box, item);
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            data.SetData(BoxDragFormat, _fileDrag.Token, false);
            SetDropEffect(data, "Preferred DropEffect", DragDropEffects.Move);
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
            _fileTransfers.CompleteExternalMove(_box, item);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "文件移动未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _fileDrag = null; RefreshContents(); RaiseChanged(); }
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
        _currentHeight = h;
        var toDevice = _source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        Point screen = toDevice.Transform(new Point(_box.X, _box.Y));
        Point size = toDevice.Transform(new Point(Math.Max(MinimumWidth, _box.Width), h));
        DesktopAttacher.SetWindowPosition(
            _source.Handle,
            (int)Math.Round(screen.X),
            (int)Math.Round(screen.Y),
            Math.Max(1, (int)Math.Round(size.X)),
            Math.Max(1, (int)Math.Round(size.Y)));
        UpdateRoundedCorners();

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
        int from = _currentHeight;
        _animTimer.Stop();
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
        if (!_dragging || _box.Locked) return;
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
        if (!_resizing || _box.Locked) return;
        Point now = _root.PointToScreen(e.GetPosition(_root));
        Vector deviceDelta = now - _dragScreenStart;
        Vector logicalDelta = (_source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(deviceDelta);
        _box.Width = Math.Max(MinimumWidth, _resizeStartW + logicalDelta.X);
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
        var menu = CreateMenu();
        menu.Items.Add(MenuItem("打开", () => OpenItem(item)));
        menu.Items.Add(MenuItem("打开所在位置", () => OpenLocation(item)));
        menu.Items.Add(MenuItem("属性", () => ShowItemProperties(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("重命名", () => RenameItem(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("移回桌面", () =>
        {
            try { _fileTransfers?.ReturnToDirectory(_box, item, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "移回桌面未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { RefreshContents(); RaiseChanged(); }
        }));
        if (item.StoragePath == null)
            menu.Items.Add(MenuItem("仅移除旧版引用（保留原文件）", () => { _box.Items.Remove(item); RefreshContents(); RaiseChanged(); }));
        menu.IsOpen = true;
    }

    private void ShowBoxMenu(FrameworkElement placement)
    {
        var menu = CreateMenu();
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
        var mi = new MenuItem { Header = header, Padding = new Thickness(10, 6, 18, 6) };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    private static ContextMenu CreateMenu() => new()
    {
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
        FontSize = 13,
        Padding = new Thickness(4),
        Background = UiTheme.Brush("#FAFAFD"),
        Foreground = UiTheme.Ink,
        BorderBrush = UiTheme.Brush("#DADFEB"),
    };

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
        var dlg = new AppearanceDialog(_box, _dataDir, supportsTransparency: true);
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

    private void RaiseChanged() => Changed?.Invoke(this);

    private const int WM_SIZE = 0x0005;
    private const int SIZE_MINIMIZED = 1;
    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_SIZE / WM_DPICHANGED / WM_DPICHANGED_AFTERPARENT: coalesce until WPF
        // has processed the new layout and scale. Never consume these messages.
        if ((msg == WM_SIZE || msg == 0x02E0 || msg == 0x02E3) && !_regionUpdatePending && !_disposed)
        {
            _regionUpdatePending = true;
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                _regionUpdatePending = false;
                UpdateRoundedCorners();
            }));
        }
        // 抵抗「显示桌面 / Win+D」的最小化：延迟恢复 + 重新置底，避免与最小化竞争
        if (msg == WM_SIZE && wParam.ToInt64() == SIZE_MINIMIZED)
        {
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
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

    /// <summary>Refresh after file moves without scheduling another save.</summary>
    public void RefreshContents()
    {
        if (!_disposed) Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animTimer.Stop();
        _source.Dispose();
    }
}
