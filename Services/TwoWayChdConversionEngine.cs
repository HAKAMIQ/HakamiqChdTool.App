using HakamiqChdTool.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class TwoWayChdConversionEngine(
    ChdConversionService conversion,
    ChdVerificationService verification,
    ISafeRecompressPipeline safeRecompressPipeline)
{
    private readonly ChdConversionService _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
    private readonly ChdVerificationService _verification = verification ?? throw new ArgumentNullException(nameof(verification));
    private readonly ISafeRecompressPipeline _safeRecompressPipeline = safeRecompressPipeline ?? throw new ArgumentNullException(nameof(safeRecompressPipeline));

    public TwoWayChdConversionEngine()
        : this(new ChdConversionService(), new ChdVerificationService(), new SafeRecompressPipeline())
    {
    }

    public TwoWayChdConversionEngine(
        ChdConversionService conversion,
        ChdVerificationService verification)
        : this(conversion, verification, new SafeRecompressPipeline())
    {
    }

    public Task<ChdConversionResult> CreateCdAsync(
        string chdmanPath,
        string cueOrGdiPath,
        string outputChdPath,
        IProgress<int>? progress = null,
        IProgress<PerformanceSample>? performanceProgress = null,
        CancellationToken cancellationToken = default) =>
        _conversion.ConvertToChdAsync(
            chdmanPath,
            cueOrGdiPath,
            outputChdPath,
            progress: progress,
            cancellationToken: cancellationToken,
            performanceProgress: performanceProgress,
            computeInputSha1: true);

    public Task<ChdConversionResult> CreateDvdAsync(
        string chdmanPath,
        string isoPath,
        string outputChdPath,
        IProgress<int>? progress = null,
        IProgress<PerformanceSample>? performanceProgress = null,
        CancellationToken cancellationToken = default) =>
        _conversion.ConvertToChdAsync(
            chdmanPath,
            isoPath,
            outputChdPath,
            progress: progress,
            cancellationToken: cancellationToken,
            isoCreateCommandOverride: IsoCreateCommandOverride.CreateDvd,
            performanceProgress: performanceProgress,
            computeInputSha1: true);

    public Task<ChdConversionResult> ExtractCdAsync(
        string chdmanPath,
        string chdPath,
        string outputCuePath,
        IProgress<int>? progress = null,
        IProgress<PerformanceSample>? performanceProgress = null,
        CancellationToken cancellationToken = default,
        bool extractionMetadataDecisionConfirmed = false) =>
        _conversion.ConvertToChdAsync(
            chdmanPath,
            chdPath,
            outputCuePath,
            progress: progress,
            cancellationToken: cancellationToken,
            extractionKind: ChdmanExtractionKind.ExtractCd,
            performanceProgress: performanceProgress,
            computeInputSha1: true,
            extractionMetadataDecisionConfirmed: extractionMetadataDecisionConfirmed);

    public Task<ChdConversionResult> ExtractDvdAsync(
        string chdmanPath,
        string chdPath,
        string outputIsoPath,
        IProgress<int>? progress = null,
        IProgress<PerformanceSample>? performanceProgress = null,
        CancellationToken cancellationToken = default,
        bool extractionMetadataDecisionConfirmed = false) =>
        _conversion.ConvertToChdAsync(
            chdmanPath,
            chdPath,
            outputIsoPath,
            progress: progress,
            cancellationToken: cancellationToken,
            extractionKind: ChdmanExtractionKind.ExtractDvd,
            performanceProgress: performanceProgress,
            computeInputSha1: true,
            extractionMetadataDecisionConfirmed: extractionMetadataDecisionConfirmed);


    public Task<SafeChdRecompressResult> RecompressChdSafelyAsync(
        SafeChdRecompressRequest request,
        CancellationToken cancellationToken = default) =>
        _safeRecompressPipeline.RecompressAsync(request, cancellationToken);

    public Task<ChdVerificationResult> VerifyChdAsync(
        string chdmanPath,
        string chdPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        _verification.VerifyAsync(chdmanPath, chdPath, progress, onProcessStarted: null, cancellationToken);
}