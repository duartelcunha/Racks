using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Racks.Core;

namespace Racks.Desktop;

internal sealed class SettingsView : ScrollViewer
{
    public SettingsView(Window owner, App app)
    {
        var session = app.Session;
        var theme = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = session.Settings.Theme, HorizontalAlignment = HorizontalAlignment.Stretch };
        var reduce = new CheckBox { Content = "Reduce motion — disable inertial scrolling", IsChecked = session.Settings.ReduceMotion };
        var desktop = new CheckBox { Content = "Keep racks on the desktop", IsChecked = session.Settings.DesktopIntegration };
        var auto = new CheckBox { Content = "Automatically check and download signed updates", IsChecked = session.Settings.AutoUpdates };
        auto.IsVisible = session.Updates.Configured;
        var updates = Ui.Text(session.Updates.Status, 13, true);
        var restart = Ui.AsyncButton("Restart and install", owner, session.Updates.InstallAsync); restart.IsVisible = session.Updates.Ready;
        void Changed() { updates.Text = session.Updates.Status; restart.IsVisible = session.Updates.Ready; }
        session.Updates.Changed += Changed; DetachedFromVisualTree += (_, _) => session.Updates.Changed -= Changed;
        var save = Ui.AsyncButton("Save preferences", owner, () =>
        {
            session.Settings.Theme = (string?)theme.SelectedItem ?? "System"; session.Settings.ReduceMotion = reduce.IsChecked == true; session.Settings.DesktopIntegration = desktop.IsChecked == true; session.Settings.AutoUpdates = auto.IsChecked == true;
            session.Save(); if (!session.Settings.AutoUpdates) session.Updates.Pause(); session.Status = "Preferences saved. Enabling desktop attachment takes effect on next launch."; return Task.CompletedTask;
        }, true);
        var pause = Ui.Button("Pause download", session.Updates.Pause); pause.IsVisible = session.Updates.Configured;
        var installButtons = Ui.Row(pause, restart); installButtons.IsVisible = session.Updates.Configured;
        var updateButtons = Ui.Stack(Ui.Row(Ui.AsyncButton("Check for updates", owner, session.Updates.CheckAsync),
            Ui.Button("View releases", () => session.Platform.OpenLink(ReleaseCheck.ReleasesUrl))), installButtons);
        Content = Ui.Stack(Ui.Text("Feel at home.", 30), Ui.Text("A few choices. The same dependable workspace.", 14, true),
            Ui.Card(Ui.Stack(Ui.Field("Appearance", theme), reduce, desktop, auto, save)),
            Ui.Card(Ui.Stack(Ui.Text("Updates", 20), Ui.Text("Installed version: " + session.Updates.CurrentVersion, 13, true), updates, updateButtons)),
            Ui.Card(Ui.Stack(Ui.Text("Your files stay yours.", 20), Ui.Text("Layouts contain settings and folder references. They do not contain your files. Keep a separate backup of the files themselves.", 13, true),
                Ui.Row(Ui.AsyncButton("Export layout…", owner, async () =>
                {
                    var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export layout — settings only", SuggestedFileName = "racks-layout.json", FileTypeChoices = [new FilePickerFileType("JSON settings") { Patterns = ["*.json"] }] });
                    if (file != null) { await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await JsonSerializer.SerializeAsync(stream, session.Settings, JsonStore.Options); session.Status = "Layout exported. This file contains settings, not your files."; }
                }), Ui.AsyncButton("Import layout…", owner, async () =>
                {
                    if (session.CurrentOperation != null) throw new IOException("Finish the file operation before importing a layout.");
                    var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import settings and folder references", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("JSON settings") { Patterns = ["*.json"] }] });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                    var imported = JsonStore.Read<AppSettings>(path); SettingsStore.Validate(imported);
                    if (!await Ui.Confirm(owner, "Replace the current layout?", "This replaces rack definitions and preferences. Your files stay where they are. Imported routing rules will be paused; unavailable folders remain visible for reconnection.", "Import layout")) return;
                    imported.RoutingPaused = true; imported.LegacyImported = true; session.Store.Save(imported);
                    await session.ReloadSettingsAsync(); app.SyncRacks();
                })), Ui.AsyncButton("Open settings folder", owner, () => { session.Platform.Open(session.Paths.Data); return Task.CompletedTask; }))),
            Ui.Text("For files on another drive, symbolic links, cloud placeholders, or unavailable locations, use Explorer or Finder. Racks leaves originals in place when it cannot safely complete a transfer.", 12, true));
    }
}

internal sealed class RecoveryView : ScrollViewer
{
    public RecoveryView(Window owner, Session session)
    {
        var stack = Ui.Stack(Ui.Text("Everything accounted for.", 30), Ui.Text("Interrupted operations keep both recorded locations. Inspect them before dismissing a warning. Racks never guesses which copy to delete.", 14, true));
        stack.Children.Add(Ui.Row(Ui.AsyncButton("Restore settings backup", owner, async () =>
        {
            if (await Ui.Confirm(owner, "Restore the last-good settings?", "Your current settings will be preserved as a dated copy. Files are not moved. Safe mode stays active until the next launch.", "Restore settings")) await session.RestoreBackupAsync();
        }), Ui.AsyncButton("Undo last operation", owner, session.UndoAsync)));
        foreach (var problem in session.Problems) stack.Children.Add(Ui.Text(problem, 13, true));
        var records = session.Operations.ReadRecords(out var errors).Where(x => x.NeedsAttention).ToArray();
        foreach (var error in errors) stack.Children.Add(Ui.Text(error, 13));
        if (records.Length == 0 && errors.Count == 0) stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text("No unresolved file operations", 20), Ui.Text("The last completed operation remains available through Undo.", 13, true))));
        foreach (var record in records)
        {
            var content = Ui.Stack(Ui.Text(record.Label, 20), Ui.Text(record.StartedUtc.ToLocalTime().ToString("g"), 12, true));
            foreach (var item in record.Items.Where(x => x.Outcome is ItemOutcome.Pending or ItemOutcome.Failed or ItemOutcome.UndoPending || x.Outcome == ItemOutcome.Completed && x.Error.Length > 0))
            {
                var explanation = Ui.Text($"{item.Outcome}: {item.Error}\nFrom: {item.Source}\nTo: {item.Destination}" + (item.UndoDestination == null ? "" : "\nUndo destination: " + item.UndoDestination), 12, true);
                var row = Ui.Row(Ui.AsyncButton("Reveal source", owner, () => { session.Platform.Reveal(item.Source); return Task.CompletedTask; }), Ui.AsyncButton("Reveal destination", owner, () => { session.Platform.Reveal(item.UndoDestination ?? item.Destination); return Task.CompletedTask; }));
                var dismiss = Ui.AsyncButton("I’ve checked both locations", owner, async () =>
                {
                    if (!await Ui.Confirm(owner, "Resolve this record?", "Only dismiss this after checking the file locations in Explorer or Finder. This does not move, delete, or retry any files.", "Mark inspected")) return;
                    session.Operations.ResolveInspected(record, item); explanation.Text = "Inspected and acknowledged. Files were unchanged."; row.IsVisible = false;
                });
                content.Children.Add(Ui.Stack(Ui.Text(Path.GetFileName(item.Source), 15), explanation, row, dismiss));
            }
            stack.Children.Add(Ui.Card(content));
        }
        Content = stack;
    }
}
