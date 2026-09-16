namespace Racks.Core;

public sealed class StartupState
{
    public int IncompleteStarts { get; set; }
    public bool Ready { get; set; }
}

public sealed class StartupRecovery(AppPaths paths)
{
    public bool Begin()
    {
        var state = new StartupState();
        if (File.Exists(paths.Startup))
        {
            try { state = JsonStore.Read<StartupState>(paths.Startup); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { state.IncompleteStarts = 2; }
        }
        var safe = !state.Ready && state.IncompleteStarts >= 2;
        state.IncompleteStarts = state.Ready ? 1 : state.IncompleteStarts + 1;
        state.Ready = false;
        JsonStore.Write(paths.Startup, state, false);
        return safe;
    }

    public void Ready() => JsonStore.Write(paths.Startup, new StartupState { Ready = true }, false);
}
