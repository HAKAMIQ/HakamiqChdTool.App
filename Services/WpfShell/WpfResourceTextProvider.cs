using HakamiqChdTool.App.Localization;
using System;
using System.Globalization;
using System.Windows;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed class WpfResourceTextProvider : IResourceTextProvider
{
    public string GetString(string resourceKey)
    {
        return ArabicUi.Get(resourceKey);
    }

    public string GetString(string resourceKey, string fallback)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return fallback;
        }

        object? value = Application.Current?.TryFindResource(resourceKey);
        return value as string ?? fallback;
    }

    public string Format(string resourceKey, CultureInfo culture, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(culture);

        string format = GetString(resourceKey);
        return arguments.Length == 0
            ? format
            : string.Format(culture, format, arguments);
    }

    public bool TryGetDouble(string resourceKey, out double value)
    {
        object? resource = Application.Current?.TryFindResource(resourceKey);

        switch (resource)
        {
            case double number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
