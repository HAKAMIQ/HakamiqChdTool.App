using System;

namespace HakamiqChdTool.App.Services.Integrity;

public sealed class IntegrityManifestEntry
{
    private string _path = string.Empty;

    private string _sha256 = string.Empty;

    public string Path
    {
        get => _path;
        set => _path = NormalizeRelativeManifestPath(value);
    }

    public string Sha256
    {
        get => _sha256;
        set => _sha256 = NormalizeSha256(value);
    }

    private static string NormalizeRelativeManifestPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim()
            .Replace('\\', '/');

        if (System.IO.Path.IsPathRooted(normalized)
            || normalized.Contains('\0', StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal)
            || ContainsParentTraversalSegment(normalized))
        {
            return string.Empty;
        }

        return normalized;
    }

    private static string NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();

        if (normalized.Length != 64)
        {
            return string.Empty;
        }

        foreach (char c in normalized)
        {
            bool isHex =
                c is >= '0' and <= '9'
                || c is >= 'a' and <= 'f'
                || c is >= 'A' and <= 'F';

            if (!isHex)
            {
                return string.Empty;
            }
        }

        return normalized.ToUpperInvariant();
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}