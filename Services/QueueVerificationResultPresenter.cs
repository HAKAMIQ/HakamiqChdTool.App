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
    ChdProbeReportView? ChdLogicalReport)
{
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

        string scope = BuildVerificationScopeText(integrityState);
        string detail = ResolvePrimaryDetail(
            isVerificationReport: true,
            integrityStatusMessage,
            queueRowDisplayDetailArabic);

        string message = BuildVerificationResultMessage(
            fileDisplay,
            status,
            scope,
            detail);

        return new QueueVerifyView(
            ArabicUi.Get("LocQueue_VerificationResultDialogTitle"),
            message,
            chdLogicalReport);
    }


    private static string BuildVerificationResultMessage(
        string fileDisplay,
        string status,
        string scope,
        string detail)
    {
        var sections = new List<string>
        {
            ArabicUi.Format("LocQueue_VerificationResultFileLine", fileDisplay),
            ArabicUi.Format("LocQueue_VerificationResultStatusLine", status),
            scope,
            detail
        };


        sections.Add(ArabicUi.Get("LocQueue_VerificationResultPlayableCaveat"));

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
