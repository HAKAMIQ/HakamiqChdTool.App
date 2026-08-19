using System;
using System.IO;
using HakamiqChdTool.App.Services.BinCueRescue;
using HakamiqChdTool.App.Services.ConsoleMedia;
using HakamiqChdTool.App.Services.DiscLayout;

namespace HakamiqChdTool.App.Services.MediaInputPolicy;

internal static class MediaInputPolicy
{
    public const string UnsupportedMessageKey =
        "LocIntake_UnknownOrUnsupported";

    public const string BinUnsafeSectorLayoutMessageKey =
        "LocIntake_BinWithoutCueUnsafeSectorLayout";

    public const string BinTemporaryCueAcceptedMessageKey =
        "LocIntake_BinWithoutCueConsoleIdentified";

    public const string BinTemporaryCueWarningMessageKey =
        "LocIntake_BinWithoutCueTemporaryCue";

    public const string BinRedirectedToCueMessageKey =
        "LocIntake_BinRedirectedToCue";

    public static MediaInputDecision Evaluate(
        string? path,
        DiscLayoutTrustMode trustMode =
            DiscLayoutTrustMode.StrictEvidence)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaInputDecision.Block(
                string.Empty,
                UnsupportedMessageKey);
        }

        string fullPath;

        try
        {
            fullPath =
                Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return MediaInputDecision.Block(
                path,
                UnsupportedMessageKey);
        }

        if (!string.Equals(
                Path.GetExtension(fullPath),
                ".bin",
                StringComparison.OrdinalIgnoreCase))
        {
            return MediaInputDecision.Accept(fullPath);
        }

        if (!File.Exists(fullPath))
        {
            return MediaInputDecision.Block(
                fullPath,
                UnsupportedMessageKey);
        }

        BinCueRescuePlan rescuePlan;

        try
        {
            rescuePlan =
                MultiBinDiscAssembler.AssembleForBin(
                    fullPath,
                    Path.ChangeExtension(
                        fullPath,
                        ".cue"));
        }
        catch (Exception ex) when (
            IsExpectedPolicyFailure(ex))
        {
            return MediaInputDecision.Block(
                fullPath,
                BinUnsafeSectorLayoutMessageKey);
        }

        if (rescuePlan.CanUseAdjacentCue
            && !string.IsNullOrWhiteSpace(
                rescuePlan.AdjacentCuePath))
        {
            return MediaInputDecision.RedirectToCue(
                fullPath,
                rescuePlan.AdjacentCuePath);
        }

        if (!rescuePlan.CanGenerateTempCue)
        {
            return MediaInputDecision.Block(
                fullPath,
                BinUnsafeSectorLayoutMessageKey);
        }

        string identityProbePath =
            GetConsoleIdentityProbePath(
                rescuePlan,
                fullPath);

        ConsoleDiscIdentityResult identity =
            ConsoleDiscIdentityService.Shared.Detect(
                identityProbePath);

        DiscLayoutDecision layoutDecision =
            DiscLayoutDecision.FromStandaloneBinPlan(
                rescuePlan,
                identity,
                trustMode);

        if (layoutDecision.RequiresTemporaryCue)
        {
            return MediaInputDecision.AcceptTemporaryCue(
                fullPath,
                layoutDecision.PlatformName
                    ?? identity.PlatformName,
                layoutDecision.PlatformConfidence > 0
                    ? layoutDecision.PlatformConfidence
                    : identity.Confidence,
                BinTemporaryCueWarningMessageKey);
        }

        return MediaInputDecision.Block(
            fullPath,
            string.IsNullOrWhiteSpace(
                layoutDecision.MessageKey)
                ? BinUnsafeSectorLayoutMessageKey
                : layoutDecision.MessageKey);
    }

    private static string GetConsoleIdentityProbePath(
        BinCueRescuePlan plan,
        string fallbackInputPath)
    {
        foreach (BinCueRescueTrackPlan track
                 in plan.OrderedTracks)
        {
            if (!track.IsDataTrack
                || string.IsNullOrWhiteSpace(
                    track.SourceBinPath))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(
                    track.SourceBinPath);
            }
            catch (Exception ex) when (IsPathFailure(ex))
            {
                return fallbackInputPath;
            }
        }

        return fallbackInputPath;
    }

    private static bool IsExpectedPolicyFailure(
        Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException;
    }

    private static bool IsPathFailure(
        Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}