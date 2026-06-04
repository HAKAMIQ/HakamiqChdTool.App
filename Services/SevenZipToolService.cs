using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public sealed class SevenZipToolService
{
    private const string FoundMessageKey = "LocArchive_SevenZipToolFound";
    private const string MissingMessageKey = "LocArchive_SevenZipToolMissing";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<SevenZipToolService>();
    private static readonly Lazy<SevenZipToolService> LazyInstance = new(() => new SevenZipToolService());

    private readonly object _sync = new();

    private bool _resolved;
    private string _executablePath = string.Empty;

    public static SevenZipToolService Instance => LazyInstance.Value;

    private SevenZipToolService()
    {
    }

    public bool IsAvailable => TryGetExecutablePath(out _);

    public bool TryGetExecutablePath(out string executablePath)
    {
        EnsureResolved();

        executablePath = _executablePath;
        return !string.IsNullOrWhiteSpace(executablePath) && IsValidSevenZipExecutable(executablePath);
    }

    public string GetStatusMessageKey() =>
        TryGetExecutablePath(out _) ? FoundMessageKey : MissingMessageKey;

    public IReadOnlyList<object?> GetStatusMessageArgs() =>
        TryGetExecutablePath(out string executablePath) ? [executablePath] : [];

    private void EnsureResolved()
    {
        if (Volatile.Read(ref _resolved))
        {
            return;
        }

        lock (_sync)
        {
            if (_resolved)
            {
                return;
            }

            _executablePath = ResolveExecutablePath();

            if (string.IsNullOrWhiteSpace(_executablePath))
            {
                Logger.Warning("7-Zip tool was not found. Archive extraction will use fallback when available.");
            }
            else
            {
                Logger.Information("7-Zip tool ready. Path={Path}", _executablePath);
            }

            Volatile.Write(ref _resolved, true);
        }
    }

    private static string ResolveExecutablePath()
    {
        foreach (string candidate in GetCandidatePaths())
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (IsValidSevenZipExecutable(fullPath))
                {
                    return fullPath;
                }
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
                Logger.Debug(ex, "7-Zip candidate path could not be evaluated. Candidate={Candidate}", candidate);
            }
        }

        return string.Empty;
    }

    private static bool IsValidSevenZipExecutable(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                return false;
            }

            if (!string.Equals(Path.GetFileName(fullPath), "7z.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            if (HasReparsePointInExistingPathFromVolumeRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(directory))
            {
                return false;
            }

            string dllPath = Path.Combine(directory, "7z.dll");
            if (!File.Exists(dllPath))
            {
                return false;
            }

            return !HasReparsePointInExistingPathFromVolumeRoot(dllPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        string baseDirectory = AppContext.BaseDirectory;

        yield return Path.Combine(baseDirectory, "Tools", "7zip", "7z.exe");
        yield return Path.Combine(baseDirectory, "7zip", "7z.exe");
        yield return Path.Combine(baseDirectory, "7z.exe");

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "7-Zip", "7z.exe");
        }
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or System.Security.SecurityException;
}