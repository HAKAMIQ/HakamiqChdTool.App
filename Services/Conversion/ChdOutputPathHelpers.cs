using Serilog;
using System;
using System.IO;
using static HakamiqChdTool.App.Services.ChdConversionMessages;

namespace HakamiqChdTool.App.Services;

internal static class ChdOutputPathHelpers
{
    internal static string BuildSingleBinExtractCdBinOutputPath(string cueOutputPath)
    {
        string? directory = Path.GetDirectoryName(cueOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
        }

        string stem = Path.GetFileNameWithoutExtension(cueOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "track" : stem;
        return Path.Combine(directory, $"{stem}.bin");
    }

    internal static bool ContainsTrackToken(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Contains("%t", StringComparison.OrdinalIgnoreCase);

    internal static bool TryResolveCompanionPathWithinDirectory(
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

    internal static string EnsureTrailingDirectorySeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
