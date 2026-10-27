using Racks.UseCases.Hotkeys;

namespace Racks.Tests.UseCases;

// Ctrl+Shift+Space / N / D. The ids, the key combinations and the conflict report are part of how the app behaves.
public class HotkeyControllerTests
{
    private sealed class FakeApi : IHotkeyApi
    {
        public Dictionary<int, bool> Results { get; } = new();
        public HashSet<int> Throws { get; } = new();
        public List<(IntPtr Hwnd, int Id, uint Mod, uint Vk)> Registered { get; } = new();
        public List<(IntPtr Hwnd, int Id)> Unregistered { get; } = new();
        public bool ThrowOnUnregister;

        public bool Register(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
        {
            Registered.Add((hwnd, id, modifiers, virtualKey));
            if (Throws.Contains(id)) throw new InvalidOperationException("boom");
            return !Results.TryGetValue(id, out var ok) || ok;
        }

        public bool Unregister(IntPtr hwnd, int id)
        {
            Unregistered.Add((hwnd, id));
            if (ThrowOnUnregister) throw new InvalidOperationException("boom");
            return true;
        }
    }

    private sealed class Rig
    {
        public FakeApi Api { get; } = new();
        public List<string> Ran { get; } = new();
        public HotkeyController Controller { get; }
        public Rig() => Controller = new HotkeyController(Api, new HotkeyActions(
            () => Ran.Add("finder"), () => Ran.Add("newrack"), () => Ran.Add("diagnostics")));
    }

    private static readonly IntPtr Hwnd = new(0x1234);

    [Fact]
    public void The_ids_and_key_combinations_are_the_published_ones()
    {
        Assert.Equal(0xB0CC, HotkeyController.QuickFinderId);
        Assert.Equal(0xB0CD, HotkeyController.NewRackId);
        Assert.Equal(0xB0CE, HotkeyController.DevDiagnosticsId);

        var rig = new Rig();
        rig.Controller.RegisterAll(Hwnd);

        Assert.Equal(
            new[]
            {
                (Hwnd, 0xB0CC, 0x6u, 0x20u),   // Ctrl+Shift+Space
                (Hwnd, 0xB0CD, 0x6u, 0x4Eu),   // Ctrl+Shift+N
                (Hwnd, 0xB0CE, 0x6u, 0x44u),   // Ctrl+Shift+D
            },
            rig.Api.Registered.ToArray());
    }

    [Fact]
    public void When_everything_registers_there_is_no_conflict_report()
    {
        var rig = new Rig();
        int reports = 0;
        var result = rig.Controller.RegisterAll(Hwnd, (_, _) => reports++);
        Assert.Equal((true, true), result);
        Assert.Equal(0, reports);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void A_busy_quick_finder_or_new_rack_combination_is_reported_with_which_ones_failed(bool finderOk, bool newRackOk)
    {
        var rig = new Rig();
        rig.Api.Results[HotkeyController.QuickFinderId] = finderOk;
        rig.Api.Results[HotkeyController.NewRackId] = newRackOk;
        var reports = new List<(bool, bool)>();

        var result = rig.Controller.RegisterAll(Hwnd, (f, n) => reports.Add((f, n)));

        Assert.Equal((finderOk, newRackOk), result);
        Assert.Equal(new[] { (finderOk, newRackOk) }, reports);
    }

    [Fact]
    public void A_busy_dev_diagnostics_combination_is_not_reported()
    {
        var rig = new Rig();
        rig.Api.Results[HotkeyController.DevDiagnosticsId] = false;
        int reports = 0;
        rig.Controller.RegisterAll(Hwnd, (_, _) => reports++);
        Assert.Equal(0, reports);
    }

    [Fact]
    public void An_exception_counts_as_a_failed_registration_of_that_one_hotkey_and_the_others_still_register()
    {
        var rig = new Rig();
        rig.Api.Throws.Add(HotkeyController.QuickFinderId);
        var reports = new List<(bool, bool)>();

        var result = rig.Controller.RegisterAll(Hwnd, (f, n) => reports.Add((f, n)));

        Assert.Equal((false, true), result);
        Assert.Equal(3, rig.Api.Registered.Count);
        Assert.Equal(new[] { (false, true) }, reports);
    }

    [Fact]
    public void A_throwing_toast_does_not_break_registration()
    {
        var rig = new Rig();
        rig.Api.Results[HotkeyController.NewRackId] = false;
        var ex = Record.Exception(() => rig.Controller.RegisterAll(Hwnd, (_, _) => throw new InvalidOperationException("no toast")));
        Assert.Null(ex);
    }

    [Fact]
    public void Dispatch_runs_the_action_of_the_matching_id_and_marks_it_handled()
    {
        var rig = new Rig();
        Assert.True(rig.Controller.TryDispatch(0x0312, new IntPtr(HotkeyController.QuickFinderId)));
        Assert.True(rig.Controller.TryDispatch(0x0312, new IntPtr(HotkeyController.NewRackId)));
        Assert.True(rig.Controller.TryDispatch(0x0312, new IntPtr(HotkeyController.DevDiagnosticsId)));
        Assert.Equal(new[] { "finder", "newrack", "diagnostics" }, rig.Ran);
    }

    [Theory]
    [InlineData(0x0312, 0x1)]      // WM_HOTKEY of somebody else's id
    [InlineData(0x0010, 0xB0CC)]   // right id, but not WM_HOTKEY
    [InlineData(0x0000, 0xB0CD)]
    public void Anything_else_is_not_handled_and_runs_nothing(int msg, int wParam)
    {
        var rig = new Rig();
        Assert.False(rig.Controller.TryDispatch(msg, new IntPtr(wParam)));
        Assert.Empty(rig.Ran);
    }

    [Fact]
    public void A_failing_quick_finder_or_new_rack_action_is_swallowed_but_still_handled()
    {
        var api = new FakeApi();
        var c = new HotkeyController(api, new HotkeyActions(
            () => throw new InvalidOperationException("a"), () => throw new InvalidOperationException("b"), () => { }));
        Assert.True(c.TryDispatch(0x0312, new IntPtr(HotkeyController.QuickFinderId)));
        Assert.True(c.TryDispatch(0x0312, new IntPtr(HotkeyController.NewRackId)));
    }

    [Fact]
    public void The_dev_diagnostics_action_is_not_wrapped_so_its_exception_reaches_the_caller()
    {
        // Characterizes the original WndProc: only quick finder and new rack had a try/catch there.
        var c = new HotkeyController(new FakeApi(), new HotkeyActions(() => { }, () => { }, () => throw new InvalidOperationException("x")));
        Assert.Throws<InvalidOperationException>(() => c.TryDispatch(0x0312, new IntPtr(HotkeyController.DevDiagnosticsId)));
    }

    [Fact]
    public void Unregister_releases_all_three_on_the_window_that_registered_them()
    {
        var rig = new Rig();
        rig.Controller.RegisterAll(Hwnd);
        rig.Controller.UnregisterAll();
        Assert.Equal(new[] { (Hwnd, 0xB0CC), (Hwnd, 0xB0CD), (Hwnd, 0xB0CE) }, rig.Api.Unregistered.ToArray());
    }

    [Fact]
    public void Unregister_before_any_registration_does_nothing()
    {
        var rig = new Rig();
        rig.Controller.UnregisterAll();
        Assert.Empty(rig.Api.Unregistered);
    }

    [Fact]
    public void Unregister_never_throws_and_still_tries_all_three()
    {
        var rig = new Rig();
        rig.Controller.RegisterAll(Hwnd);
        rig.Api.ThrowOnUnregister = true;
        var ex = Record.Exception(rig.Controller.UnregisterAll);
        Assert.Null(ex);
        Assert.Equal(3, rig.Api.Unregistered.Count);
    }
}
