using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopBoxes.Core;

namespace DesktopBoxes.UI;

/// <summary>Desktop dashboard. Selection is preserved by box identity across refreshes.</summary>
public class MasterWindow : Window
{
    private readonly IReadOnlyList<BoxWindow> _windows;
    private string _dataDir;
    private readonly Action _onChanged;
    private readonly Action _onNewBox;
    private readonly Action<BoxWindow> _onDeleteBox;
    private readonly ListBox _list;
    private readonly TextBox _search = new();
    private readonly StackPanel _details = new();
    private readonly TextBlock _summary = UiTheme.Text("", 13, UiTheme.Muted);
    private readonly TextBlock _empty = UiTheme.Text("", 14, UiTheme.Muted);
    private readonly TextBlock _storage = UiTheme.Text("", 11, UiTheme.Brush("#AEB8D0"));
    private readonly Button _lockAll;
    private readonly Button _unified;
    private readonly HashSet<BoxWindow> _subscriptions = new();

    public MasterWindow(IReadOnlyList<BoxWindow> windows, string dataDir, Action onChanged,
        Action onNewBox, Action<BoxWindow> onDeleteBox, Func<bool> isAutoStartEnabled,
        Action<bool> setAutoStart, Func<bool> isExiting)
    {
        _windows = windows;
        _dataDir = dataDir;
        _onChanged = onChanged;
        _onNewBox = onNewBox;
        _onDeleteBox = onDeleteBox;
        UiTheme.Window(this);
        Title = "DesktopBoxes · 桌面空间";
        Width = 1000;
        Height = 700;
        MinWidth = 840;
        MinHeight = 570;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var rail = new DockPanel { Margin = new Thickness(20, 30, 20, 24) };
        var brand = new StackPanel();
        brand.Children.Add(UiTheme.Text("▦", 34, UiTheme.Brush("#A79FFF")));
        brand.Children.Add(UiTheme.Text("DesktopBoxes", 19, Brushes.White));
        var tagline = UiTheme.Text("给桌面一点秩序", 12, UiTheme.Brush("#AEB8D0"));
        tagline.Margin = new Thickness(0, 8, 0, 38);
        brand.Children.Add(tagline);
        var active = UiTheme.Button("▦   我的盒子", () => _search.Clear());
        active.Background = UiTheme.Brush("#343754");
        active.Foreground = Brushes.White;
        active.BorderThickness = new Thickness(0);
        active.HorizontalContentAlignment = HorizontalAlignment.Left;
        brand.Children.Add(active);
        DockPanel.SetDock(brand, Dock.Top);
        rail.Children.Add(brand);
        var railBottom = new StackPanel();
        railBottom.Children.Add(UiTheme.Text("应用设置", 12, UiTheme.Brush("#E4E7F0")));
        var autoStart = new CheckBox
        {
            Content = "开机自动启动",
            IsChecked = isAutoStartEnabled(),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 12, 0, 24),
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "登录 Windows 后自动启动桌面盒子",
        };
        System.Windows.Automation.AutomationProperties.SetName(autoStart, "开机自动启动");
        autoStart.Click += (_, _) =>
        {
            bool enabled = autoStart.IsChecked == true;
            try
            {
                setAutoStart(enabled);
            }
            catch (Exception ex)
            {
                autoStart.IsChecked = !enabled;
                MessageBox.Show($"开机自启设置失败：{ex.Message}", "桌面盒子", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        railBottom.Children.Add(autoStart);
        railBottom.Children.Add(UiTheme.Text("使用小提示", 12, UiTheme.Brush("#E4E7F0")));
        var tips = UiTheme.Text("拖入文件即可收纳\n双击图标打开\n拖动标题栏调整位置", 12, UiTheme.Brush("#AEB8D0"));
        tips.LineHeight = 23;
        tips.Margin = new Thickness(0, 10, 0, 26);
        railBottom.Children.Add(tips);
        railBottom.Children.Add(UiTheme.Text("数据位置", 11, UiTheme.Brush("#AEB8D0")));
        _storage.Margin = new Thickness(0, 6, 0, 0);
        _storage.TextTrimming = TextTrimming.CharacterEllipsis;
        _storage.TextWrapping = TextWrapping.NoWrap;
        railBottom.Children.Add(_storage);
        DockPanel.SetDock(railBottom, Dock.Bottom);
        rail.Children.Add(railBottom);
        rail.Children.Add(new Border());
        root.Children.Add(new Border { Background = UiTheme.Brush("#202638"), Child = rail });

        var body = new Grid { Margin = new Thickness(30, 28, 30, 22) };
        Grid.SetColumn(body, 1);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 25) };
        var create = UiTheme.Button("＋  新建盒子", CreateBox, true);
        create.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(create, Dock.Right);
        heading.Children.Add(create);
        var titles = new StackPanel();
        var title = UiTheme.Text("你的桌面空间", 27);
        title.FontWeight = FontWeights.SemiBold;
        titles.Children.Add(title);
        _summary.Margin = new Thickness(0, 10, 0, 0);
        titles.Children.Add(_summary);
        heading.Children.Add(titles);
        body.Children.Add(heading);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.ToolTip = "按盒子名称搜索";
        System.Windows.Automation.AutomationProperties.SetName(_search, "搜索盒子");
        var searchHost = new Grid();
        searchHost.Children.Add(_search);
        var placeholder = UiTheme.Text("搜索盒子…", 13, UiTheme.Muted);
        placeholder.Margin = new Thickness(12, 0, 0, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        searchHost.Children.Add(placeholder);
        _search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshList();
        };
        toolbar.Children.Add(searchHost);
        _unified = UiTheme.Button("统一外观", ApplyUnifiedAppearance);
        _unified.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_unified, 1);
        toolbar.Children.Add(_unified);
        Grid.SetRow(toolbar, 1);
        body.Children.Add(toolbar);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(268) });
        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ItemTemplate = (DataTemplate)FindResource("BoxCard"),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetName(_list, "桌面盒子列表");
        _list.SelectionChanged += (_, _) => RefreshDetails();
        workspace.Children.Add(_list);
        _empty.Margin = new Thickness(18, 36, 18, 0);
        _empty.TextAlignment = TextAlignment.Center;
        _empty.IsHitTestVisible = false;
        workspace.Children.Add(_empty);
        var detailCard = UiTheme.Card(new ScrollViewer
        {
            Content = _details,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        }, 16);
        Grid.SetColumn(detailCard, 2);
        workspace.Children.Add(detailCard);
        Grid.SetRow(workspace, 2);
        body.Children.Add(workspace);
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        _lockAll = UiTheme.Button("锁定全部位置", ToggleAllLock);
        DockPanel.SetDock(_lockAll, Dock.Right);
        footer.Children.Add(_lockAll);
        var note = UiTheme.Text("关闭此窗口后，盒子仍留在桌面。", 11, UiTheme.Muted);
        note.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(note);
        Grid.SetRow(footer, 3);
        body.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        Closing += (_, e) => { if (!isExiting()) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { foreach (var w in _subscriptions) w.Changed -= OnBoxChanged; };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        UpdateDataDirectory(dataDir);
        RefreshList();
    }

    private void OnBoxChanged(BoxWindow window)
    {
        if (IsVisible) Dispatcher.BeginInvoke(new Action(RefreshList));
    }

    private void CreateBox()
    {
        _search.Clear();
        _onNewBox();
        RefreshList();
        _list.SelectedItem = _windows.LastOrDefault();
    }

    public void RefreshList()
    {
        foreach (var stale in _subscriptions.Where(w => !_windows.Contains(w)).ToList())
        {
            stale.Changed -= OnBoxChanged;
            _subscriptions.Remove(stale);
        }
        foreach (var window in _windows)
            if (_subscriptions.Add(window)) window.Changed += OnBoxChanged;
        Guid? selected = (_list.SelectedItem as BoxWindow)?.Box.Id;
        var filtered = _windows.Where(w => w.Box.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = filtered;
        _list.SelectedItem = filtered.FirstOrDefault(w => w.Box.Id == selected) ?? filtered.FirstOrDefault();
        _summary.Text = $"{_windows.Count} 个盒子   ·   {_windows.Sum(w => w.Box.Items.Count)} 个项目   ·   {_windows.Count(w => w.Box.Locked)} 个已锁定";
        _empty.Text = _windows.Count == 0 ? "桌面，从一个盒子开始。\n点击“新建盒子”添加你的第一个空间。" : "没有找到匹配的盒子。\n试试其他名称。";
        _empty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _lockAll.Content = _windows.Count > 0 && _windows.All(w => w.Box.Locked) ? "解锁全部位置" : "锁定全部位置";
        _lockAll.IsEnabled = _unified.IsEnabled = _windows.Count > 0;
        RefreshDetails();
    }

    public void UpdateDataDirectory(string dataDir)
    {
        _dataDir = dataDir;
        _storage.Text = Path.GetFileName(dataDir.TrimEnd(Path.DirectorySeparatorChar));
        _storage.ToolTip = dataDir;
    }

    private void RefreshDetails()
    {
        _details.Children.Clear();
        if (_list.SelectedItem is not BoxWindow window)
        {
            _details.Children.Add(UiTheme.Heading("盒子详情"));
            _details.Children.Add(UiTheme.Text("选择一个盒子，查看预览并调整它的外观。", 13, UiTheme.Muted));
            return;
        }
        _details.Children.Add(UiTheme.Text("盒子预览", 11, UiTheme.Muted));
        _details.Children.Add(new Viewbox
        {
            Child = new BoxPreview(window.Box, _dataDir) { Width = 320 },
            Height = 135,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 12),
        });
        var name = UiTheme.Text(window.Box.Name, 16);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = window.Box.Name;
        _details.Children.Add(name);
        var metadata = UiTheme.Text($"{window.Box.Items.Count} 个项目 · {(window.Box.Collapsed ? "已折叠" : "已展开")}", 12, UiTheme.Muted);
        metadata.Margin = new Thickness(0, 4, 0, 12);
        _details.Children.Add(metadata);
        AddDetailButton("调整外观", () => ShowAppearance(window), true);
        var secondary = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        secondary.ColumnDefinitions.Add(new ColumnDefinition());
        secondary.ColumnDefinitions.Add(new ColumnDefinition());
        var rename = UiTheme.Button("重命名", () =>
        {
            var dialog = new RenameDialog("重命名盒子", window.Box.Name) { Owner = this };
            if (dialog.ShowDialog() == true) { window.Box.Name = dialog.Value.Trim(); window.RefreshView(); RefreshList(); }
        });
        rename.Margin = new Thickness(0, 0, 4, 0);
        secondary.Children.Add(rename);
        var lockButton = UiTheme.Button(window.Box.Locked ? "解锁位置" : "锁定位置", () =>
        {
            window.Box.Locked = !window.Box.Locked;
            window.RefreshView();
            RefreshList();
        });
        lockButton.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(lockButton, 1);
        secondary.Children.Add(lockButton);
        _details.Children.Add(secondary);
        var delete = UiTheme.Button("删除盒子", () => { _onDeleteBox(window); RefreshList(); });
        delete.Foreground = UiTheme.Brush("#B54750");
        delete.Margin = new Thickness(0, 12, 0, 0);
        _details.Children.Add(delete);
    }

    private void AddDetailButton(string text, Action action, bool primary = false)
    {
        var button = UiTheme.Button(text, action, primary);
        button.Margin = new Thickness(0, 0, 0, 8);
        _details.Children.Add(button);
    }

    private void ShowAppearance(BoxWindow window)
    {
        var dialog = new AppearanceDialog(window.Box, _dataDir, true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        window.RefreshAppearance();
        RefreshList();
        _onChanged();
    }

    private void ApplyUnifiedAppearance()
    {
        if (_windows.Count == 0) return;
        var template = new Box { Name = "统一外观预览", Items = _windows[0].Box.Items };
        CopyAppearance(_windows[0].Box, template);
        var dialog = new AppearanceDialog(template, _dataDir, true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var window in _windows) { CopyAppearance(template, window.Box); window.RefreshAppearance(); }
        RefreshList();
        _onChanged();
    }

    private void ToggleAllLock()
    {
        bool target = !_windows.All(w => w.Box.Locked);
        foreach (var window in _windows) { window.Box.Locked = target; window.RefreshView(); }
        RefreshList();
        _onChanged();
    }

    private static void CopyAppearance(Box from, Box to)
    {
        to.BackgroundColor = from.BackgroundColor;
        to.TitleBarColor = from.TitleBarColor;
        to.TextColor = from.TextColor;
        to.BackgroundImagePath = from.BackgroundImagePath;
        to.BackgroundImageMode = from.BackgroundImageMode;
        to.IconSize = from.IconSize;
        to.CornerRadius = from.CornerRadius;
        to.Opacity = from.Opacity;
    }
}
