using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Services.M3u;

public sealed record M3uGenerationResult
{
    public M3uGenerationResult(
        int candidateSetCount,
        int generatedCount,
        int skippedExistingCount,
        IReadOnlyList<string> generatedPaths,
        IReadOnlyList<PostConversionArtifactFailure> failures)
    {
        if (candidateSetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateSetCount), candidateSetCount, null);
        }

        if (generatedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generatedCount), generatedCount, null);
        }

        if (skippedExistingCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedExistingCount), skippedExistingCount, null);
        }

        ArgumentNullException.ThrowIfNull(generatedPaths);
        ArgumentNullException.ThrowIfNull(failures);

        CandidateSetCount = candidateSetCount;
        GeneratedCount = generatedCount;
        SkippedExistingCount = skippedExistingCount;
        GeneratedPaths = ToReadOnlyStringList(generatedPaths);
        Failures = ToReadOnlyFailureList(failures);
    }

    public int CandidateSetCount { get; }

    public int GeneratedCount { get; }

    public int SkippedExistingCount { get; }

    public IReadOnlyList<string> GeneratedPaths { get; }

    public IReadOnlyList<PostConversionArtifactFailure> Failures { get; }

    public int FailedCount => Failures.Count;

    private static ReadOnlyCollection<string> ToReadOnlyStringList(
        IReadOnlyList<string> source)
    {
        List<string> items = new(source.Count);

        foreach (string item in source)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                items.Add(item);
            }
        }

        return new ReadOnlyCollection<string>(items);
    }

    private static ReadOnlyCollection<PostConversionArtifactFailure> ToReadOnlyFailureList(
        IReadOnlyList<PostConversionArtifactFailure> source)
    {
        List<PostConversionArtifactFailure> items = new(source.Count);

        foreach (PostConversionArtifactFailure item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<PostConversionArtifactFailure>(items);
    }
}