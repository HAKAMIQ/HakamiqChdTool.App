using System;

namespace HakamiqChdTool.App.Services.Intake;

public sealed record IntakeDecisionReason
{
    public IntakeDecisionReason(
        string code,
        string messageKey,
        IntakeDecisionSeverity severity = IntakeDecisionSeverity.Info,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown intake decision severity.");
        }

        string normalizedMessageKey = messageKey.Trim();
        if (!IsLocalizationResourceKey(normalizedMessageKey))
        {
            throw new ArgumentException(
                "Intake decision message must be a localization resource key.",
                nameof(messageKey));
        }

        Code = code.Trim();
        MessageKey = normalizedMessageKey;
        Severity = severity;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }

    public string Code { get; }

    public string MessageKey { get; }

    public IntakeDecisionSeverity Severity { get; }

    public string? Source { get; }

    private static bool IsLocalizationResourceKey(string value) =>
        value.StartsWith("Loc", StringComparison.Ordinal);
}