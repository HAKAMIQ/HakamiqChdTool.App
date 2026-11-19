using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

/// <summary>
/// Places a CloneCD .img next to the generated CUE inside the process temp
/// workspace. A hard link is used when the source is on the same volume and
/// writable (no data copy; deleting the link never touches the original). A
/// read-only or cross-volume source is copied instead, after a free-space check.
/// Nothing is ever written to the source folder.
/// </summary>
internal static class CloneCdImageStager
{
    private const long FreeSpaceReserveBytes = 64L * 1024L * 1024L;
    private const int CopyBufferBytes = 1024 * 1024;

    public static async Task<bool> TryStageAsync(
        string sourceImagePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var source = new FileInfo(sourceImagePath);
        if (!source.Exists || File.Exists(destinationPath))
        {
            return false;
        }

        if (!source.IsReadOnly && TryCreateHardLink(destinationPath, source.FullName))
        {
            if (new FileInfo(destinationPath).Length == source.Length)
            {
                return true;
            }

            TryDelete(destinationPath);
            return false;
        }

        TryDelete(destinationPath);

        string? root = Path.GetPathRoot(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(root)
            || new DriveInfo(root).AvailableFreeSpace < source.Length + FreeSpaceReserveBytes)
        {
            return false;
        }

        try
        {
            await using (var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, CopyBufferBytes, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(destinationPath).Length == source.Length)
            {
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(destinationPath);
            throw;
        }

        TryDelete(destinationPath);
        return false;
    }

    private static bool TryCreateHardLink(string destinationPath, string sourcePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                  or EntryPointNotFoundException
                                  or IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp workspace is deleted as a whole afterwards.
        }
    }

#pragma warning disable SYSLIB1054
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
#pragma warning restore SYSLIB1054
}
