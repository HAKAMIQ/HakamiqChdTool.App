using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public sealed class SevenZipToolService
{
    private const string FoundMessageKey = "LocArchive_SevenZipToolFound";
    private const string MissingMessageKey = "LocArchive_SevenZipToolMissing";
    private const string BundledSevenZipExeSha256 = "83967F1B02B43C4EFEDA302795722C809E0E81B8307DE73558D10484D5676A7D";
    private const string BundledSevenZipDllSha256 = "69FD4DF057985C40E510E2FAC182881C7F85E90AA13EC703F763A8FDB2CE61F8";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<SevenZipToolService>();
    private static readonly Lazy<SevenZipToolService> LazyInstance = new(() => new SevenZipToolService());
    private static readonly byte[] ExpectedBundledSevenZipExeSha256 = Convert.FromHexString(BundledSevenZipExeSha256);
    private static readonly byte[] ExpectedBundledSevenZipDllSha256 = Convert.FromHexString(BundledSevenZipDllSha256);

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

    internal static bool IsValidSevenZipExecutable(string path)
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

            if (HasReparsePointInExistingPathFromVolumeRoot(dllPath))
            {
                return false;
            }

            return !IsBundledDirectory(directory)
                || HasExpectedSha256(fullPath, ExpectedBundledSevenZipExeSha256)
                && HasExpectedSha256(dllPath, ExpectedBundledSevenZipDllSha256);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsBundledDirectory(string directory)
    {
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);

        return PathsEqual(directory, Path.Combine(baseDirectory, "Tools", "7zip"))
            || PathsEqual(directory, Path.Combine(baseDirectory, "7zip"))
            || PathsEqual(directory, baseDirectory);
    }

    private static bool HasExpectedSha256(string path, byte[] expectedSha256)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);

        byte[] actualSha256 = SHA256.HashData(stream);
        return CryptographicOperations.FixedTimeEquals(expectedSha256, actualSha256);
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
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);

        if (!string.IsNullOrWhiteSpace(root)
            && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
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
