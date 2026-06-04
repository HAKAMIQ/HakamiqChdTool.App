using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HakamiqChdTool.App.Services.Safety;

public static class InputSafetyAdvisoryProjector
{
    public static IReadOnlyDictionary<string, QueueIntakeAdvisory> ProjectBySource(
        InputSafetyScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        var advisories = new Dictionary<string, QueueIntakeAdvisory>(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, SuspiciousArtifact> group in scanResult.Artifacts
            .GroupBy(static artifact => SafetyPathPolicy.NormalizeForAdvisoryKey(artifact.SourcePath), StringComparer.OrdinalIgnoreCase))
        {
            SuspiciousArtifact[] artifacts = [.. group];
            bool hasBlocker = artifacts.Any(static artifact => artifact.IsBlocking);

            IReadOnlyList<QueueIntakeAdvisoryReason> reasons = ProjectReasons(
                artifacts.Where(static artifact => artifact.IsBlocking));
            IReadOnlyList<QueueIntakeAdvisoryReason> warnings = ProjectReasons(
                artifacts.Where(static artifact => !artifact.IsBlocking));

            advisories[group.Key] = new QueueIntakeAdvisory(
                hasBlocker ? QueueIntakeAdvisoryAction.Block : QueueIntakeAdvisoryAction.Warn,
                100,
                false,
                reasons,
                warnings,
                null,
                null,
                null,
                null,
                null);
        }

        return advisories;
    }

    public static QueueIntakeAdvisory Merge(
        QueueIntakeAdvisory? intakeAdvisory,
        QueueIntakeAdvisory? safetyAdvisory)
    {
        if (intakeAdvisory is null)
        {
            return safetyAdvisory ?? QueueIntakeAdvisory.Empty;
        }

        if (safetyAdvisory is null)
        {
            return intakeAdvisory;
        }

        QueueIntakeAdvisoryAction action = safetyAdvisory.IsBlocked
            ? QueueIntakeAdvisoryAction.Block
            : intakeAdvisory.Action == QueueIntakeAdvisoryAction.Unknown
                ? safetyAdvisory.Action
                : intakeAdvisory.Action;

        return intakeAdvisory with
        {
            Action = action,
            Confidence = Math.Max(intakeAdvisory.Confidence, safetyAdvisory.Confidence),
            Reasons = [.. intakeAdvisory.Reasons.Concat(safetyAdvisory.Reasons)],
            Warnings = [.. intakeAdvisory.Warnings.Concat(safetyAdvisory.Warnings)]
        };
    }

    private static IReadOnlyList<QueueIntakeAdvisoryReason> ProjectReasons(
        IEnumerable<SuspiciousArtifact> artifacts)
    {
        QueueIntakeAdvisoryReason[] reasons =
        [
            .. artifacts.Select(static artifact => new QueueIntakeAdvisoryReason(
                BuildReasonCode(artifact.Kind),
                artifact.MessageResourceKey,
                artifact.Severity,
                artifact.DisplayPath))
        ];

        return reasons.Length == 0
            ? Array.Empty<QueueIntakeAdvisoryReason>()
            : new ReadOnlyCollection<QueueIntakeAdvisoryReason>(reasons);
    }

    private static string BuildReasonCode(SuspiciousArtifactKind kind)
    {
        return kind switch
        {
            SuspiciousArtifactKind.WindowsPeSignature => "INPUT_SAFETY_WINDOWS_PE",
            SuspiciousArtifactKind.WindowsScript => "INPUT_SAFETY_SCRIPT",
            SuspiciousArtifactKind.WindowsInstallerOrShortcut => "INPUT_SAFETY_INSTALLER_OR_SHORTCUT",
            SuspiciousArtifactKind.UnsafeArchiveEntryPath => "INPUT_SAFETY_UNSAFE_ARCHIVE_ENTRY",
            SuspiciousArtifactKind.UnsafeDescriptorReference => "INPUT_SAFETY_UNSAFE_DESCRIPTOR_REFERENCE",
            SuspiciousArtifactKind.IsoContentNotScanned => "INPUT_SAFETY_ISO_NOT_SCANNED",
            SuspiciousArtifactKind.ChdInternalContentNotScanned => "INPUT_SAFETY_CHD_NOT_SCANNED",
            SuspiciousArtifactKind.UnconfirmedArchiveExecutable => "INPUT_SAFETY_UNCONFIRMED_ARCHIVE_EXECUTABLE",
            SuspiciousArtifactKind.FolderScanLimitReached => "INPUT_SAFETY_FOLDER_SCAN_LIMIT",
            _ => "INPUT_SAFETY_FINDING"
        };
    }
}
