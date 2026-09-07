using System.Windows;
using System.Windows.Controls;

namespace DesktopBoxes.UI;

/// <summary>简单的重命名对话框。</summary>
public class RenameDialog : Window
{
    private readonly TextBox _textBox;

    public string Value => _textBox.Text;

    public RenameDialog(string title, string initial)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        _textBox = new TextBox { Text = initial, MinWidth = 300 };
        var ok = new Button { Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 10, 6, 0) };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true, Margin = new Thickness(0, 10, 0, 0) };
        ok.Click += (_, _) => DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };
    }
}
