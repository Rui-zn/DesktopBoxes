using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopBoxes.UI;

internal static class UiTheme
{
    internal const string BoxBackground = "#202534";
    internal const string BoxHeader = "#292F42";
    internal const string BoxText = "#F1F3FA";
    internal static Brush Ink => Brush("#20263A");
    internal static Brush Muted => Brush("#667085");
    internal static Brush Accent => Brush("#6258E8");

    // All boxes are rounded, including legacy straight-edge configurations.
    internal static int BoxRadius(int radius) => radius <= 0 ? 16 : Math.Clamp(radius, 8, 24);

    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    internal static void Install(FrameworkElement element)
    {
        element.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DesktopBoxes.UI;component/Theme.xaml", UriKind.Relative),
        });
        element.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Display);
    }

    internal static void Window(Window window)
    {
        Install(window);
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        window.FontSize = 13;
        window.Background = Brush("#F6F7FB");
        window.Foreground = Ink;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    internal static TextBlock Text(string text, double size = 13, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Ink,
        TextWrapping = TextWrapping.Wrap,
    };

    internal static Button Button(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        button.Click += (_, _) => click();
        return button;
    }

    internal static TextBlock Heading(string text)
    {
        var label = Text(text, 13);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 18, 0, 9);
        return label;
    }

    internal static Border Card(UIElement content, int padding = 18) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush("#E6E9F2"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding),
        Child = content,
    };
}
