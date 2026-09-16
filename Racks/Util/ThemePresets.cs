using System.Collections.Generic;

namespace Racks.Util
{
    // A small palette of one-click looks. Each preset is the minimum set of color
    // fields that meaningfully change a rack's appearance. ARGB hex strings — the
    // first two hex digits are alpha; many of these are translucent on purpose so
    // wallpaper still bleeds through.
    public static class ThemePresets
    {
        public sealed class Preset
        {
            public string Name { get; init; } = "";
            public string TitleBarColor { get; init; } = "";
            public string ListViewBackgroundColor { get; init; } = "";
            public string TitleTextColor { get; init; } = "";
            public string ListViewFontColor { get; init; } = "";
            public string ListViewFontShadowColor { get; init; } = "";
            public string BorderColor { get; init; } = "";
            public bool BorderEnabled { get; init; }
        }

        public static readonly Preset CossDark = new()
        {
            Name = "Coss · Dark", TitleBarColor = "#FF202020", ListViewBackgroundColor = "#F51B1B1B",
            TitleTextColor = "#F5F5F5", ListViewFontColor = "#F5F5F5", ListViewFontShadowColor = "#00000000", BorderColor = "#FF414141", BorderEnabled = true
        };
        public static readonly Preset CossLight = new()
        {
            Name = "Coss · Light", TitleBarColor = "#FFF4F4F5", ListViewBackgroundColor = "#F5FFFFFF",
            TitleTextColor = "#18181B", ListViewFontColor = "#27272A", ListViewFontShadowColor = "#00000000", BorderColor = "#FFCECED3", BorderEnabled = true
        };
        public static readonly IReadOnlyList<Preset> All = new[]
        {
            CossDark, CossLight,
            new Preset
            {
                Name = "Dark (default)",
                TitleBarColor = "#0C000000",
                ListViewBackgroundColor = "#0C000000",
                TitleTextColor = "#FFFFFF",
                ListViewFontColor = "#FFFFFF",
                ListViewFontShadowColor = "#000000",
                BorderColor = "#FFFFFF",
                BorderEnabled = false,
            },
            new Preset
            {
                Name = "Light",
                TitleBarColor = "#CCFFFFFF",
                ListViewBackgroundColor = "#B3F0F0F0",
                TitleTextColor = "#202020",
                ListViewFontColor = "#202020",
                ListViewFontShadowColor = "#88FFFFFF",
                BorderColor = "#C0C0C0",
                BorderEnabled = true,
            },
            new Preset
            {
                Name = "Glass",
                TitleBarColor = "#22FFFFFF",
                ListViewBackgroundColor = "#11FFFFFF",
                TitleTextColor = "#FFFFFF",
                ListViewFontColor = "#FFFFFF",
                ListViewFontShadowColor = "#80000000",
                BorderColor = "#55FFFFFF",
                BorderEnabled = true,
            },
            new Preset
            {
                Name = "Neon",
                TitleBarColor = "#AA000020",
                ListViewBackgroundColor = "#88000020",
                TitleTextColor = "#00FFD0",
                ListViewFontColor = "#7DF9FF",
                ListViewFontShadowColor = "#80000040",
                BorderColor = "#00FFD0",
                BorderEnabled = true,
            },
            new Preset
            {
                Name = "Solarized Dark",
                TitleBarColor = "#CC002B36",
                ListViewBackgroundColor = "#99073642",
                TitleTextColor = "#EEE8D5",
                ListViewFontColor = "#FDF6E3",
                ListViewFontShadowColor = "#80000000",
                BorderColor = "#586E75",
                BorderEnabled = true,
            },
            new Preset
            {
                Name = "Solarized Light",
                TitleBarColor = "#DDFDF6E3",
                ListViewBackgroundColor = "#AAEEE8D5",
                TitleTextColor = "#073642",
                ListViewFontColor = "#073642",
                ListViewFontShadowColor = "#80FDF6E3",
                BorderColor = "#93A1A1",
                BorderEnabled = true,
            },
        };

        public static void Apply(Instance instance, Preset preset)
        {
            instance.TitleBarColor = preset.TitleBarColor;
            instance.ListViewBackgroundColor = preset.ListViewBackgroundColor;
            instance.TitleTextColor = preset.TitleTextColor;
            instance.ListViewFontColor = preset.ListViewFontColor;
            instance.ListViewFontShadowColor = preset.ListViewFontShadowColor;
            instance.BorderColor = preset.BorderColor;
            instance.BorderEnabled = preset.BorderEnabled;
            if (ReferenceEquals(preset, CossDark) || ReferenceEquals(preset, CossLight))
            {
                instance.ActiveBackgroundColor = preset.ListViewBackgroundColor;
                instance.ActiveBorderColor = preset.BorderColor;
                instance.ActiveTitleTextColor = preset.TitleTextColor;
                instance.TitleFontFamily = "Segoe UI"; instance.ItemFontFamily = "Segoe UI";
                instance.DropShadowEnabled = false; instance.GradientBackgroundEnabled = false;
                instance.TitleFontSize = 13; instance.Opacity = 245; instance.IdleOpacity = 1;
            }
        }
    }
}
