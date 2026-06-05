using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace HakamiqChdTool.App.Services;

public sealed class ThemeService : IThemeManager
{
    public const string LightThemeName = "Light";
    public const string DarkThemeName = "Dark";
    public const string HakamiqThemeName = "Hakamiq";

    public static ThemeService Instance { get; } = new();

    private string _currentThemeName = LightThemeName;

    public event EventHandler? ThemeChanged;

    public string CurrentThemeName => _currentThemeName;

    public bool IsDarkTheme =>
        string.Equals(_currentThemeName, DarkThemeName, StringComparison.OrdinalIgnoreCase);

    public bool IsHakamiqTheme =>
        string.Equals(_currentThemeName, HakamiqThemeName, StringComparison.OrdinalIgnoreCase);

    public bool IsDarkChrome => IsDarkTheme || IsHakamiqTheme;

    private ThemeService()
    {
    }

    public void Initialize(string? themeName)
    {
        RunOnApplicationDispatcher(() =>
        {
            string effective = NormalizeThemeName(themeName) ?? LightThemeName;

            _currentThemeName = effective;
            ApplyThemeDictionary(effective);
        });
    }

    public void ToggleTheme()
    {
        RunOnApplicationDispatcher(() =>
        {
            string next = string.Equals(_currentThemeName, LightThemeName, StringComparison.OrdinalIgnoreCase)
                ? DarkThemeName
                : string.Equals(_currentThemeName, DarkThemeName, StringComparison.OrdinalIgnoreCase)
                    ? HakamiqThemeName
                    : LightThemeName;

            SetThemeOnCurrentDispatcher(next);
        });
    }

    public void SetTheme(string themeName)
    {
        RunOnApplicationDispatcher(() => SetThemeOnCurrentDispatcher(themeName));
    }

    private void SetThemeOnCurrentDispatcher(string themeName)
    {
        string? effective = NormalizeThemeName(themeName);
        if (effective is null)
        {
            return;
        }

        if (string.Equals(_currentThemeName, effective, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentThemeName = effective;
        ApplyThemeDictionary(effective);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void RunOnApplicationDispatcher(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        WpfApplication? app = WpfApplication.Current;
        Dispatcher? dispatcher = app?.Dispatcher;

        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.Invoke(action);
        }
        catch (TaskCanceledException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
        }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
        }
    }

    private static void ApplyThemeDictionary(string themeName)
    {
        WpfApplication? app = WpfApplication.Current;
        if (app is null)
        {
            return;
        }

        IList<ResourceDictionary> merged = app.Resources.MergedDictionaries;
        var removeIndices = new List<int>();

        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source is Uri source && IsSwappableThemePaletteDictionary(source))
            {
                removeIndices.Add(i);
            }
        }

        int insertAt;

        if (removeIndices.Count > 0)
        {
            removeIndices.Sort();
            insertAt = removeIndices[0];

            for (int i = removeIndices.Count - 1; i >= 0; i--)
            {
                merged.RemoveAt(removeIndices[i]);
            }
        }
        else
        {
            insertAt = FindInsertIndexBeforeFluentControls(merged);
        }

        var next = new ResourceDictionary
        {
            Source = GetThemePackUri(themeName)
        };

        if (insertAt >= 0 && insertAt <= merged.Count)
        {
            merged.Insert(insertAt, next);
            return;
        }

        merged.Add(next);
    }

    private static string? NormalizeThemeName(string? themeName)
    {
        if (string.Equals(themeName, LightThemeName, StringComparison.OrdinalIgnoreCase))
        {
            return LightThemeName;
        }

        if (string.Equals(themeName, DarkThemeName, StringComparison.OrdinalIgnoreCase))
        {
            return DarkThemeName;
        }

        if (string.Equals(themeName, HakamiqThemeName, StringComparison.OrdinalIgnoreCase))
        {
            return HakamiqThemeName;
        }

        return null;
    }

    private static bool IsSwappableThemePaletteDictionary(Uri source)
    {
        string path = source.AbsolutePath.Replace('\\', '/');
        string raw = source.OriginalString.Replace('\\', '/');
        string probe = path.Contains("/themes/", StringComparison.OrdinalIgnoreCase)
            ? path
            : raw;

        if (!probe.Contains("/themes/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return probe.EndsWith("/light.xaml", StringComparison.OrdinalIgnoreCase)
            || probe.EndsWith("/dark.xaml", StringComparison.OrdinalIgnoreCase)
            || probe.EndsWith("/hakamiq.xaml", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri GetThemePackUri(string themeName)
    {
        string file = string.Equals(themeName, DarkThemeName, StringComparison.OrdinalIgnoreCase)
            ? "Dark.xaml"
            : string.Equals(themeName, HakamiqThemeName, StringComparison.OrdinalIgnoreCase)
                ? "Hakamiq.xaml"
                : "Light.xaml";

        return new Uri($"pack://application:,,,/Resources/Themes/{file}", UriKind.Absolute);
    }

    private static int FindInsertIndexBeforeFluentControls(IList<ResourceDictionary> merged)
    {
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source is not Uri source)
            {
                continue;
            }

            string value = source.OriginalString.Replace('\\', '/');

            if (value.Contains("/themes/fluent/controls.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}