using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Racks.Core;
using Racks.Services;
using Racks.Util;

namespace Racks.Views
{
    public partial class DevDiagnosticsOverlay : Window
    {
        private readonly InstanceController? _controller;
        private readonly DispatcherTimer _statsTimer;
        private readonly Stopwatch _fpsStopwatch = new();
        private int _frameCount;
        private double _lastFps;
        private double _lastDeltaMs;
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private bool _disposed;

        public DevDiagnosticsOverlay(InstanceController? controller = null)
        {
            InitializeComponent();
            _controller = controller;

            Title = "Racks Dev Diagnostics";
            HeaderTitle.Text = "🔧 DEV DIAGNOSTICS";
            CloseButton.Content = "✕";

            WindowFade.Attach(this);

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Top + 24;

            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statsTimer.Tick += StatsTimer_Tick;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _fpsStopwatch.Restart();
            CompositionTarget.Rendering += OnRendering;
            _statsTimer.Start();
            UpdateStats();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed) return;
            _disposed = true;
            CompositionTarget.Rendering -= OnRendering;
            _statsTimer.Stop();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            _frameCount++;
            if (e is RenderingEventArgs renderingArgs)
            {
                if (_lastRenderTime != TimeSpan.Zero)
                {
                    _lastDeltaMs = (renderingArgs.RenderingTime - _lastRenderTime).TotalMilliseconds;
                }
                _lastRenderTime = renderingArgs.RenderingTime;
            }

            if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
            {
                _lastFps = (_frameCount * 1000.0) / _fpsStopwatch.ElapsedMilliseconds;
                _frameCount = 0;
                _fpsStopwatch.Restart();

                FpsText.Text = $"FPS: {_lastFps:F1}";
                FrameDeltaText.Text = $"Frame Time: {_lastDeltaMs:F1} ms";

                if (_lastFps >= 50)
                    FpsText.Foreground = Brushes.LightGreen;
                else if (_lastFps >= 30)
                    FpsText.Foreground = Brushes.Yellow;
                else
                    FpsText.Foreground = Brushes.Salmon;
            }
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            UpdateStats();
        }

        private void UpdateStats()
        {
            try
            {
                long memBytes = GC.GetTotalMemory(false);
                MemoryText.Text = $"Memory: {memBytes / (1024.0 * 1024.0):F1} MB";

                if (_controller != null)
                {
                    int rackCount = _controller._subWindows.Count;
                    int hwndCount = _controller._subWindowsPtr.Count;
                    InstancesText.Text = $"Active Racks: {rackCount}";
                    HwndText.Text = $"Tracked HWNDs: {hwndCount}";
                }
                else
                {
                    InstancesText.Text = "Active Racks: --";
                    HwndText.Text = "Tracked HWNDs: --";
                }

                int hits = DiagnosticCounters.ThumbnailCacheHits;
                int misses = DiagnosticCounters.ThumbnailCacheMisses;
                int cached = DiagnosticCounters.ThumbnailCacheCount;
                ThumbnailCacheText.Text = $"Thumb Cache: {hits} hits / {misses} miss ({cached} items)";

                int bodyCount = GetPhysicsBodyCount();
                PhysicsText.Text = $"Physics: {(RackPhysics.Enabled ? "Active" : "Disabled")} ({bodyCount} bodies)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DevDiagnostics error: {ex.Message}");
            }
        }

        private static int GetPhysicsBodyCount()
        {
            try
            {
                var field = typeof(RackPhysics).GetField("_bodies", BindingFlags.NonPublic | BindingFlags.Static);
                if (field?.GetValue(null) is System.Collections.ICollection collection)
                {
                    return collection.Count;
                }
            }
            catch { }
            return 0;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
