using System;

namespace HakamiqChdTool.App.Services.Storage;

internal enum StorageHealthSeverity
{
    Normal = 0,
    Unavailable = 1,
    Warning = 2,
    Critical = 3,
    Abort = 4
}

internal sealed record StorageHealthDecision(
    StorageHealthSeverity Severity,
    string MessageKey,
    int? CurrentCelsius);

internal sealed class StorageHealthPolicy
{
    public StorageHealthDecision Evaluate(
        StorageTemperatureReading reading,
        StorageTemperaturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(policy);

        if (!reading.IsAvailable || reading.CurrentCelsius is not int current)
        {
            return new StorageHealthDecision(
                StorageHealthSeverity.Unavailable,
                "LocStorageTemperature_Unavailable",
                null);
        }

        if (current >= policy.AbortCelsius)
        {
            return new StorageHealthDecision(
                StorageHealthSeverity.Abort,
                "LocStorageTemperature_Abort",
                current);
        }

        if (current >= policy.CriticalCelsius)
        {
            return new StorageHealthDecision(
                StorageHealthSeverity.Critical,
                "LocStorageTemperature_Critical",
                current);
        }

        if (current >= policy.WarningCelsius)
        {
            return new StorageHealthDecision(
                StorageHealthSeverity.Warning,
                "LocStorageTemperature_Warning",
                current);
        }

        return new StorageHealthDecision(StorageHealthSeverity.Normal, string.Empty, current);
    }
}

