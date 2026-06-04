using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.Safety;

public static class InputSafetyAdvisoryProjector
{
    public static IReadOnlyDictionary<string, QueueIntakeAdvisory> ProjectBySource(
        InputSafetyScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        return new Dictionary<string, QueueIntakeAdvisory>(StringComparer.OrdinalIgnoreCase);
    }

    public static QueueIntakeAdvisory Merge(
        QueueIntakeAdvisory? intakeAdvisory,
        QueueIntakeAdvisory? safetyAdvisory)
    {
        _ = safetyAdvisory;

        return intakeAdvisory ?? QueueIntakeAdvisory.Empty;
    }
}