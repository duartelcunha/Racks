using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Racks.Core;

namespace Racks.ViewModels
{
    public class RackViewModel : INotifyPropertyChanged
    {
        private readonly Instance _instance;
        public Instance Instance => _instance;
        private readonly InstanceController _controller;

        private ObservableCollection<FileItem> _fileItems = new ObservableCollection<FileItem>();
        public ObservableCollection<FileItem> FileItems
        {
            get => _fileItems;
            set
            {
                _fileItems = value;
                OnPropertyChanged();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _folderCount = "0";
        public string FolderCount
        {
            get => _folderCount;
            set { _folderCount = value; OnPropertyChanged(); }
        }

        private string _fileCount = "0";
        public string FileCount
        {
            get => _fileCount;
            set { _fileCount = value; OnPropertyChanged(); }
        }

        private string _folderSize = "";
        public string FolderSize
        {
            get => _folderSize;
            set { _folderSize = value; OnPropertyChanged(); }
        }

        public RackViewModel(Instance instance, InstanceController controller)
        {
            _instance = instance;
            _controller = controller;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
