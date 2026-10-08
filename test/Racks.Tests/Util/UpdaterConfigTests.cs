using System.IO;
using Racks.Tests.Guards;

namespace Racks.Tests.Util;

public class UpdaterConfigTests
{
    [Fact]
    public void Release_feed_points_at_this_repository()
        => Assert.Equal("https://api.github.com/repos/duartelcunha/Racks/releases/latest", Updater.LatestReleaseApi);

    [Fact]
    public void Release_feed_is_defined_once()
    {
        // Callers must use Updater.LatestReleaseApi instead of repeating the URL.
        var hits = Directory.EnumerateFiles(RepoPaths.App(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("repos/duartelcunha/Racks/releases/latest"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(new[] { "Updater.cs" }, hits);
    }

    [Fact]
    public void Auto_update_setting_does_not_touch_windows_startup()
    {
        // "Auto update" used to add/remove the app from Windows startup. That is the tray's
        // "Start on login" switch; the settings handler must only store the preference.
        string src = File.ReadAllText(RepoPaths.App("SettingsWindow.xaml.cs"));
        int i = src.IndexOf("AutoUpdateToggleSwitch_Click", StringComparison.Ordinal);
        string handler = src.Substring(i, src.IndexOf("ManageFrameButton_Click", i, StringComparison.Ordinal) - i);
        Assert.DoesNotContain("AutoRun", handler);
        Assert.Contains("\"AutoUpdate\"", handler);
    }
}
