using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiDataGateway.LocalLogViewer.ViewModels
{
    public sealed class ZeroCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var count = value is int ? (int)value : 0;
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
