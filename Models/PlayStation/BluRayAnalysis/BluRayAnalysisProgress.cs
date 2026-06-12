using System;

namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public sealed class BluRayAnalysisProgress(
    BluRayAnalysisStage stage,
    double percent,
    string? technicalDetail = null)
{
    public BluRayAnalysisStage Stage { get; } = stage;

    public double Percent { get; } = Math.Clamp(percent, 0, 100);

    public string TechnicalDetail { get; } = technicalDetail ?? string.Empty;
}