using HakamiqChdTool.App.Services.Configuration;
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

    private readonly string _preferredToolPath;

    public CsoToolLocator()
        : this(TryLoadPreferredToolPathFromSettings())
    {
    }

    public CsoToolLocator(string? preferredToolPath)
    {
        _preferredToolPath = preferredToolPath?.Trim() ?? string.Empty;
    }

    public string BundledToolsFolderPath => Path.Combine(AppContext.BaseDirectory, "Tools", "hakamiq-cso", "win-x64");

    public IReadOnlyList<string> EnumerateCandidatePaths()
    {
        List<string> candidates = new(capacity: 2);

        if (!string.IsNullOrWhiteSpace(_preferredToolPath))
        {
            candidates.Add(_preferredToolPath);
        }

        candidates.Add(Path.Combine(BundledToolsFolderPath, ToolExecutableName));

        return candidates;
    }

    public CsoToolLocation Locate()
    {
        foreach (string candidate in EnumerateCandidatePaths())
        {
            if (TryValidateCandidate(candidate, out string normalized))
            {
                return new CsoToolLocation(
                    true,
                    normalized,
                    Path.GetDirectoryName(normalized) ?? BundledToolsFolderPath);
            }
        }

        return new CsoToolLocation(false, string.Empty, BundledToolsFolderPath);
    }

    private static string TryLoadPreferredToolPathFromSettings()
    {
        try
        {
            using AppSettingsService settingsService = new();
            return settingsService.Load().ExternalCsoKitPath?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            return string.Empty;
        }
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
                || !string.Equals(Path.GetFileName(fullPath), ToolExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(candidatePath));

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
                                  or InvalidOperationException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            return false;
        }
    }
}