using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public interface ISafeRecompressPipeline
{
    Task<SafeChdRecompressResult> RecompressAsync(
        SafeChdRecompressRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SafeRecompressPipeline : ISafeRecompressPipeline
{
    private const string DirectChdRecompressBlockedMessageKey = "LocChdPolicy_DirectChdRecompressBlocked";

    private readonly ChdInfoService _chdInfo;
    private readonly ChdConversionService _conversion;
    private readonly IMetadataAwareChdExtractionPolicy _metadataExtractionPolicy;
    private readonly IRestoreTargetPolicy _restoreTargetPolicy;

    public SafeRecompressPipeline()
        : this(
            new ChdInfoService(),
            new ChdConversionService(),
            new MetadataAwareChdExtractionPolicy(),
            new RestoreTargetPolicy())
    {
    }

    public SafeRecompressPipeline(
        ChdInfoService chdInfo,
        ChdConversionService conversion,
        IMetadataAwareChdExtractionPolicy metadataExtractionPolicy,
        IRestoreTargetPolicy restoreTargetPolicy)
    {
        _chdInfo = chdInfo ?? throw new ArgumentNullException(nameof(chdInfo));
        _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
        _metadataExtractionPolicy = metadataExtractionPolicy ?? throw new ArgumentNullException(nameof(metadataExtractionPolicy));
        _restoreTargetPolicy = restoreTargetPolicy ?? throw new ArgumentNullException(nameof(restoreTargetPolicy));
    }

    public async Task<SafeChdRecompressResult> RecompressAsync(
        SafeChdRecompressRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ChdmanPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputChdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputChdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        if (!string.Equals(Path.GetExtension(request.InputChdPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("LocExtraction_InvalidChdPath");
        }

        if (!string.Equals(Path.GetExtension(request.OutputChdPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("LocConversion_InvalidOutputPath");
        }

        string sourceChd = Path.GetFullPath(request.InputChdPath);
        string targetChd = Path.GetFullPath(request.OutputChdPath);
        if (string.Equals(sourceChd, targetChd, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(DirectChdRecompressBlockedMessageKey);
        }

        string sessionRoot = BuildSessionRoot(request.WorkingDirectory, sourceChd);
        Directory.CreateDirectory(sessionRoot);

        try
        {
            ChdInfoResult info = await _chdInfo
                .ReadInfoAsync(request.ChdmanPath, sourceChd, request.OnProcessStarted, cancellationToken)
                .ConfigureAwait(false);

            if (!info.IsSuccess)
            {
                ChdConversionResult failedInfo = new()
                {
                    IsSuccess = false,
                    WasCancelled = info.WasCancelled,
                    ExitCode = info.ExitCode,
                    InputPath = sourceChd,
                    OutputPath = targetChd,
                    Message = info.Message,
                    LogPath = info.LogPath
                };

                return SafeChdRecompressResult.ExtractionFailed(failedInfo);
            }

            PlatformDetectionResult platformDetection = string.IsNullOrWhiteSpace(request.DetectedPlatform)
                ? PlatformDetectionService.Detect(string.IsNullOrWhiteSpace(request.OriginalPath) ? sourceChd : request.OriginalPath)
                : PlatformDetectionResult.Create(request.DetectedPlatform, "Context", 85, "SafeRecompressPipeline context");

            MetadataAwareChdExtractionDecision extractionDecision = _metadataExtractionPolicy.Resolve(
                new MetadataAwareChdExtractionRequest(
                    info.MediaType,
                    sourceChd,
                    string.IsNullOrWhiteSpace(request.OriginalPath) ? sourceChd : request.OriginalPath,
                    platformDetection,
                    info.LogicalBytes));

            if (!extractionDecision.IsSupported)
            {
                ChdConversionResult unsupported = new()
                {
                    IsSuccess = false,
                    WasCancelled = false,
                    ExitCode = 1,
                    InputPath = sourceChd,
                    OutputPath = targetChd,
                    Message = extractionDecision.FailureMessageKey
                };

                return SafeChdRecompressResult.ExtractionFailed(unsupported);
            }

            string extractedOriginalLikePath = Path.Combine(
                sessionRoot,
                Path.GetFileNameWithoutExtension(sourceChd) + extractionDecision.OutputExtension);

            RestoreTargetDecision restoreTarget = _restoreTargetPolicy.Resolve(
                new RestoreTargetRequest(
                    extractionDecision,
                    extractedOriginalLikePath,
                    extractedOriginalLikePath));

            Log.Information(
                "Safe recompress pipeline extracting original-like format before rebuild. InputChd={InputChd}; ExtractCommand={ExtractCommand}; Extracted={Extracted}; OutputChd={OutputChd}; ReasonCode={ReasonCode}",
                sourceChd,
                extractionDecision.ExtractionKind,
                restoreTarget.ChdmanOutputPath,
                targetChd,
                extractionDecision.ReasonCode);

            ChdConversionResult extractionResult = await _conversion.ConvertToChdAsync(
                request.ChdmanPath,
                sourceChd,
                restoreTarget.ChdmanOutputPath,
                request.MaxProcessorCount,
                request.EnableAutoResourceLimiter,
                request.ReservedLogicalCores,
                request.CompressionCodecs,
                request.HunkSizeBytes,
                request.Progress,
                request.OnProcessStarted,
                cancellationToken,
                extractionDecision.ExtractionKind,
                IsoCreateCommandOverride.Auto,
                request.PerformanceProgress,
                computeInputSha1: false,
                expectedOutputBytes: info.LogicalBytes,
                allowOverwriteOutput: true,
                enableDiskSpaceGuard: request.EnableDiskSpaceGuard,
                performanceMode: request.PerformanceMode,
                priorityMode: request.PriorityMode,
                extractionMetadataDecisionConfirmed: true,
                extractCdCueOutputPath: restoreTarget.ExtractCdCueOutputPath,
                extractCdBinOutputPath: restoreTarget.ExtractCdBinOutputPath,
                verifyExtractCdCueBinContract: restoreTarget.VerifyExtractCdCueBinContract).ConfigureAwait(false);

            if (!extractionResult.IsSuccess || extractionResult.WasCancelled)
            {
                return SafeChdRecompressResult.ExtractionFailed(extractionResult);
            }

            string rebuildInput = restoreTarget.ChdmanOutputPath;
            Log.Information(
                "Safe recompress pipeline rebuilding CHD from extracted original-like format. Extracted={Extracted}; OutputChd={OutputChd}; Policy=PlatformAwareChdProfilePolicy",
                rebuildInput,
                targetChd);

            ChdConversionResult rebuildResult = await _conversion.ConvertToChdAsync(
                request.ChdmanPath,
                rebuildInput,
                targetChd,
                request.MaxProcessorCount,
                request.EnableAutoResourceLimiter,
                request.ReservedLogicalCores,
                request.CompressionCodecs,
                request.HunkSizeBytes,
                request.Progress,
                request.OnProcessStarted,
                cancellationToken,
                ChdmanExtractionKind.None,
                IsoCreateCommandOverride.Auto,
                request.PerformanceProgress,
                computeInputSha1: true,
                expectedOutputBytes: null,
                allowOverwriteOutput: request.AllowOverwriteOutput,
                enableDiskSpaceGuard: request.EnableDiskSpaceGuard,
                performanceMode: request.PerformanceMode,
                priorityMode: request.PriorityMode).ConfigureAwait(false);

            return rebuildResult.IsSuccess && !rebuildResult.WasCancelled
                ? SafeChdRecompressResult.Success(rebuildInput, extractionResult, rebuildResult)
                : SafeChdRecompressResult.RebuildFailed(rebuildInput, extractionResult, rebuildResult);
        }
        finally
        {
            TryDeleteSessionRoot(sessionRoot);
        }
    }

    private static string BuildSessionRoot(string workingDirectory, string sourceChd)
    {
        string fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        string safeName = Path.GetFileNameWithoutExtension(sourceChd);
        safeName = string.IsNullOrWhiteSpace(safeName) ? "chd" : Sanitize(safeName);
        return Path.Combine(fullWorkingDirectory, "SafeRecompress", safeName + "_" + Guid.NewGuid().ToString("N"));
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim(' ', '.', '_');
    }

    private static void TryDeleteSessionRoot(string sessionRoot)
    {
        try
        {
            if (Directory.Exists(sessionRoot))
            {
                Directory.Delete(sessionRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            Log.Debug(ex, "Could not delete safe recompress temporary workspace. Path={Path}", sessionRoot);
        }
    }
}
