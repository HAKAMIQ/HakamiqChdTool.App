using HakamiqChdTool.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Contracts;

public interface IChdConversionService
{
    string BuildCommand(
        string inputPath,
        ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto);

    Task<ChdConversionResult> ConvertToChdAsync(
        string chdmanPath,
        string inputPath,
        string outputPath,
        int maxProcessorCount = 0,
        bool enableAutoResourceLimiter = true,
        int reservedLogicalCores = 2,
        string? compressionCodecs = null,
        int hunkSizeBytes = 0,
        IProgress<int>? progress = null,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default,
        ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto,
        IProgress<PerformanceSample>? performanceProgress = null,
        bool computeInputSha1 = false,
        long? expectedOutputBytes = null,
        bool allowOverwriteOutput = false,
        bool enableDiskSpaceGuard = true,
        ConversionPerformanceMode performanceMode = ConversionPerformanceMode.Safe,
        ChdmanProcessPriorityMode priorityMode = ChdmanProcessPriorityMode.Quiet,
        bool extractionMetadataDecisionConfirmed = false,
        string? extractCdCueOutputPath = null,
        string? extractCdBinOutputPath = null,
        bool verifyExtractCdCueBinContract = true,
        string? platformProfileId = null);
}
