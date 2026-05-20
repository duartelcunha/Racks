using System.Globalization;
using System.Windows.Data;

namespace DeskFrame
{
    public class IconSizeToHeightWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int iconSize)
            {
                return iconSize;
            }
            return 32;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}