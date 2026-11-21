using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
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

    // Every conversion leases its source files first (see SourceFileLease); the lease travels with the
    // outcome and is released when the conversion finishes or preparation fails.
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
        if (!SourceFileLease.TryAcquire(SourceDependencySet.Resolve(resolvedInputPath), out SourceFileLease? sourceLease, out _))
        {
            return new ChdCsoInputPreparationOutcome(
                null,
                ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                    inputPath,
                    outputPath,
                    SourceFileLease.SourceUnstableMessageKey,
                    compressionResolution,
                    resolvedHunkSizeBytes,
                    executionReportContext));
        }

        try
        {
            ChdCsoInputPreparationOutcome outcome = await PrepareCoreAsync(
                    createProfile,
                    inputPath,
                    outputPath,
                    resolvedInputPath,
                    compressionResolution,
                    resolvedHunkSizeBytes,
                    executionReportContext,
                    cancellationToken)
                .ConfigureAwait(false);
            if (outcome.FailureResult is not null)
            {
                sourceLease!.Dispose();
                return outcome;
            }

            return outcome with { SourceLease = sourceLease };
        }
        catch
        {
            sourceLease!.Dispose();
            throw;
        }
    }

    private async Task<ChdCsoInputPreparationOutcome> PrepareCoreAsync(
        ChdPlatformProfile? createProfile,
        string inputPath,
        string outputPath,
        string resolvedInputPath,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext,
        CancellationToken cancellationToken)
    {
        ChdInputPreparationKind preparationKind = createProfile?.PreparationKind ?? ChdInputPreparationKind.None;
        if (preparationKind == ChdInputPreparationKind.GenerateCueFromCcd)
        {
            return await PrepareCloneCdCueAsync(inputPath, outputPath, resolvedInputPath, compressionResolution, resolvedHunkSizeBytes, executionReportContext, cancellationToken).ConfigureAwait(false);
        }

        if (preparationKind == ChdInputPreparationKind.DecodeEcm)
        {
            return await PrepareEcmAsync(inputPath, outputPath, resolvedInputPath, compressionResolution, resolvedHunkSizeBytes, executionReportContext, cancellationToken).ConfigureAwait(false);
        }

        if (preparationKind is ChdInputPreparationKind.GenerateCueFromMds or ChdInputPreparationKind.StageIsoFromMds)
        {
            return await PrepareAlcoholMdsAsync(inputPath, outputPath, resolvedInputPath, preparationKind, compressionResolution, resolvedHunkSizeBytes, executionReportContext, cancellationToken).ConfigureAwait(false);
        }

        if (preparationKind == ChdInputPreparationKind.None
            && string.Equals(Path.GetExtension(resolvedInputPath), ".cue", StringComparison.OrdinalIgnoreCase)
            && GdRomCueGdi.TryRead(resolvedInputPath) is { } gdRomTracks)
        {
            return await PrepareGdRomGdiAsync(gdRomTracks, inputPath, outputPath, compressionResolution, resolvedHunkSizeBytes, executionReportContext, cancellationToken).ConfigureAwait(false);
        }

        if (preparationKind is not (ChdInputPreparationKind.ExpandCsoToIso or ChdInputPreparationKind.ExpandZsoToIso))
        {
            return ChdCsoInputPreparationOutcome.NotRequired;
        }

        string preparationCommand = preparationKind == ChdInputPreparationKind.ExpandZsoToIso
            ? "csokit detect input.zso --json; csokit decompress input.zso -o tmp.iso --force --json"
            : "csokit info input.cso --json; csokit verify input.cso --json; csokit decompress input.cso -o tmp.iso --force --json";

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
                    PreparationCommand: preparationCommand,
                    PreparationExitCode: preparationResult.ExitCode,
                    PreparedOutputBytes: preparationResult.PreparedIsoBytes,
                    TemporaryIsoDeleted: false,
                    SourcePreserved: true);

                CsoTempWorkspace leaseWorkspace = tempWorkspace;
                tempWorkspace = null;
                // Ownership transfers to the returned lease, which the conversion caller disposes.
#pragma warning disable CA2000
                return new ChdCsoInputPreparationOutcome(new ChdCsoPreparedInputLease(leaseWorkspace, report), null);
#pragma warning restore CA2000
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
        finally
        {
            tempWorkspace?.Dispose();
        }
    }

    // A GD-ROM CUE becomes a GDI next to the staged (hard-linked, or copied) track files, so chdman keeps each track's
    // pregap where the dump has it. Nothing is written to the source folder.
    private static async Task<ChdCsoInputPreparationOutcome> PrepareGdRomGdiAsync(
        IReadOnlyList<GdRomCueTrack> tracks,
        string inputPath,
        string outputPath,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext,
        CancellationToken cancellationToken)
    {
        ChdCsoInputPreparationOutcome Fail() => new(
            null,
            ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                inputPath,
                outputPath,
                GdRomCueGdi.StagingFailedMessageKey,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext));

        CsoTempWorkspace? workspace = CsoTempWorkspace.Create();
        try
        {
            long stagedBytes = 0;
            foreach (GdRomCueTrack track in tracks)
            {
                ConversionPathValidator.ThrowIfUnsafeForChdman(track.FilePath, nameof(inputPath));
                string staged = Path.Combine(workspace.DirectoryPath, GdRomCueGdi.StagedFileName(track));
                if (!await CloneCdImageStager.TryStageAsync(track.FilePath, staged, cancellationToken).ConfigureAwait(false))
                {
                    return Fail();
                }

                stagedBytes += new FileInfo(staged).Length;
            }

            string gdiPath = Path.Combine(workspace.DirectoryPath, "tmp.gdi");
            File.WriteAllText(gdiPath, GdRomCueGdi.BuildGdi(tracks, GdRomCueGdi.StagedFileName), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var report = new ChdConversionServiceSupport.ChdInputPreparationReport(
                OriginalInputPath: inputPath,
                PreparedInputPath: gdiPath,
                PreparationTool: "GD-ROM CUE to GDI",
                PreparationToolVersion: string.Empty,
                PreparationCommand: "generate tmp.gdi from the GD-ROM .cue (track pregaps kept in place)",
                PreparationExitCode: 0,
                PreparedOutputBytes: stagedBytes,
                TemporaryIsoDeleted: false,
                SourcePreserved: true);

            // Ownership transfers to the returned lease, which the conversion caller disposes.
#pragma warning disable CA2000
            var lease = new ChdCsoPreparedInputLease(workspace, report, gdiPath);
#pragma warning restore CA2000
            workspace = null;
            return new ChdCsoInputPreparationOutcome(lease, null);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException)
        {
            return Fail();
        }
        finally
        {
            workspace?.Dispose();
        }
    }

    private static async Task<ChdCsoInputPreparationOutcome> PrepareEcmAsync(
        string inputPath,
        string outputPath,
        string resolvedInputPath,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext,
        CancellationToken cancellationToken)
    {
        ChdCsoInputPreparationOutcome Fail(string messageKey) => new(
            null,
            ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                inputPath,
                outputPath,
                messageKey,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext));

        CsoTempWorkspace? workspace = CsoTempWorkspace.Create();
        try
        {
            ConversionPathValidator.ThrowIfUnsafeForChdman(resolvedInputPath, nameof(inputPath));
            (string? preparedCue, string? failure) = await EcmSetPreparer
                .PrepareAsync(resolvedInputPath, workspace.DirectoryPath, cancellationToken)
                .ConfigureAwait(false);
            if (preparedCue is null)
            {
                return Fail(failure ?? EcmDecoder.StagingFailedMessageKey);
            }

            long preparedBytes = Directory.EnumerateFiles(workspace.DirectoryPath)
                .Where(path => !string.Equals(path, preparedCue, StringComparison.OrdinalIgnoreCase))
                .Sum(path => new FileInfo(path).Length);
            var report = new ChdConversionServiceSupport.ChdInputPreparationReport(
                OriginalInputPath: inputPath,
                PreparedInputPath: preparedCue,
                PreparationTool: "ECM decoder",
                PreparationToolVersion: string.Empty,
                PreparationCommand: "decode .ecm tracks, verify stored EDC, stage CUE",
                PreparationExitCode: 0,
                PreparedOutputBytes: preparedBytes,
                TemporaryIsoDeleted: false,
                SourcePreserved: true);

            // Ownership transfers to the returned lease, which the conversion caller disposes.
#pragma warning disable CA2000
            var lease = new ChdCsoPreparedInputLease(workspace, report, preparedCue);
#pragma warning restore CA2000
            workspace = null;
            return new ChdCsoInputPreparationOutcome(lease, null);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException)
        {
            return Fail(EcmDecoder.StagingFailedMessageKey);
        }
        finally
        {
            workspace?.Dispose();
        }
    }

    private static async Task<ChdCsoInputPreparationOutcome> PrepareAlcoholMdsAsync(
        string inputPath,
        string outputPath,
        string resolvedInputPath,
        ChdInputPreparationKind preparationKind,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext,
        CancellationToken cancellationToken)
    {
        ChdCsoInputPreparationOutcome Fail(string messageKey) => new(
            null,
            ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                inputPath,
                outputPath,
                messageKey,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext));

        AlcoholMdsParseResult parsed = AlcoholMdsCueBuilder.Parse(resolvedInputPath);
        if (!parsed.IsValid || parsed.Layout is null)
        {
            return Fail(parsed.MessageKey);
        }

        AlcoholMdsLayout layout = parsed.Layout;
        bool dvd = layout.Medium == AlcoholMdsMedium.Dvd;
        if (dvd != (preparationKind == ChdInputPreparationKind.StageIsoFromMds))
        {
            // The descriptor changed between planning and conversion.
            return Fail(AlcoholMdsCueBuilder.InvalidMessageKey);
        }

        CsoTempWorkspace? workspace = CsoTempWorkspace.Create();
        try
        {
            ConversionPathValidator.ThrowIfUnsafeForChdman(layout.ImagePath, nameof(inputPath));
            const string StagedImageName = "tmp.img";
            string stagedImagePath = dvd
                ? workspace.PreparedIsoPath
                : Path.Combine(workspace.DirectoryPath, StagedImageName);

            if (!await AlcoholMdfStager.TryStageAsync(layout, stagedImagePath, cancellationToken).ConfigureAwait(false))
            {
                return Fail(AlcoholMdsCueBuilder.StagingFailedMessageKey);
            }

            string preparedInputPath = stagedImagePath;
            if (!dvd)
            {
                preparedInputPath = Path.Combine(workspace.DirectoryPath, "tmp.cue");
                File.WriteAllText(preparedInputPath, AlcoholMdsCueBuilder.BuildCue(layout, StagedImageName), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            bool stripped = layout.SectorFormat == AlcoholMdsSectorFormat.Raw2352WithSubchannel;
            var report = new ChdConversionServiceSupport.ChdInputPreparationReport(
                OriginalInputPath: inputPath,
                PreparedInputPath: preparedInputPath,
                PreparationTool: "Alcohol 120% MDS reader",
                PreparationToolVersion: string.Empty,
                PreparationCommand: dvd
                    ? "stage .mdf as tmp.iso"
                    : stripped
                        ? "generate tmp.cue from .mds; copy .mdf without subchannel data"
                        : "generate tmp.cue from .mds",
                PreparationExitCode: 0,
                PreparedOutputBytes: new FileInfo(stagedImagePath).Length,
                TemporaryIsoDeleted: false,
                SourcePreserved: true);

            // Ownership transfers to the returned lease, which the conversion caller disposes.
#pragma warning disable CA2000
            var lease = new ChdCsoPreparedInputLease(workspace, report, preparedInputPath);
#pragma warning restore CA2000
            workspace = null;
            return new ChdCsoInputPreparationOutcome(lease, null);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException)
        {
            return Fail(AlcoholMdsCueBuilder.InvalidMessageKey);
        }
        finally
        {
            workspace?.Dispose();
        }
    }
    private static async Task<ChdCsoInputPreparationOutcome> PrepareCloneCdCueAsync(
        string inputPath,
        string outputPath,
        string resolvedInputPath,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext,
        CancellationToken cancellationToken)
    {
        CloneCdParseResult layout = CloneCdCueBuilder.Parse(resolvedInputPath);
        if (!layout.IsValid || layout.Layout is null)
        {
            return new ChdCsoInputPreparationOutcome(
                null,
                ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                    inputPath,
                    outputPath,
                    layout.MessageKey,
                    compressionResolution,
                    resolvedHunkSizeBytes,
                    executionReportContext));
        }

        CsoTempWorkspace? workspace = CsoTempWorkspace.Create();
        try
        {
            ConversionPathValidator.ThrowIfUnsafeForChdman(layout.Layout.ImagePath, nameof(inputPath));
            const string StagedImageName = "tmp.img";
            string stagedImagePath = Path.Combine(workspace.DirectoryPath, StagedImageName);

            if (!await CloneCdImageStager.TryStageAsync(layout.Layout.ImagePath, stagedImagePath, cancellationToken).ConfigureAwait(false))
            {
                return new ChdCsoInputPreparationOutcome(
                    null,
                    ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                        inputPath,
                        outputPath,
                        CloneCdCueBuilder.StagingFailedMessageKey,
                        compressionResolution,
                        resolvedHunkSizeBytes,
                        executionReportContext));
            }

            string cuePath = Path.Combine(workspace.DirectoryPath, "tmp.cue");
            File.WriteAllText(cuePath, CloneCdCueBuilder.BuildCue(layout.Layout, StagedImageName), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var report = new ChdConversionServiceSupport.ChdInputPreparationReport(
                OriginalInputPath: inputPath,
                PreparedInputPath: cuePath,
                PreparationTool: "CloneCD CUE generator",
                PreparationToolVersion: string.Empty,
                PreparationCommand: "generate tmp.cue from .ccd (subchannel .sub not used)",
                PreparationExitCode: 0,
                PreparedOutputBytes: new FileInfo(layout.Layout.ImagePath).Length,
                TemporaryIsoDeleted: false,
                SourcePreserved: true);

            // Ownership transfers to the returned lease, which the conversion caller disposes.
#pragma warning disable CA2000
            var lease = new ChdCsoPreparedInputLease(workspace, report, cuePath);
#pragma warning restore CA2000
            workspace = null;
            return new ChdCsoInputPreparationOutcome(lease, null);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException)
        {
            return new ChdCsoInputPreparationOutcome(
                null,
                ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                    inputPath,
                    outputPath,
                    CloneCdCueBuilder.InvalidControlFileMessageKey,
                    compressionResolution,
                    resolvedHunkSizeBytes,
                    executionReportContext));
        }
        finally
        {
            workspace?.Dispose();
        }
    }
}

internal sealed record ChdCsoInputPreparationOutcome(
    ChdCsoPreparedInputLease? Lease,
    ChdConversionResult? FailureResult)
{
    public static ChdCsoInputPreparationOutcome NotRequired { get; } = new(null, null);

    /// <summary>The source files held open for the conversion; disposed with the conversion.</summary>
    public SourceFileLease? SourceLease { get; init; }
}

internal sealed class ChdCsoPreparedInputLease : IDisposable
{
    private int _disposed;

    public ChdCsoPreparedInputLease(
        CsoTempWorkspace workspace,
        ChdConversionServiceSupport.ChdInputPreparationReport report,
        string? preparedInputPath = null)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Report = report;
        PreparedIsoPath = string.IsNullOrWhiteSpace(preparedInputPath)
            ? workspace.PreparedIsoPath
            : preparedInputPath;
    }

    /// <summary>The prepared chdman input inside the workspace (tmp.iso, or tmp.cue for CloneCD).</summary>
    public string PreparedIsoPath { get; }

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
