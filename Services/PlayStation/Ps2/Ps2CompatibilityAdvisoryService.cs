using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.PlayStation.Ps2;

internal static class Ps2CompatibilityAdvisoryService
{
    public static QueueIntakeAdvisory? BuildQueueAdvisory(string path, string? detectedPlatform)
    {
        Ps2DiscIdentity identity = Ps2DiscIdentityDetector.Detect(path, detectedPlatform);
        if (!identity.IsPlayStation2)
        {
            return null;
        }

        var reasons = new List<QueueIntakeAdvisoryReason>();
        string source = BuildSource(identity);

        switch (identity.MediaKind)
        {
            case Ps2DiscMediaKind.CueBinCd:
                reasons.Add(new QueueIntakeAdvisoryReason(
                    "PS2_CLASSICS_BIN_CUE_PREFERRED",
                    "LocPs2Advisory_BinCuePreferred",
                    QueueIntakeAdvisorySeverity.Info,
                    source));
                break;

            case Ps2DiscMediaKind.StandaloneBinCd:
                reasons.Add(new QueueIntakeAdvisoryReason(
                    "PS2_CLASSICS_CUE_FILE_RECOMMENDED",
                    "LocPs2Advisory_CueFileRecommended",
                    QueueIntakeAdvisorySeverity.Warning,
                    source));
                break;

            case Ps2DiscMediaKind.CompactIsoPossiblyCd:
                reasons.Add(new QueueIntakeAdvisoryReason(
                    "PS2_CLASSICS_COMPACT_ISO_MAY_BE_PS2CD",
                    "LocPs2Advisory_CompactIsoMayNeedBinCue",
                    QueueIntakeAdvisorySeverity.Warning,
                    source));
                break;
        }

        reasons.Add(new QueueIntakeAdvisoryReason(
            "PS2_CLASSICS_CONFIG_MAY_BE_REQUIRED",
            "LocPs2Advisory_ConfigMayBeRequired",
            QueueIntakeAdvisorySeverity.Info,
            source));

        int confidence = Math.Clamp(identity.Confidence, 60, 96);

        return new QueueIntakeAdvisory(
            QueueIntakeAdvisoryAction.ReportOnly,
            confidence,
            SafeOutputHint: false,
            Reasons: reasons,
            Warnings: [],
            SuggestedFileName: null,
            Platform: "PlayStation 2",
            Region: identity.Region,
            DiscGroupKey: identity.Serial,
            DiscIndex: null);
    }

    private static string BuildSource(Ps2DiscIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.Serial))
        {
            return string.IsNullOrWhiteSpace(identity.Region)
                ? identity.Serial
                : identity.Serial + " / " + identity.Region;
        }

        return identity.IsPathHintOnly
            ? "PS2 path hint"
            : "PS2 disc identity";
    }
}
