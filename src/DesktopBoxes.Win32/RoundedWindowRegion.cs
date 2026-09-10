using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopBoxes.Win32;

/// <summary>Native clipping for borderless windows, including opaque WorkerW children.</summary>
/// <remarks>Ownership: https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowrgn</remarks>
public sealed class RoundedWindowRegion
{
    private (IntPtr Handle, int Width, int Height, int Diameter)? _applied;

    public void Update(IntPtr hwnd, double logicalRadius)
    {
        if (!GetWindowRect(hwnd, out var bounds)) return;
        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return;
        uint dpi = GetDpiForWindow(hwnd);
        int diameter = Math.Clamp((int)Math.Round(logicalRadius * 2 * (dpi == 0 ? 1 : dpi / 96.0)), 2, Math.Max(2, Math.Min(width, height)));
        var key = (hwnd, width, height, diameter);
        if (_applied == key) return;

        // GDI excludes the right/bottom endpoint; +1 keeps the last window pixel.
        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero) throw new Win32Exception("无法创建盒子圆角区域。");
        var previous = _applied;
        _applied = key; // SetWindowRgn sends synchronous window-position messages.
        if (SetWindowRgn(hwnd, region, true) != 0) return;
        // Windows owns the region only on success. Never delete a transferred HRGN.
        _applied = previous;
        DeleteObject(region);
        throw new Win32Exception("无法应用盒子圆角区域。");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
