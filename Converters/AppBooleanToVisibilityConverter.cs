using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HakamiqChdTool.App.Converters;

public sealed class AppBooleanToVisibilityConverter : IValueConverter
{
    public bool CollapseWhenFalse { get; set; } = true;

    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is bool b && b;

        if (Invert)
        {
            visible = !visible;
        }

        if (visible)
        {
            return Visibility.Visible;
        }

        return CollapseWhenFalse ? Visibility.Collapsed : Visibility.Hidden;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is Visibility visibility && visibility == Visibility.Visible;
        return Invert ? !visible : visible;
    }
}