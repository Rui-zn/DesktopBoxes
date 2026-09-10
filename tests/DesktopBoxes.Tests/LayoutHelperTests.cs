using DesktopBoxes.Core;
using Xunit;

namespace DesktopBoxes.Tests;

public class LayoutHelperTests
{
    [Fact]
    public void ClampToVisible_PullsFarOffBoxBackToMinVisible()
    {
        var box = new Box { X = 5000, Y = 5000, Width = 300, Height = 240 };
        LayoutHelper.ClampToVisible(box, 0, 0, 1920, 1080);
        Assert.Equal(1920 - LayoutHelper.MinVisibleWidth, box.X); // 1770：左侧留 150px 可见
        Assert.Equal(1080 - LayoutHelper.TitleBarHeight, box.Y);  // 完整标题栏可见
    }

    [Fact]
    public void ClampToVisible_KeepsVisibleBox()
    {
        var box = new Box { X = 100, Y = 100, Width = 300, Height = 240 };
        LayoutHelper.ClampToVisible(box, 0, 0, 1920, 1080);
        Assert.Equal(100, box.X);
        Assert.Equal(100, box.Y);
    }

    [Fact]
    public void ClampToVisible_LeftEdgeCanGoOffLeftButKeepsRightVisible()
    {
        var box = new Box { X = -5000, Y = -5000, Width = 300, Height = 240 };
        LayoutHelper.ClampToVisible(box, 0, 0, 1920, 1080);
        Assert.Equal(-(300 - LayoutHelper.MinVisibleWidth), box.X); // -150：右侧留 150px 可见
        Assert.Equal(0, box.Y);                                    // 标题栏贴顶
    }
}
