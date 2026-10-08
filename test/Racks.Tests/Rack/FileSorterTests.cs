using System.Collections.ObjectModel;
using System.IO;
using Racks.Core;
using Racks.Rack.Sorting;
using Racks.Util;

namespace Racks.Tests.Rack;

public class FileSorterTests
{
    private static FileItem Item(string name, bool folder = false, int day = 1, long size = 0, string? type = null) => new()
    {
        Name = name,
        FullPath = @"C:\r\" + name,
        IsFolder = folder,
        DateModified = new DateTime(2026, 1, day),
        DateCreated = new DateTime(2025, 1, day),
        ItemSize = size,
        FileType = type ?? Path.GetExtension(name),
    };

    private static string[] Names(IEnumerable<FileItem> items) => items.Select(i => i.Name).ToArray();

    [Fact]
    public void Names_sort_naturally_and_ignore_case()
    {
        var items = new[] { Item("file10.txt"), Item("File2.txt"), Item("file1.txt"), Item("a.txt") };
        Assert.Equal(new[] { "a.txt", "file1.txt", "File2.txt", "file10.txt" }, Names(FileSorter.Sort(items, (int)SortMode.NameAsc, 0)));
        Assert.Equal(new[] { "file10.txt", "File2.txt", "file1.txt", "a.txt" }, Names(FileSorter.Sort(items, (int)SortMode.NameDesc, 0)));
    }

    [Theory]
    [InlineData(SortMode.DateModifiedAsc, "b,c,a")]
    [InlineData(SortMode.DateModifiedDesc, "a,c,b")]
    [InlineData(SortMode.DateCreatedAsc, "b,c,a")]
    [InlineData(SortMode.ItemSizeAsc, "c,a,b")]
    [InlineData(SortMode.ItemSizeDesc, "b,a,c")]
    [InlineData(SortMode.FileTypeAsc, "c,b,a")]
    public void Every_mode_orders_by_its_field(SortMode mode, string expected)
    {
        var items = new[] { Item("a", day: 9, size: 50, type: ".z"), Item("b", day: 1, size: 90, type: ".m"), Item("c", day: 5, size: 10, type: ".a") };
        Assert.Equal(expected.Split(','), Names(FileSorter.Sort(items, (int)mode, 0)));
    }

    [Fact]
    public void Folders_first_or_last_and_the_sort_is_kept_inside_each_group()
    {
        var items = new[] { Item("b.txt"), Item("Zfolder", folder: true), Item("a.txt"), Item("Afolder", folder: true) };
        Assert.Equal(new[] { "Afolder", "Zfolder", "a.txt", "b.txt" }, Names(FileSorter.Sort(items, (int)SortMode.NameAsc, (int)FolderOrder.FoldersFirst)));
        Assert.Equal(new[] { "a.txt", "b.txt", "Afolder", "Zfolder" }, Names(FileSorter.Sort(items, (int)SortMode.NameAsc, (int)FolderOrder.FoldersLast)));
        Assert.Equal(new[] { "a.txt", "Afolder", "b.txt", "Zfolder" }, Names(FileSorter.Sort(items, (int)SortMode.NameAsc, (int)FolderOrder.Mixed)));
    }

    [Fact]
    public void Ties_keep_their_original_order()
    {
        var items = new[] { Item("x", day: 3), Item("y", day: 3), Item("z", day: 3) };
        Assert.Equal(new[] { "x", "y", "z" }, Names(FileSorter.Sort(items, (int)SortMode.DateModifiedAsc, 0)));
    }

    [Fact]
    public void An_unknown_mode_keeps_the_input_order()
        => Assert.Equal(new[] { "b", "a" }, Names(FileSorter.Sort(new[] { Item("b"), Item("a") }, 0, 0)));

    [Fact]
    public void Disk_entries_sort_by_name_size_and_folder_group()
    {
        string dir = Path.Combine(Path.GetTempPath(), "racks-sort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "item10.txt"), "1");
        File.WriteAllText(Path.Combine(dir, "item2.txt"), "22");
        try
        {
            var entries = new DirectoryInfo(dir).GetFileSystemInfos()
                .Select(e => (e, e is FileInfo f ? f.Length : 0L)).ToList();
            var byName = FileSorter.Sort(entries, (int)SortMode.NameAsc, (int)FolderOrder.FoldersFirst).Select(e => e.Name);
            Assert.Equal(new[] { "sub", "item2.txt", "item10.txt" }, byName);
            var bySize = FileSorter.Sort(entries, (int)SortMode.ItemSizeDesc, (int)FolderOrder.FoldersLast).Select(e => e.Name);
            Assert.Equal(new[] { "item2.txt", "item10.txt", "sub" }, bySize);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Custom_order_moves_items_to_their_saved_index_and_skips_bad_entries()
    {
        var items = new List<string> { "a", "b", "c", "d" };
        var order = new List<Tuple<string, string>>
        {
            new("d", "0"), new("missing", "1"), new("b", "not a number"), new("a", "99"), new("c", "1"),
        };
        FileSorter.ApplyCustomOrder(items, order, x => x);
        Assert.Equal(new[] { "d", "c", "a", "b" }, items);
    }

    [Fact]
    public void Custom_order_on_a_bound_collection_uses_move()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => actions.Add(e.Action);
        FileSorter.ApplyCustomOrder(items, new List<Tuple<string, string>> { new("c", "0") }, x => x, items.Move);
        Assert.Equal(new[] { "c", "a", "b" }, items);
        Assert.Equal(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Move }, actions);
    }

    [Fact]
    public void Recent_files_move_to_the_front_in_recency_order_and_the_rest_keep_theirs()
    {
        var items = new List<string> { "a", "b", "c", "d", "e" };
        int placed = FileSorter.MoveRecentToFront(items, new[] { "d", "zz", "b", "e" }, topN: 2, fileId: x => x);
        Assert.Equal(2, placed);
        Assert.Equal(new[] { "d", "b", "a", "c", "e" }, items);
    }

    [Fact]
    public void Recent_files_with_the_same_id_stay_together()
    {
        // Two names for one file (hard links) share a file id.
        var items = new List<(string Name, string Id)> { ("a", "1"), ("b", "2"), ("b-link", "2"), ("c", "3") };
        int placed = FileSorter.MoveRecentToFront(items, new[] { "2" }, topN: 1, fileId: x => x.Id);
        Assert.Equal(2, placed);
        Assert.Equal(new[] { "b", "b-link", "a", "c" }, items.Select(x => x.Name));
    }

    [Fact]
    public void Nothing_to_do_returns_zero_and_changes_nothing()
    {
        var items = new List<string> { "a", "b" };
        Assert.Equal(0, FileSorter.MoveRecentToFront(items, null, 3, x => x));
        Assert.Equal(0, FileSorter.MoveRecentToFront(items, new[] { "b" }, 0, x => x));
        FileSorter.ApplyCustomOrder(items, null, x => x);
        Assert.Equal(new[] { "a", "b" }, items);
    }

    [Theory]
    [InlineData("file2", "file0000000002")]
    [InlineData("v10.3", "v0000000010.0000000003")]
    [InlineData(null, "")]
    public void Natural_key_pads_digit_runs(string? name, string expected)
        => Assert.Equal(expected, FileSorter.NaturalKey(name));
}

public class FolderSizeTests
{
    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "2 KB")]
    [InlineData(5_000_000, "4 883 KB")]
    public void Kilobytes_use_a_space_between_thousands(long bytes, string expected)
        => Assert.Equal(expected, FolderSize.Kilobytes(bytes));

    [Fact]
    public void Folder_size_counts_nested_files_and_does_not_follow_junctions()
    {
        string root = Path.Combine(Path.GetTempPath(), "racks-size-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "racks-size-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(root, "one.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(root, "a", "b", "two.bin"), new byte[250]);
        File.WriteAllBytes(Path.Combine(outside, "big.bin"), new byte[10_000]);
        try
        {
            Assert.True(JunctionHelper.TryCreate(outside, Path.Combine(root, "link")), "test setup: junction");
            Assert.Equal(350, FolderSize.Compute(new DirectoryInfo(root)));
        }
        finally
        {
            try { Directory.Delete(Path.Combine(root, "link"), false); } catch { }
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }
}
