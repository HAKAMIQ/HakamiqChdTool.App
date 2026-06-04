using System;
using System.Globalization;
using System.Windows;

namespace HakamiqChdTool.App.Localization;

public static class BidiText
{
    private const char LeftToRightIsolate = '\u2066';
    private const char RightToLeftIsolate = '\u2067';
    private const char FirstStrongIsolate = '\u2068';
    private const char PopDirectionalIsolate = '\u2069';

    public static string Ui(string? value)
    {
        string text = Clean(value);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return IsCurrentUiRightToLeft()
            ? Wrap(text, RightToLeftIsolate)
            : Wrap(text, LeftToRightIsolate);
    }

    public static string Mixed(string? value)
    {
        string text = Clean(value);
        return text.Length == 0 ? string.Empty : Wrap(text, FirstStrongIsolate);
    }

    public static string Technical(string? value)
    {
        string text = Clean(value);
        return text.Length == 0 ? string.Empty : Wrap(text, LeftToRightIsolate);
    }

    public static string FileName(string? value)
    {
        return Technical(value);
    }

    public static string Path(string? value)
    {
        return Technical(value);
    }

    public static string Hash(string? value)
    {
        return Technical(value);
    }

    public static string Url(string? value)
    {
        return Technical(value);
    }

    public static string ForKind(string? value, string? kind)
    {
        return NormalizeKind(kind) switch
        {
            "technical" => Technical(value),
            "filename" => FileName(value),
            "path" => Path(value),
            "hash" => Hash(value),
            "url" => Url(value),
            "ui" => Ui(value),
            _ => Mixed(value)
        };
    }

    private static string NormalizeKind(string? kind)
    {
        return string.IsNullOrWhiteSpace(kind)
            ? "mixed"
            : kind.Trim().ToLowerInvariant();
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace(LeftToRightIsolate.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(RightToLeftIsolate.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(FirstStrongIsolate.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(PopDirectionalIsolate.ToString(), string.Empty, StringComparison.Ordinal);
    }

    private static string Wrap(string value, char isolate)
    {
        return string.Concat(isolate, value, PopDirectionalIsolate);
    }

    private static bool IsCurrentUiRightToLeft()
    {
        if (Application.Current?.TryFindResource("App.FlowDirection") is FlowDirection flowDirection)
        {
            return flowDirection == FlowDirection.RightToLeft;
        }

        return CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
    }
}