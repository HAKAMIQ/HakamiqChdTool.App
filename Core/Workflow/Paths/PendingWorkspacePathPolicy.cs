using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow.Paths;

public static class PendingWorkspacePathPolicy
{
    public const string WorkspaceDirectoryName = "Hakamiq Work";
    public const string WorkspaceOperationsDirectoryName = "Operations";
    public const string LegacyDriveWorkspaceDirectoryName = ".HakamiqCHDTool";
    public const string LegacyPendingDirectoryName = "Pending";
    public const string LegacyOutputPendingDirectoryName = ".hakamiq-pending";
    public const string AppDataWorkspaceDirectoryName = "HakamiqCHDTool";
    public const string OperationFolderPrefix = "Operation_";

    public static string ResolvePendingWorkspaceRoot(string outputRoot, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.UseCustomPendingWorkspace
            && settings.PendingWorkspaceMode == PendingWorkspaceMode.Custom
            && !string.IsNullOrWhiteSpace(settings.PendingWorkspaceCustomRoot))
        {
            return NormalizeFullPath(settings.PendingWorkspaceCustomRoot.Trim());
        }

        string anchor = string.IsNullOrWhiteSpace(outputRoot)
            ? Environment.CurrentDirectory
            : outputRoot.Trim();

        string fullAnchor = NormalizeFullPath(anchor);
        return NormalizeFullPath(Path.Combine(fullAnchor, WorkspaceDirectoryName, WorkspaceOperationsDirectoryName));
    }

    public static bool IsReservedWorkspaceDirectoryName(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        string value = directoryName.Trim();

        return string.Equals(value, WorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, LegacyDriveWorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, LegacyOutputPendingDirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, AppDataWorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReservedWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = NormalizeFullPath(path);
            string[] segments = fullPath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                if (IsReservedWorkspaceDirectoryName(segment))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }

        return false;
    }

    public static bool IsKnownWorkspaceRoot(string path, AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = NormalizeFullPath(path);

            if (IsCustomWorkspaceRoot(fullPath, settings))
            {
                return true;
            }

            string? directoryName = Path.GetFileName(fullPath);
            string? parentPath = Directory.GetParent(fullPath)?.FullName;
            string? parentName = string.IsNullOrWhiteSpace(parentPath) ? null : Path.GetFileName(parentPath);

            return string.Equals(directoryName, WorkspaceOperationsDirectoryName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(parentName, WorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, LegacyPendingDirectoryName, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(parentName, LegacyDriveWorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parentName, AppDataWorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase))
                || string.Equals(directoryName, LegacyOutputPendingDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    public static bool IsKnownWorkspaceJobDirectory(string path, AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = NormalizeFullPath(path);
            string? parentPath = Directory.GetParent(fullPath)?.FullName;

            if (string.IsNullOrWhiteSpace(parentPath)
                || !IsKnownWorkspaceRoot(parentPath, settings)
                || PathsEqual(fullPath, parentPath))
            {
                return false;
            }

            string jobName = Path.GetFileName(fullPath);
            return IsKnownWorkspaceJobDirectoryName(jobName);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    public static bool IsPathUnderKnownWorkspace(string path, AppSettings? settings)
    {
        return TryGetKnownWorkspaceRootForPath(path, settings, out _);
    }

    public static bool TryGetKnownWorkspaceRootForPath(
        string path,
        AppSettings? settings,
        out string rootPath)
    {
        rootPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string current = NormalizeFullPath(path);

            while (true)
            {
                if (IsKnownWorkspaceRoot(current, settings))
                {
                    rootPath = current;
                    return true;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return false;
                }

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    public static IEnumerable<string> EnumerateLegacyWorkspaceRootCandidates(AppSettings? settings)
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            yield return Path.Combine(localApplicationData, AppDataWorkspaceDirectoryName, LegacyPendingDirectoryName);
        }

        if (settings is not null
            && settings.UseCustomPendingWorkspace
            && settings.PendingWorkspaceMode == PendingWorkspaceMode.Custom
            && !string.IsNullOrWhiteSpace(settings.PendingWorkspaceCustomRoot))
        {
            yield return settings.PendingWorkspaceCustomRoot.Trim();
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            drives = [];
        }

        foreach (DriveInfo drive in drives)
        {
            string rootDirectory;

            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                rootDirectory = drive.RootDirectory.FullName;
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rootDirectory))
            {
                yield return Path.Combine(rootDirectory, LegacyDriveWorkspaceDirectoryName, LegacyPendingDirectoryName);
            }
        }
    }

    public static bool IsKnownWorkspaceJobDirectoryName(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        string value = directoryName.Trim();

        return value.StartsWith(OperationFolderPrefix, StringComparison.OrdinalIgnoreCase)
            || IsLegacyTimestampJobDirectoryName(value);
    }

    private static bool IsCustomWorkspaceRoot(string fullPath, AppSettings? settings)
    {
        if (settings is null
            || !settings.UseCustomPendingWorkspace
            || settings.PendingWorkspaceMode != PendingWorkspaceMode.Custom
            || string.IsNullOrWhiteSpace(settings.PendingWorkspaceCustomRoot))
        {
            return false;
        }

        try
        {
            return PathsEqual(fullPath, settings.PendingWorkspaceCustomRoot.Trim());
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsLegacyTimestampJobDirectoryName(string value)
    {
        if (value.Length < 18 || value[17] != '_')
        {
            return false;
        }

        for (int i = 0; i < 17; i++)
        {
            if (!char.IsDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeFullPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizeFullPath(left), NormalizeFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedPathException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or System.Security.SecurityException;
}
