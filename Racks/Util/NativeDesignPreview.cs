using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using FontFamily = System.Windows.Media.FontFamily;
using Control = System.Windows.Controls.Control;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Racks.Util;

internal static class NativeDesignPreview
{
    public static void Show(MainWindow shell)
    {
        if (!NativeProfile.IsIsolated) throw new InvalidOperationException("An isolated profile is required.");
        CossTheme.Apply(false);
        var controller = MainWindow._controller;
        if (NativeProfile.DesignPreview && controller.Instances.Count == 0)
        {
            CreateRack("Original", 48, ThemePresets.All.First(x => x.Name == "Dark (default)"));
            CreateRack("Studio", 448, ThemePresets.CossDark);
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Title = "Racks · native design review", Width = 780, Height = 112, MinHeight = 0, MaxHeight = 112, Left = 48, Top = 44, ResizeMode = ResizeMode.NoResize, Content = panel, FontFamily = new FontFamily("Segoe UI"), FontSize = 13 };
        CossTheme.Attach(window); window.SetResourceReference(Control.BackgroundProperty, "CossBackground"); window.SetResourceReference(Control.ForegroundProperty, "CossText");
        void Add(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => action(); panel.Children.Add(button);
        }
        void Theme(bool light)
        {
            CossTheme.Apply(light);
            var studio = controller._subWindows.FirstOrDefault(x => x.Instance.Name == "Studio");
            if (studio != null) { ThemePresets.Apply(studio.Instance, light ? ThemePresets.CossLight : ThemePresets.CossDark); studio.ChangeBackgroundOpacity(studio.Instance.Opacity); }
        }
        Add("Light", () => Theme(true)); Add("Dark", () => Theme(false));
        Add("Appearance", () => { var rack = controller._subWindows.FirstOrDefault(x => x.Instance.Name == "Studio") ?? controller._subWindows.FirstOrDefault(); if (rack != null) new RackSettingsDialog(rack).Show(); });
        Add("New rack", controller.AddVirtualInstance);
        Add("Test files", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(NativeProfile.GetFolderPath(Environment.SpecialFolder.Desktop)) { UseShellExecute = true }));
        Add("Quit preview", () => System.Windows.Application.Current.Shutdown());
        window.Closed += (_, _) => System.Windows.Application.Current.Shutdown();
        System.Windows.Application.Current.MainWindow = shell;
        window.Show();
        if (Environment.GetCommandLineArgs().Contains("--native-smoke")) _ = NativePreviewSmoke.Run(window);
    }

    private static void CreateRack(string name, double left, ThemePresets.Preset preset)
    {
        var controller = MainWindow._controller;
        var folder = Path.Combine(NativeProfile.Root!, "Workspace", name);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException("Use a fresh design-preview profile; existing fixture files will not be overwritten.");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Project notes.txt"), "Racks native design review\nReal files in an isolated profile.\n");
        File.WriteAllText(Path.Combine(folder, "Colour study.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"280\"><rect width=\"400\" height=\"280\" rx=\"28\" fill=\"#e9e4db\"/><circle cx=\"136\" cy=\"128\" r=\"78\" fill=\"#407d72\"/><rect x=\"185\" y=\"72\" width=\"125\" height=\"150\" rx=\"18\" fill=\"#d1a07d\"/></svg>");
        File.WriteAllText(Path.Combine(folder, "Launch checklist.md"), "# Launch checklist\n- Review the rack\n- Check menus and keyboard focus\n- Keep the original behaviour\n");
        Directory.CreateDirectory(Path.Combine(folder, "Reference"));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(223, 220, 207)), null, new Rect(0, 0, 480, 320));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(63, 111, 98)), null, new Point(194, 128), 94, 94);
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(175, 124, 99)), null, new Rect(260, 106, 118, 168), 20, 20);
        }
        var bitmap = new RenderTargetBitmap(480, 320, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(folder, "Moodboard.png"))) encoder.Save(output);
        var rack = new Instance(name, false) { Folder = folder, TitleText = name == "Original" ? "Original rack" : "Studio", Width = 360, Height = 392, PosX = left, PosY = 194, IconSize = 48, ShowInGrid = true, IdleOpacity = 1, Opacity = 100, ShowFileExtension = true, ShowShortcutArrow = false, FolderOpenInsideFrame = false };
        ThemePresets.Apply(rack, preset);
        controller.Instances.Add(rack); controller.WriteInstanceToKey(rack);
        var window = new RackWindow(rack); controller._subWindows.Add(window); window.ChangeBackgroundOpacity(rack.Opacity); window.Show();
        var desktop = NativeProfile.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!File.Exists(Path.Combine(desktop, "Drop me.txt"))) File.WriteAllText(Path.Combine(desktop, "Drop me.txt"), "Isolated drag fixture.");
    }
}

internal static class NativePreviewSmoke
{
    public static async Task Run(Window review)
    {
        var checks = new List<string>();
        try
        {
            await Task.Delay(1800);
            var controller = MainWindow._controller;
            if (controller._subWindows.Count != 2) throw new InvalidOperationException("Expected two native racks.");
            checks.Add("Original and Coss native racks opened");
            var rack = controller._subWindows.Single(x => x.Instance.Name == "Studio");
            if (rack.FileItems.Count != 5 || rack.FileItems.Any(x => x.Thumbnail == null)) throw new InvalidOperationException("A native file icon or thumbnail is missing.");
            checks.Add("Every fixture has a real shell icon or image thumbnail");
            using var saved = ProfileRegistry.CurrentUser.OpenSubKey(rack.Instance.GetKeyLocation());
            if (!Equals(saved?.GetValue("TitleText"), "Studio")) throw new InvalidOperationException("Rack settings did not persist.");
            checks.Add("Native settings persisted inside the isolated profile");
            foreach (var light in new[] { false, true })
            {
                CossTheme.Apply(light);
                var dialog = new RackSettingsDialog(rack); dialog.Show(); await Task.Delay(350);
                if (!dialog.IsVisible || dialog.ActualWidth < 440) throw new InvalidOperationException("Appearance dialog did not render.");
                dialog.Close(); await Task.Delay(250);
                checks.Add((light ? "Light" : "Dark") + " native appearance dialog opened and closed");
            }
            Core.JsonStore.Write(Path.Combine(NativeProfile.Root!, "native-smoke.json"), new { Passed = true, Checks = checks });
        }
        catch (Exception error) { Core.JsonStore.Write(Path.Combine(NativeProfile.Root!, "native-smoke.json"), new { Passed = false, Checks = checks, Error = error.ToString() }); Environment.ExitCode = 1; }
        finally { System.Windows.Application.Current.Shutdown(); }
    }
}
