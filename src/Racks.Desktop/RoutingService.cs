using System.Collections.Concurrent;
using Avalonia.Threading;
using Racks.Core;

namespace Racks.Desktop;

public sealed class RoutingService(Session session) : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = new();
    private CancellationTokenSource lifetime = new();

    public void Rebuild()
    {
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
        if (session.SafeMode || session.ReadOnly || session.Settings.RoutingPaused) return;
        var token = lifetime.Token;
        var pending = new ConcurrentDictionary<string, byte>();
        foreach (var folder in session.Settings.Rules.Where(x => x.Enabled).Select(x => x.Source).Distinct().ToArray())
        {
            FileSystemWatcher? watcher = null;
            try
            {
                if (!Directory.Exists(folder)) throw new IOException("Source folder is unavailable. Reconnect it, then preview and enable the rule again.");
                watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                watcher.Created += (_, e) => Queue(e.FullPath, token, pending);
                watcher.Renamed += (_, e) => Queue(e.FullPath, token, pending);
                watcher.Error += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested) PauseFolder(folder, "Folder notifications were lost. Preview and enable the rule again.");
                });
                watcher.EnableRaisingEvents = true; watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                watcher?.Dispose(); PauseFolder(folder, ex.Message);
            }
        }
    }

    private void PauseFolder(string folder, string error)
    {
        foreach (var rule in session.Settings.Rules.Where(x => x.Source == folder)) { rule.Enabled = false; rule.LastError = error; }
        try { session.Save(); } catch (Exception ex) { session.Problems.Add("Could not save routing pause: " + ex.Message); }
        session.Status = "Routing paused: " + error;
    }

    private async void Queue(string path, CancellationToken token, ConcurrentDictionary<string, byte> pending)
    {
        if (token.IsCancellationRequested || !pending.TryAdd(path, 0)) return;
        try
        {
            if (!File.Exists(path)) return; // New folders may still be receiving nested files.
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".tmp" or ".part" or ".crdownload" or ".download") return;
            long size = -1; DateTime modified = default; var stable = 0;
            for (var attempt = 0; attempt < 30 && stable < 3; attempt++)
            {
                await Task.Delay(1000, token);
                var info = new FileInfo(path);
                if (!info.Exists) return;
                stable = info.Length == size && info.LastWriteTimeUtc == modified ? stable + 1 : 0;
                size = info.Length; modified = info.LastWriteTimeUtc;
            }
            if (stable < 3) return;
            // Exclusive open is an extra readiness signal; a fixed delay alone is insufficient.
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (await Dispatcher.UIThread.InvokeAsync(() => session.CurrentOperation == null && !session.Operations.IsBusy)) break;
                await Task.Delay(1000, token);
            }
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested || session.Settings.RoutingPaused) return;
                if (session.CurrentOperation != null || session.Operations.IsBusy)
                {
                    session.Status = "Routing left a waiting file in its source folder because another operation is still active.";
                    return;
                }
                foreach (var rule in session.Settings.Rules.Where(x => x.Enabled && Path.GetDirectoryName(path)!.Equals(x.Source, SafeFiles.PathComparison)))
                {
                    try
                    {
                        RuleMatcher.Validate(rule, session.Settings);
                        if (!RuleMatcher.Matches(rule, path)) continue;
                        var destination = session.Settings.Racks.First(x => x.Id == rule.DestinationRackId);
                        var result = await session.MoveIntoAsync(destination, new[] { path }, rule.Shortcut, CollisionChoice.Skip);
                        if (result.NeedsAttention) { rule.Enabled = false; rule.LastError = "A transfer failed. Check Recovery before enabling this rule."; session.Save(); }
                    }
                    catch (Exception ex) { rule.Enabled = false; rule.LastError = ex.Message; try { session.Save(); } catch (Exception saveError) { session.Problems.Add("Could not save routing pause: " + saveError.Message); } }
                    break;
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Dispatcher.UIThread.Post(() => session.Status = "Routing kept the original: " + ex.Message); }
        finally { pending.TryRemove(path, out _); }
    }

    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); }
}
