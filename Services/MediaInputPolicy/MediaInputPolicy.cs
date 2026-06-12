using System;
using System.IO;
using HakamiqChdTool.App.Services.BinCueRescue;
using HakamiqChdTool.App.Services.ConsoleMedia;
using HakamiqChdTool.App.Services.DiscLayout;

namespace HakamiqChdTool.App.Services.MediaInputPolicy;

internal static class MediaInputPolicy
{
    public const string UnsupportedMessageKey = "LocIntake_UnknownOrUnsupported";
    public const string BinUnknownPlatformMessageKey = "LocIntake_BinWithoutCueUnknownPlatform";
    public const string BinUnsafeSectorLayoutMessageKey = "LocIntake_BinWithoutCueUnsafeSectorLayout";
    public const string BinTemporaryCueAcceptedMessageKey = "LocIntake_BinWithoutCueConsoleIdentified";
    public const string BinTemporaryCueWarningMessageKey = "LocIntake_BinWithoutCueTemporaryCue";
    public const string BinRedirectedToCueMessageKey = "LocIntake_BinRedirectedToCue";

    public static MediaInputDecision Evaluate(
        string? path,
        DiscLayoutTrustMode trustMode = DiscLayoutTrustMode.StrictEvidence)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaInputDecision.Block(string.Empty, UnsupportedMessageKey);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return MediaInputDecision.Block(path, UnsupportedMessageKey);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            return MediaInputDecision.Accept(fullPath);
        }

        if (!File.Exists(fullPath))
        {
            return MediaInputDecision.Block(fullPath, UnsupportedMessageKey);
        }

        BinCueRescuePlan rescuePlan;
        try
        {
            rescuePlan = MultiBinDiscAssembler.AssembleForBin(
                fullPath,
                Path.ChangeExtension(fullPath, ".cue"));
        }
        catch (Exception ex) when (IsExpectedPolicyFailure(ex))
        {
            return MediaInputDecision.Block(fullPath, BinUnsafeSectorLayoutMessageKey);
        }

        ConsoleDiscIdentityResult identity = ConsoleDiscIdentityService.Shared.Detect(fullPath);
        DiscLayoutDecision layoutDecision = DiscLayoutDecision.FromStandaloneBinPlan(
            rescuePlan,
            identity,
            trustMode);

        string? adjacentCue = FindAdjacentCue(rescuePlan, layoutDecision);
        if (!string.IsNullOrWhiteSpace(adjacentCue))
        {
            return MediaInputDecision.RedirectToCue(fullPath, adjacentCue);
        }

        if (layoutDecision.RequiresTemporaryCue)
        {
            return MediaInputDecision.AcceptTemporaryCue(
                fullPath,
                layoutDecision.PlatformName ?? identity.PlatformName,
                layoutDecision.PlatformConfidence > 0 ? layoutDecision.PlatformConfidence : identity.Confidence,
                BinTemporaryCueWarningMessageKey);
        }

        return MediaInputDecision.Block(
            fullPath,
            string.IsNullOrWhiteSpace(layoutDecision.MessageKey)
                ? BinUnsafeSectorLayoutMessageKey
                : layoutDecision.MessageKey);
    }

    private static string? FindAdjacentCue(
        BinCueRescuePlan rescuePlan,
        DiscLayoutDecision layoutDecision) =>
        layoutDecision.UsesAdjacentCue && rescuePlan.CanUseAdjacentCue
            ? layoutDecision.EffectiveCuePath
            : null;

    private static bool IsExpectedPolicyFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or NotSupportedException
        or ArgumentException
        or InvalidOperationException;

    private static bool IsPathFailure(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;
}
