using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HakamiqChdTool.App.Services.Intake;

public sealed record IntakeDecision
{
    private const string DefaultSource = nameof(IntakeDecision);

    private IntakeDecision(
        IntakeDecisionAction action,
        int confidence,
        SafeOutputHint? safeOutputHint,
        IReadOnlyList<IntakeDecisionReason> reasons,
        IReadOnlyList<IntakeDecisionReason> warnings)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(warnings);

        Action = action;
        Confidence = ClampConfidence(confidence);
        SafeOutputHint = safeOutputHint;
        Reasons = ToReadOnlyList(reasons);
        Warnings = ToReadOnlyList(warnings);
    }

    public IntakeDecisionAction Action { get; }

    public int Confidence { get; }

    public SafeOutputHint? SafeOutputHint { get; }

    public IReadOnlyList<IntakeDecisionReason> Reasons { get; }

    public IReadOnlyList<IntakeDecisionReason> Warnings { get; }

    public bool IsBlocked =>
        Action == IntakeDecisionAction.Block
        || Reasons.Any(reason => reason.Severity == IntakeDecisionSeverity.Blocker);

    public bool HasWarnings =>
        Warnings.Count > 0
        || Reasons.Any(reason => reason.Severity == IntakeDecisionSeverity.Warning);

    public static IntakeDecision Unknown(string messageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        return Create(
            IntakeDecisionAction.Unknown,
            0,
            new IntakeDecisionReason(
                "INTAKE_UNKNOWN",
                messageKey,
                IntakeDecisionSeverity.Info,
                DefaultSource));
    }

    public static IntakeDecision Blocked(
        string code,
        string messageKey,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        return Create(
            IntakeDecisionAction.Block,
            100,
            new IntakeDecisionReason(
                code,
                messageKey,
                IntakeDecisionSeverity.Blocker,
                source ?? DefaultSource));
    }

    public static IntakeDecision Create(
        IntakeDecisionAction action,
        int confidence,
        IntakeDecisionReason reason,
        SafeOutputHint? safeOutputHint = null)
    {
        ArgumentNullException.ThrowIfNull(reason);

        return new IntakeDecision(
            action,
            confidence,
            safeOutputHint,
            [reason],
            []);
    }

    public IntakeDecision WithReason(IntakeDecisionReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        List<IntakeDecisionReason> reasons = new(Reasons)
        {
            reason
        };

        return new IntakeDecision(
            Action,
            Confidence,
            SafeOutputHint,
            reasons,
            Warnings);
    }

    public IntakeDecision WithWarning(IntakeDecisionReason warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        List<IntakeDecisionReason> warnings = new(Warnings)
        {
            warning
        };

        return new IntakeDecision(
            Action,
            Confidence,
            SafeOutputHint,
            Reasons,
            warnings);
    }

    public static int ClampConfidence(int confidence)
    {
        return Math.Clamp(confidence, 0, 100);
    }

    private static ReadOnlyCollection<IntakeDecisionReason> ToReadOnlyList(
        IReadOnlyList<IntakeDecisionReason> source)
    {
        List<IntakeDecisionReason> items = new(source.Count);

        foreach (IntakeDecisionReason item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<IntakeDecisionReason>(items);
    }
}