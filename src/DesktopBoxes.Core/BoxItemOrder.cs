namespace DesktopBoxes.Core;

/// <summary>Changes display order without touching the item's file or storage metadata.</summary>
public static class BoxItemOrder
{
    /// <summary>Moves an item to an insertion point measured before removing the source item.</summary>
    public static bool MoveToInsertionIndex(IList<BoxItem> items, BoxItem item, int insertionIndex)
    {
        int sourceIndex = items.IndexOf(item);
        if (sourceIndex < 0) return false;

        int destinationIndex = Math.Clamp(insertionIndex, 0, items.Count);
        if (sourceIndex < destinationIndex) destinationIndex--;
        if (sourceIndex == destinationIndex) return false;

        items.RemoveAt(sourceIndex);
        items.Insert(destinationIndex, item);
        return true;
    }
}
