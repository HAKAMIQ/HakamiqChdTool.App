using System;

namespace HakamiqChdTool.App.Models;

public sealed record QueueIntakeAdvisoryReason(
    string Code,
    string Message,
    QueueIntakeAdvisorySeverity Severity,
    string? Source)
{
    public string Code { get; init; } = NormalizeRequired(Code, nameof(Code));

    public string Message { get; init; } = NormalizeRequired(Message, nameof(Message));

    public QueueIntakeAdvisorySeverity Severity { get; init; } = Severity;

    public string? Source { get; init; } = NormalizeOptional(Source);

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}