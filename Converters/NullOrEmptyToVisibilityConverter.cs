using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HakamiqChdTool.App.Converters;

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isEmpty = value switch
        {
            null => true,
            string text => string.IsNullOrWhiteSpace(text),
            ICollection collection => collection.Count == 0,
            IEnumerable enumerable => IsEnumerableEmpty(enumerable),
            _ => false
        };

        bool visible = Invert ? isEmpty : !isEmpty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static bool IsEnumerableEmpty(IEnumerable enumerable)
    {
        IEnumerator enumerator = enumerable.GetEnumerator();

        try
        {
            return !enumerator.MoveNext();
        }
        finally
        {
            if (enumerator is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}