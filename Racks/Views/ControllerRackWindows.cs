using System.Diagnostics;
using Racks.UseCases.Tray;

namespace Racks.Views
{
    /// <summary>The rack windows of the <see cref="InstanceController"/>, for the tray toggles and the hot corner.</summary>
    public sealed class ControllerRackWindows : IRackWindowControl
    {
        private readonly InstanceController _controller;

        public ControllerRackWindows(InstanceController controller) => _controller = controller;

        public IReadOnlyList<Instance> Racks => _controller.Instances;

        public void ApplyLockedStateToWindows()
        {
            foreach (var w in _controller._subWindows)
            {
                try { w.ApplyLockedState(); }
                catch (Exception ex) { Debug.WriteLine($"ApplyLockedState failed: {ex.Message}"); }
            }
        }

        public void HideAll()
        {
            foreach (var w in _controller._subWindows) w.Hide();
        }

        public void ShowAll()
        {
            foreach (var w in _controller._subWindows) w.Show();
        }
    }
}
