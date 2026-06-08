namespace HakamiqChdTool.App.Services.Storage;

internal enum StorageTemperatureCapability
{
    Unknown = 0,
    Available = 1,
    AccessDenied = 2,
    Unsupported = 3,
    Unavailable = 4
}

internal sealed record StorageTemperatureReading(
    bool IsAvailable,
    int? CurrentCelsius,
    string? UnavailableReasonCode,
    StorageTemperatureCapability Capability,
    Exception? DiagnosticException = null);

internal sealed record StorageTemperaturePolicy(
    int WarningCelsius,
    int CriticalCelsius,
    int AbortCelsius,
    TimeSpan PollingInterval);

internal static class DefaultStorageTemperaturePolicies
{
    public static StorageTemperaturePolicy ExternalHdd { get; } =
        new(
            WarningCelsius: 50,
            CriticalCelsius: 55,
            AbortCelsius: 60,
            PollingInterval: TimeSpan.FromSeconds(45));

    public static StorageTemperaturePolicy ExternalSsd { get; } =
        new(
            WarningCelsius: 65,
            CriticalCelsius: 72,
            AbortCelsius: 78,
            PollingInterval: TimeSpan.FromSeconds(45));
}
