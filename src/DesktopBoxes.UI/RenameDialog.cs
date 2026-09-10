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
        UiTheme.Window(this);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(UiTheme.Text(title, 22));
        var hint = UiTheme.Text("一个容易辨认的名字，让桌面更有条理。", 12, UiTheme.Muted);
        hint.Margin = new Thickness(0, 8, 0, 20);
        panel.Children.Add(hint);
        _textBox = new TextBox { Text = initial, MaxLength = 120 };
        System.Windows.Automation.AutomationProperties.SetName(_textBox, "名称");
        var ok = UiTheme.Button("保存名称", () => DialogResult = true, true);
        ok.IsDefault = true;
        ok.Margin = new Thickness(10, 20, 0, 0);
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true, Margin = new Thickness(0, 20, 0, 0) };
        _textBox.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(_textBox.Text);
        ok.IsEnabled = !string.IsNullOrWhiteSpace(initial);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };
    }
}
