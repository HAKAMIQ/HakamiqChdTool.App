using System.Collections.Generic;
using HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

namespace HakamiqChdTool.App.Models.PlayStation;

public sealed record PS3ContentIntakeResult(
    PS3InputFormat InputFormat,
    PS3ContentKind ContentKind,
    string SourcePath,
    string? TitleId,
    string? TitleName,
    string? DiscId,
    bool HasPs3GameFolder,
    bool HasParamSfo,
    bool HasEbootBin,
    bool HasPs3DiscSfb,
    bool IsProbablyEncrypted,
    bool CanConvertToChd,
    string RecommendedPipeline,
    IReadOnlyList<string> Warnings)
{
    public BluRayIsoAnalysisResult? BluRayAnalysis { get; init; }
}