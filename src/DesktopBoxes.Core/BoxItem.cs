using System.IO;

namespace DesktopBoxes.Core;

/// <summary>盒子内的一个图标项（应用 / 快捷方式 / 文件夹 / 文件）。</summary>
public class BoxItem
{
    /// <summary>绝对路径。</summary>
    public string Path { get; set; } = "";

    /// <summary>盒子托管文件相对于数据目录的路径；null 表示旧版的外部引用。</summary>
    public string? StoragePath { get; set; }

    public ItemKind Kind { get; set; }

    /// <summary>显示名（可自定义，默认取文件名）。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>.lnk 解析后的目标（可选，用于启动与取图标）。</summary>
    public string? ResolvedTarget { get; set; }

    public static ItemKind DetectKind(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".lnk") return ItemKind.Shortcut;
        if (ext == ".exe") return ItemKind.Exe;
        if (Directory.Exists(path)) return ItemKind.Folder;
        return ItemKind.File;
    }
}
