using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Services.Safety;

public static class InputSafetyAdvisoryProjector
{
    private const string SafetyPlatform = "InputSafety";

    public static IReadOnlyDictionary<string, QueueIntakeAdvisory> ProjectBySource(
        InputSafetyScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        var reasonsBySource = new Dictionary<string, List<QueueIntakeAdvisoryReason>>(StringComparer.OrdinalIgnoreCase);
        var warningsBySource = new Dictionary<string, List<QueueIntakeAdvisoryReason>>(StringComparer.OrdinalIgnoreCase);

        foreach (SuspiciousArtifact artifact in scanResult.Artifacts)
        {
            string sourcePath = NormalizeSourcePath(artifact.SourcePath);
            if (sourcePath.Length == 0)
            {
                continue;
            }

            QueueIntakeAdvisoryReason reason = ToReason(artifact);

            Dictionary<string, List<QueueIntakeAdvisoryReason>> target =
                artifact.IsBlocking || artifact.Severity < QueueIntakeAdvisorySeverity.Warning
                    ? reasonsBySource
                    : warningsBySource;

            if (!target.TryGetValue(sourcePath, out List<QueueIntakeAdvisoryReason>? list))
            {
                list = [];
                target[sourcePath] = list;
            }

            AddDistinctReason(list, reason);
        }

        var result = new Dictionary<string, QueueIntakeAdvisory>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourcePath in reasonsBySource.Keys
            .Concat(warningsBySource.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            QueueIntakeAdvisoryReason[] reasons = reasonsBySource.TryGetValue(sourcePath, out List<QueueIntakeAdvisoryReason>? sourceReasons)
                ? [.. sourceReasons]
                : [];

            QueueIntakeAdvisoryReason[] warnings = warningsBySource.TryGetValue(sourcePath, out List<QueueIntakeAdvisoryReason>? sourceWarnings)
                ? [.. sourceWarnings]
                : [];

            result[sourcePath] = QueueIntakeAdvisory.Empty with
            {
                Action = ProjectAction(reasons, warnings),
                Platform = SafetyPlatform,
                Reasons = reasons,
                Warnings = warnings
            };
        }

        return result;
    }

    public static QueueIntakeAdvisory Merge(
        QueueIntakeAdvisory? intakeAdvisory,
        QueueIntakeAdvisory? safetyAdvisory)
    {
        if (IsEmpty(safetyAdvisory))
        {
            return intakeAdvisory ?? QueueIntakeAdvisory.Empty;
        }

        if (IsEmpty(intakeAdvisory))
        {
            return safetyAdvisory ?? QueueIntakeAdvisory.Empty;
        }

        QueueIntakeAdvisoryReason[] reasons = MergeReasons(
            intakeAdvisory!.Reasons,
            safetyAdvisory!.Reasons);

        QueueIntakeAdvisoryReason[] warnings = MergeReasons(
            intakeAdvisory.Warnings,
            safetyAdvisory.Warnings);

        return intakeAdvisory with
        {
            Action = MergeAction(intakeAdvisory, safetyAdvisory, reasons, warnings),
            Platform = string.IsNullOrWhiteSpace(intakeAdvisory.Platform)
                ? safetyAdvisory.Platform
                : intakeAdvisory.Platform,
            Reasons = reasons,
            Warnings = warnings
        };
    }

    private static QueueIntakeAdvisoryAction MergeAction(
        QueueIntakeAdvisory intakeAdvisory,
        QueueIntakeAdvisory safetyAdvisory,
        IReadOnlyList<QueueIntakeAdvisoryReason> reasons,
        IReadOnlyList<QueueIntakeAdvisoryReason> warnings)
    {
        if (reasons.Any(static reason => reason.Severity == QueueIntakeAdvisorySeverity.Blocker)
            || intakeAdvisory.Action == QueueIntakeAdvisoryAction.Block
            || safetyAdvisory.Action == QueueIntakeAdvisoryAction.Block)
        {
            return QueueIntakeAdvisoryAction.Block;
        }

        if (intakeAdvisory.Action != QueueIntakeAdvisoryAction.Unknown)
        {
            return intakeAdvisory.Action;
        }

        return ProjectAction(reasons, warnings);
    }

    private static QueueIntakeAdvisoryAction ProjectAction(
        IReadOnlyList<QueueIntakeAdvisoryReason> reasons,
        IReadOnlyList<QueueIntakeAdvisoryReason> warnings)
    {
        if (reasons.Any(static reason => reason.Severity == QueueIntakeAdvisorySeverity.Blocker))
        {
            return QueueIntakeAdvisoryAction.Block;
        }

        if (warnings.Count > 0
            || reasons.Any(static reason => reason.Severity >= QueueIntakeAdvisorySeverity.Warning))
        {
            return QueueIntakeAdvisoryAction.Warn;
        }

        return reasons.Count > 0
            ? QueueIntakeAdvisoryAction.ReportOnly
            : QueueIntakeAdvisoryAction.Unknown;
    }

    private static QueueIntakeAdvisoryReason ToReason(SuspiciousArtifact artifact)
    {
        string kind = artifact.Kind.ToString();

        string code = string.IsNullOrWhiteSpace(kind)
            ? "INPUT_SAFETY_FINDING"
            : "INPUT_SAFETY_" + kind.ToUpperInvariant();

        string message = string.IsNullOrWhiteSpace(artifact.MessageResourceKey)
            ? artifact.IsBlocking
                ? "LocIntakeAdvisory_Blocked"
                : "LocIntakeAdvisory_Warning"
            : artifact.MessageResourceKey.Trim();

        return new QueueIntakeAdvisoryReason(
            code,
            message,
            artifact.Severity,
            artifact.SourcePath);
    }

    private static QueueIntakeAdvisoryReason[] MergeReasons(
        IEnumerable<QueueIntakeAdvisoryReason>? first,
        IEnumerable<QueueIntakeAdvisoryReason>? second)
    {
        var result = new List<QueueIntakeAdvisoryReason>();

        foreach (QueueIntakeAdvisoryReason reason in EnumerateReasons(first).Concat(EnumerateReasons(second)))
        {
            AddDistinctReason(result, reason);
        }

        return [.. result];
    }

    private static IEnumerable<QueueIntakeAdvisoryReason> EnumerateReasons(
        IEnumerable<QueueIntakeAdvisoryReason>? reasons)
    {
        if (reasons is null)
        {
            yield break;
        }

        foreach (QueueIntakeAdvisoryReason reason in reasons)
        {
            if (!string.IsNullOrWhiteSpace(reason.Code)
                || !string.IsNullOrWhiteSpace(reason.Message))
            {
                yield return reason;
            }
        }
    }

    private static void AddDistinctReason(
        List<QueueIntakeAdvisoryReason> reasons,
        QueueIntakeAdvisoryReason candidate)
    {
        if (reasons.Any(existing =>
            string.Equals(existing.Code, candidate.Code, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Message, candidate.Message, StringComparison.Ordinal)
            && string.Equals(existing.Source, candidate.Source, StringComparison.OrdinalIgnoreCase)
            && existing.Severity == candidate.Severity))
        {
            return;
        }

        reasons.Add(candidate);
    }

    private static bool IsEmpty(QueueIntakeAdvisory? advisory)
    {
        return advisory is null
            || (advisory.Action == QueueIntakeAdvisoryAction.Unknown
                && advisory.Reasons.Count == 0
                && advisory.Warnings.Count == 0);
    }

    private static string NormalizeSourcePath(string? sourcePath)
    {
        return string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : sourcePath.Trim();
    }
}