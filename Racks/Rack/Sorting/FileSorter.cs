using System.IO;
using System.Text.RegularExpressions;
using Racks.Core;

namespace Racks.Rack.Sorting;

/// <summary>The rack sort order as stored in the registry: odd = ascending, even = descending.</summary>
public enum SortMode
{
    NameAsc = 1,
    NameDesc = 2,
    DateModifiedAsc = 3,
    DateModifiedDesc = 4,
    DateCreatedAsc = 5,
    DateCreatedDesc = 6,
    FileTypeAsc = 7,
    FileTypeDesc = 8,
    ItemSizeAsc = 9,
    ItemSizeDesc = 10,
}

/// <summary>Where folders go relative to files, as stored in the registry.</summary>
public enum FolderOrder
{
    Mixed = 0,
    FoldersFirst = 1,
    FoldersLast = 2,
}

/// <summary>
/// The rack's ordering rules, with no WPF, registry or file-system access: sort, custom order and
/// "recently used first". Moved out of RackWindow so they can be tested.
/// </summary>
public static class FileSorter
{
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Key for natural ordering: every run of digits is padded so "file2" sorts before "file10".
    /// Compared case-insensitively by the callers.
    /// </summary>
    public static string NaturalKey(string? name) => Digits.Replace(name ?? "", m => m.Value.PadLeft(10, '0'));

    /// <summary>Sorts rack items by <paramref name="sortBy"/>, then groups folders per <paramref name="folderOrder"/>. Stable.</summary>
    public static List<FileItem> Sort(IEnumerable<FileItem> items, int sortBy, int folderOrder)
    {
        IEnumerable<FileItem> sorted = (SortMode)sortBy switch
        {
            SortMode.NameAsc => items.OrderBy(i => NaturalKey(i.Name), StringComparer.OrdinalIgnoreCase),
            SortMode.NameDesc => items.OrderByDescending(i => NaturalKey(i.Name), StringComparer.OrdinalIgnoreCase),
            SortMode.DateModifiedAsc => items.OrderBy(i => i.DateModified),
            SortMode.DateModifiedDesc => items.OrderByDescending(i => i.DateModified),
            SortMode.DateCreatedAsc => items.OrderBy(i => i.DateCreated),
            SortMode.DateCreatedDesc => items.OrderByDescending(i => i.DateCreated),
            SortMode.FileTypeAsc => items.OrderBy(i => i.FileType),
            SortMode.FileTypeDesc => items.OrderByDescending(i => i.FileType),
            SortMode.ItemSizeAsc => items.OrderBy(i => i.ItemSize),
            SortMode.ItemSizeDesc => items.OrderByDescending(i => i.ItemSize),
            _ => items,
        };
        return GroupFolders(sorted, folderOrder, i => i.IsFolder).ToList();
    }

    /// <summary>The same rules for entries read from disk, with each entry's size measured by the caller.</summary>
    public static List<FileSystemInfo> Sort(IEnumerable<(FileSystemInfo Item, long Size)> entries, int sortBy, int folderOrder)
    {
        IEnumerable<(FileSystemInfo Item, long Size)> sorted = (SortMode)sortBy switch
        {
            SortMode.NameAsc => entries.OrderBy(e => NaturalKey(e.Item.Name), StringComparer.OrdinalIgnoreCase),
            SortMode.NameDesc => entries.OrderByDescending(e => NaturalKey(e.Item.Name), StringComparer.OrdinalIgnoreCase),
            SortMode.DateModifiedAsc => entries.OrderBy(e => e.Item.LastWriteTime),
            SortMode.DateModifiedDesc => entries.OrderByDescending(e => e.Item.LastWriteTime),
            SortMode.DateCreatedAsc => entries.OrderBy(e => e.Item.CreationTime),
            SortMode.DateCreatedDesc => entries.OrderByDescending(e => e.Item.CreationTime),
            SortMode.FileTypeAsc => entries.OrderBy(e => e.Item.Extension),
            SortMode.FileTypeDesc => entries.OrderByDescending(e => e.Item.Extension),
            SortMode.ItemSizeAsc => entries.OrderBy(e => e.Size),
            SortMode.ItemSizeDesc => entries.OrderByDescending(e => e.Size),
            _ => entries,
        };
        return GroupFolders(sorted, folderOrder, e => e.Item is DirectoryInfo).Select(e => e.Item).ToList();
    }

    private static IEnumerable<T> GroupFolders<T>(IEnumerable<T> items, int folderOrder, Func<T, bool> isFolder) =>
        (FolderOrder)folderOrder switch
        {
            FolderOrder.FoldersFirst => items.OrderBy(i => !isFolder(i)),
            FolderOrder.FoldersLast => items.OrderBy(i => isFolder(i)),
            _ => items,
        };

    /// <summary>
    /// Applies the user's drag-and-drop order: each (fileId, index) moves that item to that index, in
    /// order. Unknown ids, bad indexes and out-of-range targets are skipped.
    /// </summary>
    /// <param name="move">How to move an item from one index to another. Defaults to remove + insert; pass
    /// ObservableCollection.Move for a bound collection so the UI keeps its item containers.</param>
    public static void ApplyCustomOrder<T>(IList<T> items, IEnumerable<Tuple<string, string>>? customOrder, Func<T, string?> fileId,
        Action<int, int>? move = null)
    {
        move ??= (from, to) => MoveBy(items, from, to);
        if (items == null || items.Count == 0 || customOrder == null) return;
        foreach (var (id, indexText) in customOrder)
        {
            if (!int.TryParse(indexText, out int target)) continue;
            int current = IndexOf(items, item => fileId(item) == id);
            if (current < 0 || current == target || target < 0 || target >= items.Count) continue;
            move(current, target);
        }
    }

    /// <summary>
    /// Moves the items of the <paramref name="topN"/> most recently used files (ids in recency order) to
    /// the front, keeping everything else in its current order. Returns how many items were placed first.
    /// </summary>
    public static int MoveRecentToFront<T>(IList<T> items, IEnumerable<string>? recentIds, int topN, Func<T, string?> fileId,
        Action<int, int>? move = null)
    {
        move ??= (from, to) => MoveBy(items, from, to);
        if (items == null || items.Count == 0 || recentIds == null || topN <= 0) return 0;

        var byId = items
            .Select(item => (item, id: fileId(item)))
            .Where(x => x.id != null)
            .GroupBy(x => x.id!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.item).ToList());

        int insertAt = 0;
        foreach (var id in recentIds.Where(byId.ContainsKey).Take(topN))
        {
            foreach (var item in byId[id])
            {
                int from = items.IndexOf(item);
                if (from >= 0 && from != insertAt) move(from, insertAt);
                insertAt++;
            }
        }
        return insertAt;
    }

    private static void MoveBy<T>(IList<T> items, int from, int to)
    {
        T item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
    }

    private static int IndexOf<T>(IList<T> items, Func<T, bool> match)
    {
        for (int i = 0; i < items.Count; i++)
            if (match(items[i])) return i;
        return -1;
    }
}
