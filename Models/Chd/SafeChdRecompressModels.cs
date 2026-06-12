using System;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Models.Chd;

public sealed record SafeChdRecompressRequest(
    string ChdmanPath,
    string InputChdPath,
    string OutputChdPath,
    string WorkingDirectory,
    string OriginalPath,
    string DetectedPlatform,
    int MaxProcessorCount = 0,
    bool EnableAutoResourceLimiter = true,
    int ReservedLogicalCores = 2,
    string? CompressionCodecs = null,
    int HunkSizeBytes = 0,
    IProgress<int>? Progress = null,
    Action<int>? OnProcessStarted = null,
    ConversionPerformanceMode PerformanceMode = ConversionPerformanceMode.Safe,
    ChdmanProcessPriorityMode PriorityMode = ChdmanProcessPriorityMode.Quiet,
    bool AllowOverwriteOutput = false,
    bool EnableDiskSpaceGuard = true,
    IProgress<PerformanceSample>? PerformanceProgress = null);

public sealed record SafeChdRecompressResult(
    bool IsSuccess,
    string MessageKey,
    string ExtractedOriginalLikePath,
    ChdConversionResult ExtractionResult,
    ChdConversionResult RebuildResult)
{
    public static SafeChdRecompressResult ExtractionFailed(ChdConversionResult extractionResult) => new(
        false,
        extractionResult.Message,
        extractionResult.OutputPath,
        extractionResult,
        new ChdConversionResult
        {
            IsSuccess = false,
            Message = extractionResult.Message
        });

    public static SafeChdRecompressResult RebuildFailed(
        string extractedPath,
        ChdConversionResult extractionResult,
        ChdConversionResult rebuildResult) => new(
        false,
        rebuildResult.Message,
        extractedPath,
        extractionResult,
        rebuildResult);

    public static SafeChdRecompressResult Success(
        string extractedPath,
        ChdConversionResult extractionResult,
        ChdConversionResult rebuildResult) => new(
        true,
        rebuildResult.Message,
        extractedPath,
        extractionResult,
        rebuildResult);
}