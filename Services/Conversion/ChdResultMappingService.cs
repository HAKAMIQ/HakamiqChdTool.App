using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static HakamiqChdTool.App.Services.ChdConversionMessages;
using static HakamiqChdTool.App.Services.ChdOutputPathHelpers;

namespace HakamiqChdTool.App.Services;

public sealed class ChdResultMappingService : IChdResultMappingService
{
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
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Warning(ex, "Could not resolve incomplete output path for cleanup. Output={OutputPath}, Reason={Reason}", outputPath, reason);
            return;
        }

        IReadOnlyList<string> knownCompanions = isExtractCommand
            ? ResolveKnownExtractionCompanions(fullOutputPath)
            : Array.Empty<string>();

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
            result.Add(BuildSingleBinExtractCdBinOutputPath(outputPath));

            foreach (string trackOutput in EnumerateExtractCdTrackPatternOutputs(directory, stem))
            {
                result.Add(trackOutput);
            }

            foreach (string referenced in TryReadCueReferencedFiles(outputPath))
            {
                if (TryResolveCompanionPathWithinDirectory(directory, referenced, out string? companion)
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
                if (TryResolveCompanionPathWithinDirectory(directory, referenced, out string? companion)
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

        string pattern = $"{stem} (Track *).bin";
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
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
        if (!File.Exists(cuePath))
        {
            yield break;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(cuePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not read incomplete CUE file for companion cleanup. CuePath={CuePath}", cuePath);
            yield break;
        }

        foreach (string line in lines)
        {
            if (CueSheetFileStatementReader.TryRead(line, out string value, out _))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> TryReadGdiReferencedFiles(string gdiPath)
    {
        if (!File.Exists(gdiPath))
        {
            yield break;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(gdiPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not read incomplete GDI file for companion cleanup. GdiPath={GdiPath}", gdiPath);
            yield break;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
            {
                yield return parts[4].Trim();
            }
        }
    }

    private static bool TryResolveCompanionPathWithinDirectory(
        string directory,
        string referencedFileName,
        out string? companionPath)
    {
        companionPath = null;

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(referencedFileName))
        {
            return false;
        }

        if (Path.IsPathRooted(referencedFileName))
        {
            return false;
        }

        try
        {
            string fullDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(directory));
            string combined = Path.GetFullPath(Path.Combine(fullDirectory, referencedFileName));

            if (!combined.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            companionPath = combined;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(ex, "Rejected invalid companion output path. Directory={Directory}, Reference={Reference}", directory, referencedFileName);
            return false;
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
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
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

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
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
            if (!TryResolveCompanionPathWithinDirectory(directory, referenced, out string? candidate)
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

        string[] lines;
        try
        {
            lines = File.ReadAllLines(gdiPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not read GDI file for verification. GdiPath={GdiPath}", gdiPath);
            return false;
        }

        if (lines.Length < 2)
        {
            return false;
        }

        bool foundReferencedTrack = false;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || string.IsNullOrWhiteSpace(parts[4]))
            {
                return false;
            }

            string referenced = parts[4].Trim();

            if (!TryResolveCompanionPathWithinDirectory(directory, referenced, out string? candidate)
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


}
