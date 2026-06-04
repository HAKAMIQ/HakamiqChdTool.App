using HakamiqChdTool.App.Localization;
using System;
using System.Globalization;
using System.Windows.Data;

namespace HakamiqChdTool.App.Converters;

public sealed class ActionToArabicConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return ArabicUi.GetActionArabicLabel(value?.ToString());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}