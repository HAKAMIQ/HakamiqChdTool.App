using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Core.Session;

public readonly record struct QueueProgressSnapshot(
    bool IsTerminal,
    bool IsActiveRunning,
    bool IsIntegrityValidating,
    double ProgressBarDisplayValue);

public static class QueueSessionProgressAggregator
{
    public static double AverageOverallPercent(IReadOnlyList<QueueProgressSnapshot> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return 0;
        }

        double total = items.Sum(GetContribution);
        return Math.Clamp(total / items.Count, 0, 100);
    }

    private static double GetContribution(QueueProgressSnapshot task)
    {
        if (task.IsTerminal)
        {
            return 100.0;
        }

        if (task.IsActiveRunning || task.IsIntegrityValidating)
        {
            return NormalizePercent(task.ProgressBarDisplayValue);
        }

        return 0;
    }

    private static double NormalizePercent(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }
}