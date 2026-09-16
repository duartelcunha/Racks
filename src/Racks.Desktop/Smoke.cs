using Racks.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Diagnostics;

namespace Racks.Desktop;

internal static class Smoke
{
    public static async Task CheckRestartAsync(App app)
    {
        var session = app.Session;
        if (!session.Paths.IsIsolated) throw new InvalidOperationException("Restart testing requires an isolated profile.");
        try
        {
            var rack = session.Settings.Racks.Single(x => x.Title == "Large rack");
            if (session.Settings.Racks.Any(x => x.Title is "Pending folder removal" or "Cancelled empty group")) throw new IOException("Interrupted definition changes were not reconciled after restart.");
            if (!rack.Locked || !rack.ListView || session.Items.Count(x => x.RackId == rack.Id) != 5001) throw new IOException("Rack state did not survive a new application process.");
            await session.UndoAsync();
            if (!File.Exists(Path.Combine(session.Paths.Desktop, "restart-marker.txt"))) throw new IOException("Undo did not survive a new application process.");
            JsonStore.Write(Path.Combine(session.Paths.Data, "restart-result.json"), new { Passed = true, Checks = new[] { "Rack files, view and lock restored in a new process", "Interrupted rack removal and cancelled organization reconciled", "Last operation undone after a real application restart" } });
        }
        catch (Exception ex) { JsonStore.Write(Path.Combine(session.Paths.Data, "restart-result.json"), new { Passed = false, Error = ex.ToString() }); Environment.ExitCode = 1; }
        finally { app.Exit(); }
    }

    public static async Task RunAsync(App app)
    {
        var session = app.Session;
        if (!session.Paths.IsIsolated) throw new InvalidOperationException("Smoke testing requires an isolated profile.");
        var report = new List<string>();
        var diagnostics = new Dictionary<string, object>();
        void Check(bool result, string label) { if (!result) throw new InvalidOperationException("Smoke failed: " + label); report.Add(label); }
        try
        {
            var settingsWindow = new Window { Title = "Isolated update controls", Width = 780, Height = 640 };
            settingsWindow.Content = new SettingsView(settingsWindow, app); settingsWindow.Show(app.Home);
            await Task.Delay(100);
            var updateControls = settingsWindow.GetVisualDescendants().OfType<Button>().ToArray();
            Check(updateControls.Any(x => Equals(x.Content, "Check for updates") && x.IsEffectivelyVisible && x.IsEnabled), "Manual update check is accessible without a signed feed");
            Check(updateControls.Any(x => Equals(x.Content, "View releases") && x.IsEffectivelyVisible && x.IsEnabled), "Official release page is accessible from settings");
            Check(!session.Updates.Configured && !session.Updates.Ready, "Development builds do not offer unverified automatic installation");
            settingsWindow.Close();
            var source = Path.Combine(session.Paths.Desktop, "smoke-" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(source, "Racks isolated verification");
            var rack = session.CreateRack("Smoke rack", RackKind.Owned); app.ShowRack(rack);
            var window = app.GetRackWindow(rack);
            var originalPosition = session.Platform.ScreenPosition(window);
            rack.X = -50000; rack.Y = -50000; rack.Width = 9000; rack.Height = 9000;
            window.RestorePosition();
            await Task.Delay(100); window.FlushPosition();
            var restoredPosition = session.Platform.ScreenPosition(window);
            var activeScreen = window.Screens.All.First(screen => screen.WorkingArea.Contains(restoredPosition));
            Check(window.Width * activeScreen.Scaling <= activeScreen.WorkingArea.Width + 1 && window.Height * activeScreen.Scaling <= activeScreen.WorkingArea.Height + 1, "Off-screen and oversized rack restores within the current monitor");
            Check(session.Store.Load().Racks.Single(x => x.Id == rack.Id).X == restoredPosition.X, "Clamped rack position persists");
            rack.X = originalPosition.X; rack.Y = originalPosition.Y; rack.Width = 320; rack.Height = 360; window.RestorePosition();
            if (OperatingSystem.IsWindows())
            {
                var attached = session.Platform.AttachToDesktop(window);
                var movedPosition = new PixelPoint(originalPosition.X + 24, originalPosition.Y + 24);
                session.Platform.SetScreenPosition(window, movedPosition);
                Check(session.Platform.ScreenPosition(window) == movedPosition, attached ? "Attached rack keeps screen coordinates" : "Desktop fallback keeps screen coordinates");
                session.Platform.Detach(window);
                Check(session.Platform.ScreenPosition(window) == movedPosition, "Desktop detach retains rack position");
                session.Platform.SetScreenPosition(window, originalPosition);
            }
            await window.DropAsync([source], false, CollisionChoice.KeepBoth);
            var destination = Path.Combine(rack.Folder, Path.GetFileName(source));
            Check(File.Exists(destination) && !File.Exists(source), "Drop moves the isolated file");
            Check(session.Items.Any(x => x.Path == destination), "Catalog shows the moved file");
            session.Save(); var reloaded = session.Store.Load();
            Check(reloaded.Racks.Any(x => x.Id == rack.Id && x.Folder == rack.Folder), "Settings retain the rack and its location");
            var restarted = new FileOperations(session.Paths, session.Platform);
            var undo = restarted.LastUndo(); Check(undo != null, "Undo intent survives coordinator recreation");
            await restarted.UndoAsync(undo!);
            Check(File.Exists(source) && !File.Exists(destination), "Persistent undo restores the file");
            await session.MoveIntoAsync(rack, [source], false, CollisionChoice.Skip);
            await session.ReturnAsync(session.Items.Where(x => x.RackId == rack.Id), CollisionChoice.Skip);
            Check(File.Exists(source), "Return to Desktop restores the file");
            await session.RemoveRackAsync(rack, CollisionChoice.Skip);
            Check(!session.Settings.Racks.Any(x => x.Id == rack.Id), "Empty rack removal persists");
            await session.UndoAsync();
            Check(session.Settings.Racks.Any(x => x.Id == rack.Id), "Undo restores an empty rack definition");
            var folderRack = session.CreateRack("Folder view", RackKind.Folder, session.Paths.Desktop);
            await session.RemoveRackAsync(folderRack, CollisionChoice.Skip);
            Check(File.Exists(source) && !session.Settings.Racks.Any(x => x.Id == folderRack.Id), "Folder rack removal leaves its files in place");
            await session.UndoAsync();
            Check(session.Settings.Racks.Any(x => x.Id == folderRack.Id) && File.Exists(source), "Undo restores a folder rack without moving its files");
            var filteredRack = session.CreateRack("Imported name filter", RackKind.Owned);
            var filteredFile = Path.Combine(filteredRack.Folder, "filtered.txt"); await File.WriteAllTextAsync(filteredFile, "legacy membership");
            filteredRack.IncludedNames = ["filtered.txt"]; session.Save();
            await session.RemoveRackAsync(filteredRack, CollisionChoice.Skip); await session.UndoAsync();
            var restoredFilter = session.Settings.Racks.Single(x => x.Id == filteredRack.Id);
            Check(File.Exists(filteredFile) && restoredFilter.IncludedNames!.Contains("filtered.txt") && session.Items.Any(x => x.RackId == filteredRack.Id), "Undo of imported rack removal restores its file membership");
            var partialRack = session.CreateRack("Partial return", RackKind.Owned);
            var held = Path.Combine(partialRack.Folder, "held.txt"); var free = Path.Combine(partialRack.Folder, "free.txt");
            await File.WriteAllTextAsync(held, "held"); await File.WriteAllTextAsync(free, "free");
            // A collision works on both target platforms without relying on locking semantics.
            await File.WriteAllTextAsync(Path.Combine(session.Paths.Desktop, "held.txt"), "existing");
            var removed = await session.RemoveRackAsync(partialRack, CollisionChoice.Skip);
            Check(!removed && session.Settings.Racks.Contains(partialRack), "Partial removal keeps the rack definition");
            Check(File.Exists(held) && File.Exists(Path.Combine(session.Paths.Desktop, "free.txt")), "Partial removal preserves both failed and successful items");
            var groupingFolder = Path.Combine(session.Paths.Data, "organize-source"); Directory.CreateDirectory(groupingFolder);
            var grouped = Path.Combine(groupingFolder, "notes.txt"); await File.WriteAllTextAsync(grouped, "organize test");
            await session.OrganizeAsync(FileCatalog.Organize(groupingFolder), CollisionChoice.Skip);
            Check(!File.Exists(grouped), "Organization applies its preview");
            await session.UndoAsync(); Check(File.Exists(grouped), "Organization undo restores the source");
            var routingSource = Path.Combine(session.Paths.Data, "routing-source"); Directory.CreateDirectory(routingSource);
            var secondRack = session.CreateRack("Second routing destination", RackKind.Owned);
            session.Settings.Rules.Add(new() { Source = routingSource, Extensions = ".txt", DestinationRackId = partialRack.Id, Enabled = true });
            session.Settings.Rules.Add(new() { Source = routingSource, Extensions = ".txt", DestinationRackId = secondRack.Id, Enabled = true });
            session.Settings.RoutingPaused = false; session.Save(); session.Router.Rebuild();
            var routed = Path.Combine(routingSource, "route-me.txt"); await File.WriteAllTextAsync(routed, "first match only");
            var routedDestination = Path.Combine(partialRack.Folder, "route-me.txt");
            var routingDeadline = Stopwatch.StartNew();
            while ((!File.Exists(routedDestination) || session.CurrentOperation != null) && routingDeadline.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(100);
            Check(File.Exists(routedDestination) && !File.Exists(routed) && !File.Exists(Path.Combine(secondRack.Folder, "route-me.txt")), "Live routing uses the first matching rule exactly once");
            session.Settings.RoutingPaused = true; session.Save(); session.Router.Rebuild();
            var paused = Path.Combine(routingSource, "paused.txt"); await File.WriteAllTextAsync(paused, "leave this in source");
            await Task.Delay(4500);
            Check(File.Exists(paused) && !File.Exists(Path.Combine(partialRack.Folder, "paused.txt")), "Paused routing leaves new files in their source folder");
            var unavailableRule = new RoutingRule { Source = Path.Combine(session.Paths.Data, "unavailable-source"), DestinationRackId = secondRack.Id, Enabled = true };
            session.Settings.Rules.Add(unavailableRule); session.Settings.RoutingPaused = false; session.Router.Rebuild();
            Check(!unavailableRule.Enabled && unavailableRule.LastError.Length > 0 && !session.Store.Load().Rules.Single(x => x.Id == unavailableRule.Id).Enabled, "Unavailable routing source pauses with a persisted explanation");
            session.Settings.RoutingPaused = true; session.Save(); session.Router.Rebuild();
            var failureWindow = new Window { Title = "Isolated command failure", Width = 320, Height = 160 };
            var failingButton = Ui.Button("Check failure handling", () => throw new IOException("Isolated write failure"));
            failureWindow.Content = failingButton; failureWindow.Show(app.Home);
            failingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            var errorDialog = ((IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!).Windows.SingleOrDefault(x => x.Title == "Couldn’t finish");
            Check(errorDialog != null && errorDialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Isolated write failure"), "A failed window command shows an actionable error without terminating the app");
            errorDialog!.Close(); failureWindow.Close();
            var stress = session.CreateRack("Large rack", RackKind.Owned);
            await Task.Run(() => { for (var i = 0; i < 5000; i++) File.WriteAllText(Path.Combine(stress.Folder, $"Item-{i:D5}.txt"), "isolated sample"); });
            await session.RefreshAsync(); var stressWindow = app.GetRackWindow(stress); stressWindow.Activate();
            await Task.Delay(500);
            Check(session.Items.Count(x => x.RackId == stress.Id) == 5000, "Large rack catalogs 5,000 files");
            var visuals = stressWindow.GetVisualDescendants().Count(); Check(visuals < 1800, "Large rack virtualizes off-screen tiles");
            var frames = new List<double>(); var finished = new TaskCompletionSource(); TimeSpan? last = null; var frame = 0;
            var scroll = stressWindow.GetVisualDescendants().OfType<ScrollViewer>().First();
            void Sample(TimeSpan timestamp)
            {
                if (last is { } previous && frame > 10) frames.Add((timestamp - previous).TotalMilliseconds);
                last = timestamp; frame++; scroll.Offset = new Vector(0, frame * 8);
                if (frame < 190) stressWindow.RequestAnimationFrame(Sample); else finished.TrySetResult();
            }
            stressWindow.RequestAnimationFrame(Sample);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(100);
            var focusedTile = stressWindow.GetVisualDescendants().OfType<ToggleButton>().First(x => x.Tag is string);
            focusedTile.Focus(); var offsetBeforeSelection = scroll.Offset;
            focusedTile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control });
            Check(focusedTile.IsChecked == true && focusedTile.IsFocused && scroll.Offset == offsetBeforeSelection, "Select all preserves the focused tile and scroll position");
            focusedTile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Check(focusedTile.IsChecked == false && focusedTile.IsFocused && scroll.Offset == offsetBeforeSelection, "Clearing selection preserves the focused tile and scroll position");
            focusedTile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            var nextTile = stressWindow.GetVisualDescendants().OfType<ToggleButton>().Single(x => x.IsFocused);
            Check(!Equals(nextTile.Tag, focusedTile.Tag) && nextTile.IsChecked == true, "Arrow navigation focuses and selects the next file, not a row container");
            nextTile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Shift });
            Check(stressWindow.GetVisualDescendants().OfType<ToggleButton>().Count(x => x.IsChecked == true) == 3, "Shift-arrow selects a file range across grid rows");
            frames.Sort(); var p95 = frames[(int)(frames.Count * .95)];
            diagnostics["ScrollFrameP95Ms"] = p95;
            diagnostics["ScrollFramesOver25Ms"] = frames.Count(x => x > 25);
            diagnostics["RealizedVisuals"] = visuals;
            // Measure idle after scrolling, pending watch notifications, and delayed
            // position saves have settled, rather than counting that work as idle.
            await Task.Delay(2000);
            var refreshes = session.RefreshCount; var saves = session.SaveCount;
            var process = Process.GetCurrentProcess(); var cpuBefore = process.TotalProcessorTime; var wall = Stopwatch.StartNew(); await Task.Delay(2500); process.Refresh();
            var idleCores = (process.TotalProcessorTime - cpuBefore).TotalSeconds / wall.Elapsed.TotalSeconds;
            diagnostics["IdleCpuCores"] = idleCores;
            diagnostics["IdleRefreshes"] = session.RefreshCount - refreshes;
            diagnostics["IdleSettingsSaves"] = session.SaveCount - saves;
            Check(idleCores < .2, "Idle CPU stays below 20% of one core in isolated run");
            var pendingRemovalRack = session.CreateRack("Pending folder removal", RackKind.Folder, session.Paths.Desktop);
            await session.Operations.ExecuteAsync(new OperationRecord { RemovedRack = pendingRemovalRack, SettingsPending = true }, CollisionChoice.Skip);
            var cancelledRack = session.CreateRack("Cancelled empty group", RackKind.Owned);
            session.Operations.Persist(new OperationRecord { Finished = true, SettingsPending = true, CreatedRacks = { cancelledRack },
                Items = { new OperationItem { Source = paused, Destination = Path.Combine(cancelledRack.Folder, "paused.txt"), DestinationRackId = cancelledRack.Id, Outcome = ItemOutcome.Skipped } } });
            var restartMarker = Path.Combine(session.Paths.Desktop, "restart-marker.txt"); await File.WriteAllTextAsync(restartMarker, "Persistent undo across an actual process restart");
            await session.MoveIntoAsync(stress, [restartMarker], false, CollisionChoice.Skip);
            stress.Locked = true; stress.ListView = true; session.Save();
            JsonStore.Write(Path.Combine(session.Paths.Data, "smoke-result.json"), new { Passed = true, Checks = report, Performance = new { Files = 5000, RealizedVisuals = visuals, ScrollFrameP95Ms = p95, ScrollFramesOver25Ms = frames.Count(x => x > 25), IdleCpuCores = idleCores }, PerformanceNote = "Frame callback timing is diagnostic, not a GPU presentation or 60 fps certification." });
            app.Exit();
        }
        catch (Exception ex)
        {
            JsonStore.Write(Path.Combine(session.Paths.Data, "smoke-result.json"), new { Passed = false, Checks = report, Performance = diagnostics, Error = ex.ToString() });
            Environment.ExitCode = 1; app.Exit();
        }
    }
}
