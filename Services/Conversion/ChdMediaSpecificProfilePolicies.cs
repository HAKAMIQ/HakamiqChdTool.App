using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using static HakamiqChdTool.App.Services.ChdConversionMessages;

namespace HakamiqChdTool.App.Services;

internal enum ChdDiscHunkIntent
{
    Auto = 0,
    CdDescriptor = 1,
    Gdi = 2,
    IsoCd = 3,
    GenericDvd = 4,
    PspPpssppDvd = 5,
    Ps2Dvd = 6,
    ExistingCommand = 7
}

internal sealed record ChdDiscProfileSettings(
    string Compression,
    int HunkSize,
    string CompressionPolicyName,
    string HunkPolicyName)
{
    public static ChdDiscProfileSettings Empty { get; } = new(
        string.Empty,
        0,
        string.Empty,
        string.Empty);
}

internal interface IChdMediaCompressionPolicy
{
    string PolicyName { get; }

    string Resolve(string? requestedCompression, ChdProfileUserGoal userGoal);

    ChdCompressionResolution ResolveWithTruth(string? requestedCompression);
}

internal interface IChdMediaHunkPolicy
{
    string PolicyName { get; }

    int Resolve(int requestedHunkSizeBytes, ChdProfileUserGoal userGoal, ChdDiscHunkIntent intent);
}

internal sealed class ChdCdCompressionPolicy : IChdMediaCompressionPolicy
{
    public const string PolicyMarker = "CHD CD compression policy";

    public string PolicyName => PolicyMarker;

    public string Resolve(string? requestedCompression, ChdProfileUserGoal userGoal) =>
        NormalizeRequestedCompression(requestedCompression);

    public ChdCompressionResolution ResolveWithTruth(string? requestedCompression) =>
        ChdCompressionPresetResolver.ResolveWithTruth(requestedCompression, isCd: true);

    internal static string NormalizeRequestedCompression(string? requestedCompression) =>
        string.IsNullOrWhiteSpace(requestedCompression)
            ? string.Empty
            : requestedCompression.Trim();
}

internal sealed class ChdDvdCompressionPolicy : IChdMediaCompressionPolicy
{
    public const string PolicyMarker = "CHD DVD compression policy";

    public string PolicyName => PolicyMarker;

    public string Resolve(string? requestedCompression, ChdProfileUserGoal userGoal) =>
        ChdCdCompressionPolicy.NormalizeRequestedCompression(requestedCompression);

    public ChdCompressionResolution ResolveWithTruth(string? requestedCompression) =>
        ChdCompressionPresetResolver.ResolveWithTruth(requestedCompression, isCd: false);
}

internal static class ChdCompressionPresetResolver
{
    internal static ChdCompressionResolution ResolveWithTruth(string? requestedCompression, bool isCd)
    {
        string value = string.IsNullOrWhiteSpace(requestedCompression)
            ? "preset:default"
            : requestedCompression.Trim();

        if (!value.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
        {
            string explicitCompression = ValidateExplicitCompressionCodecs(value, isCd);
            bool explicitMatchesCdDefault = isCd
                && string.Equals(explicitCompression, ChdCompressionResolution.MameCreateCdDefaultCompression, StringComparison.OrdinalIgnoreCase);

            return new ChdCompressionResolution(
                "explicit",
                explicitCompression,
                string.IsNullOrWhiteSpace(explicitCompression) ? "default" : explicitCompression,
                explicitMatchesCdDefault,
                explicitMatchesCdDefault ? "LocConversionCompressionTruth_CdMaxSameAsDefault" : null);
        }

        string preset = value[7..].ToLowerInvariant();
        string resolved = (preset, isCd) switch
        {
            ("default", _) => string.Empty,
            ("fast", true) => "cdzs,cdfl",
            ("balanced", true) => "cdzs,cdzl,cdfl",
            ("max", true) => ChdCompressionResolution.MameCreateCdDefaultCompression,
            ("fast", false) => "zstd,flac",
            ("balanced", false) => "zstd,zlib,huff,flac",
            ("max", false) => "lzma,zlib,huff,flac",
            _ => string.Empty
        };

        bool defaultPreset = string.Equals(preset, "default", StringComparison.OrdinalIgnoreCase);
        bool sameAsMameDefault = defaultPreset
            || isCd && string.Equals(resolved, ChdCompressionResolution.MameCreateCdDefaultCompression, StringComparison.OrdinalIgnoreCase);

        string effectiveCompression = string.IsNullOrWhiteSpace(resolved)
            ? (isCd ? ChdCompressionResolution.MameCreateCdDefaultCompression : "default")
            : resolved;

        string? truthNoteKey = isCd && sameAsMameDefault
            ? "LocConversionCompressionTruth_CdMaxSameAsDefault"
            : null;

        return new ChdCompressionResolution(
            string.IsNullOrWhiteSpace(preset) ? "default" : preset,
            resolved,
            effectiveCompression,
            sameAsMameDefault,
            truthNoteKey);
    }

    private static string ValidateExplicitCompressionCodecs(string value, bool isCd)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        HashSet<string> allowed = isCd
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cdlz", "cdzl", "cdzs", "cdfl" }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lzma", "zlib", "zstd", "huff", "flac" };

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 4)
        {
            throw new InvalidOperationException(InvalidCompressionSettingMessageKey);
        }

        foreach (string codec in parts)
        {
            if (!allowed.Contains(codec))
            {
                throw new InvalidOperationException(InvalidCompressionSettingMessageKey);
            }
        }

        return string.Join(',', parts);
    }
}

internal sealed class ChdCdHunkPolicy : IChdMediaHunkPolicy
{
    public const string PolicyMarker = "CHD CD hunk policy";

    public string PolicyName => PolicyMarker;

    public int Resolve(int requestedHunkSizeBytes, ChdProfileUserGoal userGoal, ChdDiscHunkIntent intent)
    {
        if (intent is ChdDiscHunkIntent.GenericDvd or ChdDiscHunkIntent.PspPpssppDvd or ChdDiscHunkIntent.Ps2Dvd)
        {
            return 0;
        }

        if (requestedHunkSizeBytes == 2048 && userGoal != ChdProfileUserGoal.Advanced)
        {
            return 0;
        }

        return requestedHunkSizeBytes;
    }
}

internal sealed class ChdDvdHunkPolicy : IChdMediaHunkPolicy
{
    public const string PolicyMarker = "CHD DVD hunk policy";
    public const int PspPpssppCompatibilityHunkSizeBytes = 2048;

    public string PolicyName => PolicyMarker;

    public int Resolve(int requestedHunkSizeBytes, ChdProfileUserGoal userGoal, ChdDiscHunkIntent intent)
    {
        if (intent == ChdDiscHunkIntent.PspPpssppDvd)
        {
            return PspPpssppCompatibilityHunkSizeBytes;
        }

        if (intent == ChdDiscHunkIntent.Ps2Dvd)
        {
            return requestedHunkSizeBytes > 0 ? requestedHunkSizeBytes : 0;
        }

        if (intent is ChdDiscHunkIntent.CdDescriptor or ChdDiscHunkIntent.Gdi or ChdDiscHunkIntent.IsoCd)
        {
            return 0;
        }

        return requestedHunkSizeBytes;
    }
}
