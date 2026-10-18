using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static HakamiqChdTool.App.Core.Workflow.WorkflowOutputPathPlanner;
using static HakamiqChdTool.App.Core.Workflow.WorkflowPendingPathPolicy;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowSafePathValidator
{
    private const string ExtractedCueBinInvalidKey = "LocWorkflow_ExtractedCueBinInvalid";
    private readonly record struct CueDependency(
        int LineIndex,
        string SourcePath,
        string RelativeReference,
        string TargetPath);

    internal static bool TryFinalizeExtractedCueBinOutput(
        string pendingCuePath,
        string finalCuePath,
        out string failureMessageKey)
    {
        failureMessageKey = ExtractedCueBinInvalidKey;
        var promotedTargets = new List<string>();

        try
        {
            string pendingCueFullPath = Path.GetFullPath(pendingCuePath);
            string finalCueFullPath = Path.GetFullPath(finalCuePath);
            string? pendingDirectory = Path.GetDirectoryName(pendingCueFullPath);
            string? finalDirectory = Path.GetDirectoryName(finalCueFullPath);

            if (string.IsNullOrWhiteSpace(pendingDirectory)
                || string.IsNullOrWhiteSpace(finalDirectory)
                || !string.Equals(Path.GetExtension(pendingCueFullPath), ".cue", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(pendingCueFullPath))
            {
                return false;
            }

            string[] lines = ReadCueLinesBounded(pendingCueFullPath);
            if (!TryCollectExtractedCueDependencies(lines, pendingDirectory, finalDirectory, out List<CueDependency> dependencies))
            {
                return false;
            }

            CueDependency[] uniqueDependencies =
            [
                .. dependencies
                    .GroupBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
            ];

            if (uniqueDependencies.Length == 1
                && string.Equals(Path.GetExtension(uniqueDependencies[0].SourcePath), ".bin", StringComparison.OrdinalIgnoreCase))
            {
                string finalBinPath = Path.ChangeExtension(finalCueFullPath, ".bin");
                string finalBinName = Path.GetFileName(finalBinPath);

                if (string.IsNullOrWhiteSpace(finalBinName) || !IsSafeCueRelativeReference(finalBinName))
                {
                    return false;
                }

                CueDependency renamedDependency = uniqueDependencies[0] with
                {
                    RelativeReference = finalBinName,
                    TargetPath = finalBinPath
                };

                uniqueDependencies = [renamedDependency];
                dependencies = [renamedDependency];
            }

            if (File.Exists(finalCueFullPath)
                || uniqueDependencies.Any(static item => File.Exists(item.TargetPath)))
            {
                return false;
            }

            foreach (CueDependency dependency in uniqueDependencies)
            {
                string? targetDirectory = Path.GetDirectoryName(dependency.TargetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                PromoteProducedFileToFinalLocation(dependency.SourcePath, dependency.TargetPath);
                promotedTargets.Add(dependency.TargetPath);
            }

            Directory.CreateDirectory(finalDirectory);
            PromoteProducedFileToFinalLocation(pendingCueFullPath, finalCueFullPath);
            promotedTargets.Add(finalCueFullPath);

            string[] rewrittenLines = RewriteCueFileReferences(lines, dependencies);
            File.WriteAllLines(finalCueFullPath, rewrittenLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (!AllCueFileReferencesAreUsable(finalCueFullPath))
            {
                CleanupPromotedExtractedTargets(promotedTargets, finalCueFullPath);
                return false;
            }

            failureMessageKey = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex) || IsExpectedCueReadFailure(ex))
        {
            CleanupPromotedExtractedTargets(promotedTargets, finalCuePath);
            failureMessageKey = ExtractedCueBinInvalidKey;
            return false;
        }
    }

    private static bool TryCollectExtractedCueDependencies(
        string[] lines,
        string pendingDirectory,
        string finalDirectory,
        out List<CueDependency> dependencies)
    {
        dependencies = [];

        for (int index = 0; index < lines.Length; index++)
        {
            if (!CueSheetFileStatementReader.TryRead(lines[index], out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    return false;
                }

                continue;
            }

            if (!TryResolveExtractedCueDependency(
                    index,
                    referencedFileName,
                    pendingDirectory,
                    finalDirectory,
                    out CueDependency dependency))
            {
                return false;
            }

            dependencies.Add(dependency);
        }

        return dependencies.Count > 0;
    }

    private static bool TryResolveExtractedCueDependency(
        int lineIndex,
        string referencedFileName,
        string pendingDirectory,
        string finalDirectory,
        out CueDependency dependency)
    {
        dependency = default;

        if (!IsSafeCueRelativeReference(referencedFileName))
        {
            return false;
        }

        try
        {
            string sourcePath = Path.GetFullPath(Path.Combine(pendingDirectory, referencedFileName));
            if (!IsSameDirectoryChild(pendingDirectory, sourcePath)
                || !File.Exists(sourcePath)
                || TryGetFileSize(sourcePath) <= 0)
            {
                return false;
            }

            string relativePath = Path.GetRelativePath(pendingDirectory, sourcePath);
            if (!IsSafeCueRelativeReference(relativePath))
            {
                return false;
            }

            string targetPath = Path.GetFullPath(Path.Combine(finalDirectory, relativePath));
            if (!IsSameDirectoryChild(finalDirectory, targetPath))
            {
                return false;
            }

            dependency = new CueDependency(
                lineIndex,
                sourcePath,
                NormalizeCueRelativeReference(relativePath),
                targetPath);

            return true;
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            return false;
        }
    }

    private static string[] RewriteCueFileReferences(
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<CueDependency> dependencies)
    {
        string[] lines = [.. sourceLines];
        Dictionary<int, CueDependency> byLine = dependencies.ToDictionary(static item => item.LineIndex);

        foreach (KeyValuePair<int, CueDependency> item in byLine)
        {
            string line = lines[item.Key];
            string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            lines[item.Key] = $"{leadingWhitespace}FILE \"{item.Value.RelativeReference}\" BINARY";
        }

        return lines;
    }

    private static bool IsSafeCueRelativeReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('\0')
            || value.Contains("%t", StringComparison.OrdinalIgnoreCase)
            || value.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(value)
            || ContainsParentTraversalSegment(value))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeCueRelativeReference(string value) =>
        string.Join(
            "/",
            value.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
                StringSplitOptions.RemoveEmptyEntries));

    // Rollback deletes only what this call promoted. The final cue is deleted only if it is in the promoted list, so a
    // file that was already at the final path (or that appears there from elsewhere) is never removed.
    private static void CleanupPromotedExtractedTargets(IEnumerable<string> promotedTargets, string finalCuePath)
    {
        _ = finalCuePath;
        foreach (string target in promotedTargets.OrderByDescending(static path => path.Length))
        {
            TryDeletePromotedFile(target);
            TryCleanupEmptyFinalOutputDirectory(target);
        }
    }

    internal static void CleanupPromotedForTest(IEnumerable<string> promotedTargets, string finalCuePath) =>
        CleanupPromotedExtractedTargets(promotedTargets, finalCuePath);

    private static void TryDeletePromotedFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path)
                && File.Exists(path)
                && !HasReparsePointBelow(Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty, Path.GetFullPath(path)))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex) || IsExpectedCuePathFailure(ex))
        {
        }
    }

    public static void NormalizeCuePrimaryBinReference(string cuePath)
    {
        if (string.IsNullOrWhiteSpace(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string binPath = Path.ChangeExtension(cuePath, ".bin");
        _ = TryNormalizeExtractedCueBinPair(cuePath, binPath, out _);
    }

    public static bool TryNormalizeCuePrimaryBinReference(string cuePath, out string failureMessageKey) =>
        TryNormalizeCuePrimaryBinReference(
            cuePath,
            allowConstrainedAbsoluteBinReference: false,
            out failureMessageKey);

    public static bool TryNormalizeCuePrimaryBinReference(
        string cuePath,
        bool allowConstrainedAbsoluteBinReference,
        out string failureMessageKey)
    {
        const string invalidCueKey = "LocFileIntegrity_SummaryCueInvalidPath";
        const string referenceProblemKey = "LocChdmanContract_InvalidCueBinDependency";

        failureMessageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(cuePath)
            || !File.Exists(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        try
        {
            if (AllCueFileReferencesAreUsable(
                    cuePath,
                    allowConstrainedAbsoluteBinReference))
            {
                return true;
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        failureMessageKey = referenceProblemKey;
        return false;
    }

    public static bool TryNormalizeExtractedCueBinPair(
        string cuePath,
        string binPath,
        out string failureMessageKey)
    {
        const string invalidCueKey = "LocFileIntegrity_SummaryCueInvalidPath";
        const string referenceProblemKey = "LocFileIntegrity_SummaryCueReferenceProblem";

        failureMessageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(cuePath) || string.IsNullOrWhiteSpace(binPath))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        string cueFullPath;
        string binFullPath;
        string? cueDirectory;

        try
        {
            cueFullPath = Path.GetFullPath(cuePath);
            binFullPath = Path.GetFullPath(binPath);
            cueDirectory = Path.GetDirectoryName(cueFullPath);
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        if (string.IsNullOrWhiteSpace(cueDirectory)
            || !File.Exists(cueFullPath)
            || !File.Exists(binFullPath)
            || TryGetFileSize(binFullPath) <= 0
            || !IsSameDirectoryChild(cueDirectory, binFullPath))
        {
            failureMessageKey = referenceProblemKey;
            return false;
        }

        string binName = Path.GetFileName(binFullPath);
        if (string.IsNullOrWhiteSpace(binName))
        {
            failureMessageKey = referenceProblemKey;
            return false;
        }

        string[] lines;
        try
        {
            lines = ReadCueLinesBounded(cueFullPath);
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        if (!TryRewriteCueToSingleBin(cueFullPath, lines, binName)
            || !AllCueFileReferencesAreUsable(cueFullPath))
        {
            failureMessageKey = referenceProblemKey;
            return false;
        }

        return true;
    }

    private static bool TryRewriteCueToSingleBin(
        string cueFullPath,
        IReadOnlyList<string> sourceLines,
        string binFileName)
    {
        if (string.IsNullOrWhiteSpace(binFileName)
            || binFileName.Contains('\0')
            || Path.IsPathRooted(binFileName)
            || ContainsParentTraversalSegment(binFileName))
        {
            return false;
        }

        List<string> lines = [.. sourceLines];
        bool foundFileStatement = false;
        bool changed = false;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            if (!CueSheetFileStatementReader.IsFileStatementLine(line))
            {
                continue;
            }

            string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            string replacement = $"{leadingWhitespace}FILE \"{binFileName}\" BINARY";
            foundFileStatement = true;

            if (!string.Equals(line, replacement, StringComparison.Ordinal))
            {
                lines[index] = replacement;
                changed = true;
            }
        }

        if (!foundFileStatement)
        {
            int insertionIndex = FindFirstCueTrackLineIndex(lines);
            if (insertionIndex < 0)
            {
                insertionIndex = 0;
            }

            lines.Insert(insertionIndex, $"FILE \"{binFileName}\" BINARY");
            changed = true;
        }

        if (!changed)
        {
            return true;
        }

        File.WriteAllLines(cueFullPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static int FindFirstCueTrackLineIndex(List<string> lines)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    internal static bool AllCueFileReferencesAreUsable(string cuePath) =>
        AllCueFileReferencesAreUsable(
            cuePath,
            allowConstrainedAbsoluteBinReference: false);

    private static bool AllCueFileReferencesAreUsable(
        string cuePath,
        bool allowConstrainedAbsoluteBinReference)
    {
        string? cueDirectory = Path.GetDirectoryName(Path.GetFullPath(cuePath));
        if (string.IsNullOrWhiteSpace(cueDirectory))
        {
            return false;
        }

        bool foundFileStatement = false;

        string[] cueLines;
        try
        {
            cueLines = ReadCueLinesBounded(cuePath);
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
            return false;
        }

        foreach (string line in cueLines)
        {
            if (!CueSheetFileStatementReader.TryRead(line, out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    return false;
                }

                continue;
            }

            foundFileStatement = true;

            if (!IsCueReferenceUsable(
                    cueDirectory,
                    referencedFileName,
                    allowConstrainedAbsoluteBinReference))
            {
                return false;
            }
        }

        return foundFileStatement;
    }

    // A cue sheet is a few KiB; refuse anything above the same bounds CueSheetReader enforces so a hostile
    // multi-gigabyte "cue" cannot be pulled into memory.
    private const long MaxCueSheetBytes = 1024 * 1024;
    private const int MaxCueSheetLines = 8192;

    internal static string[] ReadCueLinesBounded(string cuePath)
    {
        long length = new FileInfo(cuePath).Length;
        if (length > MaxCueSheetBytes)
        {
            throw new IOException("Cue sheet exceeds the supported size.");
        }

        string[] lines = File.ReadAllLines(cuePath, Encoding.UTF8);
        if (lines.Length > MaxCueSheetLines)
        {
            throw new IOException("Cue sheet exceeds the supported line count.");
        }

        return lines;
    }

    private static bool IsCueReferenceUsable(
        string cueDirectory,
        string referencedFileName,
        bool allowConstrainedAbsoluteBinReference = false)
    {
        if (string.IsNullOrWhiteSpace(referencedFileName)
            || referencedFileName.Contains('\0')
            || ContainsParentTraversalSegment(referencedFileName))
        {
            return false;
        }

        try
        {
            if (Path.IsPathRooted(referencedFileName))
            {
                if (!allowConstrainedAbsoluteBinReference)
                {
                    return false;
                }

                string absoluteReference = Path.GetFullPath(referencedFileName);
                return string.Equals(Path.GetExtension(absoluteReference), ".bin", StringComparison.OrdinalIgnoreCase)
                       && File.Exists(absoluteReference)
                       && (File.GetAttributes(absoluteReference) & FileAttributes.ReparsePoint) == 0
                       && TryGetFileSize(absoluteReference) > 0;
            }

            string resolved = Path.GetFullPath(Path.Combine(cueDirectory, referencedFileName));
            string safeCueDirectory = Path.GetFullPath(cueDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!resolved.StartsWith(safeCueDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return File.Exists(resolved)
                   && !HasReparsePointBelow(safeCueDirectory, resolved)
                   && TryGetFileSize(resolved) > 0;
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            return false;
        }
    }

    // A referenced file reached through a link inside the cue folder can resolve outside it, so every
    // component from the cue directory down to the file must be a plain entry. Unreadable means unsafe.
    private static bool HasReparsePointBelow(string rootWithSeparator, string resolvedPath)
    {
        string current = resolvedPath;
        while (current.Length > rootWithSeparator.Length
               && current.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsExpectedCuePathFailure(ex) || ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    private static bool IsSameDirectoryChild(string directory, string childPath)
    {
        try
        {
            string fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullChild = Path.GetFullPath(childPath);

            return fullChild.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
        }
    }

    internal static void DeleteDirectoryIfEmpty(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            if (!enumerator.MoveNext())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
        }
    }

    internal static bool IsExpectedCuePathFailure(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    internal static bool IsExpectedCueReadFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or System.Security.SecurityException;

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
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
