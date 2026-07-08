using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
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

            this.WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += (_, _) =>
            {
                Racks.Util.WindowPlacement.CenterOnCursorScreen(this);
                var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
                var anim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromSeconds(0.22)) { EasingFunction = ease };
                RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
                RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
            };
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = OrganizeChoice.Cancel;
            DialogResult = false;
            Close();
        }

        private void ApplyRacks_Click(object sender, RoutedEventArgs e)
        {
            int n = _clusters.Sum(c => c.FilePaths.Count);
            if (!RacksMessageBox.Confirm(
                    $"Move {n} item(s) into {_clusters.Count} rack(s)? Your files are moved off the desktop into racks.",
                    "Organize in racks", "Organize", "Cancel"))
                return;
            Result = OrganizeChoice.Racks;
            DialogResult = true;
            Close();
        }

        private void ApplyFolders_Click(object sender, RoutedEventArgs e)
        {
            int n = _clusters.Sum(c => c.FilePaths.Count);
            if (!RacksMessageBox.Confirm(
                    $"Move {n} item(s) into {_clusters.Count} folder(s) on your desktop?",
                    "Organize in folders", "Organize", "Cancel"))
                return;
            Result = OrganizeChoice.Folders;
            DialogResult = true;
            Close();
        }
    }
}
