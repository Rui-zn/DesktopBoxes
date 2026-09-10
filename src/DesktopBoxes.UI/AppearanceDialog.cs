using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopBoxes.Core;
using Microsoft.Win32;

namespace DesktopBoxes.UI;

/// <summary>Appearance editor with a local draft. Only Save updates the user's box.</summary>
public class AppearanceDialog : Window
{
    private readonly Box _box;
    private readonly Box _draft;
    private readonly string _dataDir;
    private readonly bool _supportsTransparency;
    private readonly Border _preview = new();
    private readonly TextBlock _imageName = UiTheme.Text("", 12, UiTheme.Muted);
    private readonly TextBlock _error = UiTheme.Text("", 12, UiTheme.Brush("#B54750"));
    private readonly List<Action> _selectionUpdates = new();
    private readonly ComboBox _mode = new();
    private string? _pendingImageSource;

    public AppearanceDialog(Box box, string dataDir, bool supportsTransparency = true)
    {
        _box = box;
        _dataDir = dataDir;
        _supportsTransparency = supportsTransparency;
        _draft = new Box
        {
            Name = box.Name,
            Locked = box.Locked,
            Items = box.Items,
            BackgroundColor = box.BackgroundColor,
            TitleBarColor = box.TitleBarColor,
            TextColor = box.TextColor,
            BackgroundImagePath = box.BackgroundImagePath,
            BackgroundImageMode = box.BackgroundImageMode ?? "UniformToFill",
            IconSize = box.IconSize,
            CornerRadius = UiTheme.BoxRadius(box.CornerRadius),
            Opacity = box.Opacity,
        };
        UiTheme.Window(this);
        Title = "盒子外观 · DesktopBoxes";
        Width = 860;
        Height = 720;
        MinWidth = 760;
        MinHeight = 530;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(28, 22, 28, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        var title = UiTheme.Text("让盒子成为你的风格", 25);
        title.FontWeight = FontWeights.SemiBold;
        intro.Children.Add(title);
        var subtitle = UiTheme.Text("选择主题，微调细节。满意后再保存。", 13, UiTheme.Muted);
        subtitle.Margin = new Thickness(0, 8, 0, 0);
        intro.Children.Add(subtitle);
        root.Children.Add(intro);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        var settings = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
        var themeTitle = UiTheme.Heading("01  主题与配色");
        themeTitle.Margin = new Thickness(0, 0, 0, 12);
        settings.Children.Add(themeTitle);
        var themes = new WrapPanel();
        AddTheme(themes, "午夜", UiTheme.BoxBackground, UiTheme.BoxHeader, UiTheme.BoxText);
        AddTheme(themes, "云白", "#F5F6FA", "#E8EBF3", "#263147");
        AddTheme(themes, "松林", "#203631", "#2C4840", "#E7F3EC");
        AddTheme(themes, "暮紫", "#302C43", "#413A58", "#F2ECFF");
        settings.Children.Add(themes);
        settings.Children.Add(UiTheme.Heading("背景颜色"));
        settings.Children.Add(ColorRow(() => _draft.BackgroundColor, c => _draft.BackgroundColor = c,
            UiTheme.BoxBackground, new[] { "#202534", "#F5F6FA", "#203631", "#302C43", "#23384D", "#443330" }));
        settings.Children.Add(UiTheme.Heading("标题栏颜色"));
        settings.Children.Add(ColorRow(() => _draft.TitleBarColor, c => _draft.TitleBarColor = c,
            UiTheme.BoxHeader, new[] { "#292F42", "#E8EBF3", "#2C4840", "#413A58", "#354E67", "#60473F" }));
        settings.Children.Add(UiTheme.Heading("文字颜色"));
        settings.Children.Add(ColorRow(() => _draft.TextColor, c => _draft.TextColor = c,
            UiTheme.BoxText, new[] { "#F1F3FA", "#263147", "#E7F3EC", "#F2ECFF", "#FFF1DC" }));
        settings.Children.Add(UiTheme.Heading("02  图标与布局"));
        settings.Children.Add(Choices(new[] { ("紧凑 · 32", 32), ("舒适 · 48", 48), ("大图 · 64", 64) },
            () => _draft.IconSize, value => _draft.IconSize = value));
        settings.Children.Add(UiTheme.Heading("圆角"));
        settings.Children.Add(Choices(new[] { ("小 · 8", 8), ("标准 · 16", 16), ("大 · 24", 24) },
            () => _draft.CornerRadius, value => _draft.CornerRadius = value));
        if (supportsTransparency)
        {
            settings.Children.Add(UiTheme.Heading("背景不透明度"));
            var opacityRow = new DockPanel();
            var opacityValue = UiTheme.Text($"{_draft.Opacity}%", 12, UiTheme.Muted);
            opacityValue.Width = 48;
            opacityValue.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(opacityValue, Dock.Right);
            opacityRow.Children.Add(opacityValue);
            var opacity = new Slider { Minimum = 0, Maximum = 100, Value = _draft.Opacity, TickFrequency = 5, IsSnapToTickEnabled = true };
            System.Windows.Automation.AutomationProperties.SetName(opacity, "背景不透明度");
            opacity.ValueChanged += (_, _) => { _draft.Opacity = (int)opacity.Value; opacityValue.Text = $"{_draft.Opacity}%"; UpdatePreview(); };
            opacityRow.Children.Add(opacity);
            settings.Children.Add(opacityRow);
        }
        settings.Children.Add(UiTheme.Heading("03  背景图片"));
        var imageButtons = new WrapPanel();
        var pick = UiTheme.Button("选择图片…", PickImage);
        pick.Margin = new Thickness(0, 0, 8, 0);
        imageButtons.Children.Add(pick);
        imageButtons.Children.Add(UiTheme.Button("移除图片", () =>
        {
            _draft.BackgroundImagePath = null;
            _pendingImageSource = null;
            UpdatePreview();
        }));
        settings.Children.Add(imageButtons);
        _imageName.Margin = new Thickness(0, 8, 0, 12);
        _imageName.TextTrimming = TextTrimming.CharacterEllipsis;
        _imageName.TextWrapping = TextWrapping.NoWrap;
        settings.Children.Add(_imageName);
        foreach (var (label, value) in new[] { ("裁剪填充", "UniformToFill"), ("完整显示", "Uniform"), ("拉伸", "Fill"), ("平铺", "Tile"), ("居中", "Center") })
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            _mode.Items.Add(item);
            if (_draft.BackgroundImageMode == value) _mode.SelectedItem = item;
        }
        _mode.SelectionChanged += (_, _) =>
        {
            if (_mode.SelectedItem is ComboBoxItem item) _draft.BackgroundImageMode = (string)item.Tag;
            UpdatePreview();
        };
        System.Windows.Automation.AutomationProperties.SetName(_mode, "背景图片显示方式");
        settings.Children.Add(_mode);
        body.Children.Add(new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var previewPanel = new StackPanel();
        previewPanel.Children.Add(UiTheme.Text("实时预览", 13));
        _preview.Margin = new Thickness(0, 14, 0, 14);
        previewPanel.Children.Add(_preview);
        previewPanel.Children.Add(UiTheme.Text("展示前 6 个项目，实际排列随盒子宽度变化。", 12, UiTheme.Muted));
        var info = UiTheme.Text(supportsTransparency
            ? "可调整盒子不透明度。桌面嵌入模式会让整个盒子一起变淡。"
            : "当前模式暂不支持透明度调整。", 12, UiTheme.Muted);
        info.LineHeight = 21;
        var infoCard = UiTheme.Card(info, 14);
        infoCard.Margin = new Thickness(0, 22, 0, 0);
        previewPanel.Children.Add(infoCard);
        var previewScroll = new ScrollViewer { Content = previewPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(previewScroll, 2);
        body.Children.Add(previewScroll);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = UiTheme.Button("取消", () => { });
        cancel.IsCancel = true;
        cancel.MinWidth = 84;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        buttons.Children.Add(cancel);
        var save = UiTheme.Button("保存外观", Save, true);
        save.IsDefault = true;
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        _error.VerticalAlignment = VerticalAlignment.Center;
        _error.Margin = new Thickness(0, 0, 12, 0);
        footer.Children.Add(_error);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        UpdatePreview();
    }

    private void AddTheme(Panel row, string name, string background, string title, string text)
    {
        var face = new StackPanel();
        var swatch = new Border
        {
            Height = 24,
            Background = UiTheme.Brush(background),
            CornerRadius = new CornerRadius(4),
            BorderBrush = UiTheme.Brush(title),
            BorderThickness = new Thickness(0, 6, 0, 0),
            Margin = new Thickness(0, 0, 0, 8),
        };
        face.Children.Add(swatch);
        face.Children.Add(UiTheme.Text(name, 12));
        var button = UiTheme.Button(name, () =>
        {
            _draft.BackgroundColor = background;
            _draft.TitleBarColor = title;
            _draft.TextColor = text;
            UpdatePreview();
        });
        button.Content = face;
        button.Width = 78;
        button.Margin = new Thickness(0, 0, 8, 8);
        button.Padding = new Thickness(10);
        System.Windows.Automation.AutomationProperties.SetName(button, $"{name}主题");
        _selectionUpdates.Add(() => SetSelected(button,
            (_draft.BackgroundColor ?? UiTheme.BoxBackground) == background &&
            (_draft.TitleBarColor ?? UiTheme.BoxHeader) == title &&
            (_draft.TextColor ?? UiTheme.BoxText) == text));
        row.Children.Add(button);
    }

    private UIElement ColorRow(Func<string?> get, Action<string?> set, string fallback, string[] colors)
    {
        var row = new WrapPanel();
        foreach (string color in colors)
        {
            var button = UiTheme.Button("", () => { set(color); UpdatePreview(); });
            button.Width = 32;
            button.Height = 32;
            button.MinHeight = 32;
            button.Padding = new Thickness(3);
            button.Margin = new Thickness(0, 0, 8, 6);
            button.Background = UiTheme.Brush(color);
            Color parsed = ((SolidColorBrush)button.Background).Color;
            button.Foreground = parsed.R * 0.299 + parsed.G * 0.587 + parsed.B * 0.114 > 150 ? UiTheme.Ink : Brushes.White;
            button.ToolTip = color;
            System.Windows.Automation.AutomationProperties.SetName(button, $"颜色 {color}");
            _selectionUpdates.Add(() => button.Content = (get() ?? fallback) == color ? "✓" : "");
            row.Children.Add(button);
        }
        var reset = UiTheme.Button("默认", () => { set(null); UpdatePreview(); });
        reset.Padding = new Thickness(8, 4, 8, 4);
        reset.MinHeight = 32;
        row.Children.Add(reset);
        return row;
    }

    private UIElement Choices((string Label, int Value)[] choices, Func<int> get, Action<int> set)
    {
        var row = new WrapPanel();
        foreach (var (label, value) in choices)
        {
            var button = UiTheme.Button(label, () => { set(value); UpdatePreview(); });
            button.Margin = new Thickness(0, 0, 8, 6);
            button.Padding = new Thickness(10, 8, 10, 8);
            _selectionUpdates.Add(() => SetSelected(button, get() == value));
            row.Children.Add(button);
        }
        return row;
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.Background = selected ? UiTheme.Brush("#EEEAFE") : Brushes.White;
        button.BorderBrush = selected ? UiTheme.Accent : UiTheme.Brush("#E1E5EF");
        button.Foreground = selected ? UiTheme.Accent : UiTheme.Ink;
    }

    private void UpdatePreview()
    {
        foreach (var update in _selectionUpdates) update();
        _preview.Child = new BoxPreview(_draft, _dataDir);
        _imageName.Text = _draft.BackgroundImagePath == null ? "未使用背景图片" : Path.GetFileName(_draft.BackgroundImagePath);
        _imageName.ToolTip = _draft.BackgroundImagePath;
        _mode.IsEnabled = _draft.BackgroundImagePath != null;
    }

    private void PickImage()
    {
        var dialog = new OpenFileDialog { Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            _ = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            _pendingImageSource = dialog.FileName;
            _draft.BackgroundImagePath = dialog.FileName;
            _error.Text = "";
            UpdatePreview();
        }
        catch { _error.Text = "无法读取这张图片，请选择有效的 JPG、PNG 或 BMP 文件。"; }
    }

    private void Save()
    {
        string? image = _draft.BackgroundImagePath;
        try
        {
            if (_pendingImageSource != null)
            {
                string directory = Path.Combine(_dataDir, "backgrounds");
                Directory.CreateDirectory(directory);
                string target = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(_pendingImageSource));
                File.Copy(_pendingImageSource, target);
                image = Path.GetRelativePath(_dataDir, target);
            }
        }
        catch { _error.Text = "无法保存背景图片，请检查文件及数据目录的访问权限。"; return; }
        _box.BackgroundColor = _draft.BackgroundColor;
        _box.TitleBarColor = _draft.TitleBarColor;
        _box.TextColor = _draft.TextColor;
        _box.BackgroundImagePath = image;
        _box.BackgroundImageMode = image == null ? null : _draft.BackgroundImageMode;
        _box.IconSize = _draft.IconSize;
        _box.CornerRadius = _draft.CornerRadius;
        _box.Opacity = _draft.Opacity;
        DialogResult = true;
    }
}
