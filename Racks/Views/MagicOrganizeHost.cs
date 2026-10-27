using System.Windows;
using Racks.Core;
using Racks.UseCases.MagicOrganize;

namespace Racks.Views
{
    /// <summary>
    /// The windows and dialogs behind <see cref="MagicOrganizeService"/>: the preview dialog, the message boxes, the work area
    /// of the primary monitor, and creating/showing/closing the <see cref="RackWindow"/> of each new rack.
    /// </summary>
    public sealed class MagicOrganizeHost : IMagicOrganizeHost
    {
        private readonly InstanceController _controller;

        public MagicOrganizeHost(InstanceController controller) => _controller = controller;

        public IList<Instance> Racks => _controller.Instances;

        public void SaveRack(Instance rack) => _controller.WriteInstanceToKey(rack);

        public MagicOrganizeUndo.Mode? ChoosePlan(List<ClusterGroup> clusters)
        {
            var dialog = new AutoOrganizePreviewDialog(clusters);
            if (dialog.ShowDialog() == true && dialog.Result != OrganizeChoice.Cancel)
            {
                return dialog.Result == OrganizeChoice.Racks ? MagicOrganizeUndo.Mode.Racks
                     : dialog.Result == OrganizeChoice.Folders ? MagicOrganizeUndo.Mode.Folders
                     : null;
            }
            return null;
        }

        public MagicOrganizeWorkArea GetWorkArea()
        {
            var workingArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            double dpiScale = System.Drawing.Graphics.FromHwnd(IntPtr.Zero).DpiX / 96f;
            return new MagicOrganizeWorkArea(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height, dpiScale);
        }

        public async Task ShowRackAsync(Instance rack)
        {
            var subWindow = new RackWindow(rack);
            subWindow.ChangeBackgroundOpacity(rack.Opacity);
            _controller._subWindows.Add(subWindow);

            await Task.Delay(100); // Staggered entry animation

            subWindow.Show();
            subWindow.Topmost = true; // Ensure it is visible to the user immediately
            _controller._subWindowsPtr.Add(new System.Windows.Interop.WindowInteropHelper(subWindow).Handle);
        }

        public void CloseRack(Instance rack)
        {
            var win = _controller._subWindows.FirstOrDefault(w => w.Instance == rack);
            if (win != null) { try { win.Close(); } catch { } _controller.RemoveInstance(rack, win); }
            else _controller.Instances.Remove(rack);
        }

        public void Alert(string message, string title) => RacksMessageBox.Show(message, title);

        public bool Confirm(string message, string title, string yes, string no) => RacksMessageBox.Confirm(message, title, yes, no);
    }
}
