using HakamiqChdTool.App.Models;
using System;
using System.Linq;

namespace HakamiqChdTool.App.Core.Chd;

/// <summary>
/// Adjusts the conversion settings for the emulator the user plays on. <see cref="Auto"/> changes nothing.
/// <see cref="Dvd2048"/> makes DVD CHDs with 2048-byte hunks, which PPSSPP needs and is advised for PCSX2
/// (CD and hard-disk commands ignore a DVD hunk size). <see cref="NoZstd"/> keeps zstd out of the codec list,
/// because some emulators and older RetroArch cores (for example Beetle PSX) cannot read zstd CHDs.
/// </summary>
public static class TargetEmulatorPolicy
{
    public const string Auto = "auto";
    public const string Dvd2048 = "dvd2048";
    public const string NoZstd = "nozstd";
    public const int DvdHunkBytes = 2048;

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Dvd2048 => Dvd2048,
        NoZstd => NoZstd,
        _ => Auto
    };

    public static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        switch (Normalize(settings.TargetEmulator))
        {
            case Dvd2048:
                settings.HunkSizeBytes = DvdHunkBytes;
                break;
            case NoZstd:
                settings.CompressionCodecs = WithoutZstd(settings.CompressionCodecs);
                break;
        }
    }

    /// <summary>Fast and Balanced use zstd and become Maximum; zstd is removed from an explicit codec list.</summary>
    public static string WithoutZstd(string? compression)
    {
        string value = string.IsNullOrWhiteSpace(compression) ? "preset:default" : compression.Trim();
        if (value.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant() is "preset:fast" or "preset:balanced" ? "preset:max" : value;
        }

        string[] kept = [.. value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(codec => !codec.Equals("zstd", StringComparison.OrdinalIgnoreCase) && !codec.Equals("cdzs", StringComparison.OrdinalIgnoreCase))];
        return kept.Length == 0 ? "preset:default" : string.Join(',', kept);
    }
}
