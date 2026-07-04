using HakamiqChdTool.App.Localization;
using System;
using System.Buffers;
using System.Text;

namespace HakamiqChdTool.App.Services;

public static class RuntimeDiagnosticFormatter
{
    private const string RuntimeStageKey = "LocRuntime_StageRuntime";
    private const string UnexpectedErrorKey = "LocRuntime_UnexpectedError";
    private const string LogsLabelKey = "LocRuntime_LogsLabel";
    private const string UserSafeErrorDetailWithTypeKey = "LocRuntime_UserSafeErrorDetailWithType";
    private const string DiskSpaceBlockerMessageKey = "LocWorkflowPreflight_DiskSpaceBlockerFormat";

    private static readonly char[] LineSeparators = ['\r', '\n'];
    private static readonly SearchValues<char> ResourceKeyInvalidCharacters =
        SearchValues.Create(" \t:.,;");

    public static string SummarizeException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        Exception leaf = GetInnermost(ex);
        if (leaf is DiskPreflightException diskPreflightException)
        {
            return FormatDiskPreflightException(diskPreflightException);
        }

        string rawMessage = leaf.Message?.Trim() ?? string.Empty;

        if (TryResolveResourceMessage(rawMessage, out string localizedMessage))
        {
            return localizedMessage;
        }

        string userSafeDetail = ArabicUi.ToUserSafeDetail(rawMessage);
        if (!string.IsNullOrWhiteSpace(userSafeDetail) && !IsGenericSafeDetail(userSafeDetail))
        {
            return userSafeDetail;
        }

        return ArabicUi.Format(UserSafeErrorDetailWithTypeKey, leaf.GetType().Name);
    }

    public static string BuildCrashDialogMessage(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        string summary = SummarizeException(ex);
        string logsPath = AppPaths.LogsDirectory;

        var builder = new StringBuilder();
        builder.AppendLine(ArabicUi.ResolveDisplayString(UnexpectedErrorKey));
        builder.AppendLine();
        builder.AppendLine(summary);
        builder.AppendLine();
        builder.Append(ArabicUi.ResolveDisplayString(LogsLabelKey));
        builder.Append(' ');
        builder.Append(BidiText.Path(logsPath));

        return builder.ToString();
    }

    public static string MergeStageWithError(string stage, string? detail)
    {
        string stageText = ResolveKeyOrText(stage).Trim();
        if (string.IsNullOrWhiteSpace(stageText))
        {
            stageText = ArabicUi.ResolveDisplayString(RuntimeStageKey);
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            return stageText;
        }

        string detailText = ResolveKeyOrText(detail.Trim()).Trim();
        if (string.IsNullOrWhiteSpace(detailText))
        {
            return stageText;
        }

        if (detailText.StartsWith(stageText + ":", StringComparison.OrdinalIgnoreCase))
        {
            return detailText;
        }

        return $"{stageText}: {detailText}";
    }

    private static string FormatDiskPreflightException(DiskPreflightException ex)
    {
        DiskPreflightResult result = ex.Result;
        return ArabicUi.Format(
            DiskSpaceBlockerMessageKey,
            string.IsNullOrWhiteSpace(result.TargetRoot) ? "?" : result.TargetRoot,
            DiskSpacePreflightService.FormatBytes(result.EstimatedRequiredBytes),
            DiskSpacePreflightService.FormatBytes(result.AvailableFreeBytes));
    }

    private static string ResolveKeyOrText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        return LooksLikeResourceKey(trimmed)
            ? ArabicUi.ResolveDisplayString(trimmed)
            : trimmed;
    }


    private static bool IsGenericSafeDetail(string value)
    {
        string currentGenericDetail = ArabicUi.ToUserSafeDetail(string.Empty).Trim();
        return string.Equals(value.Trim(), currentGenericDetail, StringComparison.Ordinal);
    }

    private static bool TryResolveResourceMessage(string message, out string localizedMessage)
    {
        localizedMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string firstLine = GetFirstNonEmptyLine(message);
        if (!LooksLikeResourceKey(firstLine))
        {
            return false;
        }

        string resolved = ArabicUi.ResolveDisplayString(firstLine).Trim();
        if (string.IsNullOrWhiteSpace(resolved)
            || string.Equals(resolved, firstLine, StringComparison.Ordinal))
        {
            return false;
        }

        localizedMessage = resolved;
        return true;
    }

    private static string GetFirstNonEmptyLine(string value)
    {
        foreach (string line in value.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeResourceKey(string value) =>
        value.StartsWith("Loc", StringComparison.Ordinal)
        && value.AsSpan().IndexOfAny(ResourceKeyInvalidCharacters) < 0;

    private static Exception GetInnermost(Exception ex)
    {
        while (ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }
}