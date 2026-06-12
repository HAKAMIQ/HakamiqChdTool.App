using System;
using System.Collections.Generic;
using System.Linq;
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
    string MessageKey,
    IReadOnlyList<BinCueRescueRefusalReason> Refusals)
{
    public bool IsAccepted => Action != DiscLayoutDecisionAction.Reject;

    public bool RequiresTemporaryCue => Action == DiscLayoutDecisionAction.GenerateTemporaryCue;

    public bool UsesAdjacentCue => Action == DiscLayoutDecisionAction.UseAdjacentCue;

    public static DiscLayoutDecision Reject(
        string messageKey,
        IReadOnlyList<BinCueRescueRefusalReason>? refusals = null) => new(
        DiscLayoutDecisionAction.Reject,
        null,
        null,
        0,
        string.IsNullOrWhiteSpace(messageKey)
            ? "LocIntake_BinWithoutCueUnsafeSectorLayout"
            : messageKey,
        refusals is null ? [] : [.. refusals]);

    public static DiscLayoutDecision UseAdjacentCue(string cuePath) => new(
        DiscLayoutDecisionAction.UseAdjacentCue,
        cuePath,
        null,
        0,
        "LocIntake_BinRedirectedToCue",
        []);

    public static DiscLayoutDecision GenerateTemporaryCue(
        string platformName,
        int platformConfidence) => new(
        DiscLayoutDecisionAction.GenerateTemporaryCue,
        null,
        platformName,
        Math.Clamp(platformConfidence, 0, 100),
        "LocIntake_BinWithoutCueConsoleIdentified",
        []);

    public static DiscLayoutDecision FromStandaloneBinPlan(
        BinCueRescuePlan plan,
        ConsoleDiscIdentityResult identity,
        DiscLayoutTrustMode trustMode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(identity);

        if (plan.CanUseAdjacentCue && !string.IsNullOrWhiteSpace(plan.AdjacentCuePath))
        {
            return UseAdjacentCue(plan.AdjacentCuePath);
        }

        if (!plan.CanGenerateTempCue)
        {
            if (plan.Refusals.Contains(BinCueRescueRefusalReason.UnsupportedPlatform)
                || plan.Refusals.Contains(BinCueRescueRefusalReason.PathHintOnly))
            {
                return Reject(
                    "LocIntake_BinWithoutCueUnknownPlatform",
                    plan.Refusals);
            }

            return Reject(
                "LocIntake_BinWithoutCueUnsafeSectorLayout",
                plan.Refusals);
        }

        if (!identity.IsIdentified)
        {
            return Reject(
                "LocIntake_BinWithoutCueUnknownPlatform",
                plan.Refusals.Append(BinCueRescueRefusalReason.UnsupportedPlatform).Distinct().ToArray());
        }

        if (identity.IsPathHintOnly && trustMode != DiscLayoutTrustMode.ExplicitOperationalTrust)
        {
            return Reject(
                "LocIntake_BinWithoutCueUnknownPlatform",
                plan.Refusals.Append(BinCueRescueRefusalReason.PathHintOnly).Distinct().ToArray());
        }

        return GenerateTemporaryCue(identity.PlatformName, identity.Confidence);
    }
}
