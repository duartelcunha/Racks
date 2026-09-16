using System.Windows;
using System.Windows.Media;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Racks.Util;

public static class CossTheme
{
    public static bool IsLight { get; private set; }
    public static void Apply(bool light)
    {
        IsLight = light;
        var colors = new Dictionary<string, string>
        {
            ["Background"] = light ? "#FAFAFA" : "#151515",
            ["Surface"] = light ? "#FFFFFF" : "#1D1D1D",
            ["Inset"] = light ? "#F4F4F5" : "#181818",
            ["Hover"] = light ? "#F0F0F1" : "#303030",
            ["Pressed"] = light ? "#E4E4E7" : "#3A3A3A",
            ["Border"] = light ? "#DEDEE2" : "#3A3A3A",
            ["Text"] = light ? "#18181B" : "#F5F5F5",
            ["Muted"] = light ? "#65656D" : "#ACACB3",
            ["Focus"] = light ? "#52525B" : "#D4D4D8",
            ["Primary"] = light ? "#202023" : "#F5F5F5",
            ["PrimaryText"] = light ? "#FFFFFF" : "#18181B",
            ["Selection"] = light ? "#E8EEEE" : "#293737"
        };
        foreach (var (name, value) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze();
            System.Windows.Application.Current.Resources["Coss" + name] = brush;
        }
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(light ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }
    public static void Attach(FrameworkElement element)
    {
        element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Racks;component/Resources/CossControls.xaml", UriKind.Relative) });
    }
}
