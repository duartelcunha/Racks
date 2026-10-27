using System;
using System.Collections.Generic;
using System.IO;
using Racks.Application.AutoRoute;
using Racks.Core.Abstractions;

namespace Racks.Util
{
    // Watches the user's Desktop for new files and hands each path to the auto-route use case, once the burst
    // of events has settled. The decisions (which rack, may it move, the move itself) live in
    // Racks.Application.AutoRoute; this class only keeps the watcher alive and the events tidy.
    //
    // Everything below runs on the UI thread: the watcher's events are posted there first. That is what lets
    // the Dispatcher-based scheduler (a DispatcherTimer must be created on the UI thread) and the racks list
    // be used without locks, and it is where the old code moved files too.
    public sealed class DesktopRouter : IDisposable
    {
        // A save or a copy raises Created before the file is whole. The first event of a burst starts this
        // window; every path seen during it is routed once when it ends.
        internal static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(600);
        // After a watcher Error (buffer overflow, folder gone): wait, rebuild the watcher, look at the Desktop once.
        // A second Error before any real event waits twice as long, up to MaxRestartDelay, so a watcher that
        // keeps failing never turns into a tight loop.
        internal static readonly TimeSpan RestartDelay = TimeSpan.FromMilliseconds(250);
        internal static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(30);

        // The default 8 KB buffer overflows when hundreds of files land at once (same value as FileWatcherService).
        private const int WatcherBufferBytes = 64 * 1024;

        private readonly Action<string> _route;
        private readonly string _desktop;
        private readonly IDelayScheduler _scheduler;
        private readonly Action<Action> _post;
        private readonly Func<string, FileSystemWatcher> _watcherFactory;

        private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private IDisposable? _flushTimer;
        private IDisposable? _restartTimer;
        private FileSystemWatcher? _watcher;
        private int _errorStreak;
        private bool _disposed;

        public DesktopRouter(AutoRouteService service)
            : this(p => service.Process(p), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                   new DispatcherDelayScheduler(), PostToUiThread, NewWatcher)
        {
        }

        internal DesktopRouter(Action<string> route, string desktopFolder, IDelayScheduler scheduler,
                               Action<Action> post, Func<string, FileSystemWatcher> watcherFactory)
        {
            _route = route;
            _desktop = desktopFolder;
            _scheduler = scheduler;
            _post = post;
            _watcherFactory = watcherFactory;

            try { StartWatcher(); }
            catch (Exception ex)
            {
                // The Desktop may be unreachable for a moment; recover the same way as after a watcher Error.
                System.Diagnostics.Debug.WriteLine($"DesktopRouter start failed: {ex.Message}");
                HandleWatcherError();
            }
        }

        private static void PostToUiThread(Action action)
        {
            try { System.Windows.Application.Current?.Dispatcher.BeginInvoke(action); }
            catch { /* shutting down */ }
        }

        private static FileSystemWatcher NewWatcher(string folder) => new(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            InternalBufferSize = WatcherBufferBytes,
        };

        private void StartWatcher()
        {
            var watcher = _watcherFactory(_desktop);
            watcher.Created += OnCreatedOrRenamed;
            watcher.Renamed += OnCreatedOrRenamed;
            watcher.Error += OnWatcherError;
            try { watcher.EnableRaisingEvents = true; }
            catch { DetachAndDispose(watcher); throw; }
            _watcher = watcher;
        }

        private void StopWatcher()
        {
            var watcher = _watcher;
            _watcher = null;
            if (watcher != null) DetachAndDispose(watcher);
        }

        private void DetachAndDispose(FileSystemWatcher watcher)
        {
            watcher.Created -= OnCreatedOrRenamed;
            watcher.Renamed -= OnCreatedOrRenamed;
            watcher.Error -= OnWatcherError;
            try { watcher.EnableRaisingEvents = false; } catch { }
            try { watcher.Dispose(); } catch { }
        }

        // Watcher thread -> UI thread. A Renamed event carries the NEW path, so a download that is renamed from
        // "x.crdownload" to "x.pdf" is routed under its final name.
        private void OnCreatedOrRenamed(object sender, FileSystemEventArgs e) => _post(() => Enqueue(e.FullPath));
        private void OnWatcherError(object sender, ErrorEventArgs e) => _post(HandleWatcherError);

        internal void Enqueue(string path)
        {
            if (_disposed) return;
            _errorStreak = 0;   // a real event: the watcher is alive
            Add(path);
        }

        private void Add(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _pending.Add(path);   // the same path twice in one window is routed once
            _flushTimer ??= _scheduler.Schedule(DebounceWindow, Flush);
        }

        internal void Flush()
        {
            _flushTimer = null;
            if (_disposed || _pending.Count == 0) return;
            var snapshot = new List<string>(_pending);
            _pending.Clear();
            foreach (var path in snapshot)
            {
                if (_disposed) return;
                try { _route(path); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Auto-route failed for a path: {ex.Message}"); }
            }
        }

        // The watcher raised Error: its buffer overflowed (events were lost) or the Desktop went away, after which
        // it stays silent. Rebuild it shortly after and look at the Desktop once, so nothing that arrived in the gap
        // is missed. Errors that arrive while a restart is already waiting join that restart.
        internal void HandleWatcherError()
        {
            if (_disposed || _restartTimer != null) return;
            double factor = Math.Pow(2, Math.Min(_errorStreak, 16));
            var delay = TimeSpan.FromMilliseconds(Math.Min(RestartDelay.TotalMilliseconds * factor, MaxRestartDelay.TotalMilliseconds));
            _errorStreak++;
            _restartTimer = _scheduler.Schedule(delay, Restart);
        }

        private void Restart()
        {
            _restartTimer = null;
            if (_disposed) return;
            try
            {
                StopWatcher();
                StartWatcher();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DesktopRouter restart failed: {ex.Message}");
                HandleWatcherError();   // try again later, with a longer wait
                return;
            }
            Rescan();
        }

        // The watcher only reports files (FileName filter), so the rescan does too.
        private void Rescan()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_desktop)) Add(file);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DesktopRouter rescan failed: {ex.Message}");
            }
        }

        // Where a matching file goes. Kept here because tests and callers know this entry point; the logic is the service's.
        internal static string DestinationFor(Instance rack, string workspace)
            => AutoRouteService.DestinationFor(rack, workspace);

        public void Dispose()
        {
            _disposed = true;
            StopWatcher();
            try { _flushTimer?.Dispose(); } catch { }
            try { _restartTimer?.Dispose(); } catch { }
            _flushTimer = null;
            _restartTimer = null;
            _pending.Clear();
        }
    }
}
