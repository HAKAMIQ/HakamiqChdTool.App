using System;

namespace HakamiqChdTool.App.Services;


public interface IThemeManager
{
    event EventHandler? ThemeChanged;

    string CurrentThemeName { get; }

    bool IsDarkTheme { get; }

    bool IsHakamiqTheme { get; }

    bool IsDarkChrome { get; }

    void Initialize(string? themeName);

    void SetTheme(string themeName);

    void ToggleTheme();
}