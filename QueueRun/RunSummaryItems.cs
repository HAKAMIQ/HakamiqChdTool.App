using System;
using System.Collections.Generic;
using System.IO;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App.QueueRun;

/// <summary>
/// Shared steps after a run: a queue row as a run summary item, and the CHD outputs an M3U playlist may list.
/// The main window and the quick window both use them, so their results and playlists match.
/// </summary>
internal static class RunSummaryItems
{
    public static SessionRunSummaryItem FromRow(QueueRowData row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string currentState = row.CurrentState;

        return new SessionRunSummaryItem(
            IsCompleted: currentState == TaskQueueStateCodes.Completed,
            IsFailed: currentState is TaskQueueStateCodes.Failed or TaskQueueStateCodes.PasswordRequired,
            IsSkipped: currentState == TaskQueueStateCodes.Skipped,
            IsCancelled: currentState == TaskQueueStateCodes.Cancelled,
            IsReverseSupported: IsChdPath(row.SourcePath),
            IsDirectSupported: IsDirectSupportedInputType(row.InputType),
            IsRedumpMatched: row.IntegrityState == IntegrityValidationState.Verified,
            DeletedBytes: row.CleanupDeletedBytes,
            SbiCopiedCount: row.SbiCopiedCount,
            PostProcessingFailureCount: row.PostProcessingFailureCount,
            InputBytes: row.InputBytes,
            OutputBytes: row.OutputBytes,
            ConversionPerformanceReport: row.ConversionPerformanceReport,
            FileName: Normalize(row.FileName),
            StatusDetail: Normalize(row.StatusDetail));
    }

    /// <summary>Completed CHD outputs that exist as plain files and pass the chdman path checks, without duplicates.</summary>
    public static string[] CompletedChdOutputsForM3u(IReadOnlyList<string> outputPaths)
    {
        ArgumentNullException.ThrowIfNull(outputPaths);
        var completed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string outputPath in outputPaths)
        {
            if (TryNormalizeCompletedChdOutput(outputPath, out string normalized) && seen.Add(normalized))
            {
                completed.Add(normalized);
            }
        }

        return [.. completed];
    }

    private static bool TryNormalizeCompletedChdOutput(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            if (!string.Equals(Path.GetExtension(fullPath), ".chd", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsChdPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetExtension(path.Trim()), ".chd", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsDirectSupportedInputType(string? inputType)
    {
        return string.Equals(inputType, "ISO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "CUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "GDI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "BIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "ZIP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "RAR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "7Z", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool IsExpectedPathException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
