using Serilog;
using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Services.Storage;

internal enum StorageDeviceKind
{
    Unknown = 0,
    Hdd = 1,
    SataSsd = 2,
    NvmeSsd = 3,
    Removable = 4,
    Network = 5
}

internal sealed record StorageDeviceIdentity(
    string VolumeRoot,
    string? PhysicalDrivePath,
    bool IsExternalOrRemovable,
    StorageDeviceKind DeviceKind,
    string DisplayName);

internal sealed class StorageDeviceResolver
{
    private static readonly ILogger Log = Serilog.Log.ForContext<StorageDeviceResolver>();

    public StorageDeviceIdentity Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Unknown(string.Empty);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Log.Debug(ex, "Storage device resolution rejected invalid path. Path={Path}", path);
            return Unknown(path);
        }

        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Unknown(fullPath);
        }

        string volumeRoot = NormalizeRoot(root);
        DriveType driveType = DriveType.Unknown;
        string displayName = volumeRoot;

        try
        {
            DriveInfo drive = new(volumeRoot);
            driveType = drive.DriveType;
            if (drive.IsReady)
            {
                string label = drive.VolumeLabel;
                displayName = string.IsNullOrWhiteSpace(label) ? volumeRoot : $"{label} ({volumeRoot.TrimEnd('\\')})";
            }
        }
        catch (Exception ex) when (IsExpectedStorageException(ex))
        {
            Log.Debug(ex, "Storage device resolver could not read DriveInfo. Root={Root}", volumeRoot);
        }

        PhysicalDriveResolution physical = TryResolvePhysicalDrive(volumeRoot);
        bool external = driveType == DriveType.Removable
            || string.Equals(physical.InterfaceType, "USB", StringComparison.OrdinalIgnoreCase)
            || physical.PnpDeviceId?.Contains("USB", StringComparison.OrdinalIgnoreCase) == true;

        return new StorageDeviceIdentity(
            volumeRoot,
            physical.PhysicalDrivePath,
            external,
            ResolveDeviceKind(driveType, physical),
            string.IsNullOrWhiteSpace(physical.Model) ? displayName : $"{physical.Model} ({volumeRoot.TrimEnd('\\')})");
    }

    private static StorageDeviceIdentity Unknown(string displayPath) => new(
        string.Empty,
        null,
        false,
        StorageDeviceKind.Unknown,
        string.IsNullOrWhiteSpace(displayPath) ? "Unknown storage" : displayPath.Trim());

    private static PhysicalDriveResolution TryResolvePhysicalDrive(string volumeRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            return PhysicalDriveResolution.Unknown;
        }

        string? root = Path.GetPathRoot(volumeRoot);
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2)
        {
            return PhysicalDriveResolution.Unknown;
        }

        string driveId = root[..2].ToUpperInvariant();

        try
        {
            using var logicalDisk = new ManagementObject($"Win32_LogicalDisk.DeviceID=\"{driveId}\"");
            foreach (ManagementObject partition in logicalDisk.GetRelated("Win32_DiskPartition").Cast<ManagementObject>())
            {
                using (partition)
                {
                    foreach (ManagementObject disk in partition.GetRelated("Win32_DiskDrive").Cast<ManagementObject>())
                    {
                        using (disk)
                        {
                            string? deviceId = disk["DeviceID"]?.ToString();

                            return new PhysicalDriveResolution(
                                deviceId,
                                disk["Model"]?.ToString(),
                                disk["InterfaceType"]?.ToString(),
                                disk["PNPDeviceID"]?.ToString(),
                                TryResolvePhysicalDiskMediaType(deviceId) ?? disk["MediaType"]?.ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (IsExpectedManagementException(ex))
        {
            Log.Debug(ex, "Storage device resolver could not map logical volume to physical disk. Root={Root}", volumeRoot);
        }

        return PhysicalDriveResolution.Unknown;
    }

    private static string? TryResolvePhysicalDiskMediaType(string? physicalDrivePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(physicalDrivePath))
        {
            return null;
        }

        string? deviceId = ExtractPhysicalDriveNumber(physicalDrivePath);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery($"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceId}'"));

            foreach (ManagementObject physicalDisk in searcher.Get().Cast<ManagementObject>())
            {
                using (physicalDisk)
                {
                    return physicalDisk["MediaType"]?.ToString();
                }
            }
        }
        catch (Exception ex) when (IsExpectedManagementException(ex))
        {
            Log.Debug(ex, "Storage device resolver could not read MSFT_PhysicalDisk media type. Device={Device}", physicalDrivePath);
        }

        return null;
    }

    private static string? ExtractPhysicalDriveNumber(string physicalDrivePath)
    {
        const string marker = "PHYSICALDRIVE";
        int markerIndex = physicalDrivePath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        string suffix = physicalDrivePath[(markerIndex + marker.Length)..];
        return suffix.All(char.IsDigit) ? suffix : null;
    }

    private static StorageDeviceKind ResolveDeviceKind(
        DriveType driveType,
        PhysicalDriveResolution physical)
    {
        if (driveType == DriveType.Network)
        {
            return StorageDeviceKind.Network;
        }

        if (driveType == DriveType.Removable)
        {
            return StorageDeviceKind.Removable;
        }

        string interfaceType = physical.InterfaceType?.Trim() ?? string.Empty;
        string mediaType = physical.MediaType?.Trim() ?? string.Empty;
        string model = physical.Model?.Trim() ?? string.Empty;

        if (interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
        {
            return StorageDeviceKind.NvmeSsd;
        }

        if (string.Equals(mediaType, "4", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("Solid", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("SCM", StringComparison.OrdinalIgnoreCase)
            || model.Contains("SSD", StringComparison.OrdinalIgnoreCase))
        {
            return StorageDeviceKind.SataSsd;
        }

        if (string.Equals(mediaType, "3", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("Hard disk", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("Rotational", StringComparison.OrdinalIgnoreCase))
        {
            return StorageDeviceKind.Hdd;
        }

        return StorageDeviceKind.Unknown;
    }

    private static string NormalizeRoot(string root)
    {
        try
        {
            return Path.GetFullPath(root);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return root.Trim();
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedStorageException(Exception ex) =>
        IsExpectedPathException(ex)
        || ex is InvalidOperationException;

    private static bool IsExpectedManagementException(Exception ex) =>
        ex is ManagementException
        or COMException
        or UnauthorizedAccessException
        or InvalidOperationException
        or ArgumentException
        or NotSupportedException
        or PlatformNotSupportedException;

    private sealed record PhysicalDriveResolution(
        string? PhysicalDrivePath,
        string? Model,
        string? InterfaceType,
        string? PnpDeviceId,
        string? MediaType)
    {
        public static PhysicalDriveResolution Unknown { get; } = new(null, null, null, null, null);
    }
}
