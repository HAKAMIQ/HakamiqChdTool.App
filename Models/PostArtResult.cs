using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Models;

public sealed class PostConversionArtifactResult
{
    private int _sbiCopiedCount;
    private int _m3uGeneratedCount;
    private int _m3uSkippedExistingCount;
    private PostConversionArtifactFailure[] _failures = [];

    public int SbiCopiedCount
    {
        get => _sbiCopiedCount;
        init => _sbiCopiedCount = Math.Max(0, value);
    }

    public int M3uGeneratedCount
    {
        get => _m3uGeneratedCount;
        init => _m3uGeneratedCount = Math.Max(0, value);
    }

    public int M3uSkippedExistingCount
    {
        get => _m3uSkippedExistingCount;
        init => _m3uSkippedExistingCount = Math.Max(0, value);
    }

    public int FailedArtifactCount => _failures.Length;

    public IReadOnlyList<PostConversionArtifactFailure> Failures
    {
        get => _failures;
        init => _failures = NormalizeFailures(value);
    }

    public bool HasFailures => FailedArtifactCount > 0;

    public static PostConversionArtifactResult Empty { get; } = new();

    public static PostConversionArtifactResult WithFailure(
        string artifactKind,
        string messageCode,
        string? targetPath = null)
    {
        return new PostConversionArtifactResult
        {
            Failures =
            [
                new PostConversionArtifactFailure
                {
                    ArtifactKind = artifactKind,
                    MessageCode = messageCode,
                    TargetPath = string.IsNullOrWhiteSpace(targetPath) ? null : targetPath.Trim()
                }
            ]
        };
    }

    public static PostConversionArtifactResult Combine(params PostConversionArtifactResult?[] results)
    {
        return Combine((IEnumerable<PostConversionArtifactResult?>)results);
    }

    public static PostConversionArtifactResult Combine(IEnumerable<PostConversionArtifactResult?> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        int sbiCopied = 0;
        int m3uGenerated = 0;
        int m3uSkippedExisting = 0;
        List<PostConversionArtifactFailure> failures = [];

        foreach (PostConversionArtifactResult? result in results)
        {
            if (result is null)
            {
                continue;
            }

            sbiCopied = AddClamped(sbiCopied, result.SbiCopiedCount);
            m3uGenerated = AddClamped(m3uGenerated, result.M3uGeneratedCount);
            m3uSkippedExisting = AddClamped(m3uSkippedExisting, result.M3uSkippedExistingCount);

            foreach (PostConversionArtifactFailure? failure in result.Failures)
            {
                if (IsValidFailure(failure))
                {
                    failures.Add(failure);
                }
            }
        }

        return new PostConversionArtifactResult
        {
            SbiCopiedCount = sbiCopied,
            M3uGeneratedCount = m3uGenerated,
            M3uSkippedExistingCount = m3uSkippedExisting,
            Failures = failures.Count == 0
                ? []
                : [.. failures]
        };
    }

    private static PostConversionArtifactFailure[] NormalizeFailures(
        IEnumerable<PostConversionArtifactFailure>? failures)
    {
        if (failures is null)
        {
            return [];
        }

        List<PostConversionArtifactFailure> normalized = [];

        foreach (PostConversionArtifactFailure? failure in failures)
        {
            if (IsValidFailure(failure))
            {
                normalized.Add(failure);
            }
        }

        return normalized.Count == 0
            ? []
            : [.. normalized];
    }

    private static bool IsValidFailure(PostConversionArtifactFailure? failure)
    {
        return failure is not null
            && !string.IsNullOrWhiteSpace(failure.ArtifactKind)
            && !string.IsNullOrWhiteSpace(failure.MessageCode);
    }

    private static int AddClamped(int current, int value)
    {
        long sum = (long)Math.Max(0, current) + Math.Max(0, value);
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }
}