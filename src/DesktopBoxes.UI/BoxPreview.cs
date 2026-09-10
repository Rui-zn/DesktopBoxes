using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopBoxes.Core;

namespace DesktopBoxes.UI;

/// <summary>A non-interactive preview; it never opens a desktop host or changes a box.</summary>
internal sealed class BoxPreview : Border
{
    internal BoxPreview(Box box, string dataDir)
    {
        Brush foreground = Parse(box.TextColor, UiTheme.BoxText);
        Background = BackgroundBrush(box, dataDir);
        BorderBrush = foreground.Clone();
        BorderBrush.Opacity = 0.22;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(UiTheme.BoxRadius(box.CornerRadius));
        Background.Opacity = Math.Clamp(box.Opacity, 0, 100) / 100.0;
        MinHeight = 186;
        var panel = new DockPanel();
        var headerText = UiTheme.Text(box.Name, 13, foreground);
        headerText.TextWrapping = TextWrapping.NoWrap;
        headerText.TextTrimming = TextTrimming.CharacterEllipsis;
        headerText.FontWeight = FontWeights.SemiBold;
        var toolbar = new DockPanel();
        var lockIcon = new TextBlock
        {
            Text = box.Locked ? "\uE72E" : "\uE785",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var lockBackground = foreground.Clone();
        lockBackground.Opacity = box.Locked ? 0.16 : 0;
        var lockBadge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = lockBackground, Child = lockIcon };
        lockIcon.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(lockBadge, Dock.Right);
        var more = UiTheme.Text("\uE712", 12, foreground);
        more.FontFamily = new FontFamily("Segoe MDL2 Assets");
        more.VerticalAlignment = VerticalAlignment.Center;
        more.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(more, Dock.Right);
        toolbar.Children.Add(more);
        toolbar.Children.Add(lockBadge);
        var chevron = UiTheme.Text("\uE70D", 10, foreground);
        chevron.FontFamily = new FontFamily("Segoe MDL2 Assets");
        chevron.VerticalAlignment = VerticalAlignment.Center;
        chevron.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(chevron, Dock.Left);
        toolbar.Children.Add(chevron);
        headerText.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(headerText);
        Brush headerBackground = Parse(box.TitleBarColor, UiTheme.BoxHeader);
        headerBackground.Opacity = Math.Clamp(box.Opacity, 0, 100) / 100.0;
        var header = new Border
        {
            Background = headerBackground,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(CornerRadius.TopLeft, CornerRadius.TopRight, 0, 0),
            Child = toolbar,
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var items = new WrapPanel { Margin = new Thickness(8) };
        int size = box.IconSize is >= 24 and <= 96 ? box.IconSize : 32;
        var previews = box.Items.Take(6).ToList();
        if (previews.Count == 0)
        {
            var empty = UiTheme.Text("把文件拖到盒子里\n让常用内容触手可及", 12, foreground);
            empty.TextAlignment = TextAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.Margin = new Thickness(12, 24, 12, 24);
            panel.Children.Add(empty);
        }
        else
        {
            foreach (var item in previews)
            {
                var stack = new StackPanel { Width = size + 40, Margin = new Thickness(0, 6, 0, 8) };
                stack.Children.Add(new Image
                {
                    Source = IconCache.Get(item.Path, size) ?? IconCache.Fallback,
                    Width = size,
                    Height = size,
                    Margin = new Thickness(0, 0, 0, 8),
                });
                stack.Children.Add(new TextBlock
                {
                    Text = item.DisplayName,
                    Foreground = foreground,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 4, 0),
                });
                var tileBrush = foreground.Clone();
                tileBrush.Opacity = 0.035;
                items.Children.Add(new Border { Background = tileBrush, CornerRadius = new CornerRadius(12), Margin = new Thickness(2), Child = stack });
            }
            panel.Children.Add(items);
        }
        Child = panel;
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius.TopLeft, CornerRadius.TopLeft);
    }

    internal static Brush Parse(string? hex, string fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return UiTheme.Brush(fallback);
            string value = hex.Trim().TrimStart('#');
            // Preserve the legacy configuration's RRGGBBAA order.
            if (value.Length == 8) value = value[6..] + value[..6];
            if (value.Length != 6 && value.Length != 8) return UiTheme.Brush(fallback);
            return UiTheme.Brush("#" + value);
        }
        catch { return UiTheme.Brush(fallback); }
    }

    internal static Brush BackgroundBrush(Box box, string dataDir)
    {
        Brush fallback = Parse(box.BackgroundColor, UiTheme.BoxBackground);
        if (string.IsNullOrWhiteSpace(box.BackgroundImagePath)) return fallback;
        try
        {
            string path = Path.GetFullPath(box.BackgroundImagePath, dataDir);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1920;
            bitmap.EndInit();
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap);
            switch (box.BackgroundImageMode)
            {
                case "Fill": brush.Stretch = Stretch.Fill; break;
                case "Uniform": brush.Stretch = Stretch.Uniform; break;
                case "Center": brush.Stretch = Stretch.None; break;
                case "Tile":
                    brush.Stretch = Stretch.Fill;
                    brush.TileMode = TileMode.Tile;
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    brush.Viewport = new Rect(0, 0, 128, 128);
                    break;
                default: brush.Stretch = Stretch.UniformToFill; break;
            }
            return brush;
        }
        catch { return fallback; }
    }
}
