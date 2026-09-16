using DesktopBoxes.Core;
using Xunit;

namespace DesktopBoxes.Tests;

public class BoxItemOrderTests
{
    [Fact]
    public void MoveToInsertionIndex_ReordersWithoutReplacingItems()
    {
        var first = new BoxItem { DisplayName = "A" };
        var second = new BoxItem { DisplayName = "B" };
        var third = new BoxItem { DisplayName = "C" };
        var items = new List<BoxItem> { first, second, third };

        Assert.True(BoxItemOrder.MoveToInsertionIndex(items, first, 2));

        Assert.Equal(new[] { second, first, third }, items);
        Assert.Same(first, items[1]);
    }

    [Fact]
    public void MoveToInsertionIndex_CanMoveLastItemToFront()
    {
        var first = new BoxItem { DisplayName = "A" };
        var second = new BoxItem { DisplayName = "B" };
        var third = new BoxItem { DisplayName = "C" };
        var items = new List<BoxItem> { first, second, third };

        Assert.True(BoxItemOrder.MoveToInsertionIndex(items, third, 0));

        Assert.Equal(new[] { third, first, second }, items);
    }

    [Fact]
    public void MoveToInsertionIndex_SameSlotOrUnknownItemIsNoOp()
    {
        var first = new BoxItem { DisplayName = "A" };
        var second = new BoxItem { DisplayName = "B" };
        var items = new List<BoxItem> { first, second };

        Assert.False(BoxItemOrder.MoveToInsertionIndex(items, first, 1));
        Assert.False(BoxItemOrder.MoveToInsertionIndex(items, new BoxItem(), 0));
        Assert.Equal(new[] { first, second }, items);
    }

    [Fact]
    public void ReorderedItems_RoundTripThroughBoxStore()
    {
        string directory = Path.Combine(Path.GetTempPath(), "desktopboxes-order-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new BoxItem { Path = @"C:\fixture\a.txt", DisplayName = "A" };
            var second = new BoxItem { Path = @"C:\fixture\b.txt", DisplayName = "B" };
            var box = new Box { Name = "排序测试", Items = new List<BoxItem> { first, second } };
            Assert.True(BoxItemOrder.MoveToInsertionIndex(box.Items, first, 2));

            var store = new BoxStore(directory);
            store.Save(new List<Box> { box });
            var loaded = store.Load();

            Assert.Equal(new[] { "B", "A" }, Assert.Single(loaded).Items.Select(item => item.DisplayName));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
