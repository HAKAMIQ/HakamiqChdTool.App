using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed record QueueVerifyView(
    string Title,
    string Message,
    string FileLabel,
    string FileName,
    string StatusLabel,
    string Status,
    string DetailsTitle,
    string Details,
    string CaveatTitle,
    string Caveat,
    string FailureActionTitle,
    string FailureAction,
    ChdProbeReportView? ChdLogicalReport)
{
    public bool HasCaveat => !string.IsNullOrWhiteSpace(Caveat);

    public bool HasFailureAction => !string.IsNullOrWhiteSpace(FailureAction);

    public bool HasChdLogicalReport => ChdLogicalReport?.HasMetrics == true;
}

public static class QueueVerificationResultPresenter
{
    public static string BuildOperationLogDisplay(bool hasLogPath, string logPathDisplay)
    {
        return hasLogPath
            ? ArabicUi.Format("LocQueue_OperationLogDisplay", logPathDisplay)
            : string.Empty;
    }

    public static bool IsVerificationReport(
        string requestedAction,
        string finalResult,
        IntegrityValidationState integrityState)
    {
        return string.Equals(requestedAction, TaskActionCodes.VerifyChd, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.FailedVerify, StringComparison.Ordinal)
            || integrityState != IntegrityValidationState.None;
    }

    public static string BuildOperationReportTitle(bool isVerificationReport)
    {
        return ArabicUi.Get(isVerificationReport
            ? "LocQueue_VerificationReportTitle"
            : "LocQueue_OperationReportTitle");
    }

    public static string BuildOperationReportMessage(
        bool isVerificationReport,
        string? integrityStatusMessage,
        string queueRowDisplayDetailArabic,
        string operationLogDisplay)
    {
        string primary = ResolvePrimaryDetail(
            isVerificationReport,
            integrityStatusMessage,
            queueRowDisplayDetailArabic);

        if (!isVerificationReport && !string.IsNullOrWhiteSpace(operationLogDisplay))
        {
            return string.IsNullOrWhiteSpace(primary)
                ? operationLogDisplay
                : primary + Environment.NewLine + operationLogDisplay;
        }

        return primary;
    }

    public static bool HasVerificationResult(
        bool isVerificationReport,
        string? logPath)
    {
        return isVerificationReport || IsVerificationLogPath(logPath);
    }

    public static string BuildVerificationResultBadgeText(
        IntegrityValidationState integrityState,
        string finalResult,
        bool isVerificationReport,
        string? logPath)
    {
        if (integrityState == IntegrityValidationState.Verified)
        {
            return ArabicUi.Get("LocQueue_VerificationBadgeRedumpMatched");
        }

        if (integrityState is IntegrityValidationState.Failed or IntegrityValidationState.Error)
        {
            return ArabicUi.Get("LocQueue_VerificationBadgeMismatch");
        }

        if (integrityState == IntegrityValidationState.NoRedumpMatch)
        {
            return ArabicUi.Get("LocDeepHash_StatusModified");
        }

        if (integrityState is IntegrityValidationState.NoDat or IntegrityValidationState.NoDirectRedump)
        {
            return ArabicUi.Get("LocDeepHash_StatusNoDatabase");
        }

        if (string.Equals(finalResult, TaskFinalResultCodes.FailedVerify, StringComparison.Ordinal)
            || integrityState == IntegrityValidationState.Unsupported)
        {
            return ArabicUi.Get("LocQueue_VerificationBadgeInvalid");
        }

        if (isVerificationReport || IsVerificationLogPath(logPath))
        {
            return ArabicUi.Get("LocQueue_VerificationBadgeInternallyValid");
        }

        return string.Empty;
    }

    public static QueueVerifyView BuildVerifyView(
        string? fileName,
        string fileTitleDisplay,
        string verificationResultBadgeText,
        IntegrityValidationState integrityState,
        string? integrityStatusMessage,
        string queueRowDisplayDetailArabic,
        ChdProbeReportView? chdLogicalReport)
    {
        string status = string.IsNullOrWhiteSpace(verificationResultBadgeText)
            ? queueRowDisplayDetailArabic
            : verificationResultBadgeText;

        string fileDisplay = string.IsNullOrWhiteSpace(fileName)
            ? fileTitleDisplay
            : fileName.Trim();

        bool isFailure = IsFailureResult(integrityState, status);

        string details = BuildVerificationDetailsText(
            isFailure,
            integrityState,
            integrityStatusMessage,
            queueRowDisplayDetailArabic);

        string caveat = isFailure
            ? string.Empty
            : ArabicUi.Get("LocQueue_VerificationResultPlayableCaveat");

        string failureAction = isFailure
            ? ArabicUi.Get("LocQueue_VerificationResultFailureAction")
            : string.Empty;

        string message = BuildVerificationResultMessage(
            fileDisplay,
            status,
            details,
            caveat,
            failureAction);

        return new QueueVerifyView(
            ArabicUi.Get("LocQueue_VerificationResultDialogTitle"),
            message,
            ArabicUi.Get("LocQueue_VerificationResultFileLabel"),
            fileDisplay,
            ArabicUi.Get("LocQueue_VerificationResultStatusLabel"),
            status,
            ArabicUi.Get("LocQueue_VerificationResultDetailsTitle"),
            details,
            ArabicUi.Get("LocQueue_VerificationResultWarningTitle"),
            caveat,
            ArabicUi.Get("LocQueue_VerificationResultActionTitle"),
            failureAction,
            chdLogicalReport);
    }

    private static bool IsFailureResult(
        IntegrityValidationState integrityState,
        string status)
    {
        if (integrityState is IntegrityValidationState.Failed
            or IntegrityValidationState.Error
            or IntegrityValidationState.Unsupported)
        {
            return true;
        }

        string invalid = ArabicUi.Get("LocQueue_VerificationBadgeInvalid");
        string mismatch = ArabicUi.Get("LocQueue_VerificationBadgeMismatch");

        return string.Equals(status, invalid, StringComparison.Ordinal)
            || string.Equals(status, mismatch, StringComparison.Ordinal);
    }

    private static string BuildVerificationDetailsText(
        bool isFailure,
        IntegrityValidationState integrityState,
        string? integrityStatusMessage,
        string queueRowDisplayDetailArabic)
    {
        if (isFailure)
        {
            return ArabicUi.Get("LocQueue_VerificationResultFailureDetails");
        }

        if (integrityState == IntegrityValidationState.Verified)
        {
            return ArabicUi.Get("LocQueue_VerificationResultScopeRedumpMatched");
        }

        if (integrityState is IntegrityValidationState.NoRedumpMatch
            or IntegrityValidationState.NoDat
            or IntegrityValidationState.NoDirectRedump
            or IntegrityValidationState.Unsupported)
        {
            return BuildVerificationScopeText(integrityState);
        }

        return ArabicUi.Get("LocQueue_VerificationResultInternalDetails");
    }

    private static string BuildVerificationResultMessage(
        string fileDisplay,
        string status,
        string details,
        string caveat,
        string failureAction)
    {
        var sections = new List<string>
        {
            ArabicUi.Format("LocQueue_VerificationResultFileLine", fileDisplay),
            ArabicUi.Format("LocQueue_VerificationResultStatusLine", status),
            details
        };

        if (!string.IsNullOrWhiteSpace(caveat))
        {
            sections.Add(caveat);
        }

        if (!string.IsNullOrWhiteSpace(failureAction))
        {
            sections.Add(failureAction);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string ResolvePrimaryDetail(
        bool isVerificationReport,
        string? integrityStatusMessage,
        string queueRowDisplayDetailArabic)
    {
        if (isVerificationReport
            && !string.IsNullOrWhiteSpace(integrityStatusMessage)
            && !string.Equals(integrityStatusMessage, "-", StringComparison.Ordinal))
        {
            return integrityStatusMessage;
        }

        return queueRowDisplayDetailArabic;
    }

    private static string BuildVerificationScopeText(IntegrityValidationState integrityState)
    {
        return integrityState switch
        {
            IntegrityValidationState.Verified => ArabicUi.Get("LocQueue_VerificationResultScopeRedumpMatched"),
            IntegrityValidationState.Failed or IntegrityValidationState.Error => ArabicUi.Get("LocQueue_VerificationResultScopeMismatch"),
            IntegrityValidationState.NoRedumpMatch => ArabicUi.Get("LocQueue_VerificationResultScopeNoRedumpMatch"),
            IntegrityValidationState.NoDat or IntegrityValidationState.NoDirectRedump => ArabicUi.Get("LocQueue_VerificationResultScopeNoDat"),
            IntegrityValidationState.Unsupported => ArabicUi.Get("LocQueue_VerificationResultScopeUnsupported"),
            _ => ArabicUi.Get("LocQueue_VerificationResultScopeInternalOnly")
        };
    }

    private static bool IsVerificationLogPath(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return false;
        }

        string fileName = Path.GetFileName(logPath.Trim());
        return fileName.StartsWith("verify_", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("info_", StringComparison.OrdinalIgnoreCase);
    }
}
