namespace Racks.Rack.Sorting;

/// <summary>
/// The rack's sort order is one number: 1 = name ascending, 2 = name descending, 3 = date modified
/// ascending, 4 = descending, and so on (odd = ascending, even = descending).
/// </summary>
public static class SortToggle
{
    /// <summary>
    /// What clicking a sort key should do. Choosing a different key sorts by it ascending; choosing
    /// the key you are already on flips its direction. (The old handlers' conditions never flipped
    /// four of the five keys, and flipped the file-size key from the wrong starting points.)
    /// </summary>
    public static int Next(int current, int ascendingValue)
    {
        if (current == ascendingValue) return ascendingValue + 1;   // ascending -> descending
        return ascendingValue;                                      // descending, or another key -> ascending
    }
}
