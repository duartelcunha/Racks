using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Racks.Util
{
    // Watches the user's Desktop for newly-created files. Any file whose name matches
    // the first rack's AutoRouteRegex gets a .lnk created in that rack's folder; the
    // original on the Desktop is left alone (consistent with the safe-drop philosophy).
    // First-match-wins keeps behavior deterministic when rules overlap.
    public sealed class DesktopRouter : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly DispatcherTimer _debounce;
        private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<IEnumerable<Instance>> _instancesProvider;
        private readonly Action<string, string> _routeShortcut; // (sourcePath, destFolder)

        public DesktopRouter(Func<IEnumerable<Instance>> instancesProvider,
                             Action<string, string> routeShortcut)
        {
            _instancesProvider = instancesProvider;
            _routeShortcut = routeShortcut;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            _watcher = new FileSystemWatcher(desktop)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) => Enqueue(e.FullPath);
            _watcher.Renamed += (_, e) => Enqueue(e.FullPath);

            // Debounce — a save or copy can fire Created before the file is fully on
            // disk; wait ~600ms for things to settle before we try to shortcut it.
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _debounce.Tick += (_, _) => Flush();
        }

        private void Enqueue(string path)
        {
            lock (_pending) _pending.Add(path);
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _debounce.Stop();
                _debounce.Start();
            }));
        }

        private void Flush()
        {
            _debounce.Stop();
            List<string> snapshot;
            lock (_pending)
            {
                snapshot = new List<string>(_pending);
                _pending.Clear();
            }

            var instances = new List<Instance>(_instancesProvider());
            foreach (var path in snapshot)
            {
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                string name = Path.GetFileName(path);
                foreach (var inst in instances)
                {
                    if (string.IsNullOrWhiteSpace(inst.AutoRouteRegex)) continue;
                    if (string.IsNullOrEmpty(inst.Folder) || !Directory.Exists(inst.Folder)) continue;
                    bool match;
                    try { match = Regex.IsMatch(name, inst.AutoRouteRegex, RegexOptions.IgnoreCase); }
                    catch { continue; /* bad regex — skip silently rather than spam */ }
                    if (!match) continue;

                    try { _routeShortcut(path, inst.Folder); } catch { }
                    break; // first match wins
                }
            }
        }

        public void Dispose()
        {
            try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } catch { }
            try { _debounce.Stop(); } catch { }
        }
    }
}
