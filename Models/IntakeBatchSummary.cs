using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Models;

public sealed record IntakeBatchSummary(
    int SupportedFileCount,
    int ArchiveFileCount,
    int UnsupportedFileCount,
    int DuplicateFileCount,
    int MissingPathCount,
    int DirectoryCount,
    IReadOnlyList<SuspiciousArtifact>? SafetyArtifacts = null)
{
    public int SupportedFileCount { get; init; } = Math.Max(0, SupportedFileCount);

    public int ArchiveFileCount { get; init; } = Math.Max(0, ArchiveFileCount);

    public int UnsupportedFileCount { get; init; } = Math.Max(0, UnsupportedFileCount);

    public int DuplicateFileCount { get; init; } = Math.Max(0, DuplicateFileCount);

    public int MissingPathCount { get; init; } = Math.Max(0, MissingPathCount);

    public int DirectoryCount { get; init; } = Math.Max(0, DirectoryCount);

    public IReadOnlyList<SuspiciousArtifact> SafetyArtifacts { get; init; } =
        NormalizeSafetyArtifacts(SafetyArtifacts);

    public int AcceptedFileCount => SupportedFileCount;

    public int SkippedFileCount => UnsupportedFileCount + DuplicateFileCount + MissingPathCount;

    public int SafetyBlockedCount => SafetyArtifacts.Count(static artifact => artifact.IsBlocking);

    public int SafetyWarningCount => SafetyArtifacts.Count(static artifact => !artifact.IsBlocking
        && artifact.Severity >= QueueIntakeAdvisorySeverity.Warning);

    public bool HasSafetyFindings => SafetyArtifacts.Count > 0;

    public bool HasWarnings => ArchiveFileCount > 0
        || UnsupportedFileCount > 0
        || DuplicateFileCount > 0
        || MissingPathCount > 0
        || HasSafetyFindings;

    public bool ShouldPromptUser => DirectoryCount > 0 || HasWarnings;

    public IntakeBatchSummary WithSafety(InputSafetyScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        return this with
        {
            SafetyArtifacts = scanResult.Artifacts
        };
    }

    private static IReadOnlyList<SuspiciousArtifact> NormalizeSafetyArtifacts(
        IEnumerable<SuspiciousArtifact>? artifacts)
    {
        if (artifacts is null)
        {
            return Array.Empty<SuspiciousArtifact>();
        }

        SuspiciousArtifact[] normalized = artifacts
            .Where(static artifact => artifact is not null)
            .ToArray();

        return normalized.Length == 0
            ? Array.Empty<SuspiciousArtifact>()
            : normalized;
    }
}