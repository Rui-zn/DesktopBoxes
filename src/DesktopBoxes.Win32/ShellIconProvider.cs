using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Vanara.Windows.Shell;

namespace DesktopBoxes.Win32;

/// <summary>从 Shell 提取文件/目录/快捷方式的系统图标（支持多档尺寸）。</summary>
public static class ShellIconProvider
{
    private const uint SHGFI_SYSICONINDEX = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    /// <summary>获取指定像素大小的系统图标，失败返回 null。</summary>
    public static Bitmap? GetIconBitmap(string path, int pixels)
    {
        // 1) 取「具体文件」在系统图像列表中的索引。
        //    不带 SHGFI_USEFILEATTRIBUTES，让 Shell 真正访问文件：.lnk 会解析目标、.exe 取自身图标。
        var info = new SHFILEINFO();
        IntPtr list = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_SYSICONINDEX);
        if (list == IntPtr.Zero || info.iIcon < 0)
        {
            return null;
        }

        // 2) 按尺寸从系统图像列表提取
        ShellImageSize size = pixels >= 64 ? ShellImageSize.Jumbo
            : pixels >= 48 ? ShellImageSize.ExtraLarge
            : pixels >= 32 ? ShellImageSize.Large
            : ShellImageSize.Small;

        try
        {
            using var hicon = ShellImageList.GetSystemIcon(info.iIcon, size);
            if (hicon is null || hicon.IsInvalid)
            {
                return null;
            }
            using var icon = Icon.FromHandle(hicon.DangerousGetHandle());
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
    }
}
