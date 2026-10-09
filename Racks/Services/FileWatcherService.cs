using System;
using System.Collections.Generic;
using System.IO;
using Racks.Core.Abstractions;

namespace Racks.Services
{
    public class FileWatcherService : IDisposable
    {
        // A file that is being written (a download, a copy) raises a Changed event for every
        // chunk. Content changes are held back until the file has been quiet for this long, then
        // raised once. Created/Deleted/Renamed are structural and go through immediately.
        internal static readonly TimeSpan ChangedQuietPeriod = TimeSpan.FromMilliseconds(750);
        internal static readonly TimeSpan RestartDelay = TimeSpan.FromMilliseconds(250);

        // The default 8 KB buffer overflows when a folder gets hundreds of changes at once.
        private const int WatcherBufferBytes = 64 * 1024;

        private readonly IDelayScheduler _scheduler;
        private readonly object _gate = new();
        private readonly Dictionary<string, IDisposable> _pendingChanged = new(StringComparer.OrdinalIgnoreCase);
        private IDisposable? _pendingRestart;
        private bool _disposed;

        private FileSystemWatcher? _parentWatcher;
        private FileSystemWatcher? _fileWatcher;
        private FileSystemWatcher? _extraWatcher;
        private string _instanceFolder = "";
        private string _currentFolderPath = "";
        private string? _alsoWatchFolder;

        public FileWatcherService() : this(new TimerDelayScheduler()) { }

        internal FileWatcherService(IDelayScheduler scheduler) => _scheduler = scheduler;

        public event EventHandler<FileSystemEventArgs>? ParentChanged;
        public event EventHandler<RenamedEventArgs>? ParentRenamed;
        public event EventHandler<FileSystemEventArgs>? FileChanged;
        public event EventHandler<RenamedEventArgs>? FileRenamed;

        // `alsoWatchFolder` is a second folder whose changes refresh the owner too. A desktop rack lists the
        // Desktop but keeps its files in the workspace, so it needs both.
        public void Initialize(string instanceFolder, string currentFolderPath, string? alsoWatchFolder = null)
        {
            _instanceFolder = instanceFolder;
            _currentFolderPath = currentFolderPath;
            _alsoWatchFolder = alsoWatchFolder;

            if (!string.IsNullOrEmpty(instanceFolder) && instanceFolder != "empty")
            {
                DisposeParentWatcher();

                string? parentDir = Path.GetDirectoryName(instanceFolder);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    _parentWatcher = new FileSystemWatcher(parentDir)
                    {
                        NotifyFilter = NotifyFilters.DirectoryName,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _parentWatcher.Created += OnParentChanged;
                    _parentWatcher.Deleted += OnParentChanged;
                    _parentWatcher.Renamed += OnParentRenamed;
                }
            }

            if (!string.IsNullOrEmpty(instanceFolder) && instanceFolder != "empty")
            {
                DisposeFileWatcher();

                if (Directory.Exists(currentFolderPath)) _fileWatcher = NewFileWatcher(currentFolderPath);
                if (!string.IsNullOrEmpty(alsoWatchFolder)
                    && !string.Equals(alsoWatchFolder, currentFolderPath, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(alsoWatchFolder))
                    _extraWatcher = NewFileWatcher(alsoWatchFolder);
            }
        }

        private FileSystemWatcher NewFileWatcher(string folder)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                InternalBufferSize = WatcherBufferBytes,
                EnableRaisingEvents = true
            };
            watcher.Created += OnFileStructureChanged;
            watcher.Deleted += OnFileStructureChanged;
            watcher.Renamed += OnFileRenamed;
            watcher.Changed += OnFileContentChanged;
            watcher.Error += OnFileWatcherError;
            return watcher;
        }

        private void OnParentChanged(object sender, FileSystemEventArgs e) => ParentChanged?.Invoke(this, e);
        private void OnParentRenamed(object sender, RenamedEventArgs e) => ParentRenamed?.Invoke(this, e);
        private void OnFileStructureChanged(object sender, FileSystemEventArgs e) => FileChanged?.Invoke(this, e);
        private void OnFileRenamed(object sender, RenamedEventArgs e) => FileRenamed?.Invoke(this, e);

        // Content change: raise once per file, after it has been quiet for ChangedQuietPeriod.
        private void OnFileContentChanged(object? sender, FileSystemEventArgs e)
        {
            string key = e.FullPath ?? e.Name ?? "";
            lock (_gate)
            {
                if (_disposed) return;
                if (_pendingChanged.Remove(key, out var previous)) previous.Dispose();

                IDisposable? self = null;
                self = _scheduler.Schedule(ChangedQuietPeriod, () =>
                {
                    lock (_gate)
                    {
                        // A newer event for the same file replaced this timer, or we were disposed.
                        if (_disposed || !_pendingChanged.TryGetValue(key, out var current) || !ReferenceEquals(current, self)) return;
                        _pendingChanged.Remove(key);
                    }
                    FileChanged?.Invoke(this, e);
                });
                _pendingChanged[key] = self;
            }
        }

        // The watcher raised Error: its buffer overflowed (events were lost) or the folder went
        // away. Without this the rack silently stops updating. Rebuild the watcher shortly after,
        // and ask the owner for a full rescan so anything missed shows up.
        private void OnFileWatcherError(object sender, ErrorEventArgs e) => RecoverFromWatcherError();

        internal void RecoverFromWatcherError()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _pendingRestart?.Dispose();
                _pendingRestart = _scheduler.Schedule(RestartDelay, () =>
                {
                    string folder, current;
                    string? also;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        _pendingRestart = null;
                        folder = _instanceFolder;
                        current = _currentFolderPath;
                        also = _alsoWatchFolder;
                    }
                    try { Initialize(folder, current, also); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FileWatcher restart failed: {ex.Message}"); }
                    // A rescan request, not a real file event: the owner reloads the folder.
                    FileChanged?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Changed, current, name: null));
                });
            }
        }

        private void DisposeParentWatcher()
        {
            if (_parentWatcher == null) return;
            _parentWatcher.Created -= OnParentChanged;
            _parentWatcher.Deleted -= OnParentChanged;
            _parentWatcher.Renamed -= OnParentRenamed;
            _parentWatcher.Dispose();
            _parentWatcher = null;
        }

        private void DisposeFileWatcher()
        {
            DisposeWatcher(ref _fileWatcher);
            DisposeWatcher(ref _extraWatcher);
        }

        private void DisposeWatcher(ref FileSystemWatcher? watcher)
        {
            if (watcher == null) return;
            watcher.Created -= OnFileStructureChanged;
            watcher.Deleted -= OnFileStructureChanged;
            watcher.Renamed -= OnFileRenamed;
            watcher.Changed -= OnFileContentChanged;
            watcher.Error -= OnFileWatcherError;
            watcher.Dispose();
            watcher = null;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                foreach (var pending in _pendingChanged.Values) pending.Dispose();
                _pendingChanged.Clear();
                _pendingRestart?.Dispose();
                _pendingRestart = null;
            }
            DisposeParentWatcher();
            DisposeFileWatcher();
        }
    }
}
