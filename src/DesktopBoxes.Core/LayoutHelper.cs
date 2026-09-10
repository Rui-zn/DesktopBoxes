using System;

namespace DesktopBoxes.Core;

/// <summary>布局辅助：约束盒子位置，防止盒子跑出屏幕 / 丢失。</summary>
public static class LayoutHelper
{
    /// <summary>标题栏高度（保证标题栏始终可见、可抓取）。</summary>
    public const double TitleBarHeight = 42;

    /// <summary>水平方向至少保留可见的宽度（保证能抓住标题栏拖回）。</summary>
    public const double MinVisibleWidth = 150;

    /// <summary>
    /// 约束盒子位置：
    /// - 水平方向至少保留 MinVisibleWidth 宽的一段可见；
    /// - 垂直方向保证标题栏（顶部 TitleBarHeight）始终在屏幕内。
    /// </summary>
    public static void ClampToVisible(Box box, double screenLeft, double screenTop, double screenRight, double screenBottom)
    {
        double w = box.Width;
        double h = box.Height;

        double vw = Math.Min(MinVisibleWidth, Math.Max(1, w));
        double minX = screenLeft - (w - vw);
        double maxX = screenRight - vw;
        box.X = Clamp(box.X, minX, maxX);

        double vh = Math.Min(TitleBarHeight, Math.Max(1, h));
        double minY = screenTop;
        double maxY = screenBottom - vh;
        box.Y = Clamp(box.Y, minY, maxY);
    }

    private static double Clamp(double v, double a, double b)
        => Math.Clamp(v, Math.Min(a, b), Math.Max(a, b));
}
