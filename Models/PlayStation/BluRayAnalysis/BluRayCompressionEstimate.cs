namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public sealed record BluRayCompressionEstimate(
    long SampledBytes,
    long EstimatedCompressedBytes,
    long EstimatedSavedBytes,
    double EstimatedSavedPercent,
    BluRayCompressionRating Rating,
    string ProfileName,
    int SampleCount,
    double ZeroOrPatternSamplePercent,
    double IncompressibleSamplePercent,
    double CompressibleSamplePercent)
{
    public static BluRayCompressionEstimate Empty { get; } = new(
        0,
        0,
        0,
        0,
        BluRayCompressionRating.Unknown,
        string.Empty,
        0,
        0,
        0,
        0);
}