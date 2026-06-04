using System;

namespace HakamiqChdTool.App.Services;

public sealed class PlatformDetectionResult
{
    private PlatformDetectionResult(
        string platformName,
        string confidenceLabel,
        int confidenceScore,
        string reason)
    {
        PlatformName = string.IsNullOrWhiteSpace(platformName)
            ? "Unknown Platform"
            : platformName.Trim();

        ConfidenceLabel = confidenceLabel?.Trim() ?? string.Empty;
        ConfidenceScore = Math.Clamp(confidenceScore, 0, 100);
        Reason = reason?.Trim() ?? string.Empty;
    }

    public string PlatformName { get; }

    public string ConfidenceLabel { get; }

    public int ConfidenceScore { get; }

    public string Reason { get; }

    public static PlatformDetectionResult Create(
        string platformName,
        string confidenceLabel,
        int confidenceScore,
        string reason)
    {
        return new PlatformDetectionResult(
            platformName,
            confidenceLabel,
            confidenceScore,
            reason);
    }
}