using HakamiqChdTool.App.Core.Chd.Commands;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

internal readonly record struct ChdRequestedCreateProfileSelection(
    string Command,
    ChdPlatformProfile? Profile,
    ChdConversionResult? FailureResult);

internal static class ChdPlatformProfileExecutionPolicy
{
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
