using DesktopBoxes.Core;

namespace DesktopBoxes.Tests;

public class BoxLayoutPresetTests
{
    [Fact]
    public void Apply_UsesStableGridDimensions()
    {
        var box = new Box { IconSize = 48, Width = 999, Height = 999 };

        Assert.True(BoxLayoutPreset.Apply(box, 3, 2));

        Assert.Equal(3, box.PresetColumns);
        Assert.Equal(2, box.PresetRows);
        Assert.Equal(328, box.Width);
        Assert.Equal(254, box.Height);
    }

    [Fact]
    public void RefreshSize_KeepsPresetWhenIconSizeChanges()
    {
        var box = new Box { IconSize = 32 };
        BoxLayoutPreset.Apply(box, 3, 2);

        box.IconSize = 64;
        Assert.True(BoxLayoutPreset.RefreshSize(box));

        Assert.Equal(3, box.PresetColumns);
        Assert.Equal(2, box.PresetRows);
        Assert.Equal(376, box.Width);
        Assert.Equal(286, box.Height);
    }

    [Fact]
    public void Clear_PreservesCurrentSizeAndStopsAutomaticChanges()
    {
        var box = new Box { IconSize = 48 };
        BoxLayoutPreset.Apply(box, 4, 3);
        double width = box.Width;
        double height = box.Height;

        Assert.True(BoxLayoutPreset.Clear(box));
        box.IconSize = 64;

        Assert.False(BoxLayoutPreset.RefreshSize(box));
        Assert.Equal(0, box.PresetColumns);
        Assert.Equal(0, box.PresetRows);
        Assert.Equal(width, box.Width);
        Assert.Equal(height, box.Height);
    }

    [Fact]
    public void Apply_RejectsUnsupportedDimensions()
    {
        var box = new Box();

        Assert.False(BoxLayoutPreset.Apply(box, 3, 3));
        Assert.False(BoxLayoutPreset.IsActive(box));
    }
}
