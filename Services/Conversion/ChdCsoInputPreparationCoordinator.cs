using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

internal sealed class ChdCsoInputPreparationCoordinator
{
    private readonly ICsoPreprocessor _csoPreprocessor;

    public ChdCsoInputPreparationCoordinator(ICsoPreprocessor csoPreprocessor)
    {
        _csoPreprocessor = csoPreprocessor ?? throw new ArgumentNullException(nameof(csoPreprocessor));
    }

    public async Task<ChdCsoInputPreparationOutcome> PrepareIfRequiredAsync(
        ChdPlatformProfile? createProfile,
        string inputPath,
        string outputPath,
        string resolvedInputPath,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext,
        CancellationToken cancellationToken)
    {
        if (createProfile?.PreparationKind != ChdInputPreparationKind.ExpandCsoToIso)
        {
            return ChdCsoInputPreparationOutcome.NotRequired;
        }

        CsoTempWorkspace? tempWorkspace = null;

        try
        {
            tempWorkspace = CsoTempWorkspace.Create();

            CsoPreprocessResult preparationResult = await _csoPreprocessor
                .PreprocessAsync(resolvedInputPath, tempWorkspace.PreparedIsoPath, cancellationToken)
                .ConfigureAwait(false);

            if (preparationResult.IsSuccess)
            {
                var report = new ChdConversionServiceSupport.ChdInputPreparationReport(
                    OriginalInputPath: inputPath,
                    PreparedInputPath: preparationResult.PreparedIsoPath,
                    PreparationTool: preparationResult.ToolName,
                    PreparationToolVersion: preparationResult.ToolVersion,
                    PreparationCommand: "hakamiq-cso info input.cso --json; hakamiq-cso verify input.cso --json; hakamiq-cso decompress input.cso -o prepared.iso --force --json",
                    PreparationExitCode: preparationResult.ExitCode,
                    PreparedOutputBytes: preparationResult.PreparedIsoBytes,
                    TemporaryIsoDeleted: false,
                    SourcePreserved: true);

                CsoTempWorkspace leaseWorkspace = tempWorkspace;
                tempWorkspace = null;
                return new ChdCsoInputPreparationOutcome(new ChdCsoPreparedInputLease(leaseWorkspace, report), null);
            }

            tempWorkspace.Dispose();
            tempWorkspace = null;

            if (preparationResult.WasCancelled)
            {
                return new ChdCsoInputPreparationOutcome(
                    null,
                    ChdConversionServiceSupport.BuildPreparationCancelledResult(
                        inputPath,
                        outputPath,
                        preparationResult.MessageKey,
                        compressionResolution,
                        resolvedHunkSizeBytes,
                        executionReportContext));
            }

            return new ChdCsoInputPreparationOutcome(
                null,
                ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                    inputPath,
                    outputPath,
                    preparationResult.MessageKey,
                    compressionResolution,
                    resolvedHunkSizeBytes,
                    executionReportContext));
        }
        catch
        {
            tempWorkspace?.Dispose();
            throw;
        }
        finally
        {
            tempWorkspace?.Dispose();
        }
    }
}

internal sealed record ChdCsoInputPreparationOutcome(
    ChdCsoPreparedInputLease? Lease,
    ChdConversionResult? FailureResult)
{
    public static ChdCsoInputPreparationOutcome NotRequired { get; } = new(null, null);
}

internal sealed class ChdCsoPreparedInputLease : IDisposable
{
    private int _disposed;

    public ChdCsoPreparedInputLease(
        CsoTempWorkspace workspace,
        ChdConversionServiceSupport.ChdInputPreparationReport report)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Report = report;
    }

    public string PreparedIsoPath => Workspace.PreparedIsoPath;

    private CsoTempWorkspace Workspace { get; }

    public ChdConversionServiceSupport.ChdInputPreparationReport Report { get; private set; }

    public bool TemporaryIsoDeleted { get; private set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Workspace.Dispose();
        TemporaryIsoDeleted = Workspace.TemporaryIsoDeleted;
        Report = Report with { TemporaryIsoDeleted = TemporaryIsoDeleted };
    }
}
