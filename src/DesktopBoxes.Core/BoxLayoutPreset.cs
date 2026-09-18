namespace DesktopBoxes.Core;

/// <summary>Calculates and persists icon-grid-based box sizes.</summary>
public static class BoxLayoutPreset
{
    public const double MinimumWidth = 220;

    public static IReadOnlyList<(int Columns, int Rows)> Supported { get; } = Array.AsReadOnly(
        new[] { (2, 2), (3, 2), (4, 2), (4, 3) });

    public static bool IsActive(Box box) => IsSupported(box.PresetColumns, box.PresetRows);

    public static bool IsSupported(int columns, int rows) =>
        Supported.Contains((columns, rows));

    public static (double Width, double Height) Calculate(int iconSize, int columns, int rows)
    {
        if (!IsSupported(columns, rows))
            throw new ArgumentOutOfRangeException(nameof(columns), "不支持的盒子尺寸预设。");

        int normalizedIconSize = iconSize is 32 or 48 or 64 ? iconSize : 32;

        // Width includes the WrapPanel margins, window frame, and a stable scrollbar reserve.
        // Height includes the title bar, WrapPanel margins, window frame, and one-line labels.
        double width = Math.Max(MinimumWidth, columns * (normalizedIconSize + 48) + 40);
        double height = LayoutHelper.TitleBarHeight + rows * (normalizedIconSize + 44) + 28;
        return (width, height);
    }

    public static bool Apply(Box box, int columns, int rows)
    {
        if (!IsSupported(columns, rows)) return false;

        box.PresetColumns = columns;
        box.PresetRows = rows;
        RefreshSize(box);
        return true;
    }

    public static bool RefreshSize(Box box)
    {
        if (!IsActive(box)) return false;

        (double width, double height) = Calculate(box.IconSize, box.PresetColumns, box.PresetRows);
        bool changed = box.Width != width || box.Height != height;
        box.Width = width;
        box.Height = height;
        return changed;
    }

    public static bool Clear(Box box)
    {
        bool changed = box.PresetColumns != 0 || box.PresetRows != 0;
        box.PresetColumns = 0;
        box.PresetRows = 0;
        return changed;
    }

    public static void Normalize(Box box)
    {
        if (!IsActive(box))
        {
            Clear(box);
            return;
        }

        RefreshSize(box);
    }
}
