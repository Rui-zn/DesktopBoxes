namespace DesktopBoxes.Core;

/// <summary>一个盒子（桌面分区）。</summary>
public class Box
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新建盒子";

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 220;

    public bool Collapsed { get; set; }

    public bool Locked { get; set; }

    // 外观（可空则用默认主题）
    public string? BackgroundColor { get; set; }     // "#RRGGBB"
    public string? TitleBarColor { get; set; }       // "#RRGGBB"
    public string? TextColor { get; set; }           // "#RRGGBB"
    public string? BackgroundImagePath { get; set; }
    public string? BackgroundImageMode { get; set; } // UniformToFill / Uniform / Fill / Tile / Center
    public int IconSize { get; set; } = 32;         // 32 / 48 / 64
    public int CornerRadius { get; set; } = 16;     // 圆角半径；旧配置的 0 在界面中按默认圆角显示
    public int Opacity { get; set; } = 65;          // 盒子不透明度 0-100

    public List<BoxItem> Items { get; set; } = new();
}
