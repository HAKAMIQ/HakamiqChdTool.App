using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public static class RedumpCompactDisplayFormatter
{
    public static string FormatRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return "Redump";
        }

        string trimmed = rootPath.Trim();

        try
        {
            string fullPath = Path.GetFullPath(trimmed);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            string lastSegment = GetLastSegment(fullPath);

            if (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(lastSegment))
            {
                return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar +
                    "…" +
                    Path.DirectorySeparatorChar +
                    lastSegment;
            }

            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                return lastSegment;
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return CompactRawPath(trimmed);
        }

        return CompactRawPath(trimmed);
    }

    public static string FormatFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Redump";
        }

        string name = Path.GetFileNameWithoutExtension(fileName.Trim());

        name = RemoveKnownSuffix(name, " - Datfile");
        name = RemoveKnownSuffix(name, " Datfile");
        name = RemoveKnownSuffix(name, " - BIOS Images");
        name = RemoveKnownSuffix(name, " BIOS Images");

        name = name.Replace("Sony - PlayStation 2", "PS2", StringComparison.OrdinalIgnoreCase);
        name = name.Replace("Sony - PlayStation", "PS1", StringComparison.OrdinalIgnoreCase);
        name = name.Replace("Nintendo - GameCube", "GameCube", StringComparison.OrdinalIgnoreCase);
        name = name.Replace("Sega - Dreamcast", "Dreamcast", StringComparison.OrdinalIgnoreCase);
        name = name.Replace("Sega - Saturn", "Saturn", StringComparison.OrdinalIgnoreCase);
        name = name.Replace("IBM - PC compatible", "PC", StringComparison.OrdinalIgnoreCase);

        name = StripTrailingParenthesisTokens(name);

        if (name.Length <= 40)
        {
            return name.Trim();
        }

        return name.Substring(0, 37).TrimEnd() + "…";
    }

    private static string GetLastSegment(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return trimmed;
    }

    private static string CompactRawPath(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length <= 48)
        {
            return trimmed;
        }

        return trimmed.Substring(0, 18) + "…" + trimmed.Substring(trimmed.Length - 24);
    }

    private static string RemoveKnownSuffix(
        string value,
        string suffix)
    {
        int index = value.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return value;
        }

        return value.Substring(0, index).Trim();
    }

    private static string StripTrailingParenthesisTokens(string value)
    {
        string result = value.Trim();

        while (result.EndsWith(")", StringComparison.Ordinal))
        {
            int start = result.LastIndexOf('(');
            if (start < 0)
            {
                break;
            }

            string token = result.Substring(start);
            if (token.IndexOf("serial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                token.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                token.IndexOf("20", StringComparison.OrdinalIgnoreCase) >= 0 ||
                token.IndexOf("19", StringComparison.OrdinalIgnoreCase) >= 0 ||
                IsCountToken(token))
            {
                result = result.Substring(0, start).TrimEnd();
                continue;
            }

            break;
        }

        return result.Trim();
    }

    private static bool IsCountToken(string token)
    {
        if (token.Length < 3)
        {
            return false;
        }

        for (int index = 1; index < token.Length - 1; index++)
        {
            if (!char.IsDigit(token[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExpectedPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException
            or ArgumentException;
    }
}
