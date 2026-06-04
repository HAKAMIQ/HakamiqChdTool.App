using System;

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