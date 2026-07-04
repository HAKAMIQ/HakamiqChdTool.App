using Serilog;
using System;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Storage;

internal interface IStorageTemperatureMonitor
{
    Task<StorageTemperatureReading> TryReadAsync(
        StorageDeviceIdentity device,
        CancellationToken cancellationToken);
}

internal sealed class StorageTemperatureMonitor : IStorageTemperatureMonitor
{
    private const string UnsupportedPlatformCode = "UnsupportedPlatform";
    private const string PhysicalDriveUnavailableCode = "PhysicalDriveUnavailable";
    private const string SmartUnavailableCode = "SmartUnavailable";
    private const string AccessDeniedCode = "AccessDenied";

    public StorageTemperatureMonitor()
        : this(Log.ForContext<StorageTemperatureMonitor>())
    {
    }

    public StorageTemperatureMonitor(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
    }

    public Task<StorageTemperatureReading> TryReadAsync(
        StorageDeviceIdentity device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Task.Run(
            () => TryRead(device, cancellationToken),
            cancellationToken);
    }

    private StorageTemperatureReading TryRead(
        StorageDeviceIdentity device,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(UnsupportedPlatformCode, StorageTemperatureCapability.Unsupported);
        }

        int? physicalDriveIndex = TryParsePhysicalDriveIndex(device.PhysicalDrivePath);
        if (physicalDriveIndex is null)
        {
            return Unavailable(PhysicalDriveUnavailableCode, StorageTemperatureCapability.Unavailable);
        }

        try
        {
            if (TryReadStorageReliabilityTemperature(physicalDriveIndex.Value, out int temperatureCelsius))
            {
                return new StorageTemperatureReading(
                    true,
                    temperatureCelsius,
                    null,
                    StorageTemperatureCapability.Available);
            }
        }
        catch (Exception ex) when (IsExpectedManagementException(ex))
        {
            return Unavailable(ClassifyUnavailableReason(ex), ClassifyCapability(ex), ex);
        }

        return Unavailable(SmartUnavailableCode, StorageTemperatureCapability.Unavailable);
    }

    private static StorageTemperatureReading Unavailable(
        string reasonCode,
        StorageTemperatureCapability capability,
        Exception? diagnosticException = null) => new(
        false,
        null,
        reasonCode,
        capability,
        diagnosticException);

    private static bool TryReadStorageReliabilityTemperature(
        int physicalDriveIndex,
        out int temperatureCelsius)
    {
        temperatureCelsius = 0;

        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM MSFT_StorageReliabilityCounter"));

        using ManagementObjectCollection results = searcher.Get();
        foreach (ManagementObject item in results.Cast<ManagementObject>())
        {
            using (item)
            {
                if (!IsCounterForPhysicalDrive(item, physicalDriveIndex))
                {
                    continue;
                }

                object? value = TryReadProperty(item, "Temperature");
                if (value is null)
                {
                    continue;
                }

                if (TryConvertTemperature(value, out temperatureCelsius))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCounterForPhysicalDrive(
        ManagementObject item,
        int physicalDriveIndex)
    {
        string expected = physicalDriveIndex.ToString(CultureInfo.InvariantCulture);

        object? deviceId = TryReadProperty(item, "DeviceId");
        if (deviceId is not null
            && string.Equals(deviceId.ToString(), expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        object? instanceName = TryReadProperty(item, "InstanceName")
            ?? TryReadProperty(item, "ObjectId")
            ?? TryReadProperty(item, "Name");

        return instanceName?.ToString()?.Contains(
            "PhysicalDisk" + expected,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static object? TryReadProperty(ManagementObject item, string propertyName)
    {
        try
        {
            return item.Properties[propertyName]?.Value;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static bool TryConvertTemperature(object value, out int celsius)
    {
        celsius = 0;

        try
        {
            int candidate = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (candidate is < -20 or > 120)
            {
                return false;
            }

            celsius = candidate;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static int? TryParsePhysicalDriveIndex(string? physicalDrivePath)
    {
        if (string.IsNullOrWhiteSpace(physicalDrivePath))
        {
            return null;
        }

        const string prefix = @"\\.\PHYSICALDRIVE";
        string value = physicalDrivePath.Trim();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(value[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? index
            : null;
    }

    private static bool IsExpectedManagementException(Exception ex) =>
        ex is ManagementException
        or COMException
        or UnauthorizedAccessException
        or InvalidOperationException
        or PlatformNotSupportedException;

    private static string ClassifyUnavailableReason(Exception ex) =>
        ClassifyCapability(ex) == StorageTemperatureCapability.AccessDenied
            ? AccessDeniedCode
            : SmartUnavailableCode;

    private static StorageTemperatureCapability ClassifyCapability(Exception ex)
    {
        if (ex is UnauthorizedAccessException or ManagementException { ErrorCode: ManagementStatus.AccessDenied })
        {
            return StorageTemperatureCapability.AccessDenied;
        }

        if (ex is COMException { ErrorCode: unchecked((int)0x80070005) })
        {
            return StorageTemperatureCapability.AccessDenied;
        }

        string message = ex.Message ?? string.Empty;
        if (message.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return StorageTemperatureCapability.AccessDenied;
        }

        if (ex is PlatformNotSupportedException)
        {
            return StorageTemperatureCapability.Unsupported;
        }

        return StorageTemperatureCapability.Unavailable;
    }
}
