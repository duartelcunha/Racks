using System.Diagnostics;
using System.Windows;

namespace Racks.Views
{
    /// <summary>
    /// A window the tray can open at most once at a time (settings, help, dev diagnostics): open it if it is not showing,
    /// otherwise bring it to the front (or, for <see cref="Toggle"/>, close it). A failure is logged, never thrown.
    /// </summary>
    public sealed class SingleInstanceWindow<T> where T : Window
    {
        private readonly Func<T> _create;
        private readonly string _name;
        private T? _window;

        public SingleInstanceWindow(string name, Func<T> create)
        {
            _name = name;
            _create = create;
        }

        public void ShowOrActivate()
        {
            try
            {
                if (!Open()) _window!.Activate();
            }
            catch (Exception ex) { Debug.WriteLine($"{_name} open failed: {ex.Message}"); }
        }

        public void Toggle()
        {
            try
            {
                if (!Open())
                {
                    _window!.Close();
                    _window = null;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"{_name} toggle failed: {ex.Message}"); }
        }

        // True when it was not showing and a new one was opened.
        private bool Open()
        {
            if (_window != null && _window.IsLoaded) return false;
            var window = _create();
            _window = window;
            window.Closed += (_, _) => _window = null;
            window.Show();
            return true;
        }
    }
}
