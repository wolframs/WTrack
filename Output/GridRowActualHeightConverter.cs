using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WTrack.Output
{
    public class GridRowActualHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Grid grid && parameter != null && int.TryParse(parameter.ToString(), out int rowIndex))
            {
                grid.UpdateLayout();
                if (rowIndex >= 0 && rowIndex < grid.RowDefinitions.Count)
                {
                    return grid.RowDefinitions[rowIndex].ActualHeight;
                }
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}