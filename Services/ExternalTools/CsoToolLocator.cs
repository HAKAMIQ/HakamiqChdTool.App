using HakamiqChdTool.App.Services.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace HakamiqChdTool.App.Services;

public sealed record CsoToolLocation(
    bool IsFound,
    string ToolPath,
    string ToolsFolderPath);

public sealed class CsoToolLocator
{
    public const string ToolExecutableName = "csokit.exe";
    public const string NativeLibraryName = "CsoKit.Native.dll";
    private const string BundledToolSha256 = "0A822E643F4B414F6F9CA53EE09B1DB49D3EA3AB68BC5A0145D394C86DA0E97E";
    private const string BundledNativeDllSha256 = "BEFDAB67749B019F02C104F9297A47692C596CB3E6244D934C04AEA896631C55";

    private static readonly byte[] ExpectedBundledToolSha256 = Convert.FromHexString(BundledToolSha256);
    private static readonly byte[] ExpectedBundledNativeDllSha256 = Convert.FromHexString(BundledNativeDllSha256);

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

    internal static bool TryValidateCandidate(string candidatePath, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            if (!ExternalToolPathValidator.TryValidateCandidate(candidatePath, ToolExecutableName, out string fullPath))
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string nativeLibraryPath = Path.GetFullPath(Path.Combine(directory, NativeLibraryName));
            if (!HasValidRuntimeFile(nativeLibraryPath))
            {
                return false;
            }

            if (IsBundledToolPath(fullPath)
                && !HasExpectedBundledRuntime(fullPath, nativeLibraryPath))
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

    private static bool IsBundledToolPath(string path)
    {
        string expectedPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "hakamiq-cso",
            "win-x64",
            ToolExecutableName));

        return string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidRuntimeFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(path, nameof(path));
        return new FileInfo(path).Length > 0;
    }

    private static bool HasExpectedBundledRuntime(string executablePath, string nativeLibraryPath) =>
        HasExpectedSha256(executablePath, ExpectedBundledToolSha256)
            && HasExpectedSha256(nativeLibraryPath, ExpectedBundledNativeDllSha256);

    private static bool HasExpectedSha256(string path, byte[] expectedSha256)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);

        byte[] actualSha256 = SHA256.HashData(stream);
        return CryptographicOperations.FixedTimeEquals(expectedSha256, actualSha256);
    }
}
