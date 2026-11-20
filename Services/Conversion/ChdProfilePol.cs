using HakamiqChdTool.App.Core.Chd.Commands;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using Serilog;

namespace HakamiqChdTool.App.Services;

internal readonly record struct ChdRequestedCreateProfileSelection(
    string Command,
    ChdPlatformProfile? Profile,
    ChdConversionResult? FailureResult);

internal readonly record struct ChdResolvedSourceCommand(
    string Command,
    IsoChdmanCreateDiagnostics? IsoDiagnostics,
    ChdConversionResult? FailureResult);

internal static class ChdPlatformProfileExecutionPolicy
{
    public static ChdResolvedSourceCommand ResolveSourceCommand(
        IChdCommandPreparationService commandPreparation,
        string inputExtension,
        ChdmanExtractionKind extractionKind,
        string resolvedInputPath,
        IsoCreateCommandOverride isoCreateCommandOverride,
        string? platformProfileId,
        string inputPath,
        string outputPath)
    {
        if (extractionKind != ChdmanExtractionKind.None
            || ChdPlatformProfiles.FindById(platformProfileId) is not { CommandKind: ChdCommandKind.CreateHd })
        {
            (string command, IsoChdmanCreateDiagnostics? isoDiagnostics) =
                commandPreparation.ResolveTwoWayCommandWithOptionalIsoDiagnostics(
                    inputExtension,
                    extractionKind,
                    resolvedInputPath,
                    isoCreateCommandOverride);

            return new ChdResolvedSourceCommand(command, isoDiagnostics, null);
        }

        // Hard-disk images are only converted on explicit user selection; the
        // extension-based resolver deliberately rejects .img/.hdd.
        ChdWorkflowProfilePlan hardDiskPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            resolvedInputPath,
            isoCreateCommandOverride,
            ChdMediaContainerKind.DirectFile,
            platformProfileId);

        if (hardDiskPlan.IsSupported)
        {
            return new ChdResolvedSourceCommand(hardDiskPlan.Command, null, null);
        }

        Log.Warning(
            "Hard-disk image rejected before chdman. Input={InputPath}; MessageKey={MessageKey}",
            resolvedInputPath,
            hardDiskPlan.FailureMessage);

        return new ChdResolvedSourceCommand(
            string.Empty,
            null,
            ChdConversionServiceSupport.BuildPreExecutionFailureResult(inputPath, outputPath, hardDiskPlan.FailureMessage));
    }

    public static ChdRequestedCreateProfileSelection ApplyRequestedCreateProfile(
        string command,
        string inputExtension,
        string? platformProfileId,
        ChdmanExtractionKind extractionKind,
        string inputPath,
        string outputPath)
    {
        ChdPlatformProfile? requestedProfile = null;
        if (extractionKind != ChdmanExtractionKind.None)
        {
            return new ChdRequestedCreateProfileSelection(command, requestedProfile, null);
        }

        requestedProfile = ChdPlatformProfiles.FindById(platformProfileId);
        if (requestedProfile is null)
        {
            return new ChdRequestedCreateProfileSelection(command, requestedProfile, null);
        }

        // Another disc type in the same queue converts with Auto instead of failing.
        if (!ChdPlatformProfiles.AppliesToInput(requestedProfile, inputExtension))
        {
            return new ChdRequestedCreateProfileSelection(command, null, null);
        }

        if (!ChdPlatformProfiles.SupportsExtension(requestedProfile, inputExtension))
        {
            return Failure(command, requestedProfile, inputPath, outputPath, ChdWorkflowProfilePlanner.UnsupportedMessageKey);
        }

        return new ChdRequestedCreateProfileSelection(
            ChdPlatformProfiles.ToCommandName(requestedProfile.CommandKind),
            requestedProfile,
            null);
    }

    public static ChdPlatformProfile? ResolveCreateProfile(
        bool isExtractCommand,
        ChdPlatformProfile? requestedCreateProfile,
        string command,
        string inputPath,
        string detectedPlatform,
        string? platformProfileId) =>
        isExtractCommand
            ? null
            : requestedCreateProfile
              ?? ChdPlatformProfiles.ResolveForCommand(command, inputPath, detectedPlatform, platformProfileId);

    public static ChdConversionResult? ValidateDvdSectorAlignment(
        ChdPlatformProfile? createProfile,
        string resolvedInputPath,
        string inputPath,
        string outputPath,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext)
    {
        if (createProfile is not { RequiresDvdSectorAlignment: true })
        {
            return null;
        }

        try
        {
            ChdmanCommandBuilder.ValidateDvdSectorAlignment(resolvedInputPath);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                inputPath,
                outputPath,
                ex.Message,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext);
        }
    }

    private static ChdRequestedCreateProfileSelection Failure(
        string command,
        ChdPlatformProfile requestedProfile,
        string inputPath,
        string outputPath,
        string messageKey) =>
        new(
            command,
            requestedProfile,
            ChdConversionServiceSupport.BuildPreExecutionFailureResult(inputPath, outputPath, messageKey));
}
