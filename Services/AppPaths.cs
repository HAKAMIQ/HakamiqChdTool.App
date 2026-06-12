using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HakamiqChdTool.App.Services;

public static class AppPaths
{
    public const string AppDirectoryName = "HakamiqChdTool";

    private const long MaximumConfigProbeBytes = 1024 * 1024;

    private const string TempSegmentRequiredMessageKey = "LocAppPaths_TempSegmentRequired";
    private const string EmptyTempSegmentMessageKey = "LocAppPaths_EmptyTempSegment";
    private const string UnsafeTempSegmentMessageKey = "LocAppPaths_UnsafeTempSegment";
    private const string SystemTempUnavailableMessageKey = "LocAppPaths_SystemTempUnavailable";
    private const string UnsafeSystemTempRootMessageKey = "LocAppPaths_UnsafeSystemTempRoot";
    private const string OutsideProcessTempRootMessageKey = "LocAppPaths_OutsideProcessTempRoot";

    private static readonly object ModeSync = new();
    private static readonly Lazy<string> LazyProcessTempRoot = new(InitializeProcessTempRoot);

    private static bool _portableMode;

    public static bool PortableMode
    {
        get
        {
            lock (ModeSync)
            {
                return _portableMode;
            }
        }
    }

    public static string LocalAppRoot => EnsureDirectory(LocalAppRootPath);

    public static string PortableRoot => EnsureDirectory(PortableRootPath);

    public static string ActiveDataRoot => PortableMode
        ? PortableRoot
        : LocalAppRoot;

    public static string ConfigFilePath => Path.Combine(ActiveDataRoot, "config.json");

    public static string LegacyConfigFilePath => Path.Combine(LocalAppRoot, "config.json");

    public static string PortableConfigFilePath => Path.Combine(PortableRoot, "config.json");

    public static string LogsDirectory => EnsureDirectory(Path.Combine(ActiveDataRoot, "Logs"));

    public static string RuntimeDirectory => EnsureDirectory(Path.Combine(LocalAppRoot, ".runtime"));

    public static string ProcessTempRoot => LazyProcessTempRoot.Value;

    public static string TempDirectory => ProcessTempRoot;

    private static string LocalAppRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDirectoryName);

    private static string PortableRootPath => Path.Combine(
        AppContext.BaseDirectory,
        ".portable");

    private static string LegacyConfigFilePathNoCreate => Path.Combine(LocalAppRootPath, "config.json");

    private static string PortableConfigFilePathNoCreate => Path.Combine(PortableRootPath, "config.json");

    public static void SetPortableMode(bool enabled)
    {
        lock (ModeSync)
        {
            _portableMode = enabled;
        }
    }

    public static bool DetectPortableModePreference()
    {
        try
        {
            if (File.Exists(PortableConfigFilePathNoCreate)
                && !IsReparsePoint(PortableConfigFilePathNoCreate)
                && !HasReparsePointInExistingPath(PortableConfigFilePathNoCreate, AppContext.BaseDirectory))
            {
                return true;
            }

            return File.Exists(LegacyConfigFilePathNoCreate)
                && !IsReparsePoint(LegacyConfigFilePathNoCreate)
                && !HasReparsePointInExistingPath(LegacyConfigFilePathNoCreate, LocalAppRootPath)
                && TryReadPortableModeFlag(LegacyConfigFilePathNoCreate);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    public static string CombineProcessTemp(params string[] segments)
    {
        if (segments is null || segments.Length == 0)
        {
            throw new ArgumentException(TempSegmentRequiredMessageKey, nameof(segments));
        }

        string combined = ProcessTempRoot;

        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new ArgumentException(EmptyTempSegmentMessageKey, nameof(segments));
            }

            if (Path.IsPathRooted(segment) || ContainsParentTraversalSegment(segment))
            {
                throw new InvalidOperationException(UnsafeTempSegmentMessageKey);
            }

            combined = Path.Combine(combined, segment);
        }

        return AssertUnderProcessTempRoot(combined);
    }

    public static bool IsPathUnderProcessTempRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string root = ProcessTempRoot;

            return IsSamePathOrChild(path, root)
                   && !HasReparsePointInExistingPath(path, root);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    public static IEnumerable<string> EnumerateKnownPendingWorkspaceRoots(AppSettings? settings)
    {
        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in EnumerateKnownPendingWorkspaceRootCandidates(settings))
        {
            if (!TryNormalizeExistingSafeDirectory(candidate, out string normalized))
            {
                continue;
            }

            if (returned.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    public static bool IsPathUnderKnownPendingWorkspace(string path, AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (PendingWorkspacePathPolicy.TryGetKnownWorkspaceRootForPath(path, settings, out string root))
            {
                return !HasReparsePointInExistingPath(path, root);
            }
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
        }

        return false;
    }

    public static bool IsKnownPendingWorkspaceJobDirectory(string path, AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            string? parent = Directory.GetParent(fullPath)?.FullName;

            if (string.IsNullOrWhiteSpace(parent)
                || !PendingWorkspacePathPolicy.IsKnownWorkspaceJobDirectory(fullPath, settings)
                || HasReparsePointInExistingPath(fullPath, parent))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
        }

        return false;
    }

    public static bool TryGetKnownPendingWorkspaceRootForPath(
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
            if (PendingWorkspacePathPolicy.TryGetKnownWorkspaceRootForPath(path, settings, out string root)
                && !HasReparsePointInExistingPath(path, root))
            {
                rootPath = root;
                return true;
            }
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
        }

        return false;
    }

    private static IEnumerable<string> EnumerateKnownPendingWorkspaceRootCandidates(AppSettings? settings)
    {
        foreach (string candidate in PendingWorkspacePathPolicy.EnumerateLegacyWorkspaceRootCandidates(settings))
        {
            yield return candidate;
        }
    }

    private static bool TryNormalizeExistingSafeDirectory(string path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!Directory.Exists(fullPath)
                || IsUnsafeDeletionRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    private static bool IsUnsafeDeletionRoot(string path)
    {
        try
        {
            string fullPath = TrimDirectorySeparators(Path.GetFullPath(path));
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool TryReadPortableModeFlag(string configPath)
    {
        try
        {
            FileInfo configFile = new(configPath);
            if (!configFile.Exists
                || configFile.Length <= 0
                || configFile.Length > MaximumConfigProbeBytes
                || HasReparsePointInExistingPathFromVolumeRoot(configPath))
            {
                return false;
            }

            using FileStream stream = File.OpenRead(configPath);
            using JsonDocument document = JsonDocument.Parse(stream);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("PortableMode", out JsonElement value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException
                                   or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(UnsafeSystemTempRootMessageKey);
        }

        string fullPath = Path.GetFullPath(path);

        if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(UnsafeSystemTempRootMessageKey);
        }

        Directory.CreateDirectory(fullPath);

        if (!Directory.Exists(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(UnsafeSystemTempRootMessageKey);
        }

        return fullPath;
    }

    private static string InitializeProcessTempRoot()
    {
        string systemTempCandidate = Path.GetTempPath();

        if (string.IsNullOrWhiteSpace(systemTempCandidate))
        {
            throw new InvalidOperationException(SystemTempUnavailableMessageKey);
        }

        string systemTemp = EnsureUsableSystemTempRoot(systemTempCandidate);

        string root = Path.GetFullPath(Path.Combine(systemTemp, AppDirectoryName));

        if (!IsSamePathOrChild(root, systemTemp)
            || PathsEqual(root, systemTemp)
            || HasReparsePointInExistingPath(root, systemTemp))
        {
            throw new InvalidOperationException(UnsafeSystemTempRootMessageKey);
        }

        Directory.CreateDirectory(root);

        if (!Directory.Exists(root)
            || IsUnsafeDeletionRoot(root)
            || HasReparsePointInExistingPath(root, systemTemp))
        {
            throw new InvalidOperationException(UnsafeSystemTempRootMessageKey);
        }

        return root;
    }

    private static string EnsureUsableSystemTempRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(SystemTempUnavailableMessageKey);
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);

        if (!Directory.Exists(fullPath)
            || IsUnsafeDeletionRoot(fullPath)
            || IsReparsePoint(fullPath))
        {
            throw new InvalidOperationException(UnsafeSystemTempRootMessageKey);
        }

        return fullPath;
    }

    private static string AssertUnderProcessTempRoot(string candidatePath)
    {
        string fullPath = Path.GetFullPath(candidatePath);
        string root = ProcessTempRoot;

        if (!IsSamePathOrChild(fullPath, root)
            || HasReparsePointInExistingPath(fullPath, root))
        {
            throw new InvalidOperationException(OutsideProcessTempRootMessageKey);
        }

        return fullPath;
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsPathOrIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}