using HakamiqChdTool.App.Models;
using System;
using System.Globalization;
using System.Windows;

namespace HakamiqChdTool.App.Localization;

public static class ArabicUi
{
    private const string MissingArabicText = "النص غير متوفر.";
    private const string MissingEnglishText = "Text unavailable.";
    private const string SafeArabicDetail = "تعذر عرض التفاصيل. راجع السجل للمزيد من المعلومات.";
    private const string SafeEnglishDetail = "Details are unavailable. Check the log for more information.";

    public static string Get(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey))
        {
            return string.Empty;
        }

        if (Application.Current?.TryFindResource(resourceKey) is string s)
        {
            return s;
        }

        return IsLocalizationResourceKey(resourceKey)
            ? MissingText()
            : resourceKey;
    }

    public static string ToUserSafeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return SafeDetail();
        }

        string t = detail.Trim();

        if (Application.Current?.TryFindResource(t) is string fromResource)
        {
            return fromResource;
        }

        if (LegacyStatusLineLocalizer.TryResolveToResourceKey(t, out string localizedResourceKey))
        {
            return Get(localizedResourceKey);
        }

        if (IsLocalizationResourceKey(t))
        {
            return MissingText();
        }

        if (HasArabicLetters(t))
        {
            return IsCurrentUiRightToLeft()
                ? LegacyStatusLineLocalizer.StripEmbeddedTechnicalSuffix(t)
                : SafeDetail();
        }

        return SafeDetail();
    }

    public static string GetActionArabicLabel(string? actionCode)
    {
        return actionCode switch
        {
            TaskActionCodes.ConvertToChd => Get("LocAction_ConvertToChd_Arabic"),
            TaskActionCodes.RestoreDiscImageFromChd => Get("LocAction_ExtractFromChd_Arabic"),
            TaskActionCodes.VerifyChd => Get("LocAction_VerifyChd_Arabic"),
            TaskActionCodes.StageArchiveForConversion => Get("LocAction_ExtractArchiveThenProcess"),
            TaskActionCodes.Unsupported => Get("LocAction_Unsupported"),
            TaskActionCodes.PendingSelection => Get("LocAction_PendingSelection"),
            _ => string.IsNullOrWhiteSpace(actionCode) ? string.Empty : Get("LocAction_Unsupported"),
        };
    }

    public static string GetActionTechnicalToken(string? actionCode)
    {
        return actionCode switch
        {
            TaskActionCodes.ConvertToChd
                or TaskActionCodes.StageArchiveForConversion
                or TaskActionCodes.RestoreDiscImageFromChd
                or TaskActionCodes.VerifyChd => "CHD",
            _ => string.Empty,
        };
    }

    public static string QueueCardArabicOnly(string? english)
    {
        if (string.IsNullOrWhiteSpace(english) || english == "-")
        {
            return "-";
        }

        string t = english.Trim();

        if (Application.Current?.TryFindResource(t) is string fromDict)
        {
            return LegacyStatusLineLocalizer.StripEmbeddedTechnicalSuffix(fromDict);
        }

        if (LegacyStatusLineLocalizer.TryResolveToResourceKey(t, out string localizedResourceKey))
        {
            return LegacyStatusLineLocalizer.StripEmbeddedTechnicalSuffix(Get(localizedResourceKey));
        }

        if (IsLocalizationResourceKey(t))
        {
            return MissingText();
        }

        if (HasArabicLetters(t))
        {
            return IsCurrentUiRightToLeft() ? t : Get("LocStatus_GenericActivity");
        }

        return Get("LocStatus_GenericActivity");
    }

    public static string QueueCardTechnicalTokens(string? english)
    {
        return LegacyStatusLineLocalizer.ExtractTechnicalTokens(english);
    }

    public static string DisplayStatusDetail(string? english) =>
        QueueCardArabicOnly(english);

    public static string ResolveDisplayString(string? keyOrText)
    {
        if (string.IsNullOrWhiteSpace(keyOrText))
        {
            return Get(MainWindowUiKeys.Footer_IdleNoTasks);
        }

        string t = keyOrText.Trim();

        if (Application.Current?.TryFindResource(t) is string s)
        {
            return s;
        }

        if (LegacyStatusLineLocalizer.TryResolveToResourceKey(t, out string localizedResourceKey))
        {
            return Get(localizedResourceKey);
        }

        if (IsLocalizationResourceKey(t))
        {
            return MissingText();
        }

        if (HasArabicLetters(t) && !IsCurrentUiRightToLeft())
        {
            return Get("LocStatus_GenericActivity");
        }

        if (HasLatinLetters(t) && IsCurrentUiRightToLeft())
        {
            return Get("LocStatus_GenericActivity");
        }

        return t;
    }

    public static string ResolveExecutionLogLine(string? keyOrText)
    {
        if (string.IsNullOrWhiteSpace(keyOrText))
        {
            return string.Empty;
        }

        string t = keyOrText.Trim();

        if (Application.Current?.TryFindResource(t) is string s)
        {
            return s;
        }

        if (LegacyStatusLineLocalizer.TryResolveToResourceKey(t, out string localizedResourceKey))
        {
            return Get(localizedResourceKey);
        }

        if (IsLocalizationResourceKey(t))
        {
            return MissingText();
        }

        return BidiText.Mixed(t);
    }

    public static string Format(string formatResourceKey, params object?[] args) =>
        FormatText(Get(formatResourceKey), args);

    public static string FormatText(string formatText, params object?[] args)
    {
        if (string.IsNullOrEmpty(formatText))
        {
            return string.Empty;
        }

        if (args.Length == 0)
        {
            return formatText;
        }

        object?[] safeArgs = BuildSafeFormatArguments(args);

        return string.Format(CultureInfo.InvariantCulture, formatText, safeArgs);
    }

    public static string ProcessingPhaseHeadline(ProcessingState phase) =>
        phase switch
        {
            ProcessingState.Idle => Get("LocProcessingPhase_Idle"),
            ProcessingState.Queued => Get("LocProcessingPhase_Queued"),
            ProcessingState.AwaitingOperation => Get("LocProcessingPhase_AwaitingOperation"),
            ProcessingState.Processing => Get("LocProcessingPhase_Processing"),
            ProcessingState.Skipped => Get("LocProcessingPhase_Skipped"),
            ProcessingState.Completed => Get("LocProcessingPhase_Completed"),
            ProcessingState.Failed => Get("LocProcessingPhase_Failed"),
            _ => Get("LocProcessingPhase_Idle")
        };

    public static string QueueRowOperationalPhaseHeadline(
        string? currentState,
        IntegrityValidationState integrityState,
        string? statusDetail)
    {
        if (integrityState == IntegrityValidationState.Validating)
        {
            return Get("LocRowPhase_Scanning");
        }

        string s = string.IsNullOrEmpty(currentState) ? TaskQueueStateCodes.Pending : currentState;
        if (s == TaskQueueStateCodes.Ready)
        {
            s = TaskQueueStateCodes.Pending;
        }

        string? d = string.IsNullOrWhiteSpace(statusDetail) ? null : statusDetail.Trim();
        if (d is not null)
        {
            if (LegacyStatusLineLocalizer.IsKnownFinalizingOrCleanupDetail(d))
            {
                return QueueCardArabicOnly(d);
            }

            if (s == TaskQueueStateCodes.Verifying
                && LegacyStatusLineLocalizer.IsKnownVerificationCleanupDetail(d))
            {
                return Get("LocRowPhase_CleaningUp");
            }
        }

        return s switch
        {
            TaskQueueStateCodes.AwaitingOperationSelection => Get("LocRowPhase_ChooseOperation"),
            TaskQueueStateCodes.Pending => Get("LocRowPhase_Waiting"),
            TaskQueueStateCodes.ReadingFile => Get("LocRowPhase_Scanning"),
            TaskQueueStateCodes.Extracting => Get("LocRowPhase_Extracting"),
            TaskQueueStateCodes.Converting => Get("LocRowPhase_Converting"),
            TaskQueueStateCodes.Verifying => Get("LocRowPhase_Verifying"),
            TaskQueueStateCodes.Processing => Get("LocRowPhase_Processing"),
            TaskQueueStateCodes.Completed => Get("LocRowPhase_Completed"),
            TaskQueueStateCodes.Skipped => Get("LocRowPhase_Skipped"),
            TaskQueueStateCodes.Failed => Get("LocRowPhase_Failed"),
            TaskQueueStateCodes.PasswordRequired => Get("LocState_PasswordRequired"),
            TaskQueueStateCodes.Cancelled => Get("LocRowPhase_Cancelled"),
            _ => Get("LocRowPhase_Waiting")
        };
    }

    public static string IntegrityColumnDisplay(
        IntegrityValidationState state,
        string statusMessage,
        string? detailTooltip)
    {
        _ = detailTooltip;

        if (state == IntegrityValidationState.None)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(statusMessage) || string.Equals(statusMessage, "-", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string msg = statusMessage.Trim();

        if (msg.StartsWith("Loc", StringComparison.Ordinal) && Application.Current?.TryFindResource(msg) is string fromKey)
        {
            return IsolateForCurrentLanguage(fromKey);
        }

        if (IsLocalizationResourceKey(msg))
        {
            return IsolateForCurrentLanguage(MissingText());
        }

        if (HasArabicLetters(msg))
        {
            return IsolateForCurrentLanguage(IsCurrentUiRightToLeft() ? msg : Get("LocStatus_GenericActivity"));
        }

        string resolved = ResolveDisplayString(msg);
        if (!string.Equals(resolved, msg, StringComparison.Ordinal))
        {
            return IsolateForCurrentLanguage(resolved);
        }

        return IsolateForCurrentLanguage(QueueCardArabicOnly(msg));
    }

    private static object?[] BuildSafeFormatArguments(object?[] args)
    {
        if (!IsCurrentUiRightToLeft())
        {
            return args;
        }

        object?[] safeArgs = new object?[args.Length];

        for (int i = 0; i < args.Length; i++)
        {
            object? value = args[i];
            safeArgs[i] = value is null ? null : new BidirectionalFormatValue(value);
        }

        return safeArgs;
    }

    private static string IsolateDynamicValueForRightToLeftUi(string text)
    {
        if (string.IsNullOrEmpty(text) || !NeedsLeftToRightIsolation(text))
        {
            return text;
        }

        return "\u2066" + text + "\u2069";
    }

    private static bool NeedsLeftToRightIsolation(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or >= 'a' and <= 'z')
            {
                return true;
            }
        }

        return text.Contains('\\', StringComparison.Ordinal)
            || text.Contains('/', StringComparison.Ordinal)
            || text.Contains('.', StringComparison.Ordinal)
            || text.Contains(':', StringComparison.Ordinal)
            || text.Contains('%', StringComparison.Ordinal)
            || text.Contains('_', StringComparison.Ordinal)
            || text.Contains('-', StringComparison.Ordinal);
    }

    private sealed class BidirectionalFormatValue(object value) : IFormattable
    {
        private readonly object _value = value;

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            string text = _value switch
            {
                IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
                _ => _value.ToString() ?? string.Empty
            };

            return IsolateDynamicValueForRightToLeftUi(text);
        }

        public override string ToString()
        {
            return ToString(null, CultureInfo.InvariantCulture);
        }
    }

    private static bool HasArabicLetters(string s)
    {
        foreach (char c in s)
        {
            if (c is >= '\u0600' and <= '\u06FF'
                or >= '\u0750' and <= '\u077F'
                or >= '\u08A0' and <= '\u08FF')
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLatinLetters(string s)
    {
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLocalizationResourceKey(string value) =>
        value.StartsWith("Loc", StringComparison.Ordinal);

    private static string MissingText() =>
        IsCurrentUiRightToLeft() ? MissingArabicText : MissingEnglishText;

    private static string SafeDetail() =>
        IsCurrentUiRightToLeft() ? SafeArabicDetail : SafeEnglishDetail;

    private static bool IsCurrentUiRightToLeft()
    {
        if (Application.Current?.TryFindResource("App.FlowDirection") is FlowDirection flowDirection)
        {
            return flowDirection == FlowDirection.RightToLeft;
        }

        return AppLanguageService.IsRightToLeftLanguage(CultureInfo.CurrentUICulture.Name);
    }

    private static string IsolateForCurrentLanguage(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return IsCurrentUiRightToLeft()
            ? "\u2067" + s + "\u2069"
            : "\u2066" + s + "\u2069";
    }
}
