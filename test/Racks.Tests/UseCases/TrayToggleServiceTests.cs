using System.Windows;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;
using Racks.Infrastructure.Shell;
using Racks.Tests.Fakes;
using Racks.UseCases.HotCorner;
using Racks.UseCases.Tray;
using Wpf.Ui.Controls;

namespace Racks.Tests.UseCases;

// What each tray switch does once it has flipped. The settings are written exactly as MainWindow wrote them (REG_SZ
// "True"/"False", the Run entry), and the "runs twice" behavior of the IPC messages and the startup replay are pinned here.
public class TrayToggleServiceTests
{
    private sealed class FakeRacks : IRackWindowControl
    {
        public List<Instance> List { get; } = new();
        public int Applied;
        public IReadOnlyList<Instance> Racks => List;
        public void ApplyLockedStateToWindows() => Applied++;
    }

    private sealed class FakeProbe : IHotCornerProbe
    {
        public (int X, int Y) CursorPosition => (0, 0);
        public (int Right, int Top) PrimaryTopRight => (1920, 0);
    }

    private sealed class Rig
    {
        public InMemoryRegistryRoot Root { get; } = new();
        public InMemoryRegistryRoot Run { get; } = new();
        public List<string> Shell { get; } = new();
        public ManualRepeatingScheduler Scheduler { get; } = new();
        public FakeRacks Racks { get; } = new();
        public List<bool> Physics { get; } = new();
        public int ExePathAsked;
        public AppSettings Settings { get; }
        public HotCornerService HotCorner { get; }
        public TrayToggleService Service { get; }

        public Rig()
        {
            Settings = new AppSettings(Root, Run, () => "Racks", NullAuditSink.Instance);
            HotCorner = new HotCornerService(Scheduler, new FakeProbe(), () => { }, () => { });
            var gateway = new ShellOperationGateway(new ShellBackend { SetDesktopIconsHidden = h => Shell.Add("hidden=" + h) });
            Service = new TrayToggleService(Settings, gateway, HotCorner, Racks, Physics.Add, () => { ExePathAsked++; return @"C:\Apps\Racks.exe"; });
        }
    }

    // ---- settings are written exactly as before -------------------------------------------------------

    [Fact]
    public void Ice_physics_is_saved_as_True_or_False_text_and_applied_live()
    {
        var rig = new Rig();
        rig.Service.SetIcePhysics(false);
        rig.Service.SetIcePhysics(true);
        Assert.Equal(new[] { ("IcePhysics", "False"), ("IcePhysics", "True") }, rig.Root.Writes.ToArray());
        Assert.Equal(new[] { false, true }, rig.Physics.ToArray());
    }

    [Fact]
    public void Hiding_the_desktop_icons_saves_the_switch_then_asks_the_shell()
    {
        var rig = new Rig();
        rig.Service.SetHideDesktopIcons(true);
        rig.Service.SetHideDesktopIcons(false);
        Assert.Equal(new[] { ("HideDesktopIcons", "True"), ("HideDesktopIcons", "False") }, rig.Root.Writes.ToArray());
        Assert.Equal(new[] { "hidden=True", "hidden=False" }, rig.Shell.ToArray());
    }

    [Fact]
    public void Start_on_login_adds_the_Run_entry_with_the_exe_path_then_saves_the_switch()
    {
        var rig = new Rig();
        rig.Service.SetStartOnLogin(true);
        Assert.Equal(@"C:\Apps\Racks.exe", rig.Run.Text("Racks"));
        Assert.Equal("True", rig.Root.Text("startOnLogin"));
        Assert.Equal(1, rig.ExePathAsked);
    }

    [Fact]
    public void Start_on_login_off_removes_the_Run_entry_and_the_legacy_one_without_looking_up_the_exe()
    {
        var rig = new Rig();
        rig.Service.SetStartOnLogin(true);
        rig.Service.SetStartOnLogin(false);
        Assert.Null(rig.Run.Text("Racks"));
        Assert.Contains("DeskFrame", rig.Run.Deletes);
        Assert.Equal("False", rig.Root.Text("startOnLogin"));
        Assert.Equal(1, rig.ExePathAsked);   // only the "on" call asked
    }

    [Fact]
    public void The_hot_corner_switch_saves_and_starts_or_stops_the_watch()
    {
        var rig = new Rig();
        rig.Service.SetHotCorner(true);
        Assert.Equal("True", rig.Root.Text("HotCornerHide"));
        Assert.True(rig.HotCorner.IsWatching);
        rig.Service.SetHotCorner(false);
        Assert.Equal("False", rig.Root.Text("HotCornerHide"));
        Assert.False(rig.HotCorner.IsWatching);
    }

    // ---- lock all ---------------------------------------------------------------------------------------

    private static Instance Rack(string name, bool locked)
    {
        var rack = new Instance(name, false);
        using (rack.SuspendPersistence()) rack.IsLocked = locked;
        return rack;
    }

    [Fact]
    public void The_lock_all_switch_reads_on_only_when_there_is_a_rack_and_every_rack_is_locked()
    {
        var rig = new Rig();
        Assert.False(rig.Service.AllRacksLocked());              // no racks
        rig.Racks.List.Add(Rack("A", true));
        Assert.True(rig.Service.AllRacksLocked());
        rig.Racks.List.Add(Rack("B", false));
        Assert.False(rig.Service.AllRacksLocked());
    }

    [Fact]
    public void Lock_all_sets_every_rack_and_then_tells_the_windows_once()
    {
        var rig = new Rig();
        var a = Rack("A", false);
        var b = Rack("B", true);
        rig.Racks.List.AddRange(new[] { a, b });

        using (a.SuspendPersistence())
        using (b.SuspendPersistence())
            rig.Service.SetLockAll(true);

        Assert.True(a.IsLocked);
        Assert.True(b.IsLocked);
        Assert.Equal(1, rig.Racks.Applied);

        using (a.SuspendPersistence())
        using (b.SuspendPersistence())
            rig.Service.SetLockAll(false);
        Assert.False(a.IsLocked);
        Assert.False(b.IsLocked);
        Assert.Equal(2, rig.Racks.Applied);
    }

    // ---- characterized quirks ---------------------------------------------------------------------------

    [Fact]
    public void Startup_replay_writes_back_the_value_it_just_read_even_on_a_fresh_install()
    {
        // MainWindow's constructor sets IsChecked from the saved value, which fires the handler. On a fresh install the
        // ice-physics default (on) is therefore written to the registry on the first start. Kept on purpose.
        var rig = new Rig();
        Assert.Null(rig.Root.Text("IcePhysics"));
        rig.Service.SetIcePhysics(rig.Settings.IcePhysics);
        Assert.Equal("True", rig.Root.Text("IcePhysics"));
    }

    [Fact]
    public void An_IPC_toggle_message_runs_the_handler_twice_and_every_handler_is_idempotent()
    {
        // WM_RACKS_TOGGLE_*: IsChecked changes (the event runs the handler) and the code calls the handler again.
        var rig = new Rig();
        var racks = new[] { Rack("A", false) };
        rig.Racks.List.AddRange(racks);

        for (int i = 0; i < 2; i++)
        {
            rig.Service.SetIcePhysics(false);
            rig.Service.SetHideDesktopIcons(true);
            rig.Service.SetHotCorner(true);
            using (racks[0].SuspendPersistence()) rig.Service.SetLockAll(true);
        }

        Assert.Equal(new[] { ("IcePhysics", "False"), ("IcePhysics", "False") }, rig.Root.Writes.Where(w => w.Name == "IcePhysics").ToArray());
        Assert.Equal(new[] { false, false }, rig.Physics.ToArray());
        Assert.Equal(new[] { "hidden=True", "hidden=True" }, rig.Shell.ToArray());
        Assert.Equal(1, rig.Scheduler.ActiveCount);            // the hot corner watch is still ONE timer
        Assert.Equal(2, rig.Racks.Applied);
        Assert.True(racks[0].IsLocked);
    }

    [Fact]
    public void Assigning_IsChecked_from_code_fires_the_handler_so_the_IPC_path_calls_it_twice()
    {
        // The premise of the two tests above, on the real Wpf.Ui switch MainWindow uses: setting IsChecked raises
        // Checked/Unchecked, and the IPC code then calls the same handler by hand.
        int calls = 0;
        RunSta(() =>
        {
            var toggle = new ToggleSwitch();
            RoutedEventHandler handler = (_, _) => calls++;
            toggle.Checked += handler;
            toggle.Unchecked += handler;

            toggle.IsChecked = true;                 // constructor-time assignment: fires once
            Assert.Equal(1, calls);

            toggle.IsChecked = !(toggle.IsChecked == true);   // what FlipAndRun does ...
            handler(toggle, new RoutedEventArgs());           // ... and then it calls the handler too
            Assert.Equal(3, calls);

            toggle.IsChecked = false;                // setting the value it already has does not fire
            Assert.Equal(3, calls);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException("STA thread failed: " + error);
    }
}
