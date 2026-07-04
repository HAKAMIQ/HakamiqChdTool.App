using System.Collections.Generic;

namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public sealed record BluRayIsoAnalysisResult(
    string FilePath,
    long FileSizeBytes,
    BluRayPs3DiscMetadata Metadata,
    BluRayCompressionEstimate CompressionEstimate,
    IReadOnlyList<BluRayCheckResult> Checks)
{
    public bool LooksLikePs3Disc => Metadata.LooksLikePs3Disc;

    public bool LooksLikeBluRayStyleIso => Metadata.LooksLikeBluRayStyleIso;
}