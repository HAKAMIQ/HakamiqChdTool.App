using Microsoft.Win32.SafeHandles;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HakamiqChdTool.App.Services.StorageAdvisor;

internal sealed class StoragePathAnalyzer
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;

    private const uint IoctlStorageQueryProperty = 0x002D1400;

    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;

    private const int StorageDeviceDescriptorBusTypeOffset = 28;
    private const int DeviceSeekPenaltyIncursSeekPenaltyOffset = 8;

    private const int BusTypeNvme = 17;

    private readonly bool _rejectReparsePoints;

    public StoragePathAnalyzer()
        : this(rejectReparsePoints: true)
    {
    }

    internal StoragePathAnalyzer(bool rejectReparsePoints)
    {
        _rejectReparsePoints = rejectReparsePoints;
    }

    public StoragePathAnalysis Analyze(
        StoragePathRole role,
        string? path)
    {
        string originalPath = path ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateUnknown(role, originalPath);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return CreateUnknown(role, originalPath);
        }

        bool isFile = File.Exists(fullPath);
        bool isDirectory = Directory.Exists(fullPath);
        bool exists = isFile || isDirectory;

        string directoryPath = ResolveDirectoryPath(role, fullPath, isFile, isDirectory);
        bool isRoot = IsRootPath(fullPath);
        bool isReparsePoint = exists
            && _rejectReparsePoints
            && HasReparsePointInExistingPathFromVolumeRoot(fullPath);

        StorageVolumeIdentity volume = ResolveVolumeIdentity(fullPath);
        StorageDeviceKind deviceKind = ResolveDeviceKind(volume.RootPath);

        bool isWritableCandidate = !isRoot
            && !isReparsePoint
            && IsSafeWritableCandidate(directoryPath);

        return new StoragePathAnalysis(
            role,
            originalPath,
            fullPath,
            directoryPath,
            volume,
            deviceKind,
            exists,
            isDirectory,
            isFile,
            isRoot,
            isReparsePoint,
            isWritableCandidate);
    }

    private static StoragePathAnalysis CreateUnknown(
        StoragePathRole role,
        string originalPath)
    {
        return new StoragePathAnalysis(
            role,
            originalPath,
            string.Empty,
            string.Empty,
            StorageVolumeIdentity.Unknown,
            StorageDeviceKind.Unknown,
            false,
            false,
            false,
            false,
            false,
            false);
    }

    private static string ResolveDirectoryPath(
        StoragePathRole role,
        string fullPath,
        bool isFile,
        bool isDirectory)
    {
        if (isDirectory)
        {
            return fullPath;
        }

        if (isFile)
        {
            return Path.GetDirectoryName(fullPath) ?? string.Empty;
        }

        if (role is StoragePathRole.Output or StoragePathRole.PendingWorkspace or StoragePathRole.BinCueRescueWorkspace)
        {
            return fullPath;
        }

        return Path.GetDirectoryName(fullPath) ?? string.Empty;
    }

    private static StorageVolumeIdentity ResolveVolumeIdentity(string fullPath)
    {
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return StorageVolumeIdentity.Unknown;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(root);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return StorageVolumeIdentity.Unknown;
        }

        string volumeLabel = string.Empty;
        string fileSystem = string.Empty;
        string serial = string.Empty;

        try
        {
            DriveInfo drive = new(normalizedRoot);
            if (drive.IsReady)
            {
                volumeLabel = drive.VolumeLabel ?? string.Empty;
                fileSystem = drive.DriveFormat ?? string.Empty;
            }
        }
        catch (Exception ex) when (IsStorageQueryFailure(ex))
        {
        }

        try
        {
            StringBuilder volumeName = new(261);
            StringBuilder fileSystemName = new(261);

            if (GetVolumeInformationW(
                    normalizedRoot,
                    volumeName,
                    volumeName.Capacity,
                    out uint serialNumber,
                    out _,
                    out _,
                    fileSystemName,
                    fileSystemName.Capacity))
            {
                if (string.IsNullOrWhiteSpace(volumeLabel))
                {
                    volumeLabel = volumeName.ToString();
                }

                if (string.IsNullOrWhiteSpace(fileSystem))
                {
                    fileSystem = fileSystemName.ToString();
                }

                serial = serialNumber.ToString("X8", CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (IsStorageQueryFailure(ex))
        {
        }

        return new StorageVolumeIdentity(
            normalizedRoot,
            volumeLabel,
            fileSystem,
            serial);
    }

    private static StorageDeviceKind ResolveDeviceKind(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return StorageDeviceKind.Unknown;
        }

        DriveType driveType;
        try
        {
            DriveInfo drive = new(rootPath);
            driveType = drive.DriveType;
        }
        catch (Exception ex) when (IsStorageQueryFailure(ex))
        {
            return StorageDeviceKind.Unknown;
        }

        if (driveType == DriveType.Network)
        {
            return StorageDeviceKind.Network;
        }

        if (driveType == DriveType.Removable)
        {
            return StorageDeviceKind.Removable;
        }

        if (driveType != DriveType.Fixed)
        {
            return StorageDeviceKind.Unknown;
        }

        WindowsStorageProbeResult probe = TryProbeWindowsStorage(rootPath);

        if (probe.BusType == BusTypeNvme)
        {
            return StorageDeviceKind.NvmeSsd;
        }

        if (probe.IncursSeekPenalty == true)
        {
            return StorageDeviceKind.Hdd;
        }

        if (probe.IncursSeekPenalty == false)
        {
            return StorageDeviceKind.SataSsd;
        }

        return StorageDeviceKind.Unknown;
    }

    private static WindowsStorageProbeResult TryProbeWindowsStorage(string rootPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return WindowsStorageProbeResult.Unknown;
        }

        string? root = Path.GetPathRoot(rootPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return WindowsStorageProbeResult.Unknown;
        }

        string volumePath = @"\\.\" + root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            using SafeFileHandle handle = CreateFileW(
                volumePath,
                0,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return WindowsStorageProbeResult.Unknown;
            }

            int? busType = QueryStorageDeviceBusType(handle);
            bool? incursSeekPenalty = QueryStorageSeekPenalty(handle);

            return new WindowsStorageProbeResult(busType, incursSeekPenalty);
        }
        catch (Exception ex) when (IsStorageQueryFailure(ex))
        {
            return WindowsStorageProbeResult.Unknown;
        }
    }

    private static int? QueryStorageDeviceBusType(SafeFileHandle handle)
    {
        byte[] output = QueryStorageProperty(handle, StorageDeviceProperty, 512);
        if (output.Length < StorageDeviceDescriptorBusTypeOffset + sizeof(int))
        {
            return null;
        }

        return BitConverter.ToInt32(output, StorageDeviceDescriptorBusTypeOffset);
    }

    private static bool? QueryStorageSeekPenalty(SafeFileHandle handle)
    {
        byte[] output = QueryStorageProperty(handle, StorageDeviceSeekPenaltyProperty, 64);
        if (output.Length <= DeviceSeekPenaltyIncursSeekPenaltyOffset)
        {
            return null;
        }

        return output[DeviceSeekPenaltyIncursSeekPenaltyOffset] != 0;
    }

    private static byte[] QueryStorageProperty(
        SafeFileHandle handle,
        int propertyId,
        int outputLength)
    {
        byte[] query = new byte[12];
        BitConverter.GetBytes(propertyId).CopyTo(query, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(query, 4);

        byte[] output = new byte[outputLength];

        bool success = DeviceIoControl(
            handle,
            IoctlStorageQueryProperty,
            query,
            query.Length,
            output,
            output.Length,
            out int bytesReturned,
            IntPtr.Zero);

        if (!success || bytesReturned <= 0)
        {
            return Array.Empty<byte>();
        }

        if (bytesReturned >= output.Length)
        {
            return output;
        }

        Array.Resize(ref output, bytesReturned);
        return output;
    }

    private static bool IsRootPath(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }
    }

    private bool IsSafeWritableCandidate(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            string fullDirectory = Path.GetFullPath(directoryPath);

            if (IsRootPath(fullDirectory))
            {
                return false;
            }

            string? existingDirectory = FindExistingDirectory(fullDirectory);
            if (string.IsNullOrWhiteSpace(existingDirectory))
            {
                return false;
            }

            return !_rejectReparsePoints
                   || !HasReparsePointInExistingPathFromVolumeRoot(existingDirectory);
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsStorageQueryFailure(ex))
        {
            return false;
        }
    }

    private static string? FindExistingDirectory(string directoryPath)
    {
        string current = Path.GetFullPath(directoryPath);

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || PathsEqual(parent, current))
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsStorageQueryFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsExistingPathReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsStorageQueryFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsStorageQueryFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsPathFailure(Exception ex)
    {
        return ex is IOException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsStorageQueryFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }

    private readonly struct WindowsStorageProbeResult
    {
        public WindowsStorageProbeResult(int? busType, bool? incursSeekPenalty)
        {
            BusType = busType;
            IncursSeekPenalty = incursSeekPenalty;
        }

        public int? BusType { get; }

        public bool? IncursSeekPenalty { get; }

        public static WindowsStorageProbeResult Unknown { get; } = new(null, null);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}