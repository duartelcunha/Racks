using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Racks.Core;

namespace Racks.Desktop;

public sealed partial class App : Application
{
    public Session Session { get; private set; } = null!;
    public HomeWindow Home { get; private set; } = null!;
    private readonly Dictionary<Guid, RackWindow> windows = new();
    private bool exiting;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Session = new(Program.Profile, Program.ForceSafeMode);
            ApplyTheme(); Session.SettingsChanged += ApplyTheme;
            Home = new HomeWindow(this); desktop.MainWindow = Home; desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Session.RacksChanged += SyncRacks;
            Session.Updates.CloseRequested += () => Exit();
            desktop.ShutdownRequested += (_, e) => { if (Session.CurrentOperation != null) { e.Cancel = true; Home.Show(); Home.Activate(); Session.Status = "Finish or cancel the file operation before quitting."; } };
            desktop.Exit += (_, _) => Session.Dispose();
            Home.Opened += async (_, _) =>
            {
                try { await Session.InitializeAsync(); SyncRacks(); Session.Startup.Ready(); if (Program.SmokeTest) await Smoke.RunAsync(this); else if (Program.RestartCheck) await Smoke.CheckRestartAsync(this); }
                catch (Exception ex) { await Ui.Error(Home, ex); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    private void ApplyTheme()
    {
        RequestedThemeVariant = Session.Settings.Theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        Resources["RacksScrollInertia"] = !Session.Settings.ReduceMotion && !Session.SafeMode;
    }
    public void SyncRacks()
    {
        foreach (var id in windows.Keys.Where(id => !Session.Settings.Racks.Any(x => x.Id == id && x.Visible && ReferenceEquals(x, windows[id].Rack))).ToArray()) { windows[id].CloseForApp(); windows.Remove(id); }
        foreach (var rack in Session.Settings.Racks.Where(x => x.Visible))
            if (!windows.ContainsKey(rack.Id)) { var window = new RackWindow(this, rack); windows.Add(rack.Id, window); window.Show(); }
    }
    public void ShowRack(RackDefinition rack) { rack.Visible = true; Session.Save(); SyncRacks(); windows[rack.Id].Activate(); }
    internal RackWindow GetRackWindow(RackDefinition rack) => windows[rack.Id];
    public void ShowHome() { Home.Show(); Home.Activate(); }
    public void ResetPositions()
    {
        var screen = Home.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 800); var i = 0;
        foreach (var rack in Session.Settings.Racks) { rack.X = screen.X + 40 + (i % 4) * 60; rack.Y = screen.Y + 40 + (i / 4) * 60; i++; if (windows.TryGetValue(rack.Id, out var window)) window.RestorePosition(); }
        Session.Save();
    }
    public void Exit()
    {
        if (exiting) return;
        if (Session.CurrentOperation != null) { Session.Status = "Finish or cancel the active file operation before quitting."; ShowHome(); return; }
        exiting = true;
        foreach (var window in windows.Values) { window.FlushPosition(); window.CloseForApp(); }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(Environment.ExitCode);
    }
}
