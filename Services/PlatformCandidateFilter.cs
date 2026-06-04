using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public static class PlatformCandidateFilter
{
    private const string IncompatibleDescriptorPlatformReasonKey = "LocPlatformDetect_CueMultiTrackAmbiguous";
    private const string NonChdRecommendedPlatformReasonKey = "LocWorkflow_NonChdRecommendedCompression";

    public static PlatformDetectionResult Apply(string inputPath, PlatformDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);

        if (!PlatformDetectionService.IsActionablePlatformName(detection.PlatformName))
        {
            return detection;
        }

        string extension = Path.GetExtension(inputPath ?? string.Empty).ToLowerInvariant();

        return extension switch
        {
            ".cue" or ".toc" or ".nrg" => IsCueBinCompatiblePlatform(detection.PlatformName)
                ? detection
                : BuildUnknownConflict(detection, IncompatibleDescriptorPlatformReasonKey),

            ".gdi" => IsDreamcast(detection.PlatformName)
                ? detection
                : BuildUnknownConflict(detection, IncompatibleDescriptorPlatformReasonKey),

            ".iso" => IsKnownNonChdRecommendedPlatform(detection.PlatformName)
                ? BuildUnknownConflict(detection, NonChdRecommendedPlatformReasonKey)
                : detection,

            _ => detection
        };
    }

    private static PlatformDetectionResult BuildUnknownConflict(
        PlatformDetectionResult detection,
        string reasonKey) =>
        PlatformDetectionResult.Create(
            string.Empty,
            detection.ConfidenceLabel,
            Math.Min(detection.ConfidenceScore, 45),
            string.IsNullOrWhiteSpace(reasonKey)
                ? IncompatibleDescriptorPlatformReasonKey
                : reasonKey);

    private static bool IsCueBinCompatiblePlatform(string? platformName)
    {
        string platform = platformName ?? string.Empty;

        return IsPlayStation1(platform)
            || IsPlayStation2(platform)
            || IsSegaSaturn(platform)
            || IsSegaCd(platform)
            || IsDreamcast(platform)
            || IsNeoGeoCd(platform)
            || IsThreeDo(platform)
            || IsPcEngineCd(platform);
    }

    private static bool IsKnownNonChdRecommendedPlatform(string? platformName)
    {
        string platform = platformName ?? string.Empty;

        return IsPlayStation4OrNewer(platform)
            || IsXboxFamily(platform)
            || IsNintendoGameCube(platform)
            || IsNintendoWii(platform)
            || IsNintendoWiiU(platform)
            || IsNintendo64(platform)
            || IsNintendo64Dd(platform);
    }

    private static bool IsPlayStation1(string platform) =>
        (platform.Contains("PlayStation 1", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("Sony PlayStation", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("PSX", StringComparison.OrdinalIgnoreCase))
        && !platform.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
        && !platform.Contains("PlayStation 4", StringComparison.OrdinalIgnoreCase)
        && !platform.Contains("PlayStation 5", StringComparison.OrdinalIgnoreCase)
        && !platform.Contains("Portable", StringComparison.OrdinalIgnoreCase)
        && !platform.Contains("Vita", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayStation2(string platform) =>
        platform.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("Sony PlayStation 2", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayStation4OrNewer(string platform) =>
        platform.Contains("PlayStation 4", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("PlayStation 5", StringComparison.OrdinalIgnoreCase);

    private static bool IsSegaSaturn(string platform) =>
        platform.Contains("SEGA Saturn", StringComparison.OrdinalIgnoreCase);

    private static bool IsSegaCd(string platform) =>
        platform.Contains("SEGA CD", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("Mega CD", StringComparison.OrdinalIgnoreCase);

    private static bool IsDreamcast(string platform) =>
        platform.Contains("Dreamcast", StringComparison.OrdinalIgnoreCase);

    private static bool IsNeoGeoCd(string platform) =>
        platform.Contains("Neo Geo CD", StringComparison.OrdinalIgnoreCase);

    private static bool IsThreeDo(string platform) =>
        platform.Contains("3DO", StringComparison.OrdinalIgnoreCase);

    private static bool IsPcEngineCd(string platform) =>
        platform.Contains("PC Engine", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("TurboGrafx", StringComparison.OrdinalIgnoreCase);

    private static bool IsXboxFamily(string platform) =>
        platform.Contains("Xbox", StringComparison.OrdinalIgnoreCase);

    private static bool IsNintendoGameCube(string platform) =>
        platform.Contains("Nintendo GameCube", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("GameCube", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("Game Cube", StringComparison.OrdinalIgnoreCase);

    private static bool IsNintendoWii(string platform) =>
        platform.Contains("Nintendo Wii", StringComparison.OrdinalIgnoreCase)
        && !platform.Contains("Wii U", StringComparison.OrdinalIgnoreCase)
        && !platform.Contains("WiiU", StringComparison.OrdinalIgnoreCase);

    private static bool IsNintendoWiiU(string platform) =>
        platform.Contains("Nintendo Wii U", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("WiiU", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("Wii U", StringComparison.OrdinalIgnoreCase);

    private static bool IsNintendo64(string platform) =>
        platform.Contains("Nintendo 64", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("N64", StringComparison.OrdinalIgnoreCase);

    private static bool IsNintendo64Dd(string platform) =>
        platform.Contains("Nintendo 64DD", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("64DD", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("N64DD", StringComparison.OrdinalIgnoreCase);
}