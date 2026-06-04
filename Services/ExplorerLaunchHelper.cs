using Microsoft.CSharp.RuntimeBinder;
using Serilog;
using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Services;

public static class ExplorerLaunchHelper
{
    private const int ShowNormal = 1;

    private static readonly ILogger Logger = Log.ForContext(typeof(ExplorerLaunchHelper));

    public static bool TrySelectPathInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            bool isFile = File.Exists(fullPath);
            bool isDirectory = Directory.Exists(fullPath);

            if (!isFile && !isDirectory)
            {
                return false;
            }

            string? explorerFolder = isDirectory
                ? fullPath
                : Path.GetDirectoryName(fullPath);

            if (TryActivateExistingExplorerWindow(explorerFolder))
            {
                return true;
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorerPath = string.IsNullOrWhiteSpace(windowsDirectory)
                ? "explorer.exe"
                : Path.Combine(windowsDirectory, "explorer.exe");

            if (!string.Equals(explorerPath, "explorer.exe", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(explorerPath))
            {
                explorerPath = "explorer.exe";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                ErrorDialog = false
            };

            startInfo.Arguments = isFile
                ? $"/select,\"{fullPath}\""
                : $"\"{fullPath}\"";

            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (ArgumentException ex)
        {
            Logger.Debug(ex, "Explorer select rejected an invalid path. Path={Path}", path);
            return false;
        }
        catch (NotSupportedException ex)
        {
            Logger.Debug(ex, "Explorer select rejected an unsupported path. Path={Path}", path);
            return false;
        }
        catch (PathTooLongException ex)
        {
            Logger.Debug(ex, "Explorer select failed because the target path is too long. Path={Path}", path);
            return false;
        }
        catch (IOException ex)
        {
            Logger.Debug(ex, "Explorer select failed because the target path could not be accessed. Path={Path}", path);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Debug(ex, "Explorer select failed because access was denied. Path={Path}", path);
            return false;
        }
        catch (System.Security.SecurityException ex)
        {
            Logger.Debug(ex, "Explorer select failed because security validation blocked the request. Path={Path}", path);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "Explorer select failed because the process could not be started. Path={Path}", path);
            return false;
        }
        catch (Win32Exception ex)
        {
            Logger.Debug(ex, "Explorer select failed because Windows rejected the process start request. Path={Path}", path);
            return false;
        }
    }

    private static bool TryActivateExistingExplorerWindow(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        object? shellApplication = null;
        object? shellWindows = null;

        try
        {
            string normalizedFolderPath = Path.GetFullPath(folderPath.Trim());
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shellApplication = Activator.CreateInstance(shellType);
            if (shellApplication is null)
            {
                return false;
            }

            shellWindows = ((dynamic)shellApplication).Windows();
            if (shellWindows is not IEnumerable windows)
            {
                return false;
            }

            foreach (object window in windows)
            {
                if (!TryGetExplorerWindowFolderPath(window, out string? windowFolderPath)
                    || string.IsNullOrWhiteSpace(windowFolderPath)
                    || !PathsEqual(windowFolderPath, normalizedFolderPath)
                    || !TryGetExplorerWindowHandle(window, out IntPtr handle))
                {
                    continue;
                }

                _ = ShowWindow(handle, ShowNormal);
                _ = SetForegroundWindow(handle);
                return true;
            }
        }
        catch (Exception ex) when (IsExpectedExplorerWindowActivationException(ex))
        {
            Logger.Debug(ex, "Explorer activation probe failed. FolderPath={FolderPath}", folderPath);
        }
        finally
        {
            ReleaseComObject(shellWindows);
            ReleaseComObject(shellApplication);
        }

        return false;
    }

    private static bool TryGetExplorerWindowFolderPath(object window, out string? folderPath)
    {
        folderPath = null;

        try
        {
            object? document = ((dynamic)window).Document;
            object? folder = ((dynamic)document).Folder;
            object? self = ((dynamic)folder).Self;
            object? rawPath = ((dynamic)self).Path;
            string? candidate = Convert.ToString(rawPath, CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            {
                return false;
            }

            folderPath = Path.GetFullPath(candidate.Trim());
            return true;
        }
        catch (Exception ex) when (IsExpectedExplorerWindowActivationException(ex))
        {
            return false;
        }
    }

    private static bool TryGetExplorerWindowHandle(object window, out IntPtr handle)
    {
        handle = IntPtr.Zero;

        try
        {
            object? rawWindowHandle = ((dynamic)window).HWND;
            int rawHandle = Convert.ToInt32(rawWindowHandle, CultureInfo.InvariantCulture);
            if (rawHandle == 0)
            {
                return false;
            }

            handle = new IntPtr(rawHandle);
            return true;
        }
        catch (Exception ex) when (IsExpectedExplorerWindowActivationException(ex))
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            string leftFull = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string rightFull = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(leftFull, rightFull, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedExplorerWindowActivationException(ex))
        {
            return false;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(value))
            {
                _ = Marshal.FinalReleaseComObject(value);
            }
        }
        catch (Exception ex) when (IsExpectedExplorerWindowActivationException(ex))
        {
            Logger.Debug(ex, "Explorer COM object release failed.");
        }
    }

    private static bool IsExpectedExplorerWindowActivationException(Exception ex)
    {
        return ex is ArgumentException
            or IOException
            or InvalidCastException
            or InvalidOperationException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException
            or RuntimeBinderException
            or System.Security.SecurityException
            or COMException;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#pragma warning restore SYSLIB1054
}
