using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Racks.Core;

namespace Racks.Views
{
    public enum OrganizeChoice
    {
        Cancel,
        Racks,
        Folders
    }

    public partial class AutoOrganizePreviewDialog : Window
    {
        private List<ClusterGroup> _clusters;
        public OrganizeChoice Result { get; private set; } = OrganizeChoice.Cancel;

        public AutoOrganizePreviewDialog(List<ClusterGroup> clusters)
        {
            InitializeComponent();
            Racks.Util.WindowFade.Attach(this);
            _clusters = clusters;
            
            // Format FilePaths to just show filenames for a cleaner UI
            var displayClusters = clusters.Select(c => new 
            {
                Name = c.Name,
                FilePaths = c.FilePaths.Select(f => Path.GetFileName(f)).ToList()
            }).ToList();

            ClustersControl.ItemsSource = displayClusters;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = OrganizeChoice.Cancel;
            DialogResult = false;
            Close();
        }

        private void ApplyRacks_Click(object sender, RoutedEventArgs e)
        {
            Result = OrganizeChoice.Racks;
            DialogResult = true;
            Close();
        }

        private void ApplyFolders_Click(object sender, RoutedEventArgs e)
        {
            Result = OrganizeChoice.Folders;
            DialogResult = true;
            Close();
        }
    }
}
