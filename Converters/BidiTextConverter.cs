using HakamiqChdTool.App.Localization;
using System;
using System.Globalization;
using System.Windows.Data;

namespace HakamiqChdTool.App.Converters;

public sealed class BidiTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? kind = parameter?.ToString();
        return BidiText.ForKind(value?.ToString(), kind);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}