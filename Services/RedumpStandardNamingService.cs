using HakamiqChdTool.App.ViewModels;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

internal sealed record RedumpStandardNamingSuggestion(
    bool IsApplicable,
    string SourcePath,
    string SourceFileName,
    string SafeFileName,
    string TargetPath,
    string ErrorMessageKey)
{
    public static RedumpStandardNamingSuggestion Blocked(string sourcePath, string errorMessageKey)
    {
        string normalizedSource = string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : sourcePath;
        string sourceFileName = string.Empty;

        if (!string.IsNullOrWhiteSpace(normalizedSource))
        {
            try
            {
                sourceFileName = Path.GetFileName(normalizedSource);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                sourceFileName = string.Empty;
            }
        }

        return new RedumpStandardNamingSuggestion(
            false,
            normalizedSource,
            sourceFileName,
            string.Empty,
            string.Empty,
            errorMessageKey);
    }
}

internal static class RedumpStandardNamingService
{
    private const string OriginalPathMissingMessageKey = "LocNaming_OriginalPathMissing";
    private const string OriginalFileNotFoundMessageKey = "LocNaming_OriginalFileNotFound";
    private const string SuggestedNameMissingMessageKey = "LocNaming_SuggestedNameMissing";
    private const string OriginalDirectoryMissingMessageKey = "LocNaming_OriginalDirectoryMissing";
    private const string SuggestedNameInvalidMessageKey = "LocNaming_SuggestedNameInvalid";
    private const string SuggestedNameUnsafeMessageKey = "LocNaming_SuggestedNameUnsafe";
    private const string TargetFileExistsMessageKey = "LocNaming_TargetFileExists";

    public static RedumpStandardNamingSuggestion Evaluate(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Evaluate(item.SourcePath, item.SuggestedStandardName);
    }

    public static RedumpStandardNamingSuggestion Evaluate(string originalPath, string suggestedFileName)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return RedumpStandardNamingSuggestion.Blocked(originalPath, OriginalPathMissingMessageKey);
        }

        string fullOriginalPath;
        try
        {
            fullOriginalPath = Path.GetFullPath(originalPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return RedumpStandardNamingSuggestion.Blocked(originalPath, OriginalPathMissingMessageKey);
        }

        if (!File.Exists(fullOriginalPath))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, OriginalFileNotFoundMessageKey);
        }

        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, SuggestedNameMissingMessageKey);
        }

        string directory = Path.GetDirectoryName(fullOriginalPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, OriginalDirectoryMissingMessageKey);
        }

        string sourceExtension = Path.GetExtension(fullOriginalPath);
        string safeFileName = SanitizeSuggestedFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, SuggestedNameInvalidMessageKey);
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
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, SuggestedNameInvalidMessageKey);
        }

        if (!IsUnderDirectory(directory, targetPath))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, SuggestedNameUnsafeMessageKey);
        }

        if (string.Equals(fullOriginalPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, SuggestedNameMissingMessageKey);
        }

        if (File.Exists(targetPath))
        {
            return RedumpStandardNamingSuggestion.Blocked(fullOriginalPath, TargetFileExistsMessageKey);
        }

        return new RedumpStandardNamingSuggestion(
            true,
            fullOriginalPath,
            Path.GetFileName(fullOriginalPath),
            Path.GetFileName(targetPath),
            targetPath,
            string.Empty);
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

        safe = Regex.Replace(safe, @"\s+", " ").Trim();
        return safe.TrimEnd('.', ' ');
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = Path.GetFullPath(baseDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        string path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
        or PathTooLongException;
}
