using System;
using Vanara.Windows.Shell;

namespace DesktopBoxes.Win32;

/// <summary>解析 .lnk 快捷方式。</summary>
public static class ShellLinkResolver
{
    /// <summary>解析 .lnk 的目标路径；失败返回 null。</summary>
    public static string? ResolveTarget(string lnkPath)
    {
        try
        {
            using var link = new ShellLink(lnkPath, LinkResolution.None, default, TimeSpan.FromSeconds(2));
            return link.TargetPath;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
