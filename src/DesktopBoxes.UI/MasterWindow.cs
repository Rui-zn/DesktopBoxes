using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DesktopBoxes.Core;

namespace DesktopBoxes.UI;

/// <summary>盒子总控页面：列出盒子、创建/删除、逐个或统一设置外观、统一锁定。</summary>
public class MasterWindow : Window
{
    private readonly IReadOnlyList<BoxWindow> _windows;
    private string _dataDir;
    private readonly Action _onChanged;
    private readonly Action _onNewBox;
    private readonly Action<BoxWindow> _onDeleteBox;
    private readonly ListBox _list;

    public MasterWindow(
        IReadOnlyList<BoxWindow> windows,
        string dataDir,
        Action onChanged,
        Action onNewBox,
        Action<BoxWindow> onDeleteBox,
        Func<bool> isExiting)
    {
        _windows = windows;
        _dataDir = dataDir;
        _onChanged = onChanged;
        _onNewBox = onNewBox;
        _onDeleteBox = onDeleteBox;

        Title = "桌面盒子 - 总控";
        Width = 380;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 320;
        MinHeight = 360;

        _list = new ListBox
        {
            Margin = new Thickness(12),
            DisplayMemberPath = "Box.Name",
            FontSize = 13,
        };

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(MakeButton("新建盒子", () => { _onNewBox(); RefreshList(); }));
        buttons.Children.Add(MakeButton("删除盒子", DeleteSelected));
        buttons.Children.Add(MakeButton("盒子外观…", () =>
        {
            if (_list.SelectedItem is BoxWindow w)
            {
                ShowAppearance(w);
            }
        }));
        buttons.Children.Add(MakeButton("统一设置…", ApplyUnifiedAppearance));
        buttons.Children.Add(MakeButton("全部锁定/解锁", ToggleAllLock));

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;

        Closing += (_, e) =>
        {
            if (!isExiting())
            {
                e.Cancel = true;
                Hide();
            }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        };

        RefreshList();
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 6, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    public void RefreshList()
    {
        int prev = _list.SelectedIndex;
        _list.ItemsSource = null;
        _list.ItemsSource = _windows;
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = prev >= 0 && prev < _list.Items.Count ? prev : 0;
        }
    }

    public void UpdateDataDirectory(string dataDir) => _dataDir = dataDir;

    private void DeleteSelected()
    {
        if (_list.SelectedItem is BoxWindow w)
        {
            _onDeleteBox(w);
            RefreshList();
        }
    }

    private void ShowAppearance(BoxWindow w)
    {
        var dlg = new AppearanceDialog(w.Box, _dataDir, supportsTransparency: !w.IsEmbedded);
        if (dlg.ShowDialog() == true)
        {
            w.RefreshAppearance();
            RefreshList();
            _onChanged();
        }
    }

    private void ApplyUnifiedAppearance()
    {
        if (_windows.Count == 0)
        {
            return;
        }
        var template = new Box();
        CopyAppearance(_windows[0].Box, template);
        var dlg = new AppearanceDialog(template, _dataDir, supportsTransparency: _windows.All(w => !w.IsEmbedded));
        if (dlg.ShowDialog() == true)
        {
            foreach (var w in _windows)
            {
                CopyAppearance(template, w.Box);
                w.RefreshAppearance();
            }
            RefreshList();
            _onChanged();
        }
    }

    private void ToggleAllLock()
    {
        if (_windows.Count == 0)
        {
            return;
        }
        bool target = !_windows[0].Box.Locked;
        foreach (var w in _windows)
        {
            w.Box.Locked = target;
            w.RefreshView();
        }
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
