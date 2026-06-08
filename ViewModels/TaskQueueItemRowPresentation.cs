using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.Linq;
using MediaBrush = System.Windows.Media.Brush;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    public string DisplayFileName =>
        string.IsNullOrEmpty(FileName) ? string.Empty : "\u2066" + FileName + "\u2069";

    public string StatusText => OperationAwareStatePhaseArabic;

    public string StatusHeadlineIsolated => WrapDisplayText(OperationAwareStatePhaseArabic);

    public string QueueRowDisplayState => BuildQueueRowDisplayState();

    public string QueueRowDisplayFinalResult => BuildQueueRowDisplayFinalResultText();

    public string QueueRowDisplayDetailArabic => BuildQueueRowDisplayDetailArabic();

    public bool QueueRowDisplayDetailIsVisible => !IsSuppressedQueueRowDetail(QueueRowDisplayDetailArabic);

    public string QueueRowDisplayPhaseIsolated
    {
        get
        {
            string text = HasRuntimeProgressDetail && !string.IsNullOrWhiteSpace(RuntimeProgressPrimaryMessageKey)
                ? ArabicUi.ResolveDisplayString(RuntimeProgressPrimaryMessageKey)
                : BuildOperationAwareQueueRowPhase();

            return WrapDisplayText(text);
        }
    }
    private string OperationAwareStatePhaseArabic => StatePhaseArabic;

    private string BuildOperationAwareQueueRowPhase() =>
        ArabicUi.QueueRowOperationalPhaseHeadline(QueueRowDisplayState, IntegrityState, StatusDetail);

    public string QueueCardBadgeTextIsolated
    {
        get
        {
            string displayState = QueueRowDisplayState;
            string displayResultCode = BuildQueueRowDisplayFinalResultCode();

            if (displayResultCode == TaskFinalResultCodes.SkippedExists || displayState == TaskQueueStateCodes.Skipped)
            {
                return WrapDisplayText(ArabicUi.Get("LocRowPhase_Skipped"));
            }

            if (displayState == TaskQueueStateCodes.Completed)
            {
                return displayResultCode switch
                {
                    TaskFinalResultCodes.Healthy or TaskFinalResultCodes.Moved or TaskFinalResultCodes.Extracted =>
                        WrapDisplayText(ArabicUi.Get("LocRowPhase_Completed")),
                    _ => WrapDisplayText(ArabicUi.Get("LocRowPhase_Waiting"))
                };
            }

            if (displayState == TaskQueueStateCodes.Failed || displayState == TaskQueueStateCodes.PasswordRequired)
            {
                return WrapDisplayText(ArabicUi.Get("LocRowPhase_Failed"));
            }

            if (displayState == TaskQueueStateCodes.Cancelled)
            {
                return WrapDisplayText(ArabicUi.Get("LocRowPhase_Cancelled"));
            }

            if (displayState == TaskQueueStateCodes.AwaitingOperationSelection)
            {
                return WrapDisplayText(ArabicUi.Get("LocRowPhase_ChooseOperation"));
            }

            if (TaskQueueStateCodes.IsActiveRunning(displayState))
            {
                return WrapDisplayText(ArabicUi.Get("LocState_Processing"));
            }

            return WrapDisplayText(ArabicUi.Get("LocRowPhase_Waiting"));
        }
    }

    public string TaskActionShortLabel => RequestedAction switch
    {
        TaskActionCodes.ExtractArchiveThenProcess => ArabicUi.Get("LocQueue_ActionShortArchiveToChd"),
        TaskActionCodes.ConvertToChd => ArabicUi.GetActionArabicLabel(TaskActionCodes.ConvertToChd),
        TaskActionCodes.ExtractFromChd => ArabicUi.GetActionArabicLabel(TaskActionCodes.ExtractFromChd),
        TaskActionCodes.VerifyChd => ArabicUi.GetActionArabicLabel(TaskActionCodes.VerifyChd),
        _ => ArabicUi.GetActionArabicLabel(RequestedAction)
    };

    public string TaskActionIconGlyph => RequestedAction switch
    {
        TaskActionCodes.ExtractArchiveThenProcess => "\uE8B7",
        TaskActionCodes.ConvertToChd => "\uE895",
        TaskActionCodes.ExtractFromChd => "\uEDE1",
        TaskActionCodes.VerifyChd => "\uE73E",
        TaskActionCodes.PendingSelection => "\uE8FD",
        _ => "\uE711"
    };

    public string StateIconGlyph =>
        ProcessingPhase switch
        {
            ProcessingState.Processing => "\uE895",
            ProcessingState.Completed => "\uE73E",
            ProcessingState.Failed => CurrentState == TaskQueueStateCodes.PasswordRequired ? "\uE7BA" : "\uE711",
            ProcessingState.AwaitingOperation => "\uE70F",
            ProcessingState.Queued or ProcessingState.Idle => "\uE8FD",
            _ => "\uE8FD"
        };

    public string ResultStatusIconGlyph
    {
        get
        {
            if (!IsResultDetermined)
            {
                return "\uE8FD";
            }

            return FinalResult switch
            {
                TaskFinalResultCodes.Healthy or TaskFinalResultCodes.Moved or TaskFinalResultCodes.Extracted => "\uE73E",
                TaskFinalResultCodes.SkippedExists => "\uE7E8",
                TaskFinalResultCodes.Failed
                    or TaskFinalResultCodes.FailedConvert
                    or TaskFinalResultCodes.FailedVerify
                    or TaskFinalResultCodes.FailedExtract
                    or TaskFinalResultCodes.SourceUnreadable
                    or TaskFinalResultCodes.Cancelled => "\uE711",
                TaskFinalResultCodes.PasswordRequired => "\uE7BA",
                TaskFinalResultCodes.Unsupported => "\uE711",
                _ => "\uE946"
            };
        }
    }

    public string IntegrityGlyph => IntegrityState switch
    {
        IntegrityValidationState.None => "\u2014",
        IntegrityValidationState.Validating => "\uE895",
        IntegrityValidationState.Verified => "\uE73E",
        IntegrityValidationState.Failed or IntegrityValidationState.Error => "\uE711",
        IntegrityValidationState.NoDat or IntegrityValidationState.NoRedumpMatch => "\uE946",
        IntegrityValidationState.Unsupported => "\uE711",
        IntegrityValidationState.NoDirectRedump => "\uE946",
        _ => "\u2014"
    };

    public MediaBrush IntegrityForegroundBrush => IntegrityState switch
    {
        IntegrityValidationState.Verified => ResolveBrush("SuccessBadgeForegroundBrush", 207, 207, 207),
        IntegrityValidationState.Failed or IntegrityValidationState.Error => ResolveBrush("ErrorBadgeForegroundBrush", 196, 43, 28),
        IntegrityValidationState.NoDat or IntegrityValidationState.NoRedumpMatch => ResolveBrush("WarningBadgeForegroundBrush", 161, 92, 0),
        IntegrityValidationState.Unsupported => ResolveBrush("SecondaryTextBrush", 107, 114, 128),
        IntegrityValidationState.NoDirectRedump => ResolveBrush("WarningBadgeForegroundBrush", 161, 92, 0),
        IntegrityValidationState.Validating => ResolveBrush("AccentBrush", 0, 120, 212),
        _ => ResolveBrush("TertiaryTextBrush", 107, 114, 128)
    };

    public bool IsRedumpVerified => IntegrityState == IntegrityValidationState.Verified;

    public string IntegrityColumnDisplayArabic =>
        ArabicUi.IntegrityColumnDisplay(IntegrityState, IntegrityStatusMessage, IntegrityDetailTooltip);

    public bool HasIntegrityColumnDetail => !string.IsNullOrWhiteSpace(IntegrityDetailTooltip);

    public bool HasIntakeAdvisory => IntakeAdvisory is not null;

    public string IntakeAdvisoryActionDisplay => IntakeAdvisory is null
        ? string.Empty
        : FormatIntakeAdvisoryAction(IntakeAdvisory.Action);

    public string IntakeAdvisoryConfidenceDisplay => IntakeAdvisory is null
        ? string.Empty
        : $"{IntakeAdvisory.Confidence}%";

    public string IntakeAdvisoryPrimaryReasonDisplay
    {
        get
        {
            QueueIntakeAdvisory? advisory = IntakeAdvisory;
            return advisory is null
                ? string.Empty
                : BuildReleaseSafeIntakeAdvisorySummary(advisory);
        }
    }

    public string IntakeAdvisoryTooltip
    {
        get
        {
            QueueIntakeAdvisory? advisory = IntakeAdvisory;
            return advisory is null
                ? string.Empty
                : BuildReleaseSafeIntakeAdvisorySummary(advisory);
        }
    }

    public string QueueRowExtendedTooltip
    {
        get
        {
            string rowDetail = QueueRowDisplayDetailArabic;
            string advisoryText = IntakeAdvisoryTooltip;

            if (string.IsNullOrWhiteSpace(rowDetail))
            {
                return advisoryText;
            }

            if (string.IsNullOrWhiteSpace(advisoryText))
            {
                return rowDetail;
            }

            return rowDetail + Environment.NewLine + Environment.NewLine + advisoryText;
        }
    }

    private static string WrapDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = value.Trim();
        return AppLanguageService.IsRightToLeftLanguage(AppLanguageService.Instance.CurrentLanguageName)
            ? "\u2067" + text + "\u2069"
            : text;
    }

    private string BuildQueueRowDisplayState()
    {
        string resultCode = BuildQueueRowDisplayFinalResultCode();
        return resultCode switch
        {
            TaskFinalResultCodes.Healthy or TaskFinalResultCodes.Moved or TaskFinalResultCodes.Extracted => TaskQueueStateCodes.Completed,
            TaskFinalResultCodes.SkippedExists or TaskFinalResultCodes.Unsupported => TaskQueueStateCodes.Skipped,
            TaskFinalResultCodes.Cancelled => TaskQueueStateCodes.Cancelled,
            TaskFinalResultCodes.PasswordRequired => TaskQueueStateCodes.PasswordRequired,
            TaskFinalResultCodes.Failed
                or TaskFinalResultCodes.FailedConvert
                or TaskFinalResultCodes.FailedVerify
                or TaskFinalResultCodes.FailedExtract
                or TaskFinalResultCodes.SourceUnreadable => TaskQueueStateCodes.Failed,
            _ => NormalizeQueueStateCode(_currentState)
        };
    }

    private string BuildQueueRowDisplayFinalResultText()
    {
        string resultCode = BuildQueueRowDisplayFinalResultCode();

        return resultCode switch
        {
            TaskFinalResultCodes.None => string.Empty,
            TaskFinalResultCodes.SkippedExists => BuildSkippedOutputExistsDetail(),
            TaskFinalResultCodes.Healthy or TaskFinalResultCodes.Moved => BuildSuccessfulCompletionDetail(),
            TaskFinalResultCodes.Extracted => ArabicUi.Get("LocStatus_ExtractionCompletedSuccess"),
            TaskFinalResultCodes.Cancelled => ArabicUi.Get("LocStatus_UserCancelled"),
            TaskFinalResultCodes.PasswordRequired => ArabicUi.Get("LocState_PasswordRequired"),
            TaskFinalResultCodes.Unsupported => ArabicUi.Get("LocStatus_FileTypeUnsupportedStage"),
            TaskFinalResultCodes.SourceUnreadable => ArabicUi.Get("LocStatus_SourceUnreadableBlocked"),
            TaskFinalResultCodes.Failed
                or TaskFinalResultCodes.FailedConvert
                or TaskFinalResultCodes.FailedVerify
                or TaskFinalResultCodes.FailedExtract => ArabicUi.Get("LocRowPhase_Failed"),
            _ => ArabicUi.Get("LocStatus_GenericActivity")
        };
    }

    private string BuildQueueRowDisplayFinalResultCode()
    {
        if (IsOutputExistsStatusDetail(StatusDetail)
            || string.Equals(_finalResult, TaskFinalResultCodes.SkippedExists, StringComparison.Ordinal)
            || string.Equals(_currentState, TaskQueueStateCodes.Skipped, StringComparison.Ordinal))
        {
            return TaskFinalResultCodes.SkippedExists;
        }

        if (!string.IsNullOrWhiteSpace(_finalResult) && _finalResult != TaskFinalResultCodes.None)
        {
            return _finalResult;
        }

        if (string.Equals(_queueSnapshotStatus, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return TaskFinalResultCodes.Failed;
        }

        if (string.Equals(_queueSnapshotStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return TaskFinalResultCodes.Cancelled;
        }

        if (string.Equals(_queueSnapshotStatus, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return InferSuccessfulFinalResultFromRequestedAction();
        }

        return TaskFinalResultCodes.None;
    }

    private string BuildQueueRowDisplayDetailArabic()
    {
        string displayState = QueueRowDisplayState;
        string displayResultCode = BuildQueueRowDisplayFinalResultCode();

        return displayResultCode switch
        {
            TaskFinalResultCodes.SkippedExists => BuildSkippedOutputExistsDetail(),
            TaskFinalResultCodes.Healthy or TaskFinalResultCodes.Moved => BuildSuccessfulCompletionDetail(),
            TaskFinalResultCodes.Extracted => ArabicUi.Get("LocStatus_ExtractionCompletedSuccess"),
            TaskFinalResultCodes.Cancelled => ArabicUi.Get("LocStatus_UserCancelled"),
            TaskFinalResultCodes.SourceUnreadable =>
                string.IsNullOrWhiteSpace(StatusDetail) ? ArabicUi.Get("LocStatus_SourceUnreadableBlocked") : ArabicUi.QueueCardArabicOnly(ChdmanOutputParser.StripPercentTokensForNarrative(StatusDetail)),
            TaskFinalResultCodes.Failed or TaskFinalResultCodes.FailedConvert or TaskFinalResultCodes.FailedVerify or TaskFinalResultCodes.FailedExtract =>
                string.IsNullOrWhiteSpace(StatusDetail) ? ArabicUi.Get("LocRowPhase_Failed") : ArabicUi.QueueCardArabicOnly(ChdmanOutputParser.StripPercentTokensForNarrative(StatusDetail)),
            TaskFinalResultCodes.PasswordRequired =>
                string.IsNullOrWhiteSpace(StatusDetail) ? ArabicUi.Get("LocRowPhase_Failed") : ArabicUi.QueueCardArabicOnly(ChdmanOutputParser.StripPercentTokensForNarrative(StatusDetail)),
            _ when TaskQueueStateCodes.IsWaiting(displayState) => ArabicUi.Get("LocRowPhase_Waiting"),
            _ when TaskQueueStateCodes.IsActiveRunning(displayState) => BuildActiveRunningDetailArabic(displayState),
            _ when string.IsNullOrWhiteSpace(StatusDetail) || StatusDetailDisplay == "-" => "-",
            _ => ArabicUi.QueueCardArabicOnly(ChdmanOutputParser.StripPercentTokensForNarrative(StatusDetail))
        };
    }

    private string BuildActiveRunningDetailArabic(string displayState)
    {
        if (HasRuntimeProgressDetail)
        {
            string runtimeDetail = RuntimeProgressDetailArabic;
            return string.IsNullOrWhiteSpace(runtimeDetail) ? "-" : runtimeDetail;
        }

        if (string.IsNullOrWhiteSpace(StatusDetail) || StatusDetailDisplay == "-")
        {
            return "-";
        }

        string phase = ArabicUi.QueueRowOperationalPhaseHeadline(displayState, IntegrityState, StatusDetail);
        string detail = ArabicUi.QueueCardArabicOnly(ChdmanOutputParser.StripPercentTokensForNarrative(StatusDetail));

        if (IsSuppressedQueueRowDetail(detail) || AreSameQueueUiText(detail, phase))
        {
            return "-";
        }

        return detail;
    }

    private static bool IsSuppressedQueueRowDetail(string? detail)
    {
        string normalized = NormalizeQueueUiText(detail);
        if (normalized.Length == 0 || normalized == "-")
        {
            return true;
        }

        string[] genericOperationalLines =
        [
            ArabicUi.Get("LocStatus_GenericActivity"),
            ArabicUi.Get("LocState_Processing"),
            ArabicUi.Get("LocRowPhase_Processing"),
            ArabicUi.Get("LocRowPhase_Converting"),
            ArabicUi.Get("LocRowPhase_Verifying"),
            ArabicUi.Get("LocRowPhase_Extracting"),
            ArabicUi.Get("LocRowPhase_Scanning"),
            ArabicUi.Get("LocRowPhase_CleaningUp")
        ];

        return genericOperationalLines.Any(line => AreSameQueueUiText(normalized, line));
    }

    private static bool AreSameQueueUiText(string? left, string? right) =>
        string.Equals(NormalizeQueueUiText(left), NormalizeQueueUiText(right), StringComparison.Ordinal);

    private static string NormalizeQueueUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        bool pendingSpace = false;

        foreach (char character in value)
        {
            if (character is '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u200E' or '\u200F')
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(character);
            pendingSpace = false;
        }

        return builder.ToString().Trim();
    }

    private string BuildSuccessfulCompletionDetail()
    {
        if (IsVerifyChdMetadataRun()
            || string.Equals(_queueItemMode, "Verify", StringComparison.OrdinalIgnoreCase)
            || RequestedAction == TaskActionCodes.VerifyChd)
        {
            return ArabicUi.Get("LocStatus_VerifyCompletedSuccess");
        }

        if (RequestedAction == TaskActionCodes.StageArchiveForConversion
            || RequestedAction == TaskActionCodes.ExtractArchiveThenProcess)
        {
            return ArabicUi.Get("LocStatus_ArchiveConversionCompletedSuccess");
        }

        if (RequestedAction == TaskActionCodes.RestoreDiscImageFromChd
            || RequestedAction == TaskActionCodes.ExtractFromChd)
        {
            return ArabicUi.Get("LocStatus_ExtractionCompletedSuccess");
        }

        return ArabicUi.Get("LocStatus_ConversionCompletedSuccess");
    }

    private string BuildSkippedOutputExistsDetail()
    {
        if (string.Equals(StatusDetail, MainWindowMessages.StatusDetail_OutputFileExists, StringComparison.Ordinal))
        {
            return ArabicUi.Get("LocStatus_OutputFileExists");
        }

        return ArabicUi.Get("LocStatus_FinalOutputExists");
    }

    private string InferSuccessfulFinalResultFromRequestedAction()
    {
        if (RequestedAction == TaskActionCodes.RestoreDiscImageFromChd
            || RequestedAction == TaskActionCodes.ExtractFromChd)
        {
            return TaskFinalResultCodes.Extracted;
        }

        return TaskFinalResultCodes.Healthy;
    }

    private static string NormalizeQueueStateCode(string? state)
    {
        if (string.IsNullOrEmpty(state) || state == TaskQueueStateCodes.Ready)
        {
            return TaskQueueStateCodes.Pending;
        }

        return state;
    }

    private static bool IsOutputExistsStatusDetail(string? statusDetail)
    {
        if (string.IsNullOrWhiteSpace(statusDetail))
        {
            return false;
        }

        string text = statusDetail.Trim();
        return string.Equals(text, MainWindowMessages.StatusDetail_OutputFileExists, StringComparison.Ordinal)
            || string.Equals(text, MainWindowMessages.StatusDetail_FinalOutputExists, StringComparison.Ordinal)
            || text.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || text.Contains("output exists", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Output file exists", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ملف CHD الناتج موجود", StringComparison.Ordinal)
            || text.Contains("بنفس الاسم", StringComparison.Ordinal);
    }

    private bool IsVerifyChdMetadataRun() =>
        IsChdExtension(Path.GetExtension(SourcePath))
        && string.Equals(_queueItemMode, "Verify", StringComparison.OrdinalIgnoreCase);

    private static bool IsChdExtension(string extension) =>
        extension.Equals(".chd", StringComparison.OrdinalIgnoreCase);

    private static string BuildReleaseSafeIntakeAdvisorySummary(QueueIntakeAdvisory advisory)
    {
        string reasonCode = advisory.Reasons.FirstOrDefault()?.Code ?? string.Empty;

        if (string.Equals(reasonCode, "INTAKE_ARCHIVE_EXTRACT_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return ArabicUi.Get("LocIntakeAdvisory_ArchiveExtractRequired");
        }

        if (string.Equals(reasonCode, "INTAKE_DISC_IMAGE_CANDIDATE", StringComparison.OrdinalIgnoreCase))
        {
            return ArabicUi.Get("LocIntakeAdvisory_DiscImageCandidate");
        }

        if (string.Equals(reasonCode, "INTAKE_CHD_INPUT_DETECTED", StringComparison.OrdinalIgnoreCase))
        {
            return ArabicUi.Get("LocIntakeAdvisory_ChdInputDetected");
        }

        if (advisory.IsBlocked)
        {
            return ArabicUi.Get("LocIntakeAdvisory_Blocked");
        }

        if (advisory.HasWarnings)
        {
            return ArabicUi.Get("LocIntakeAdvisory_Warning");
        }

        return advisory.Action switch
        {
            QueueIntakeAdvisoryAction.Extract => ArabicUi.Get("LocIntakeAdvisory_Extract"),
            QueueIntakeAdvisoryAction.Convert => ArabicUi.Get("LocIntakeAdvisory_Convert"),
            QueueIntakeAdvisoryAction.Warn => ArabicUi.Get("LocIntakeAdvisory_Warning"),
            QueueIntakeAdvisoryAction.Block => ArabicUi.Get("LocIntakeAdvisory_Blocked"),
            _ => string.Empty
        };
    }

    private static string FormatIntakeAdvisoryAction(QueueIntakeAdvisoryAction action)
    {
        return action switch
        {
            QueueIntakeAdvisoryAction.Extract => ArabicUi.Get("LocIntakeAdvisoryAction_Extract"),
            QueueIntakeAdvisoryAction.Convert => ArabicUi.Get("LocIntakeAdvisoryAction_Convert"),
            QueueIntakeAdvisoryAction.Block => ArabicUi.Get("LocIntakeAdvisoryAction_Block"),
            QueueIntakeAdvisoryAction.ReportOnly => ArabicUi.Get("LocIntakeAdvisoryAction_ReportOnly"),
            QueueIntakeAdvisoryAction.Unknown => ArabicUi.Get("LocIntakeAdvisoryAction_Unknown"),
            _ => string.Empty
        };
    }
}
