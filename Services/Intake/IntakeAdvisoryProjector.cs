using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Services.Intake;

public static class IntakeAdvisoryProjector
{
    public static QueueIntakeAdvisory Project(IntakeDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        SafeOutputHint? hint = decision.SafeOutputHint;

        return new QueueIntakeAdvisory(
            ProjectAction(decision.Action),
            IntakeDecision.ClampConfidence(decision.Confidence),
            hint is not null,
            ProjectReasons(decision.Reasons),
            ProjectReasons(decision.Warnings),
            hint?.SuggestedFileName,
            hint?.Platform,
            hint?.Region,
            hint?.DiscGroupKey,
            hint?.DiscIndex);
    }

    private static QueueIntakeAdvisoryAction ProjectAction(IntakeDecisionAction action)
    {
        return action switch
        {
            IntakeDecisionAction.Convert => QueueIntakeAdvisoryAction.Convert,
            IntakeDecisionAction.Extract => QueueIntakeAdvisoryAction.Extract,
            IntakeDecisionAction.Verify => QueueIntakeAdvisoryAction.Verify,
            IntakeDecisionAction.Skip => QueueIntakeAdvisoryAction.Skip,
            IntakeDecisionAction.Warn => QueueIntakeAdvisoryAction.Warn,
            IntakeDecisionAction.Block => QueueIntakeAdvisoryAction.Block,
            IntakeDecisionAction.ReportOnly => QueueIntakeAdvisoryAction.ReportOnly,
            _ => QueueIntakeAdvisoryAction.Unknown
        };
    }

    private static QueueIntakeAdvisorySeverity ProjectSeverity(IntakeDecisionSeverity severity)
    {
        return severity switch
        {
            IntakeDecisionSeverity.Warning => QueueIntakeAdvisorySeverity.Warning,
            IntakeDecisionSeverity.Error => QueueIntakeAdvisorySeverity.Error,
            IntakeDecisionSeverity.Blocker => QueueIntakeAdvisorySeverity.Blocker,
            _ => QueueIntakeAdvisorySeverity.Info
        };
    }

    private static IReadOnlyList<QueueIntakeAdvisoryReason> ProjectReasons(
        IReadOnlyList<IntakeDecisionReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        List<QueueIntakeAdvisoryReason> projected = new(reasons.Count);

        foreach (IntakeDecisionReason reason in reasons)
        {
            projected.Add(new QueueIntakeAdvisoryReason(
                reason.Code,
                reason.MessageKey,
                ProjectSeverity(reason.Severity),
                reason.Source));
        }

        return new ReadOnlyCollection<QueueIntakeAdvisoryReason>(projected);
    }
}