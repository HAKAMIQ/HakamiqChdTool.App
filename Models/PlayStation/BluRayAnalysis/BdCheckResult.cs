namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public sealed record BluRayCheckResult(
    BluRayCheckCode Code,
    bool Passed,
    string TechnicalValue = "");