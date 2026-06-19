using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed record CsoToolLocation(
    bool IsFound,
    string ToolPath,
    string ToolsFolderPath);

public sealed class CsoToolLocator
{
    public const string ToolExecutableName = "hakamiq-cso.exe";

    public string BundledToolsFolderPath => Path.Combine(AppContext.BaseDirectory, "Tools", "hakamiq-cso", "win-x64");

    public IReadOnlyList<string> EnumerateCandidatePaths() =>
    [
        Path.Combine(BundledToolsFolderPath, ToolExecutableName)
    ];

    public CsoToolLocation Locate()
    {
        foreach (string candidate in EnumerateCandidatePaths())
        {
            if (TryValidateCandidate(candidate, out string normalized))
            {
                return new CsoToolLocation(true, normalized, Path.GetDirectoryName(normalized) ?? BundledToolsFolderPath);
            }
        }

        return new CsoToolLocation(false, string.Empty, BundledToolsFolderPath);
    }

    private static bool TryValidateCandidate(string candidatePath, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(candidatePath.Trim());
            if (!File.Exists(fullPath)
                || !string.Equals(Path.GetFileName(fullPath), ToolExecutableName, StringComparison.OrdinalIgnoreCase)
                || IsReparsePoint(fullPath))
            {
                return false;
            }

            FileInfo info = new(fullPath);
            if (info.Length <= 0)
            {
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            return true;
        }
    }
}
