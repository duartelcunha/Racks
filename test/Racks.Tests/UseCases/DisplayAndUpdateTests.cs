using Racks.UseCases.Display;
using Racks.UseCases.Updates;

namespace Racks.Tests.UseCases;

// The display-change debounce (state machine moved out of WndProc) and the update-check glue moved out of MainWindow.
public class DisplayChangeDebouncerTests
{
    private sealed class Rig
    {
        public bool Initializing;
        public Queue<Action> Posted { get; } = new();
        public int Passes;
        public Exception? PassThrows;
        public DisplayChangeDebouncer Debouncer { get; }

        public Rig() => Debouncer = new DisplayChangeDebouncer(
            () => Initializing, Posted.Enqueue, () => { Passes++; if (PassThrows != null) throw PassThrows; });

        public void RunPosted() { while (Posted.Count > 0) Posted.Dequeue()(); }
    }

    private const int DisplayChange = 0x007E;
    private const int Second = 0x001C;

    [Fact]
    public void A_display_change_posts_one_reposition_pass_to_the_UI_thread()
    {
        var rig = new Rig();
        rig.Debouncer.OnMessage(DisplayChange);
        Assert.Equal(0, rig.Passes);       // posted, not run inline
        rig.RunPosted();
        Assert.Equal(1, rig.Passes);
    }

    [Fact]
    public void A_burst_of_messages_before_the_pass_runs_makes_a_single_pass()
    {
        var rig = new Rig();
        rig.Debouncer.OnMessage(DisplayChange);
        rig.Debouncer.OnMessage(DisplayChange);
        rig.Debouncer.OnMessage(Second);
        rig.Debouncer.OnMessage(DisplayChange);
        Assert.Single(rig.Posted);
        rig.RunPosted();
        Assert.Equal(1, rig.Passes);
    }

    [Fact]
    public void After_the_pass_ran_the_next_message_starts_a_new_one()
    {
        var rig = new Rig();
        rig.Debouncer.OnMessage(DisplayChange);
        rig.RunPosted();
        rig.Debouncer.OnMessage(DisplayChange);
        rig.RunPosted();
        Assert.Equal(2, rig.Passes);
    }

    [Fact]
    public void The_second_message_only_counts_after_a_display_change_and_only_once()
    {
        var rig = new Rig();
        rig.Debouncer.OnMessage(Second);            // not armed: nothing
        Assert.Empty(rig.Posted);

        rig.Debouncer.OnMessage(DisplayChange);     // arms it
        rig.RunPosted();
        rig.Debouncer.OnMessage(Second);            // disarms it, one more pass
        rig.RunPosted();
        rig.Debouncer.OnMessage(Second);            // disarmed again: nothing
        rig.RunPosted();
        Assert.Equal(2, rig.Passes);
    }

    [Fact]
    public void While_racks_are_loading_messages_are_ignored_and_do_not_arm_the_second_one()
    {
        var rig = new Rig { Initializing = true };
        rig.Debouncer.OnMessage(DisplayChange);
        Assert.Empty(rig.Posted);

        rig.Initializing = false;
        rig.Debouncer.OnMessage(Second);            // the ignored display change never armed it
        Assert.Empty(rig.Posted);
    }

    [Fact]
    public void A_second_message_during_loading_leaves_the_flag_armed()
    {
        var rig = new Rig();
        rig.Debouncer.OnMessage(DisplayChange);
        rig.RunPosted();
        rig.Initializing = true;
        rig.Debouncer.OnMessage(Second);
        Assert.Empty(rig.Posted);
        rig.Initializing = false;
        rig.Debouncer.OnMessage(Second);            // still armed
        Assert.Single(rig.Posted);
    }

    [Fact]
    public void A_pass_that_throws_still_clears_the_pending_flag()
    {
        var rig = new Rig { PassThrows = new InvalidOperationException("boom") };
        rig.Debouncer.OnMessage(DisplayChange);
        Assert.Throws<InvalidOperationException>(rig.RunPosted);

        rig.PassThrows = null;
        rig.Debouncer.OnMessage(DisplayChange);
        rig.RunPosted();
        Assert.Equal(2, rig.Passes);
    }

    [Fact]
    public void Other_messages_do_nothing()
    {
        var rig = new Rig();
        foreach (var msg in new[] { 0, 0x0010, 0x0312, 0x001A, 0x0400 + 102 }) rig.Debouncer.OnMessage(msg);
        Assert.Empty(rig.Posted);
    }
}

public class UpdateCheckServiceTests
{
    private sealed class Rig
    {
        public List<string> Log { get; } = new();
        public Exception? CheckThrows;
        public UpdateCheckService Service { get; }

        public Rig() => Service = new UpdateCheckService(
            delay: d => { Log.Add("delay " + d.TotalSeconds); return Task.CompletedTask; },
            check: toast => { Log.Add("check toast=" + toast); return CheckThrows != null ? Task.FromException(CheckThrows) : Task.CompletedTask; },
            cleanup: () => Log.Add("cleanup"));
    }

    [Fact]
    public async Task The_background_check_waits_20_seconds_then_checks_without_a_no_update_toast()
    {
        var rig = new Rig();
        await rig.Service.CheckInBackgroundAsync();
        Assert.Equal(new[] { "delay 20", "check toast=False" }, rig.Log.ToArray());
    }

    [Fact]
    public async Task The_manual_check_runs_at_once_and_always_reports()
    {
        var rig = new Rig();
        await rig.Service.CheckNowAsync();
        Assert.Equal(new[] { "check toast=True" }, rig.Log.ToArray());
    }

    [Fact]
    public async Task Failures_are_swallowed_in_both_paths()
    {
        var rig = new Rig { CheckThrows = new System.Net.Http.HttpRequestException("offline") };
        await rig.Service.CheckInBackgroundAsync();
        await rig.Service.CheckNowAsync();
        Assert.Equal(2, rig.Log.Count(l => l.StartsWith("check")));
    }

    [Fact]
    public async Task Old_update_folders_are_cleaned_after_a_minute_off_the_calling_thread()
    {
        var rig = new Rig();
        await rig.Service.ScheduleCleanup();
        Assert.Equal(new[] { "delay 60", "cleanup" }, rig.Log.ToArray());
    }
}
