using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Ui.Queue;

internal sealed record RedumpNameSuggestion(
    bool IsApplicable,
    string SourcePath,
    string SourceFileName,
    string SafeFileName,
    string TargetPath,
    string ErrorMessageKey)
{
    public static RedumpNameSuggestion Blocked(string sourcePath, string errorMessageKey)
    {
        string normalizedSource = string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : sourcePath;
        string sourceFileName = string.Empty;

        if (!string.IsNullOrWhiteSpace(normalizedSource))
        {
            try
            {
                sourceFileName = Path.GetFileName(normalizedSource);
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
                sourceFileName = string.Empty;
            }
        }

        return new RedumpNameSuggestion(
            false,
            normalizedSource,
            sourceFileName,
            string.Empty,
            string.Empty,
            errorMessageKey);
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;
}

internal static class RedumpNameService
{
    private const string OriginalPathMissingMessageKey = "LocNaming_OriginalPathMissing";
    private const string OriginalFileNotFoundMessageKey = "LocNaming_OriginalFileNotFound";
    private const string SuggestedNameMissingMessageKey = "LocNaming_SuggestedNameMissing";
    private const string OriginalDirectoryMissingMessageKey = "LocNaming_OriginalDirectoryMissing";
    private const string SuggestedNameInvalidMessageKey = "LocNaming_SuggestedNameInvalid";
    private const string SuggestedNameUnsafeMessageKey = "LocNaming_SuggestedNameUnsafe";
    private const string TargetFileExistsMessageKey = "LocNaming_TargetFileExists";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static RedumpNameSuggestion Evaluate(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Evaluate(item.SourcePath, item.SuggestedStandardName);
    }

    public static RedumpNameSuggestion Evaluate(string originalPath, string suggestedFileName)
    {
        if (!TryResolveSourcePath(originalPath, out string fullOriginalPath, out string sourcePathErrorKey))
        {
            return RedumpNameSuggestion.Blocked(originalPath, sourcePathErrorKey);
        }

        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, SuggestedNameMissingMessageKey);
        }

        string directory = Path.GetDirectoryName(fullOriginalPath) ?? string.Empty;
        if (!TryValidateExistingDirectory(directory))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, OriginalDirectoryMissingMessageKey);
        }

        string sourceExtension = Path.GetExtension(fullOriginalPath);
        string safeFileName = SanitizeSuggestedFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, SuggestedNameInvalidMessageKey);
        }

        if (!HasKnownMediaFileExtension(Path.GetExtension(safeFileName)) && HasKnownMediaFileExtension(sourceExtension))
        {
            safeFileName += sourceExtension;
        }

        string targetPath;
        try
        {
            targetPath = Path.GetFullPath(Path.Combine(directory, safeFileName));
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, SuggestedNameInvalidMessageKey);
        }

        if (!IsUnderDirectory(directory, targetPath)
            || HasReparsePointInExistingPath(targetPath, directory))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, SuggestedNameUnsafeMessageKey);
        }

        try
        {
            ConversionPathValidator.ThrowIfUnsafeForChdman(targetPath, nameof(targetPath));
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, SuggestedNameUnsafeMessageKey);
        }

        if (string.Equals(fullOriginalPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, SuggestedNameMissingMessageKey);
        }

        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            return RedumpNameSuggestion.Blocked(fullOriginalPath, TargetFileExistsMessageKey);
        }

        return new RedumpNameSuggestion(
            true,
            fullOriginalPath,
            Path.GetFileName(fullOriginalPath),
            Path.GetFileName(targetPath),
            targetPath,
            string.Empty);
    }

    private static bool TryResolveSourcePath(
        string originalPath,
        out string fullOriginalPath,
        out string errorMessageKey)
    {
        fullOriginalPath = string.Empty;
        errorMessageKey = OriginalPathMissingMessageKey;

        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return false;
        }

        try
        {
            fullOriginalPath = Path.GetFullPath(originalPath.Trim());

            if (!File.Exists(fullOriginalPath))
            {
                errorMessageKey = OriginalFileNotFoundMessageKey;
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullOriginalPath, nameof(originalPath));

            if (HasReparsePointInExistingPathFromVolumeRoot(fullOriginalPath))
            {
                errorMessageKey = SuggestedNameUnsafeMessageKey;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            errorMessageKey = OriginalPathMissingMessageKey;
            fullOriginalPath = string.Empty;
            return false;
        }
    }

    private static bool TryValidateExistingDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string fullDirectory = Path.GetFullPath(directory);

            if (!Directory.Exists(fullDirectory)
                || HasReparsePointInExistingPathFromVolumeRoot(fullDirectory))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullDirectory, nameof(directory));
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static string SanitizeSuggestedFileName(string value)
    {
        string fileNameOnly = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(fileNameOnly))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(
        [
            .. fileNameOnly.Select(character => invalid.Contains(character) ? ' ' : character)
        ]);

        try
        {
            safe = Regex.Replace(
                    safe,
                    @"\s+",
                    " ",
                    RegexOptions.CultureInvariant,
                    RegexTimeout)
                .Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }

        return safe.TrimEnd('.', ' ');
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = TrimDirectorySeparators(Path.GetFullPath(baseDirectory));
        string path = TrimDirectorySeparators(Path.GetFullPath(candidate));

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsUnderDirectory(root, candidate))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);

        if (!string.IsNullOrWhiteSpace(root)
            && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
    }

    private static bool HasKnownMediaFileExtension(string? extension) =>
        extension?.ToLowerInvariant() is ".chd"
            or ".cue"
            or ".bin"
            or ".iso"
            or ".gdi"
            or ".toc";

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or System.Security.SecurityException;
}