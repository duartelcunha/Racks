using System.Globalization;
using System.Windows.Data;
using System.Windows;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Diagnostics;

namespace DeskFrame
{
    public class TitleTextAlignmentToMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is System.Windows.HorizontalAlignment alignment &&
                values[1] is Visibility vis1 &&
                values[2] is Visibility vis2)
            {
                int visibleCount = 0;
                int margin = 0;
                switch (alignment)
                {
                    case HorizontalAlignment.Left:
                        return new Thickness(10,0,0,0);
                    case HorizontalAlignment.Right:
                        if (vis1 == Visibility.Visible) visibleCount++;
                        if (vis2 == Visibility.Visible) visibleCount++;
                        margin = visibleCount switch
                        {
                            1 => 60,
                            2 => 90,
                            _ => 30
                        };
                        return new Thickness(0, 0, margin, 0);
                    case HorizontalAlignment.Center:
                        return new Thickness(0, 0, 0, 0);
                    default:
                        return new Thickness(0);
                }
            }

            return new Thickness(0);
        }


        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
