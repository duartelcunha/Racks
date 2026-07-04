using System;
using System.IO;

namespace Racks.Services
{
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher? _parentWatcher;
        private FileSystemWatcher? _fileWatcher;

        public event EventHandler<FileSystemEventArgs>? ParentChanged;
        public event EventHandler<RenamedEventArgs>? ParentRenamed;
        public event EventHandler<FileSystemEventArgs>? FileChanged;
        public event EventHandler<RenamedEventArgs>? FileRenamed;

        public void Initialize(string instanceFolder, string currentFolderPath)
        {
            if (!string.IsNullOrEmpty(instanceFolder) && instanceFolder != "empty")
            {
                if (_parentWatcher != null)
                {
                    _parentWatcher.Created -= OnParentChanged;
                    _parentWatcher.Deleted -= OnParentChanged;
                    _parentWatcher.Renamed -= OnParentRenamed;
                    _parentWatcher.Dispose();
                }

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
                if (_fileWatcher != null)
                {
                    _fileWatcher.Created -= OnFileChanged;
                    _fileWatcher.Deleted -= OnFileChanged;
                    _fileWatcher.Renamed -= OnFileRenamed;
                    _fileWatcher.Changed -= OnFileChanged;
                    _fileWatcher.Dispose();
                }

                if (Directory.Exists(currentFolderPath))
                {
                    _fileWatcher = new FileSystemWatcher(currentFolderPath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _fileWatcher.Created += OnFileChanged;
                    _fileWatcher.Deleted += OnFileChanged;
                    _fileWatcher.Renamed += OnFileRenamed;
                    _fileWatcher.Changed += OnFileChanged;
                }
            }
        }

        private void OnParentChanged(object sender, FileSystemEventArgs e) => ParentChanged?.Invoke(this, e);
        private void OnParentRenamed(object sender, RenamedEventArgs e) => ParentRenamed?.Invoke(this, e);
        private void OnFileChanged(object sender, FileSystemEventArgs e) => FileChanged?.Invoke(this, e);
        private void OnFileRenamed(object sender, RenamedEventArgs e) => FileRenamed?.Invoke(this, e);

        public void Dispose()
        {
            if (_parentWatcher != null)
            {
                _parentWatcher.Created -= OnParentChanged;
                _parentWatcher.Deleted -= OnParentChanged;
                _parentWatcher.Renamed -= OnParentRenamed;
                _parentWatcher.Dispose();
                _parentWatcher = null;
            }

            if (_fileWatcher != null)
            {
                _fileWatcher.Created -= OnFileChanged;
                _fileWatcher.Deleted -= OnFileChanged;
                _fileWatcher.Renamed -= OnFileRenamed;
                _fileWatcher.Changed -= OnFileChanged;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }
    }
}
