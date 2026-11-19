using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HakamiqChdTool.App.Core.Input;

/// <summary>
/// Checks that an opened file handle is the object at an already validated path. A path check
/// alone is not atomic with a later open: a path component can be swapped for a junction or
/// symbolic link in between. Validating the opened object closes that window: the handle must not
/// be a reparse point, and its final path (all links resolved) must equal the expected path.
/// </summary>
internal static class HandleIdentity
{
    private const uint FileAttributeReparsePoint = 0x400;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint OpenExisting = 3;
    private const uint ShareAll = 0x1 | 0x2 | 0x4;

    /// <summary>True when <paramref name="handle"/> is the regular file at <paramref name="fullPath"/>.</summary>
    public static bool IsHandleAtPath(SafeFileHandle handle, string fullPath)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.IsInvalid || string.IsNullOrWhiteSpace(fullPath) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info)
            || (info.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            return false;
        }

        string? actual = GetFinalPath(handle);
        string? expected = BuildExpectedFinalPath(fullPath);
        return actual is not null
            && expected is not null
            && string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);
    }

    // The volume root is resolved through a handle (so mapped and subst drives map to their real
    // location); the rest of the path is compared in its long-name form.
    private static string? BuildExpectedFinalPath(string fullPath)
    {
        string full = Path.GetFullPath(fullPath);
        string longPath = GetLongPath(full) ?? full;
        string? root = Path.GetPathRoot(longPath);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        using SafeFileHandle rootHandle = CreateFileW(root, 0, ShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (rootHandle.IsInvalid)
        {
            return null;
        }

        string? rootFinal = GetFinalPath(rootHandle);
        return rootFinal is null
            ? null
            : Normalize(rootFinal).TrimEnd('\\') + "\\" + longPath[root.Length..].TrimStart('\\');
    }

    private static string? GetFinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(512);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Capacity)
            {
                return buffer.ToString(0, (int)length);
            }

            buffer.Capacity = (int)length + 1;
        }

        return null;
    }

    private static string? GetLongPath(string path)
    {
        var buffer = new StringBuilder(512);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint length = GetLongPathNameW(path, buffer, (uint)buffer.Capacity);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Capacity)
            {
                return buffer.ToString(0, (int)length);
            }

            buffer.Capacity = (int)length + 1;
        }

        return null;
    }

    private static string Normalize(string path)
    {
        string value = path;
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            value = @"\\" + value[8..];
        }
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        return value.TrimEnd('\\');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

#pragma warning disable SYSLIB1054
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
#pragma warning restore SYSLIB1054
}
