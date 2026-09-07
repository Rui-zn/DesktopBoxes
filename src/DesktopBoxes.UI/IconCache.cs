using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopBoxes.Win32;

namespace DesktopBoxes.UI;

/// <summary>图标内存缓存：路径 → 冻结的 ImageSource。</summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _fallback;

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>获取图标；提取失败返回 null。</summary>
    public static ImageSource? Get(string path, int size = 32)
    {
        string key = $"{path.ToLowerInvariant()}|{size}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var bmp = ShellIconProvider.GetIconBitmap(path, size);
        var src = bmp == null ? null : Convert(bmp, size);
        if (src != null)
        {
            _cache[key] = src;
        }
        return src;
    }

    public static ImageSource Fallback => _fallback ??= CreateFallback();

    public static void Clear() => _cache.Clear();

    private static ImageSource Convert(System.Drawing.Bitmap bmp, int size)
    {
        IntPtr hbmp = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hbmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            src.Freeze();
            return src;
        }
        finally
        {
            DeleteObject(hbmp);
        }
    }

    private static ImageSource CreateFallback()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Gray, new Pen(Brushes.DimGray, 1), new Rect(0, 0, 32, 32));
        }
        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
