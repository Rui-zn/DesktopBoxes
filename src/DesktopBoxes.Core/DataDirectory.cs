using System.IO;

namespace DesktopBoxes.Core;

/// <summary>解析数据目录（默认 / 自定义 / 便携），见《技术方案》第 5.4 节。</summary>
public static class DataDirectory
{
    public const string DefaultFolderName = "DesktopBoxes";

    /// <summary>
    /// 启动时按顺序判断：
    /// 1) exe 同目录存在 portable 标记 → 程序目录 data\
    /// 2) 默认位置存在 storage.location 指针 → 该目录
    /// 3) 否则 → %AppData%\DesktopBoxes
    /// </summary>
    public static string Resolve(string? exeDir = null)
    {
        string exe = exeDir ?? AppContext.BaseDirectory;

        if (IsPortable(exe))
        {
            return Path.Combine(exe, "data");
        }

        string def = GetDefaultDirectory();
        string pointer = Path.Combine(def, "storage.location");
        if (File.Exists(pointer))
        {
            try
            {
                string? dir = File.ReadAllText(pointer).Trim();
                if (!string.IsNullOrWhiteSpace(dir) && Path.IsPathFullyQualified(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }
            catch
            {
                // 指针不可读或自定义目录失效时安全回退到默认目录。
            }
        }

        return def;
    }

    public static string GetDefaultDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DefaultFolderName);
    }

    public static bool IsPortable(string? exeDir = null)
    {
        string exe = exeDir ?? AppContext.BaseDirectory;
        return File.Exists(Path.Combine(exe, "portable"));
    }

    /// <summary>更新默认数据目录中的自定义位置指针；指向默认目录时移除指针。</summary>
    public static void SetCustomLocation(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("数据目录必须是绝对路径。", nameof(directory));
        }

        string target = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string def = Path.GetFullPath(GetDefaultDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(def);
        string pointer = Path.Combine(def, "storage.location");

        if (string.Equals(target, def, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(pointer))
            {
                File.Delete(pointer);
            }
            return;
        }

        Directory.CreateDirectory(target);
        string temp = Path.Combine(def, $"storage.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, target);
            File.Move(temp, pointer, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
