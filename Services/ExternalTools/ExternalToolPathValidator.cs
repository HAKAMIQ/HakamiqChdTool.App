using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

internal static class ExternalToolPathValidator
{
    internal static bool TryValidateCandidate(string candidatePath, string expectedFileName, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(expectedFileName)) return false;

        try
        {
            string fullPath = Path.GetFullPath(candidatePath.Trim());
            if (!File.Exists(fullPath)
                || !string.Equals(Path.GetFileName(fullPath), expectedFileName, StringComparison.OrdinalIgnoreCase))
                return false;

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(candidatePath));
            if (new FileInfo(fullPath).Length <= 0) return false;

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            return false;
        }
    }
}
