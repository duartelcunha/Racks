using System.Globalization;
using System.Windows.Data;

namespace DeskFrame
{
    public class ShowDisplayNameToItemWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool showDisplayName = values[0] is bool b && b;
            double.TryParse(values[1]?.ToString(), out double iconSize);
            if (iconSize <=  64 && showDisplayName)
            {
                return showDisplayName ? 85 : iconSize + 10;
            }
            return iconSize + 20;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
