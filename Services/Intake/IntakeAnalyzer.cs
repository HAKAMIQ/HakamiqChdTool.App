using System;
using System.IO;
using HakamiqChdTool.App.Services.MediaInputPolicy;

namespace HakamiqChdTool.App.Services.Intake;

public sealed class IntakeAnalyzer
{
    private const string SourceName = nameof(IntakeAnalyzer);

    public IntakeDecision Analyze(IntakeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsArchive)
        {
            return IntakeDecision.Create(
                IntakeDecisionAction.Extract,
                95,
                CreateReason(
                    "INTAKE_ARCHIVE_EXTRACT_REQUIRED",
                    "LocIntake_ArchiveExtractRequired",
                    IntakeDecisionSeverity.Info),
                CreateSafeOutputHint());
        }

        if (context.IsUnsupportedDiscImage)
        {
            return IntakeDecision.Blocked(
                "INTAKE_UNSUPPORTED_DISC_IMAGE",
                "LocIntake_UnknownOrUnsupported",
                SourceName);
        }

        if (IsStandaloneBinWithoutCue(context))
        {
            MediaInputDecision mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(context.InputPath);
            if (mediaDecision.IsBlocked)
            {
                return IntakeDecision.Blocked(
                    "INTAKE_BIN_WITHOUT_CUE_BLOCKED",
                    mediaDecision.MessageKey,
                    SourceName);
            }

            return IntakeDecision.Create(
                    IntakeDecisionAction.Convert,
                    mediaDecision.PlatformConfidence > 0 ? Math.Min(90, mediaDecision.PlatformConfidence) : 70,
                    CreateReason(
                        "INTAKE_BIN_WITHOUT_CUE_ACCEPTED",
                        string.IsNullOrWhiteSpace(mediaDecision.MessageKey)
                            ? "LocIntake_BinWithoutCueConsoleIdentified"
                            : mediaDecision.MessageKey,
                        IntakeDecisionSeverity.Info),
                    CreateSafeOutputHint())
                .WithWarning(CreateReason(
                    "INTAKE_BIN_WITHOUT_CUE_TEMP_CUE",
                    mediaDecision.WarningKey ?? "LocIntake_BinWithoutCueTemporaryCue",
                    IntakeDecisionSeverity.Warning));
        }

        if (context.IsDiscImage)
        {
            return IntakeDecision.Create(
                IntakeDecisionAction.Convert,
                90,
                CreateReason(
                    "INTAKE_DISC_IMAGE_CANDIDATE",
                    "LocIntake_DiscImageCandidate",
                    IntakeDecisionSeverity.Info),
                CreateSafeOutputHint());
        }

        if (context.IsChd)
        {
            return IntakeDecision.Create(
                IntakeDecisionAction.Unknown,
                90,
                CreateReason(
                    "INTAKE_CHD_INPUT_DETECTED",
                    "LocIntake_ChdInputDetected",
                    IntakeDecisionSeverity.Info),
                CreateSafeOutputHint());
        }

        return IntakeDecision.Unknown("LocIntake_UnknownOrUnsupported");
    }

    private static bool IsStandaloneBinWithoutCue(IntakeContext context)
    {
        if (!string.Equals(context.Extension, ".bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(context.InputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return true;
        }

        string sameBaseCue = Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(context.InputPath) + ".cue");

        return !File.Exists(sameBaseCue);
    }

    private static IntakeDecisionReason CreateReason(
        string code,
        string message,
        IntakeDecisionSeverity severity)
    {
        return new IntakeDecisionReason(
            code,
            message,
            severity,
            SourceName);
    }

    private static SafeOutputHint CreateSafeOutputHint()
    {
        return new SafeOutputHint(
            suggestedFileName: null,
            platform: null,
            region: null,
            discGroupKey: null,
            discIndex: null);
    }
}