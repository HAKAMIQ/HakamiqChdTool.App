using System;
using System.Globalization;

namespace HakamiqChdTool.App.Services.WpfShell;

public interface IResourceTextProvider
{
    string GetString(string resourceKey);

    string GetString(string resourceKey, string fallback);

    string Format(string resourceKey, CultureInfo culture, params object[] arguments);

    bool TryGetDouble(string resourceKey, out double value);
}
