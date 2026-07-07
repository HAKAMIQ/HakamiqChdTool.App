using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class RedumpLocalLibraryScanner
{
    public Task<RedumpLocalLibraryScanResult> ScanAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        string normalizedRoot;

        try
        {
            normalizedRoot = string.IsNullOrWhiteSpace(rootPath)
                ? string.Empty
                : Path.GetFullPath(rootPath.Trim());

            if (string.IsNullOrWhiteSpace(normalizedRoot)
                || !Directory.Exists(normalizedRoot)
                || IsReparsePoint(normalizedRoot))
            {
                return Task.FromResult(new RedumpLocalLibraryScanResult
                {
                    RootPath = normalizedRoot
                });
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(normalizedRoot, nameof(rootPath));
        }
        catch (Exception ex) when (IsExpectedFileException(ex))
        {
            return Task.FromResult(new RedumpLocalLibraryScanResult
            {
                RootPath = string.Empty
            });
        }

        return Task.Run(
            () => ScanCore(normalizedRoot, cancellationToken),
            cancellationToken);
    }

    private static RedumpLocalLibraryScanResult ScanCore(
        string rootPath,
        CancellationToken cancellationToken)
    {
        int totalFileCount = 0;
        long totalSizeBytes = 0;
        int datFileCount = 0;
        int xmlFileCount = 0;
        int cueFileCount = 0;
        int gdiFileCount = 0;
        int sbiFileCount = 0;
        int lsdFileCount = 0;
        int keyFileCount = 0;
        int dkeyFileCount = 0;
        DateTime? newestModifiedLocal = null;
        HashSet<string> topLevelFolders = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in EnumerateFilesSafe(rootPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info;
            try
            {
                string fullPath = Path.GetFullPath(file);

                if (!IsSamePathOrChild(fullPath, rootPath) || IsReparsePoint(fullPath))
                {
                    continue;
                }

                ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(file));

                info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    continue;
                }
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                continue;
            }

            totalFileCount++;
            totalSizeBytes = SaturatingAdd(totalSizeBytes, Math.Max(0L, info.Length));

            DateTime modified = info.LastWriteTime;
            if (newestModifiedLocal is null || modified > newestModifiedLocal.Value)
            {
                newestModifiedLocal = modified;
            }

            string relative = Path.GetRelativePath(rootPath, info.FullName);
            string top = GetTopSegment(relative);
            if (!string.IsNullOrWhiteSpace(top))
            {
                topLevelFolders.Add(top);
            }

            switch (info.Extension.ToLowerInvariant())
            {
                case ".dat":
                    datFileCount++;
                    break;

                case ".xml":
                    xmlFileCount++;
                    break;

                case ".cue":
                    cueFileCount++;
                    break;

                case ".gdi":
                    gdiFileCount++;
                    break;

                case ".sbi":
                    sbiFileCount++;
                    break;

                case ".lsd":
                    lsdFileCount++;
                    break;

                case ".key":
                    keyFileCount++;
                    break;

                case ".dkey":
                    dkeyFileCount++;
                    break;
            }
        }

        return new RedumpLocalLibraryScanResult
        {
            RootPath = rootPath,
            TotalFileCount = totalFileCount,
            TotalSizeBytes = totalSizeBytes,
            TopLevelFolderCount = topLevelFolders.Count,
            DatFileCount = datFileCount,
            XmlFileCount = xmlFileCount,
            CueFileCount = cueFileCount,
            GdiFileCount = gdiFileCount,
            SbiFileCount = sbiFileCount,
            LsdFileCount = lsdFileCount,
            KeyFileCount = keyFileCount,
            DkeyFileCount = dkeyFileCount,
            NewestModifiedLocal = newestModifiedLocal
        };
    }

    private static IEnumerable<string> EnumerateFilesSafe(
        string rootPath,
        CancellationToken cancellationToken)
    {
        Stack<string> pending = new();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDirectory = pending.Pop();

            if (!IsSamePathOrChild(currentDirectory, rootPath) || IsReparsePoint(currentDirectory))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                continue;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(file)
                    && IsSamePathOrChild(file, rootPath)
                    && !IsReparsePoint(file))
                {
                    yield return file;
                }
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                continue;
            }

            foreach (string directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(directory)
                    && IsSamePathOrChild(directory, rootPath)
                    && !IsReparsePoint(directory))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static string GetTopSegment(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        int slash = relativePath.IndexOf(Path.DirectorySeparatorChar);
        int altSlash = relativePath.IndexOf(Path.AltDirectorySeparatorChar);

        int index = slash < 0
            ? altSlash
            : altSlash < 0
                ? slash
                : Math.Min(slash, altSlash);

        return index < 0
            ? "<root>"
            : relativePath[..index];
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedFileException(ex))
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

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }

    private static bool IsExpectedFileException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or System.Security.SecurityException;
    }
}