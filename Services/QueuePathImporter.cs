using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services;

public static class QueuePathImporter
{
    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(QueuePathImporter));

    public static List<string> ExpandPaths(
        IReadOnlyList<string> rawList,
        QueueIngestKind inputKind,
        SearchOption searchOption,
        IInputResolver inputResolver)
    {
        return ExpandPathsWithSummary(rawList, inputKind, searchOption, inputResolver).SupportedPaths.ToList();
    }

    public static QueuePathImportResult ExpandPathsWithSummary(
        IReadOnlyList<string> rawList,
        QueueIngestKind inputKind,
        SearchOption searchOption,
        IInputResolver inputResolver)
    {
        ArgumentNullException.ThrowIfNull(rawList);
        ArgumentNullException.ThrowIfNull(inputResolver);

        var results = new List<string>();
        int archiveFileCount = 0;
        int unsupportedFileCount = 0;
        int duplicateFileCount = 0;
        int missingPathCount = 0;
        int directoryCount = 0;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in rawList)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (inputKind == QueueIngestKind.FilesOnly)
            {
                AddFileWithSummary(
                    path,
                    allowBinCueRescueCandidate: true,
                    results,
                    seen,
                    ref archiveFileCount,
                    ref unsupportedFileCount,
                    ref duplicateFileCount,
                    ref missingPathCount);
                continue;
            }

            ExpandPathWithSummary(
                path,
                searchOption,
                inputResolver,
                results,
                seen,
                ref archiveFileCount,
                ref unsupportedFileCount,
                ref duplicateFileCount,
                ref missingPathCount,
                ref directoryCount);
        }

        IntakeBatchSummary summary = new(
            SupportedFileCount: results.Count,
            ArchiveFileCount: archiveFileCount,
            UnsupportedFileCount: unsupportedFileCount,
            DuplicateFileCount: duplicateFileCount,
            MissingPathCount: missingPathCount,
            DirectoryCount: directoryCount);

        return new QueuePathImportResult(results, summary);
    }

    private static void ExpandPathWithSummary(
        string path,
        SearchOption searchOption,
        IInputResolver inputResolver,
        List<string> results,
        HashSet<string> seen,
        ref int archiveFileCount,
        ref int unsupportedFileCount,
        ref int duplicateFileCount,
        ref int missingPathCount,
        ref int directoryCount)
    {
        try
        {
            if (IsInternalWorkspacePath(path))
            {
                unsupportedFileCount++;
                return;
            }

            if (File.Exists(path))
            {
                AddFileWithSummary(
                    path,
                    allowBinCueRescueCandidate: true,
                    results,
                    seen,
                    ref archiveFileCount,
                    ref unsupportedFileCount,
                    ref duplicateFileCount,
                    ref missingPathCount);
                return;
            }

            if (!Directory.Exists(path))
            {
                missingPathCount++;
                return;
            }

            directoryCount++;

            IReadOnlyList<string> resolvedPaths = inputResolver.Resolve(path, searchOption).ToArray();
            foreach (string resolvedPath in resolvedPaths)
            {
                AddFileWithSummary(
                    resolvedPath,
                    allowBinCueRescueCandidate: false,
                    results,
                    seen,
                    ref archiveFileCount,
                    ref unsupportedFileCount,
                    ref duplicateFileCount,
                    ref missingPathCount);
            }
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            Logger.Debug(ex, "Queue path expansion skipped. Path={Path}", path);
            missingPathCount++;
        }
    }

    private static void AddFileWithSummary(
        string path,
        bool allowBinCueRescueCandidate,
        List<string> results,
        HashSet<string> seen,
        ref int archiveFileCount,
        ref int unsupportedFileCount,
        ref int duplicateFileCount,
        ref int missingPathCount)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                missingPathCount++;
                return;
            }

            if (IsInternalWorkspacePath(path))
            {
                unsupportedFileCount++;
                return;
            }

            QueueInputClassification classification = QueueInputClassifier.Classify(path);
            if (!classification.IsSupported
                || (classification.IsBinCueRescueCandidate && !allowBinCueRescueCandidate))
            {
                unsupportedFileCount++;
                return;
            }

            string normalized = NormalizePathForSet(path);
            if (!seen.Add(normalized))
            {
                duplicateFileCount++;
                return;
            }

            if (classification.IsArchiveContainer)
            {
                archiveFileCount++;
            }

            results.Add(path);
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            Logger.Debug(ex, "Queue path import skipped unsupported or inaccessible file. Path={Path}", path);
            missingPathCount++;
        }
    }

    private static bool IsInternalWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return PendingWorkspacePathPolicy.IsReservedWorkspacePath(path);
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            return true;
        }
    }

    private static string NormalizePathForSet(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool IsExpectedPathExpansionException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException;
}
