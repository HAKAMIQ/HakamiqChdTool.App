using System;
using HakamiqChdTool.App.Services.BinCueRescue;
using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.DiscLayout;

internal enum DiscLayoutDecisionAction
{
    Reject = 0,
    UseAdjacentCue = 1,
    GenerateTemporaryCue = 2
}

internal enum DiscLayoutTrustMode
{
    StrictEvidence = 0,
    ExplicitOperationalTrust = 1
}

internal sealed record DiscLayoutDecision(
    DiscLayoutDecisionAction Action,
    string? EffectiveCuePath,
    string? PlatformName,
    int PlatformConfidence,
    string MessageKey)
{
    public bool IsAccepted =>
        Action != DiscLayoutDecisionAction.Reject;

    public bool RequiresTemporaryCue =>
        Action == DiscLayoutDecisionAction.GenerateTemporaryCue;

    public bool UsesAdjacentCue =>
        Action == DiscLayoutDecisionAction.UseAdjacentCue;

    public static DiscLayoutDecision Reject(
        string messageKey)
    {
        return new DiscLayoutDecision(
            DiscLayoutDecisionAction.Reject,
            null,
            null,
            0,
            string.IsNullOrWhiteSpace(messageKey)
                ? "LocIntake_BinWithoutCueUnsafeSectorLayout"
                : messageKey);
    }

    public static DiscLayoutDecision UseAdjacentCue(
        string cuePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cuePath);

        return new DiscLayoutDecision(
            DiscLayoutDecisionAction.UseAdjacentCue,
            cuePath,
            null,
            0,
            "LocIntake_BinRedirectedToCue");
    }

    public static DiscLayoutDecision GenerateTemporaryCue(
        string platformName,
        int platformConfidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            platformName);

        return new DiscLayoutDecision(
            DiscLayoutDecisionAction.GenerateTemporaryCue,
            null,
            platformName,
            Math.Clamp(platformConfidence, 0, 100),
            "LocIntake_BinWithoutCueConsoleIdentified");
    }

    public static DiscLayoutDecision FromStandaloneBinPlan(
        BinCueRescuePlan plan,
        ConsoleDiscIdentityResult identity,
        DiscLayoutTrustMode trustMode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(identity);

        if (plan.CanUseAdjacentCue
            && !string.IsNullOrWhiteSpace(
                plan.AdjacentCuePath))
        {
            return UseAdjacentCue(
                plan.AdjacentCuePath);
        }

        if (!plan.CanGenerateTempCue)
        {
            return Reject(
                "LocIntake_BinWithoutCueUnsafeSectorLayout");
        }

        if (!identity.IsIdentified)
        {
            return Reject(
                "LocIntake_BinWithoutCueUnknownPlatform");
        }

        if (identity.IsPathHintOnly
            && trustMode
                != DiscLayoutTrustMode.ExplicitOperationalTrust)
        {
            return Reject(
                "LocIntake_BinWithoutCueUnknownPlatform");
        }

        return GenerateTemporaryCue(
            identity.PlatformName,
            identity.Confidence);
    }
}