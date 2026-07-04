using Serilog;
using System.Management;
using System.Runtime.InteropServices;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public readonly record struct ProcessorTopologyInfo(
    string Name,
    int PhysicalCores,
    int AvailableLogicalProcessors,
    int InstalledLogicalProcessors);

public static class ProcessorTopologyService
{
    private const int DefaultAutoProcessorLimitCap = 6;
    private const int SmallMachineReservedLogicalProcessors = 1;
    private const int StandardMachineReservedLogicalProcessors = 2;
    private const int SmallMachineLogicalProcessorThreshold = 4;

    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(ProcessorTopologyService));

    public static int GetAvailableLogicalProcessorCount() =>
        Math.Max(1, Environment.ProcessorCount);

    public static int ResolveDefaultAutoChdmanProcessorCount()
    {
        int availableLogical = GetAvailableLogicalProcessorCount();
        return ResolveSafeAutoProcessorCount(availableLogical, 0);
    }

    public static int ResolveChdmanProcessorCount(
        int maxProcessorCount,
        bool enableAutoResourceLimiter,
        int reservedLogicalCores,
        ConversionPerformanceMode performanceMode = ConversionPerformanceMode.Safe)
    {
        if (maxProcessorCount <= 0 && !enableAutoResourceLimiter)
        {
            return 0;
        }

        int availableLogical = GetAvailableLogicalProcessorCount();

        if (maxProcessorCount > 0)
        {
            return Math.Clamp(maxProcessorCount, 1, availableLogical);
        }

        return performanceMode switch
        {
            ConversionPerformanceMode.Fast => ResolveFastAutoProcessorCount(availableLogical),
            ConversionPerformanceMode.Balanced => ResolveBalancedAutoProcessorCount(availableLogical, reservedLogicalCores),
            _ => ResolveSafeAutoProcessorCount(availableLogical, reservedLogicalCores)
        };
    }

    private static int ResolveBalancedAutoProcessorCount(
        int availableLogical,
        int configuredReservedLogicalCores)
    {
        int normalizedAvailable = Math.Max(1, availableLogical);
        int reserve = Math.Clamp(
            configuredReservedLogicalCores > 0 ? configuredReservedLogicalCores : 1,
            0,
            Math.Max(0, normalizedAvailable - 1));

        int reserveAwareLimit = Math.Max(1, normalizedAvailable - reserve);
        int threeQuarterLimit = Math.Max(1, (int)Math.Ceiling(normalizedAvailable * 0.75d));
        int balancedLimit = Math.Min(reserveAwareLimit, threeQuarterLimit);

        return Math.Clamp(balancedLimit, 1, normalizedAvailable);
    }

    private static int ResolveFastAutoProcessorCount(int availableLogical)
    {
        int normalizedAvailable = Math.Max(1, availableLogical);
        return Math.Clamp(Math.Max(1, normalizedAvailable - 1), 1, normalizedAvailable);
    }

    private static int ResolveSafeAutoProcessorCount(
        int availableLogical,
        int configuredReservedLogicalCores)
    {
        int normalizedAvailable = Math.Max(1, availableLogical);
        int defaultReservedForSystem = normalizedAvailable <= SmallMachineLogicalProcessorThreshold
            ? SmallMachineReservedLogicalProcessors
            : StandardMachineReservedLogicalProcessors;

        int configuredReserve = configuredReservedLogicalCores > 0
            ? configuredReservedLogicalCores
            : defaultReservedForSystem;

        int reserve = Math.Clamp(
            Math.Max(defaultReservedForSystem, configuredReserve),
            0,
            Math.Max(0, normalizedAvailable - 1));

        int halfLogicalProcessors = Math.Max(1, normalizedAvailable / 2);
        int reserveAwareLimit = Math.Max(1, normalizedAvailable - reserve);
        int autoLimit = Math.Min(halfLogicalProcessors, reserveAwareLimit);
        int cappedAutoLimit = Math.Min(autoLimit, DefaultAutoProcessorLimitCap);

        return Math.Clamp(cappedAutoLimit, 1, normalizedAvailable);
    }

    public static ProcessorTopologyInfo ReadCurrent()
    {
        int availableLogical = GetAvailableLogicalProcessorCount();
        string name = string.Empty;
        int physicalCores = 0;
        int installedLogical = 0;
        bool foundAny = false;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementObject item in results.Cast<ManagementObject>())
            {
                using (item)
                {
                    foundAny = true;

                    string? partName = item["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(partName) && string.IsNullOrWhiteSpace(name))
                    {
                        name = partName;
                    }

                    physicalCores += SafeToInt(item["NumberOfCores"]);
                    installedLogical += SafeToInt(item["NumberOfLogicalProcessors"]);
                }
            }
        }
        catch (Exception ex) when (IsExpectedManagementException(ex))
        {
            Logger.Debug(ex, "Processor topology WMI read failed.");
        }

        if (!foundAny)
        {
            return new ProcessorTopologyInfo(
                string.Empty,
                0,
                availableLogical,
                availableLogical);
        }

        if (installedLogical <= 0)
        {
            installedLogical = availableLogical;
        }

        int effectiveAvailable = Math.Clamp(
            availableLogical,
            1,
            Math.Max(1, installedLogical));

        return new ProcessorTopologyInfo(
            name,
            Math.Max(0, physicalCores),
            effectiveAvailable,
            installedLogical);
    }

    private static int SafeToInt(object? value)
    {
        try
        {
            return Convert.ToInt32(value ?? 0);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static bool IsExpectedManagementException(Exception ex) =>
        ex is ManagementException
        or UnauthorizedAccessException
        or InvalidOperationException
        or COMException
        or PlatformNotSupportedException;
}
