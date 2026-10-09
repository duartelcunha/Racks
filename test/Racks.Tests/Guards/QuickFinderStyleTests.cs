using System.IO;
using System.Xml.Linq;

namespace Racks.Tests.Guards;

// Wpf.Ui paints a selected ListBoxItem with the Windows accent and black text. On a dark accent that is
// hard to read, and the "Rack - path" subtitle had a fixed gray on top of it. QuickFinderWindow.xaml
// overrides the two theme brushes; these tests keep it that way without needing a WPF host.
public class QuickFinderStyleTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Load() => XDocument.Load(RepoPaths.App("QuickFinderWindow.xaml"));

    private static string? ResourceColor(string key) =>
        Load().Descendants(Presentation + "SolidColorBrush")
            .FirstOrDefault(b => (string?)b.Attribute(Xaml + "Key") == key)
            ?.Attribute("Color")?.Value;

    [Fact]
    public void The_selected_row_does_not_use_the_accent_colour_for_its_background()
    {
        var color = ResourceColor("ListBoxItemSelectedBackgroundThemeBrush");
        Assert.True(color != null, "QuickFinderWindow.xaml must override ListBoxItemSelectedBackgroundThemeBrush");
        Assert.Matches("^#[0-9A-Fa-f]{2}FFFFFF$", color!); // white with alpha: neutral on any accent
    }

    [Fact]
    public void The_selected_row_keeps_white_text()
    {
        Assert.Equal("White", ResourceColor("ListBoxItemSelectedForegroundThemeBrush"));
    }

    [Fact]
    public void The_subtitle_is_brighter_than_the_old_fixed_gray()
    {
        var subtitle = Load().Descendants(Presentation + "TextBlock")
            .First(t => ((string?)t.Attribute("Text"))?.Contains("Subtitle") == true);
        var foreground = (string?)subtitle.Attribute("Foreground");
        Assert.NotNull(foreground);
        Assert.NotEqual("#888", foreground);
        Assert.Matches("^#[B-F][0-9A-Fa-f]FFFFFF$", foreground!); // alpha 0xB0 or more, white
    }
}
