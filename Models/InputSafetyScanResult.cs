using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Models;

public sealed record InputSafetyScanResult(IReadOnlyList<SuspiciousArtifact>? Artifacts)
{
    public IReadOnlyList<SuspiciousArtifact> Artifacts { get; init; } =
        NormalizeArtifacts(Artifacts);

    public int BlockedCount => Artifacts.Count(static artifact => artifact.IsBlocking);

    public int WarningCount => Artifacts.Count(static artifact => !artifact.IsBlocking
        && artifact.Severity >= QueueIntakeAdvisorySeverity.Warning);

    public bool HasFindings => Artifacts.Count > 0;

    public bool HasBlockingFindings => BlockedCount > 0;

    public bool HasWarnings => WarningCount > 0;

    public static InputSafetyScanResult Empty { get; } = new([]);

    public static InputSafetyScanResult FromArtifacts(IEnumerable<SuspiciousArtifact>? artifacts) =>
        new(artifacts is null ? [] : [.. artifacts]);

    public static InputSafetyScanResult Merge(params InputSafetyScanResult?[]? results)
    {
        if (results is null || results.Length == 0)
        {
            return Empty;
        }

        List<SuspiciousArtifact> artifacts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (InputSafetyScanResult? result in results)
        {
            if (result is null)
            {
                continue;
            }

            foreach (SuspiciousArtifact artifact in result.Artifacts)
            {
                string normalizedSourcePath = NormalizePathForSet(artifact.SourcePath);
                if (string.IsNullOrWhiteSpace(normalizedSourcePath))
                {
                    continue;
                }

                string key = string.Join(
                    "|",
                    normalizedSourcePath,
                    artifact.ContainerPath?.Trim() ?? string.Empty,
                    artifact.Kind.ToString(),
                    artifact.MessageResourceKey?.Trim() ?? string.Empty);

                if (seen.Add(key))
                {
                    artifacts.Add(artifact);
                }
            }
        }

        return FromArtifacts(artifacts);
    }

    public InputSafetyScanResult ForSources(IEnumerable<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        HashSet<string> sourceSet = sourcePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePathForSet)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sourceSet.Count == 0)
        {
            return Empty;
        }

        return FromArtifacts(Artifacts.Where(artifact => sourceSet.Contains(NormalizePathForSet(artifact.SourcePath))));
    }

    public HashSet<string> BuildBlockedSourcePathSet()
    {
        return Artifacts
            .Where(static artifact => artifact.IsBlocking)
            .Select(static artifact => NormalizePathForSet(artifact.SourcePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizePathForSet(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static SuspiciousArtifact[] NormalizeArtifacts(
        IEnumerable<SuspiciousArtifact>? artifacts)
    {
        if (artifacts is null)
        {
            return [];
        }

        SuspiciousArtifact[] normalized =
        [
            .. artifacts.Where(static artifact => artifact is not null)
        ];

        return normalized.Length == 0
            ? []
            : normalized;
    }

    private static bool IsExpectedPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}