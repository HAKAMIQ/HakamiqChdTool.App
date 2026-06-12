using System;

namespace HakamiqChdTool.App.Models;

public sealed record SuspiciousArtifact(
    string SourcePath,
    string? ContainerPath,
    SuspiciousArtifactKind Kind,
    QueueIntakeAdvisorySeverity Severity,
    string MessageResourceKey)
{
    public string SourcePath { get; init; } = NormalizeRequired(SourcePath, nameof(SourcePath));

    public string? ContainerPath { get; init; } = NormalizeOptional(ContainerPath);

    public SuspiciousArtifactKind Kind { get; init; } = Kind;

    public QueueIntakeAdvisorySeverity Severity { get; init; } = Severity;

    public string MessageResourceKey { get; init; } = NormalizeRequired(MessageResourceKey, nameof(MessageResourceKey));

    public string DisplayPath => string.IsNullOrWhiteSpace(ContainerPath)
        ? SourcePath
        : SourcePath + " :: " + ContainerPath;

    public bool IsBlocking => Severity == QueueIntakeAdvisorySeverity.Blocker;

    public bool IsWarning => Severity == QueueIntakeAdvisorySeverity.Warning
        || Severity == QueueIntakeAdvisorySeverity.Error;

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