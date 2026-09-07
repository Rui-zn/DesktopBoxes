using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopBoxes.Core;
using Microsoft.Win32;

namespace DesktopBoxes.UI;

/// <summary>盒子外观设置对话框（背景色 / 标题栏色 / 文字色 / 背景图片）。</summary>
public class AppearanceDialog : Window
{
    private static readonly string[] PresetColors =
    {
        "#282C34", "#1E1E1E", "#2D3748", "#1F2937", "#263238", "#3B3B3B", "#374151", "#4A2E2E",
    };

    private readonly Box _box;
    private readonly string _dataDir;

    private string? _bgColor;
    private string? _titleColor;
    private string? _textColor;
    private string? _bgImage;
    private string? _pendingImageSource;
    private string? _bgMode;
    private int _iconSize;
    private int _cornerRadius;
    private int _opacity;

    public AppearanceDialog(Box box, string dataDir, bool supportsTransparency = true)
    {
        _box = box;
        _dataDir = dataDir;
        _bgColor = box.BackgroundColor;
        _titleColor = box.TitleBarColor;
        _textColor = box.TextColor;
        _bgImage = box.BackgroundImagePath;
        _bgMode = box.BackgroundImageMode ?? "UniformToFill";
        _iconSize = box.IconSize;
        _cornerRadius = box.CornerRadius;
        _opacity = box.Opacity;

        Title = "盒子外观";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(Label("主题预设"));
        panel.Children.Add(ThemePresetRow());

        panel.Children.Add(Label("背景颜色"));
        panel.Children.Add(SwatchRow(_bgColor, c => _bgColor = c));

        panel.Children.Add(Label("标题栏颜色"));
        panel.Children.Add(SwatchRow(_titleColor, c => _titleColor = c));

        panel.Children.Add(Label("文字颜色"));
        panel.Children.Add(TextColorRow());

        panel.Children.Add(Label("图标大小"));
        panel.Children.Add(IconSizeRow());

        if (supportsTransparency)
        {
            panel.Children.Add(Label("圆角"));
            panel.Children.Add(CornerRadiusRow());

            panel.Children.Add(Label("不透明度"));
            panel.Children.Add(OpacityRow());
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "WorkerW 桌面嵌入模式下使用不透明直角窗口，以保证桌面层级稳定。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });
        }

        panel.Children.Add(Label("背景图片"));
        var imageRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var pick = new Button { Content = "选择图片…", Width = 96, Margin = new Thickness(0, 0, 6, 0) };
        pick.Click += (_, _) => PickImage();
        var clear = new Button { Content = "清除图片", Width = 96 };
        clear.Click += (_, _) => { _bgImage = null; _pendingImageSource = null; };
        imageRow.Children.Add(pick);
        imageRow.Children.Add(clear);
        panel.Children.Add(imageRow);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        modeRow.Children.Add(new TextBlock { Text = "显示方式：", VerticalAlignment = VerticalAlignment.Center });
        var mode = new ComboBox { Width = 120 };
        mode.Items.Add(new ComboBoxItem { Content = "适应", Tag = "UniformToFill" });
        mode.Items.Add(new ComboBoxItem { Content = "拉伸", Tag = "Fill" });
        mode.Items.Add(new ComboBoxItem { Content = "平铺", Tag = "Tile" });
        mode.Items.Add(new ComboBoxItem { Content = "居中", Tag = "Center" });
        foreach (var it in mode.Items)
        {
            if (it is ComboBoxItem cbi && (string?)cbi.Tag == _bgMode)
            {
                mode.SelectedItem = cbi;
                break;
            }
        }
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is ComboBoxItem cbi && cbi.Tag is string t) _bgMode = t;
        };
        modeRow.Children.Add(mode);
        panel.Children.Add(modeRow);

        var ok = new Button { Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 14, 6, 0) };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true, Margin = new Thickness(0, 14, 0, 0) };
        ok.Click += (_, _) => { Apply(); DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
    }

    private void Apply()
    {
        if (!string.IsNullOrWhiteSpace(_pendingImageSource))
        {
            _bgImage = CopyImageToDataDir(_pendingImageSource);
        }
        _box.BackgroundColor = _bgColor;
        _box.TitleBarColor = _titleColor;
        _box.TextColor = _textColor;
        _box.BackgroundImagePath = _bgImage;
        _box.BackgroundImageMode = _bgImage == null ? null : _bgMode;
        _box.IconSize = _iconSize;
        _box.CornerRadius = _cornerRadius;
        _box.Opacity = _opacity;
    }

    private void PickImage()
    {
        var dlg = new OpenFileDialog { Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
        if (dlg.ShowDialog() == true)
        {
            _pendingImageSource = dlg.FileName;
        }
    }

    private string CopyImageToDataDir(string source)
    {
        string dir = Path.Combine(_dataDir, "backgrounds");
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        File.Copy(source, dest, true);
        return Path.GetRelativePath(_dataDir, dest);
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold };
    }

    private UIElement ThemePresetRow()
    {
        var row = new WrapPanel();
        AddThemeButton(row, "深色", "#282C34", "#343944", "#FFFFFF");
        AddThemeButton(row, "浅色", "#F2F4F7", "#D8DDE6", "#1F2937");
        AddThemeButton(row, "蓝灰", "#263238", "#37474F", "#FFFFFF");
        AddThemeButton(row, "暖色", "#4A2E2E", "#68403D", "#FFF7ED");
        return row;
    }

    private void AddThemeButton(WrapPanel row, string label, string background, string title, string text)
    {
        var button = new Button
        {
            Content = label,
            Width = 62,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 0),
        };
        button.Click += (_, _) =>
        {
            _bgColor = background;
            _titleColor = title;
            _textColor = text;
        };
        row.Children.Add(button);
    }

    private static UIElement SwatchRow(string? current, Action<string?> onPick)
    {
        var row = new WrapPanel();
        foreach (var hex in PresetColors)
        {
            var b = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Background = ParseBrush(hex),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Tag = hex,
                ToolTip = hex,
            };
            b.Click += (s, _) => onPick((string?)((Button)s).Tag);
            row.Children.Add(b);
        }
        var def = new Button { Content = "默认", Width = 52, Height = 24 };
        def.Click += (_, _) => onPick(null);
        row.Children.Add(def);
        return row;
    }

    private UIElement TextColorRow()
    {
        var row = new WrapPanel();
        var light = new Button { Content = "浅色", Width = 52, Height = 24, Margin = new Thickness(0, 0, 6, 0), Tag = "#FFFFFF" };
        var dark = new Button { Content = "深色", Width = 52, Height = 24, Margin = new Thickness(0, 0, 6, 0), Tag = "#000000" };
        var def = new Button { Content = "默认", Width = 52, Height = 24 };
        light.Click += (s, _) => _textColor = (string?)((Button)s).Tag;
        dark.Click += (s, _) => _textColor = (string?)((Button)s).Tag;
        def.Click += (_, _) => _textColor = null;
        row.Children.Add(light);
        row.Children.Add(dark);
        row.Children.Add(def);
        return row;
    }

    private UIElement IconSizeRow()
    {
        var row = new WrapPanel();
        AddSizeButton(row, "小", 32);
        AddSizeButton(row, "中", 48);
        AddSizeButton(row, "大", 64);
        return row;
    }

    private void AddSizeButton(WrapPanel row, string label, int size)
    {
        var b = new Button { Content = label, Width = 52, Height = 24, Margin = new Thickness(0, 0, 6, 0), Tag = size };
        b.Click += (s, _) => _iconSize = (int)((Button)s).Tag;
        row.Children.Add(b);
    }

    private UIElement CornerRadiusRow()
    {
        var row = new WrapPanel();
        AddCornerButton(row, "直角", 0);
        AddCornerButton(row, "小", 6);
        AddCornerButton(row, "中", 12);
        AddCornerButton(row, "大", 20);
        return row;
    }

    private void AddCornerButton(WrapPanel row, string label, int radius)
    {
        var b = new Button { Content = label, Width = 52, Height = 24, Margin = new Thickness(0, 0, 6, 0), Tag = radius };
        b.Click += (s, _) => _cornerRadius = (int)((Button)s).Tag;
        row.Children.Add(b);
    }

    private UIElement OpacityRow()
    {
        var row = new WrapPanel();
        AddOpacityButton(row, "100%", 100);
        AddOpacityButton(row, "80%", 80);
        AddOpacityButton(row, "60%", 60);
        AddOpacityButton(row, "40%", 40);
        return row;
    }

    private void AddOpacityButton(WrapPanel row, string label, int opacity)
    {
        var b = new Button { Content = label, Width = 52, Height = 24, Margin = new Thickness(0, 0, 6, 0), Tag = opacity };
        b.Click += (s, _) => _opacity = (int)((Button)s).Tag;
        row.Children.Add(b);
    }

    private static Brush ParseBrush(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            return new SolidColorBrush(c);
        }
        catch
        {
            return Brushes.Gray;
        }
    }
}
