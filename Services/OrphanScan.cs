using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services;

internal sealed class OrphanedWorkItemScanner
{
    private static readonly ILogger Logger = Log.ForContext<OrphanedWorkItemScanner>();
    private static readonly string[] KnownWorkspaceRootNames = ["TempExtraction", "BinCueRescue", "RedumpSync"];

    private readonly AppSettings? _settings;
    private readonly TimeSpan _minimumAge;
    private readonly IReadOnlyList<string> _additionalPendingRoots;

    public OrphanedWorkItemScanner()
        : this(null, TimeSpan.FromMinutes(2))
    {
    }

    public OrphanedWorkItemScanner(AppSettings? settings)
        : this(settings, TimeSpan.FromMinutes(2))
    {
    }

    public OrphanedWorkItemScanner(TimeSpan minimumAge)
        : this(null, minimumAge)
    {
    }

    public OrphanedWorkItemScanner(AppSettings? settings, TimeSpan minimumAge)
        : this(settings, minimumAge, [])
    {
    }

    public OrphanedWorkItemScanner(
        AppSettings? settings,
        TimeSpan minimumAge,
        IEnumerable<string>? additionalPendingRoots)
    {
        _settings = settings;
        _minimumAge = minimumAge < TimeSpan.Zero ? TimeSpan.Zero : minimumAge;
        _additionalPendingRoots = NormalizeAdditionalPendingRoots(additionalPendingRoots);
    }

    public OrphanedWorkItemScanResult Scan(
        TimeSpan minimumAge,
        IEnumerable<string>? additionalPendingRoots)
    {
        return new OrphanedWorkItemScanner(_settings, minimumAge, additionalPendingRoots).Scan();
    }

    public OrphanedWorkItemScanResult Scan()
    {
        string processTempRoot;
        try
        {
            processTempRoot = AppPaths.ProcessTempRoot;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Logger.Debug(ex, "Orphaned work item scan skipped because the process temp root is unavailable.");
            processTempRoot = string.Empty;
        }

        if (HasPotentiallyActiveSiblingWorkload())
        {
            Logger.Debug("Orphaned work item scan skipped because another Hakamiq/chdman/7zip process appears to be active.");
            return OrphanedWorkItemScanResult.Empty;
        }

        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow.Subtract(_minimumAge);
        List<OrphanedWorkItem> items = new();
        var rootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanProcessTempWorkspaces(processTempRoot, cutoffUtc, items, rootPaths);
        ScanPendingConversionWorkspaces(cutoffUtc, items, rootPaths);

        if (items.Count == 0)
        {
            return OrphanedWorkItemScanResult.Empty;
        }

        return new OrphanedWorkItemScanResult(
            items
                .OrderByDescending(static item => item.SizeBytes)
                .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            rootPaths
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void ScanProcessTempWorkspaces(
        string processTempRoot,
        DateTimeOffset cutoffUtc,
        List<OrphanedWorkItem> items,
        HashSet<string> rootPaths)
    {
        if (string.IsNullOrWhiteSpace(processTempRoot) || !Directory.Exists(processTempRoot))
        {
            return;
        }

        foreach (string workspaceRoot in EnumerateKnownWorkspaceRoots(processTempRoot))
        {
            foreach (string directory in EnumerateTopLevelDirectories(workspaceRoot))
            {
                if (IsReparsePoint(directory) || !AppPaths.IsPathUnderProcessTempRoot(directory))
                {
                    continue;
                }

                if (!TryGetLastWriteUtc(directory, isDirectory: true, out DateTimeOffset lastWriteUtc)
                    || lastWriteUtc > cutoffUtc)
                {
                    continue;
                }

                OrphanedWorkItem item = CreateDirectoryItem(directory, OrphanedWorkItemKind.Directory, lastWriteUtc, settings: null);
                if (item.SizeBytes > 0 || item.FileCount > 0)
                {
                    items.Add(item);
                    rootPaths.Add(workspaceRoot);
                }
            }
        }

        foreach (string file in EnumerateTopLevelFiles(processTempRoot))
        {
            if (IsReparsePoint(file) || !AppPaths.IsPathUnderProcessTempRoot(file))
            {
                continue;
            }

            if (!TryGetLastWriteUtc(file, isDirectory: false, out DateTimeOffset lastWriteUtc)
                || lastWriteUtc > cutoffUtc)
            {
                continue;
            }

            OrphanedWorkItem item = CreateFileItem(file, lastWriteUtc);
            if (item.SizeBytes > 0)
            {
                items.Add(item);
                rootPaths.Add(processTempRoot);
            }
        }
    }

    private void ScanPendingConversionWorkspaces(
        DateTimeOffset cutoffUtc,
        List<OrphanedWorkItem> items,
        HashSet<string> rootPaths)
    {
        foreach (string pendingRoot in EnumeratePendingWorkspaceRoots())
        {
            foreach (string directory in EnumerateTopLevelDirectories(pendingRoot))
            {
                if (IsReparsePoint(directory) || !AppPaths.IsKnownPendingWorkspaceJobDirectory(directory, _settings))
                {
                    continue;
                }

                if (!TryGetLastWriteUtc(directory, isDirectory: true, out DateTimeOffset lastWriteUtc)
                    || lastWriteUtc > cutoffUtc)
                {
                    continue;
                }

                OrphanedWorkItem item = CreateDirectoryItem(directory, OrphanedWorkItemKind.PendingDirectory, lastWriteUtc, _settings);
                if (item.SizeBytes > 0 || item.FileCount > 0)
                {
                    items.Add(item);
                    rootPaths.Add(pendingRoot);
                }
            }
        }
    }

    private IEnumerable<string> EnumeratePendingWorkspaceRoots()
    {
        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in AppPaths.EnumerateKnownPendingWorkspaceRoots(_settings))
        {
            if (!IsReparsePoint(root) && returned.Add(root))
            {
                yield return root;
            }
        }

        foreach (string root in _additionalPendingRoots)
        {
            if (!IsReparsePoint(root) && returned.Add(root))
            {
                yield return root;
            }
        }
    }

    private static IReadOnlyList<string> NormalizeAdditionalPendingRoots(IEnumerable<string>? roots)
    {
        if (roots is null)
        {
            return [];
        }

        List<string> result = [];
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            try
            {
                string fullPath = Path.GetFullPath(root.Trim());
                if (Directory.Exists(fullPath)
                    && !IsReparsePoint(fullPath)
                    && !result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(fullPath);
                }
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateKnownWorkspaceRoots(string processTempRoot)
    {
        foreach (string name in KnownWorkspaceRootNames)
        {
            string path = Path.Combine(processTempRoot, name);

            if (Directory.Exists(path)
                && !IsReparsePoint(path)
                && AppPaths.IsPathUnderProcessTempRoot(path))
            {
                yield return path;
            }
        }
    }

    private static bool HasPotentiallyActiveSiblingWorkload()
    {
        int currentProcessId = Environment.ProcessId;
        string? currentProcessName = null;

        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            currentProcessName = currentProcess.ProcessName;
        }
        catch
        {
        }

        string[] processNames = string.IsNullOrWhiteSpace(currentProcessName)
            ? ["HakamiqChdTool", "chdman", "7z", "7za"]
            : [currentProcessName, "HakamiqChdTool", "chdman", "7z", "7za"];

        foreach (string processName in processNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        if (process.Id != currentProcessId && !process.HasExited)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Logger.Debug(ex, "Could not inspect process list for orphaned work item safety. ProcessName={ProcessName}", processName);
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateTopLevelDirectories(string root)
    {
        try
        {
            if (IsReparsePoint(root))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => !IsReparsePoint(path))
                .ToArray();
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is DirectoryNotFoundException)
        {
            Logger.Debug(ex, "Orphaned work item directory enumeration failed. Root={Root}", root);
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateTopLevelFiles(string root)
    {
        try
        {
            if (IsReparsePoint(root))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => !IsReparsePoint(path))
                .ToArray();
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is DirectoryNotFoundException)
        {
            Logger.Debug(ex, "Orphaned work item file enumeration failed. Root={Root}", root);
            return Array.Empty<string>();
        }
    }

    private static OrphanedWorkItem CreateDirectoryItem(
        string directory,
        OrphanedWorkItemKind kind,
        DateTimeOffset lastWriteUtc,
        AppSettings? settings)
    {
        long sizeBytes = 0;
        int fileCount = 0;

        try
        {
            foreach (string file in EnumerateFilesWithoutReparse(directory))
            {
                if (!AppPaths.IsPathUnderProcessTempRoot(file)
                    && !AppPaths.IsPathUnderKnownPendingWorkspace(file, settings))
                {
                    continue;
                }

                try
                {
                    FileInfo info = new(file);
                    sizeBytes = SaturatingAdd(sizeBytes, Math.Max(0, info.Length));
                    fileCount = fileCount == int.MaxValue ? int.MaxValue : fileCount + 1;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    Logger.Debug(ex, "Could not measure orphaned temp file. File={File}", file);
                }
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is DirectoryNotFoundException)
        {
            Logger.Debug(ex, "Could not measure orphaned temp directory. Directory={Directory}", directory);
        }

        return new OrphanedWorkItem(
            Path.GetFullPath(directory),
            kind,
            sizeBytes,
            fileCount,
            lastWriteUtc);
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparse(string rootDirectory)
    {
        if (IsReparsePoint(rootDirectory))
        {
            yield break;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootDirectory);

        while (pendingDirectories.Count > 0)
        {
            string currentDirectory = pendingDirectories.Pop();
            if (IsReparsePoint(currentDirectory))
            {
                continue;
            }

            foreach (string file in EnumerateTopLevelFiles(currentDirectory))
            {
                if (!IsReparsePoint(file))
                {
                    yield return file;
                }
            }

            foreach (string childDirectory in EnumerateTopLevelDirectories(currentDirectory))
            {
                if (!IsReparsePoint(childDirectory))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }
        }
    }

    private static OrphanedWorkItem CreateFileItem(string file, DateTimeOffset lastWriteUtc)
    {
        long sizeBytes = 0;

        try
        {
            if (!IsReparsePoint(file))
            {
                sizeBytes = Math.Max(0, new FileInfo(file).Length);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            Logger.Debug(ex, "Could not measure orphaned temp file. File={File}", file);
        }

        return new OrphanedWorkItem(
            Path.GetFullPath(file),
            OrphanedWorkItemKind.File,
            sizeBytes,
            sizeBytes > 0 ? 1 : 0,
            lastWriteUtc);
    }

    private static bool TryGetLastWriteUtc(string path, bool isDirectory, out DateTimeOffset lastWriteUtc)
    {
        lastWriteUtc = DateTimeOffset.MinValue;

        try
        {
            if (IsReparsePoint(path))
            {
                return false;
            }

            DateTime value = isDirectory
                ? Directory.GetLastWriteTimeUtc(path)
                : File.GetLastWriteTimeUtc(path);

            if (value == DateTime.MinValue)
            {
                return false;
            }

            lastWriteUtc = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            Logger.Debug(ex, "Could not read orphaned work item timestamp. Path={Path}", path);
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Logger.Debug(ex, "Could not inspect orphaned work item attributes. Path={Path}", path);
            return true;
        }
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;
}