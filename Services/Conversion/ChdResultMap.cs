using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public sealed class ChdResultMappingService : IChdResultMappingService
{
    private const long MaxDescriptorReadBytes = 256 * 1024;
    private const int MaxTrackPatternCleanupCandidates = 256;

    public void TryDeleteIncompleteOutputs(string outputPath, bool isExtractCommand, string reason)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        string fullOutputPath;
        try
        {
            fullOutputPath = Path.GetFullPath(outputPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Log.Warning(ex, "Could not resolve incomplete output path for cleanup. Output={OutputPath}, Reason={Reason}", outputPath, reason);
            return;
        }

        IReadOnlyList<string> knownCompanions = isExtractCommand
            ? ResolveKnownExtractionCompanions(fullOutputPath)
            : [];

        TryDeleteIncompleteFile(fullOutputPath, reason);
        TryDeleteIncompleteFile(Path.ChangeExtension(fullOutputPath, ".sbi"), reason);

        foreach (string companion in knownCompanions)
        {
            TryDeleteIncompleteFile(companion, reason);
        }
    }

    private static IReadOnlyList<string> ResolveKnownExtractionCompanions(string outputPath)
    {
        var result = new List<string>();
        string? directory = Path.GetDirectoryName(outputPath);
        string stem = Path.GetFileNameWithoutExtension(outputPath);
        string extension = Path.GetExtension(outputPath);

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem))
        {
            return result;
        }

        if (string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(ChdOutputPathHelpers.BuildSingleBinExtractCdBinOutputPath(outputPath));

            foreach (string trackOutput in EnumerateExtractCdTrackPatternOutputs(directory, stem))
            {
                result.Add(trackOutput);
            }

            foreach (string referenced in TryReadCueReferencedFiles(outputPath))
            {
                if (ChdOutputPathHelpers.TryResolveCompanionPathWithinDirectory(directory, referenced, out string? companion)
                    && !string.IsNullOrWhiteSpace(companion))
                {
                    result.Add(companion);
                }
            }
        }
        else if (string.Equals(extension, ".gdi", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string referenced in TryReadGdiReferencedFiles(outputPath))
            {
                if (ChdOutputPathHelpers.TryResolveCompanionPathWithinDirectory(directory, referenced, out string? companion)
                    && !string.IsNullOrWhiteSpace(companion))
                {
                    result.Add(companion);
                }
            }
        }

        return DeduplicatePaths(result);
    }

    private static IEnumerable<string> EnumerateExtractCdTrackPatternOutputs(string directory, string stem)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(stem)
            || !Directory.Exists(directory))
        {
            yield break;
        }

        var candidates = new List<string>();
        string pattern = $"{stem} (Track *).bin";

        try
        {
            foreach (string candidate in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                candidates.Add(candidate);
                if (candidates.Count >= MaxTrackPatternCleanupCandidates)
                {
                    Log.Warning(
                        "Stopped enumerating extractcd track-pattern outputs after cleanup candidate limit. Directory={Directory}; Stem={Stem}; Limit={Limit}",
                        directory,
                        stem,
                        MaxTrackPatternCleanupCandidates);
                    break;
                }
            }
        }
        catch (Exception ex) when (IsExpectedPathOrIoException(ex))
        {
            yield break;
        }

        foreach (string candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> TryReadCueReferencedFiles(string cuePath)
    {
        foreach (string line in TryReadSmallDescriptorLines(cuePath, "CUE"))
        {
            if (CueSheetFileStatementReader.TryRead(line, out string value, out _))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> TryReadGdiReferencedFiles(string gdiPath)
    {
        IReadOnlyList<string> lines = TryReadSmallDescriptorLines(gdiPath, "GDI");
        for (int i = 1; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
            {
                yield return parts[4].Trim();
            }
        }
    }

    private static IReadOnlyList<string> TryReadSmallDescriptorLines(string descriptorPath, string descriptorKind)
    {
        if (string.IsNullOrWhiteSpace(descriptorPath))
        {
            return [];
        }

        try
        {
            string fullPath = Path.GetFullPath(descriptorPath);
            FileInfo fileInfo = new(fullPath);

            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                return [];
            }

            if (fileInfo.Length > MaxDescriptorReadBytes)
            {
                Log.Debug(
                    "Skipped oversized descriptor during result mapping. Kind={Kind}; Path={Path}; Bytes={Bytes}; Limit={Limit}",
                    descriptorKind,
                    fullPath,
                    fileInfo.Length,
                    MaxDescriptorReadBytes);
                return [];
            }

            var lines = new List<string>();

            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            using StreamReader reader = new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);

            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception ex) when (IsExpectedPathOrIoException(ex))
        {
            Log.Debug(ex, "Could not read descriptor file for result mapping. Kind={Kind}; Path={Path}", descriptorKind, descriptorPath);
            return [];
        }
    }

    private static IReadOnlyList<string> DeduplicatePaths(IEnumerable<string> paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
                Log.Debug(ex, "Rejected invalid cleanup path candidate. Path={Path}", path);
                continue;
            }

            if (seen.Add(fullPath))
            {
                result.Add(fullPath);
            }
        }

        return result;
    }

    private static void TryDeleteIncompleteFile(string? path, string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                return;
            }

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.Delete(fullPath);
                    Log.Information("Deleted incomplete chdman output. Path={Path}, Reason={Reason}", fullPath, reason);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(250);
                }
            }

            if (File.Exists(fullPath))
            {
                Log.Warning("Incomplete output still exists after retry cleanup. Path={Path}, Reason={Reason}", fullPath, reason);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete incomplete chdman output. Path={Path}, Reason={Reason}", path, reason);
        }
    }

    public bool VerifyOutputExists(string outputPath, bool isExtractCommand)
    {
        if (!File.Exists(outputPath))
        {
            return false;
        }

        if (!isExtractCommand)
        {
            return true;
        }

        try
        {
            FileInfo primary = new(outputPath);
            if (primary.Length <= 0)
            {
                return false;
            }

            string ext = primary.Extension.ToLowerInvariant();
            return ext switch
            {
                ".cue" => VerifyCueBundle(primary.FullName),
                ".gdi" => VerifyGdiBundle(primary.FullName),
                _ => true
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyCueBundle(string cuePath)
    {
        string directory = Path.GetDirectoryName(cuePath) ?? string.Empty;
        bool foundReferencedFile = false;

        foreach (string referenced in TryReadCueReferencedFiles(cuePath))
        {
            if (!ChdOutputPathHelpers.TryResolveCompanionPathWithinDirectory(directory, referenced, out string? candidate)
                || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            foundReferencedFile = true;

            if (!File.Exists(candidate))
            {
                return false;
            }

            FileInfo sidecar = new(candidate);
            if (sidecar.Length <= 0)
            {
                return false;
            }
        }

        return foundReferencedFile;
    }

    private static bool VerifyGdiBundle(string gdiPath)
    {
        string directory = Path.GetDirectoryName(gdiPath) ?? string.Empty;
        IReadOnlyList<string> lines = TryReadSmallDescriptorLines(gdiPath, "GDI");

        if (lines.Count < 2)
        {
            return false;
        }

        bool foundReferencedTrack = false;

        for (int i = 1; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || string.IsNullOrWhiteSpace(parts[4]))
            {
                return false;
            }

            string referenced = parts[4].Trim();

            if (!ChdOutputPathHelpers.TryResolveCompanionPathWithinDirectory(directory, referenced, out string? candidate)
                || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            foundReferencedTrack = true;

            if (!File.Exists(candidate))
            {
                return false;
            }

            FileInfo sidecar = new(candidate);
            if (sidecar.Length <= 0)
            {
                return false;
            }
        }

        return foundReferencedTrack;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedPathOrIoException(Exception ex) =>
        IsExpectedPathException(ex)
        || ex is IOException
        or UnauthorizedAccessException
        or InvalidDataException;
}