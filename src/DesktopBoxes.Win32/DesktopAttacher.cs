using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopBoxes.Win32;

/// <summary>把窗口「贴到桌面」的策略结果。</summary>
public enum AttachMode
{
    None = 0,
    WorkerW = 1,
    BottomZOrder = 2,
}

/// <summary>
/// 负责把任意 HWND 窗口放到「桌面层」：优先嵌入 WorkerW，失败则降级为置底 z-order。
/// </summary>
public static class DesktopAttacher
{
    private const uint WM_SPAWN_WORKER = 0x052C;
    private const uint SMTO_NORMAL = 0x0000;

    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOOLWINDOW = 0x80;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    private const int SW_RESTORE = 9;

    /// <summary>输出当前桌面窗口层级结构（诊断用）。</summary>
    public static string DescribeDesktopStructure()
    {
        var sb = new StringBuilder();
        IntPtr progman = FindWindow("Progman", null);
        sb.AppendLine($"Progman=0x{progman.ToInt64():X}");

        IntPtr w = IntPtr.Zero;
        while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
        {
            bool hasDef = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
            sb.AppendLine($"  TopWorkerW=0x{w.ToInt64():X} hasDefView={hasDef}");
        }

        w = IntPtr.Zero;
        while ((w = FindWindowEx(progman, w, "WorkerW", null)) != IntPtr.Zero)
        {
            bool hasDef = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
            sb.AppendLine($"  ProgmanWorkerW=0x{w.ToInt64():X} hasDefView={hasDef}");
        }

        IntPtr def = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        sb.AppendLine($"ProgmanDefView=0x{def.ToInt64():X}");
        return sb.ToString();
    }

    /// <summary>把窗口贴到桌面，返回实际采用的模式。</summary>
    public static AttachMode Attach(IntPtr hwnd)
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            // 触发 Progman 生成 WorkerW（经典技巧）
            SendMessageTimeout(progman, WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
        }

        IntPtr worker = FindAnyWorkerW(progman);
        if (worker != IntPtr.Zero)
        {
            SetParent(hwnd, worker);
            if (GetParent(hwnd) == worker)
            {
                return AttachMode.WorkerW;
            }
        }

        // 降级：置底 z-order（位于桌面之上、所有普通窗口之下）
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        return AttachMode.BottomZOrder;
    }

    private static IntPtr FindAnyWorkerW(IntPtr progman)
    {
        IntPtr w = IntPtr.Zero;
        while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
        {
            return w;
        }

        w = IntPtr.Zero;
        while ((w = FindWindowEx(progman, w, "WorkerW", null)) != IntPtr.Zero)
        {
            return w;
        }
        return IntPtr.Zero;
    }

    /// <summary>查找承载 SHELLDLL_DefView（桌面图标列表）的 WorkerW。</summary>
    public static IntPtr FindWorkerHostingDefView()
    {
        IntPtr w = IntPtr.Zero;
        while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
        {
            if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                return w;
            }
        }

        IntPtr progman = FindWindow("Progman", null);
        w = IntPtr.Zero;
        while ((w = FindWindowEx(progman, w, "WorkerW", null)) != IntPtr.Zero)
        {
            if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                return w;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 返回用于嵌入盒子的桌面父窗口 HWND：
    /// 优先「承载 DefView 的 WorkerW」，其次 Progman（DefView 是其直接子窗口时），
    /// 都没有则返回 0（调用方降级为顶层置底窗口）。
    /// </summary>
    public static IntPtr GetDesktopParent()
    {
        IntPtr worker = FindWorkerHostingDefView();
        if (worker != IntPtr.Zero)
        {
            return worker;
        }

        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                return progman;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>把窗口提到其父窗口内的 z-order 顶部（确保盒子在桌面图标之上）。</summary>
    public static void EnsureTopInParent(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>降级方案：把顶层窗口钉到 z-order 底部（桌面之上、普通窗口之下）。</summary>
    public static void PinToBottom(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>移动 / 缩放窗口（坐标相对其父窗口）。</summary>
    public static void SetWindowPosition(IntPtr hwnd, int x, int y, int width, int height)
    {
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOACTIVATE);
    }

    /// <summary>把屏幕坐标转换为指定父窗口的客户区坐标。</summary>
    public static (int X, int Y) ScreenToParentClient(IntPtr parent, int screenX, int screenY)
    {
        if (parent == IntPtr.Zero)
        {
            return (screenX, screenY);
        }

        var point = new POINT { X = screenX, Y = screenY };
        return ScreenToClient(parent, ref point) ? (point.X, point.Y) : (screenX, screenY);
    }

    public static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);

    /// <summary>恢复窗口并重新钉到桌面底层（Win+D 兜底）。</summary>
    public static void RestoreAndPinToBottom(IntPtr hwnd)
    {
        ShowWindow(hwnd, SW_RESTORE);
        PinToBottom(hwnd);
    }

    public static IntPtr GetParentOf(IntPtr hwnd) => GetParent(hwnd);

    public static bool IsVisible(IntPtr hwnd) => IsWindowVisible(hwnd);

    public static bool IsValidWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);
}
