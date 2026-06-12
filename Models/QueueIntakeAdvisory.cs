using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Models;

public sealed record QueueIntakeAdvisory(
    QueueIntakeAdvisoryAction Action,
    int Confidence,
    bool SafeOutputHint,
    IReadOnlyList<QueueIntakeAdvisoryReason>? Reasons,
    IReadOnlyList<QueueIntakeAdvisoryReason>? Warnings,
    string? SuggestedFileName,
    string? Platform,
    string? Region,
    string? DiscGroupKey,
    string? DiscIndex)
{
    public int Confidence { get; init; } = Math.Clamp(Confidence, 0, 100);

    public IReadOnlyList<QueueIntakeAdvisoryReason> Reasons { get; init; } =
        NormalizeReasons(Reasons);

    public IReadOnlyList<QueueIntakeAdvisoryReason> Warnings { get; init; } =
        NormalizeReasons(Warnings);

    public string? SuggestedFileName { get; init; } = NormalizeText(SuggestedFileName);

    public string? Platform { get; init; } = NormalizeText(Platform);

    public string? Region { get; init; } = NormalizeText(Region);

    public string? DiscGroupKey { get; init; } = NormalizeText(DiscGroupKey);

    public string? DiscIndex { get; init; } = NormalizeText(DiscIndex);

    public bool IsBlocked => Action == QueueIntakeAdvisoryAction.Block
        || Reasons.Any(static reason => reason.Severity == QueueIntakeAdvisorySeverity.Blocker);

    public bool HasWarnings => Warnings.Count > 0
        || Reasons.Any(static reason => reason.Severity >= QueueIntakeAdvisorySeverity.Warning);

    public static QueueIntakeAdvisory Empty { get; } = new(
        QueueIntakeAdvisoryAction.Unknown,
        0,
        false,
        [],
        [],
        null,
        null,
        null,
        null,
        null);

    private static QueueIntakeAdvisoryReason[] NormalizeReasons(
        IEnumerable<QueueIntakeAdvisoryReason>? reasons)
    {
        if (reasons is null)
        {
            return [];
        }

        QueueIntakeAdvisoryReason[] normalized =
        [
            .. reasons.Where(static reason => reason is not null)
        ];

        return normalized.Length == 0
            ? []
            : normalized;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}