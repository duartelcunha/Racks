using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Racks.Util;

namespace Racks.Tests.Util;

public class ThemePresetsTests
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var p in ThemePresets.All) data.Add(p.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Applying_a_preset_sets_every_colour_and_the_matching_opacity(string name)
    {
        var preset = ThemePresets.All.Single(p => p.Name == name);
        var instance = new Instance("theme-test", false);
        using (instance.SuspendPersistence())
        {
            ThemePresets.Apply(instance, preset);
        }

        Assert.Equal(preset.TitleBarColor, instance.TitleBarColor);
        Assert.Equal(preset.ListViewBackgroundColor, instance.ListViewBackgroundColor);
        Assert.Equal(preset.TitleTextColor, instance.TitleTextColor);
        Assert.Equal(preset.ListViewFontColor, instance.ListViewFontColor);
        Assert.Equal(preset.ListViewFontShadowColor, instance.ListViewFontShadowColor);
        Assert.Equal(preset.BorderColor, instance.BorderColor);
        Assert.Equal(preset.BorderEnabled, instance.BorderEnabled);
        // The rack background is painted with Instance.Opacity, so it must follow the preset's alpha.
        Assert.Equal(C(preset.ListViewBackgroundColor).A, instance.Opacity);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Dark_text_never_gets_a_light_glow(string name)
    {
        var preset = ThemePresets.All.Single(p => p.Name == name);
        var text = C(preset.ListViewFontColor);
        var shadow = C(preset.ListViewFontShadowColor);
        if (Luminance(text) >= 0.5) return; // light text: a dark shadow is what keeps it readable
        Assert.True(shadow.A == 0 || Luminance(shadow) < 0.5,
            $"{name}: dark text with a light shadow ({preset.ListViewFontShadowColor}) looks blurry");
    }

    [Fact]
    public void Every_preset_has_a_distinct_look()
    {
        var looks = ThemePresets.All.Select(p => (p.TitleBarColor, p.ListViewBackgroundColor, p.ListViewFontColor)).ToList();
        Assert.Equal(looks.Count, looks.Distinct().Count());
    }
}
