using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow.Extraction;

internal sealed record ExtractionOutputProofResult(
    bool IsVerified,
    string ReasonCode)
{
    public static ExtractionOutputProofResult Verified() => new(true, "Verified");

    public static ExtractionOutputProofResult NotVerified(string reasonCode) =>
        new(false, string.IsNullOrWhiteSpace(reasonCode) ? "NotVerified" : reasonCode);
}

internal sealed class ExtractionOutputProofVerifier(
    ChdInfoService chdInfo,
    ChdConversionService conversion,
    ILogger log)
{
    private readonly ChdInfoService _chdInfo =
        chdInfo ?? throw new ArgumentNullException(nameof(chdInfo));

    private readonly ChdConversionService _conversion =
        conversion ?? throw new ArgumentNullException(nameof(conversion));

    private readonly ILogger _log =
        log ?? throw new ArgumentNullException(nameof(log));

    public async Task<ExtractionOutputProofResult> VerifyAsync(
        string chdmanPath,
        string sourceChdPath,
        ChdInfoResult sourceInfo,
        MetadataAwareChdExtractionDecision extractionDecision,
        ExtractionOutputBundle outputBundle,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chdmanPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceChdPath);
        ArgumentNullException.ThrowIfNull(sourceInfo);
        ArgumentNullException.ThrowIfNull(extractionDecision);
        ArgumentNullException.ThrowIfNull(outputBundle);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TryNormalizeSha1(sourceInfo.DataSha1, out string sourceDataSha1))
        {
            return ExtractionOutputProofResult.NotVerified("SourceDataSha1Unavailable");
        }

        if (extractionDecision.RestoreTargetMode != ChdRestoreTargetMode.Standard)
        {
            return ExtractionOutputProofResult.NotVerified("LegacyRestoreProofUnavailable");
        }

        if (outputBundle.Kind == ExtractionOutputKind.SingleFile)
        {
            return await VerifySingleFileAsync(
                    sourceInfo,
                    sourceDataSha1,
                    outputBundle,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (outputBundle.Kind == ExtractionOutputKind.CueBinBundle
            && extractionDecision.ExtractionKind == ChdmanExtractionKind.ExtractCd)
        {
            return await VerifyCueBinRoundTripAsync(
                    chdmanPath,
                    sourceChdPath,
                    sourceInfo,
                    sourceDataSha1,
                    outputBundle,
                    settings,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return ExtractionOutputProofResult.NotVerified("UnsupportedExtractionProofKind");
    }

    private static async Task<ExtractionOutputProofResult> VerifySingleFileAsync(
        ChdInfoResult sourceInfo,
        string sourceDataSha1,
        ExtractionOutputBundle outputBundle,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(outputBundle.PrimaryPath))
        {
            return ExtractionOutputProofResult.NotVerified("OutputFileMissing");
        }

        FileHashResult outputHash = await FileHashService
            .ComputeAsync(
                outputBundle.PrimaryPath,
                FileHashAlgorithm.SHA1,
                cancellationToken)
            .ConfigureAwait(false);

        long logicalBytes = sourceInfo.LogicalBytes.GetValueOrDefault();
        if (logicalBytes > 0 && outputHash.BytesRead != logicalBytes)
        {
            return ExtractionOutputProofResult.NotVerified("OutputLengthMismatch");
        }

        return DigestsEqual(sourceDataSha1, outputHash.Hex)
            ? ExtractionOutputProofResult.Verified()
            : ExtractionOutputProofResult.NotVerified("OutputDataSha1Mismatch");
    }

    private async Task<ExtractionOutputProofResult> VerifyCueBinRoundTripAsync(
        string chdmanPath,
        string sourceChdPath,
        ChdInfoResult sourceInfo,
        string sourceDataSha1,
        ExtractionOutputBundle outputBundle,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeSha1(sourceInfo.Sha1, out string sourceCombinedSha1))
        {
            return ExtractionOutputProofResult.NotVerified("SourceCombinedSha1Unavailable");
        }

        string proofDirectory = AppPaths.CombineProcessTemp(
            "ep" + Guid.NewGuid().ToString("N")[..10]);
        string proofChdPath = Path.Combine(proofDirectory, "proof.chd");

        try
        {
            Directory.CreateDirectory(proofDirectory);

            if (!AppPaths.IsPathUnderProcessTempRoot(proofDirectory)
                || !AppPaths.IsPathUnderProcessTempRoot(proofChdPath))
            {
                return ExtractionOutputProofResult.NotVerified("UnsafeProofWorkspace");
            }

            ChdConversionResult rebuilt = await _conversion
                .ConvertToChdAsync(
                    chdmanPath,
                    outputBundle.PrimaryPath,
                    proofChdPath,
                    maxProcessorCount: settings.MaxProcessorCount,
                    enableAutoResourceLimiter: settings.EnableAutoResourceLimiter,
                    reservedLogicalCores: settings.ReservedLogicalCores,
                    compressionCodecs: null,
                    hunkSizeBytes: 0,
                    progress: null,
                    onProcessStarted: null,
                    cancellationToken: cancellationToken,
                    extractionKind: ChdmanExtractionKind.None,
                    isoCreateCommandOverride: IsoCreateCommandOverride.Auto,
                    performanceProgress: null,
                    computeInputSha1: false,
                    expectedOutputBytes: null,
                    allowOverwriteOutput: false,
                    enableDiskSpaceGuard: true,
                    performanceMode: ConversionPerformanceMode.Safe,
                    priorityMode: settings.ChdmanPriorityMode,
                    extractionMetadataDecisionConfirmed: false,
                    extractCdCueOutputPath: null,
                    extractCdBinOutputPath: null,
                    verifyExtractCdCueBinContract: true,
                    platformProfileId: null)
                .ConfigureAwait(false);

            if (rebuilt.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ExtractionOutputProofResult.NotVerified("ProofRebuildCancelled");
            }

            if (!rebuilt.IsSuccess || !File.Exists(proofChdPath))
            {
                _log.Warning(
                    "Extracted CUE/BIN proof rebuild did not complete successfully. Source={SourcePath}; Output={OutputPath}; Message={Message}",
                    sourceChdPath,
                    outputBundle.PrimaryPath,
                    rebuilt.Message);

                return ExtractionOutputProofResult.NotVerified("ProofRebuildFailed");
            }

            ChdInfoResult rebuiltInfo = await _chdInfo
                .ReadInfoAsync(
                    chdmanPath,
                    proofChdPath,
                    onProcessStarted: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (rebuiltInfo.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ExtractionOutputProofResult.NotVerified("ProofInfoCancelled");
            }

            if (!rebuiltInfo.IsSuccess
                || !TryNormalizeSha1(rebuiltInfo.DataSha1, out string rebuiltDataSha1)
                || !TryNormalizeSha1(rebuiltInfo.Sha1, out string rebuiltCombinedSha1))
            {
                return ExtractionOutputProofResult.NotVerified("ProofDigestUnavailable");
            }

            if (!DigestsEqual(sourceDataSha1, rebuiltDataSha1))
            {
                return ExtractionOutputProofResult.NotVerified("OutputDataSha1Mismatch");
            }

            if (!DigestsEqual(sourceCombinedSha1, rebuiltCombinedSha1))
            {
                return ExtractionOutputProofResult.NotVerified("OutputMetadataSha1Mismatch");
            }

            return ExtractionOutputProofResult.Verified();
        }
        finally
        {
            TryCleanupProofWorkspace(proofDirectory);
        }
    }

    private static bool DigestsEqual(string left, string right) =>
        TryNormalizeSha1(left, out string normalizedLeft)
        && TryNormalizeSha1(right, out string normalizedRight)
        && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);

    private static bool TryNormalizeSha1(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();
        if (candidate.Length != 40)
        {
            return false;
        }

        foreach (char ch in candidate)
        {
            bool isHex = ch is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';

            if (!isHex)
            {
                return false;
            }
        }

        normalized = candidate.ToLowerInvariant();
        return true;
    }

    private static void TryCleanupProofWorkspace(string proofDirectory)
    {
        try
        {
            if (Directory.Exists(proofDirectory)
                && AppPaths.IsPathUnderProcessTempRoot(proofDirectory))
            {
                Directory.Delete(proofDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            Log.ForContext<ExtractionOutputProofVerifier>().Debug(
                ex,
                "Could not delete extraction proof workspace. Path={Path}",
                proofDirectory);
        }
    }
}
