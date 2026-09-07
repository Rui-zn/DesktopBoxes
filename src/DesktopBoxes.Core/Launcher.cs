using System.Diagnostics;
using System.IO;

namespace DesktopBoxes.Core;

/// <summary>打开盒子内的图标项（启动应用 / 打开文件夹 / 打开文件）。</summary>
public static class Launcher
{
    public static void Open(BoxItem item)
    {
        string path = item.Path;
        if (!File.Exists(path) && !Directory.Exists(path) &&
            item.Kind == ItemKind.Shortcut &&
            !string.IsNullOrWhiteSpace(item.ResolvedTarget))
        {
            path = item.ResolvedTarget;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("项目指向的文件或文件夹不存在。", path);
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? "",
        });
    }

    public static void ShowProperties(BoxItem item)
    {
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            throw new FileNotFoundException("项目指向的文件或文件夹不存在。", item.Path);
        }

        Process.Start(new ProcessStartInfo(item.Path)
        {
            UseShellExecute = true,
            Verb = "properties",
        });
    }
}
