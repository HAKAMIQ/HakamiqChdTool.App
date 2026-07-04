using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HakamiqChdTool.App.ViewModels;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool booleanValue
            ? !booleanValue
            : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool booleanValue
            ? !booleanValue
            : Binding.DoNothing;
    }
}