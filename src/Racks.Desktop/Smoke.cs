using Racks.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Diagnostics;

namespace Racks.Desktop;

internal static class Smoke
{
    public static async Task RunAsync(App app)
    {
        var session = app.Session;
        if (!session.Paths.IsIsolated) throw new InvalidOperationException("Smoke testing requires an isolated profile.");
        var report = new List<string>();
        var diagnostics = new Dictionary<string, object>();
        void Check(bool result, string label) { if (!result) throw new InvalidOperationException("Smoke failed: " + label); report.Add(label); }
        try
        {
            var source = Path.Combine(session.Paths.Desktop, "smoke-" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(source, "Racks isolated verification");
            var rack = session.CreateRack("Smoke rack", RackKind.Owned); app.ShowRack(rack);
            var window = new RackWindow(app, rack); window.Show();
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
            window.CloseForApp();
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
            var stress = session.CreateRack("Large rack", RackKind.Owned);
            await Task.Run(() => { for (var i = 0; i < 5000; i++) File.WriteAllText(Path.Combine(stress.Folder, $"Item-{i:D5}.txt"), "isolated sample"); });
            await session.RefreshAsync(); var stressWindow = new RackWindow(app, stress); stressWindow.Show();
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
            stressWindow.CloseForApp();
            JsonStore.Write(Path.Combine(session.Paths.Data, "smoke-result.json"), new { Passed = true, Checks = report, Performance = new { Files = 5000, RealizedVisuals = visuals, ScrollFrameP95Ms = p95, ScrollFramesOver25Ms = frames.Count(x => x > 25), IdleCpuCores = idleCores }, PerformanceNote = "Frame callback timing is diagnostic, not a GPU presentation or 60 fps certification." });
            app.Exit();
        }
        catch (Exception ex)
        {
            JsonStore.Write(Path.Combine(session.Paths.Data, "smoke-result.json"), new { Passed = false, Checks = report, Performance = diagnostics, Error = ex.ToString() });
            app.Exit(); Environment.ExitCode = 1;
        }
    }
}
