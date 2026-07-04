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
        string normalizedRoot = string.IsNullOrWhiteSpace(rootPath)
            ? string.Empty
            : Path.GetFullPath(rootPath.Trim());

        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            return Task.FromResult(new RedumpLocalLibraryScanResult
            {
                RootPath = normalizedRoot
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

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        };

        foreach (string file in Directory.EnumerateFiles(rootPath, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info;
            try
            {
                info = new FileInfo(file);
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                continue;
            }

            totalFileCount++;
            totalSizeBytes += Math.Max(0L, info.Length);

            DateTime modified = info.LastWriteTime;
            if (newestModifiedLocal is null || modified > newestModifiedLocal.Value)
            {
                newestModifiedLocal = modified;
            }

            string relative = Path.GetRelativePath(rootPath, file);
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

    private static bool IsExpectedFileException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException;
    }
}
