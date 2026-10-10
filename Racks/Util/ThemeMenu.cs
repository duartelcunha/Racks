using System;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace Racks.Util
{
    // The rack right-click "Theme" submenu: one item per ThemePresets entry.
    public static class ThemeMenu
    {
        public static MenuItem Build(Action<ThemePresets.Preset> apply)
        {
            var menu = new MenuItem
            {
                Header = "Theme",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Color20),
            };
            foreach (var preset in ThemePresets.All)
            {
                var item = new MenuItem { Header = preset.Name, Height = 30 };
                item.Click += (_, _) => apply(preset);
                menu.Items.Add(item);
            }
            return menu;
        }
    }
}
