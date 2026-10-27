using Racks.Infrastructure.Shell;

namespace Racks.Tests.Shell;

/// <summary>
/// ShellOperationGateway is a thin delegation layer. These tests replace the shell calls with recording
/// delegates (internal seam <c>ShellBackend</c>), so nothing here touches the real desktop or Explorer.
/// </summary>
public class ShellOperationGatewayTests
{
    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
        public bool Throw { get; set; }
        public string Workspace { get; set; } = @"C:\ws";
        public IntPtr IconList { get; set; } = new IntPtr(42);

        private void Rec(string call)
        {
            Calls.Add(call);
            if (Throw) throw new InvalidOperationException("boom");
        }

        public ShellOperationGateway Gateway() => new(new ShellBackend
        {
            GetWorkspacePath = () => Workspace,
            InitializeWorkspace = () => Rec("init"),
            NotifyItemMoved = (a, b, d) => Rec($"moved|{a}|{b}|{d}"),
            NotifyDirectoryChanged = p => Rec($"dir|{p}"),
            ArrangeDesktopIcons = paths => Rec($"arrange|{string.Join(",", paths)}"),
            SetDesktopIconsHidden = h => Rec($"hidden|{h}"),
            EnsureDesktopVisible = () => Rec("visible"),
            FindDesktopIconList = () => { Rec("find"); return IconList; },
            RestoreDesktopIconsIfHiddenByRacks = () => Rec("restore"),
        });
    }

    [Fact]
    public void NotifyItemMoved_forwards_both_paths_and_the_directory_flag()
    {
        var r = new Recorder();
        r.Gateway().NotifyItemMoved(@"C:\a\x.txt", @"C:\b\x.txt", false);
        r.Gateway().NotifyItemMoved(@"C:\a\d", @"C:\b\d", true);
        Assert.Equal([@"moved|C:\a\x.txt|C:\b\x.txt|False", @"moved|C:\a\d|C:\b\d|True"], r.Calls);
    }

    [Theory]
    [InlineData(null, @"C:\b")]
    [InlineData("", @"C:\b")]
    [InlineData(@"C:\a", null)]
    [InlineData(@"C:\a", "")]
    public void NotifyItemMoved_ignores_an_empty_path(string? oldPath, string? newPath)
    {
        var r = new Recorder();
        r.Gateway().NotifyItemMoved(oldPath!, newPath!, false);
        Assert.Empty(r.Calls);
    }

    [Fact]
    public void NotifyDirectoryChanged_forwards_the_folder_and_ignores_empty()
    {
        var r = new Recorder();
        var g = r.Gateway();
        g.NotifyDirectoryChanged(@"C:\Users\x\Desktop");
        g.NotifyDirectoryChanged("");
        g.NotifyDirectoryChanged(null);
        Assert.Equal([@"dir|C:\Users\x\Desktop"], r.Calls);
    }

    [Fact]
    public void Notifications_and_arrange_never_throw_even_if_the_shell_call_does()
    {
        var r = new Recorder { Throw = true };
        var g = r.Gateway();
        Assert.Null(Record.Exception(() => g.NotifyItemMoved("a", "b", false)));
        Assert.Null(Record.Exception(() => g.NotifyDirectoryChanged("a")));
        Assert.Null(Record.Exception(() => g.ArrangeDesktopIcons(["a"])));
        Assert.Equal(3, r.Calls.Count); // each was attempted
    }

    [Fact]
    public void ArrangeDesktopIcons_forwards_the_paths_even_when_there_are_none()
    {
        var r = new Recorder();
        var g = r.Gateway();
        g.ArrangeDesktopIcons(["a", "b"]);
        g.ArrangeDesktopIcons([]);
        Assert.Equal(["arrange|a,b", "arrange|"], r.Calls);
    }

    [Fact]
    public void Desktop_icon_calls_are_forwarded_unchanged()
    {
        var r = new Recorder();
        var g = r.Gateway();
        g.SetDesktopIconsHidden(true);
        g.SetDesktopIconsHidden(false);
        g.EnsureDesktopVisible();
        g.RestoreDesktopIconsIfHiddenByRacks();
        Assert.Equal(new IntPtr(42), g.FindDesktopIconList());
        Assert.Equal(["hidden|True", "hidden|False", "visible", "restore", "find"], r.Calls);
    }

    [Fact]
    public void Calls_that_were_not_swallowed_before_still_propagate()
    {
        // FindIconList / EnsureDefViewVisible / InitializeWorkspace never caught exceptions at their call
        // sites in the old code (SetHidden and Initialize catch inside, which the real helpers still do).
        var r = new Recorder { Throw = true };
        var g = r.Gateway();
        Assert.Throws<InvalidOperationException>(() => g.EnsureDesktopVisible());
        Assert.Throws<InvalidOperationException>(() => g.FindDesktopIconList());
    }

    [Fact]
    public void WorkspacePath_reads_through_and_InitializeWorkspace_runs_once_per_call()
    {
        var r = new Recorder { Workspace = @"C:\Users\x\RacksWorkspace" };
        var g = r.Gateway();
        Assert.Equal(@"C:\Users\x\RacksWorkspace", g.WorkspacePath);
        g.InitializeWorkspace();
        Assert.Equal(["init"], r.Calls);
    }

    [Fact]
    public void Default_is_one_shared_instance_wired_to_the_real_helpers()
    {
        Assert.Same(ShellOperationGateway.Default, ShellOperationGateway.Default);
        // Reading the property must not create anything on disk; it is the (possibly empty) static path.
        Assert.NotNull(ShellOperationGateway.Default.WorkspacePath);
    }
}
