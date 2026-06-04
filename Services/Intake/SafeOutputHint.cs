using System;
using System.IO;

namespace HakamiqChdTool.App.Services.Intake;

public sealed record SafeOutputHint
{
    public SafeOutputHint(
        string? suggestedFileName,
        string? platform,
        string? region,
        string? discGroupKey = null,
        string? discIndex = null)
    {
        SuggestedFileName = NormalizeSafePathSegment(suggestedFileName);
        Platform = NormalizeSafePathSegment(platform);
        Region = NormalizeSafePathSegment(region);
        DiscGroupKey = NormalizeSafePathSegment(discGroupKey);
        DiscIndex = NormalizeSafePathSegment(discIndex);
    }

    public string? SuggestedFileName { get; }

    public string? Platform { get; }

    public string? Region { get; }

    public string? DiscGroupKey { get; }

    public string? DiscIndex { get; }

    private static string? NormalizeSafePathSegment(string? value)
    {
        string? normalized = NormalizeOptionalValue(value);
        if (normalized is null)
        {
            return null;
        }

        if (!string.Equals(normalized, Path.GetFileName(normalized), StringComparison.Ordinal))
        {
            return null;
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        if (normalized.Contains('\0', StringComparison.Ordinal)
            || normalized.Contains('\\', StringComparison.Ordinal)
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)
            || string.Equals(normalized, ".", StringComparison.Ordinal)
            || normalized.EndsWith(' ')
            || normalized.EndsWith('.')
            || IsReservedWindowsDeviceName(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static bool IsReservedWindowsDeviceName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);

        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || IsReservedNumberedDeviceName(stem, "COM")
            || IsReservedNumberedDeviceName(stem, "LPT");
    }

    private static bool IsReservedNumberedDeviceName(string stem, string prefix)
    {
        return stem.Length == 4
            && stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && stem[3] is >= '1' and <= '9';
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}