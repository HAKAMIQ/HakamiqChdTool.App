using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using static HakamiqChdTool.App.Services.ChdConversionMessages;
using static HakamiqChdTool.App.Services.ChdOutputPathHelpers;

namespace HakamiqChdTool.App.Services;

public sealed class ChdVerificationBridge : IChdVerificationBridge
{
    public bool TryNormalizeExtractedCueBinOutput(string cueOutputPath)
    {
        if (string.IsNullOrWhiteSpace(cueOutputPath)
            || !string.Equals(Path.GetExtension(cueOutputPath), ".cue", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(cueOutputPath))
        {
            return false;
        }

        if (!TryRepairLiteralTrackTokenCueOutput(cueOutputPath))
        {
            return false;
        }

        if (VerifyCueBinDependenciesStrict(cueOutputPath))
        {
            return true;
        }

        string binOutputPath = BuildSingleBinExtractCdBinOutputPath(cueOutputPath);
        if (!File.Exists(binOutputPath))
        {
            return false;
        }

        try
        {
            FileInfo binInfo = new(binOutputPath);
            if (binInfo.Length <= 0)
            {
                return false;
            }

            string[] lines = File.ReadAllLines(cueOutputPath, Encoding.UTF8);
            string binFileName = Path.GetFileName(binOutputPath);
            bool foundFileStatement = false;
            bool changed = false;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4]))
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
                List<string> updated = new(lines);
                int insertionIndex = FindFirstCueTrackLineIndex(updated);
                updated.Insert(insertionIndex < 0 ? 0 : insertionIndex, $"FILE \"{binFileName}\" BINARY");
                lines = updated.ToArray();
                changed = true;
            }

            if (changed)
            {
                File.WriteAllLines(cueOutputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Log.Information(
                    "Normalized extractcd CUE/BIN output contract. Cue={CuePath}; Bin={BinPath}",
                    cueOutputPath,
                    binOutputPath);
            }

            return VerifyCueBinDependenciesStrict(cueOutputPath);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidOperationException)
        {
            Log.Warning(ex, "Could not normalize extractcd CUE/BIN output contract. Cue={CuePath}", cueOutputPath);
            return false;
        }
    }

    private static bool TryRepairLiteralTrackTokenCueOutput(string cueOutputPath)
    {
        try
        {
            string? cueDirectory = Path.GetDirectoryName(Path.GetFullPath(cueOutputPath));
            if (string.IsNullOrWhiteSpace(cueDirectory))
            {
                return false;
            }

            string[] lines = File.ReadAllLines(cueOutputPath, Encoding.UTF8);
            var tokenLineIndexes = new List<int>();
            var tokenSourcePaths = new List<string>();

            for (int index = 0; index < lines.Length; index++)
            {
                if (!TryReadCueFileStatementStrict(lines[index], out string referencedFileName, out bool hasFileStatement))
                {
                    if (hasFileStatement)
                    {
                        return false;
                    }

                    continue;
                }

                if (!ContainsTrackToken(referencedFileName))
                {
                    continue;
                }

                if (!TryResolveCompanionPathWithinDirectory(cueDirectory, referencedFileName, out string? sourcePath)
                    || string.IsNullOrWhiteSpace(sourcePath))
                {
                    return false;
                }

                string resolvedSourcePath = sourcePath;
                if (!File.Exists(resolvedSourcePath)
                    || new FileInfo(resolvedSourcePath).Length <= 0)
                {
                    return false;
                }

                tokenLineIndexes.Add(index);
                tokenSourcePaths.Add(Path.GetFullPath(resolvedSourcePath));
            }

            if (tokenLineIndexes.Count == 0)
            {
                return true;
            }

            string[] uniqueTokenSources =
            [
                .. tokenSourcePaths.Distinct(StringComparer.OrdinalIgnoreCase)
            ];

            if (uniqueTokenSources.Length != 1)
            {
                return false;
            }

            string singleBinPath = BuildSingleBinExtractCdBinOutputPath(cueOutputPath);
            string singleBinFullPath = Path.GetFullPath(singleBinPath);
            string tokenSourcePath = uniqueTokenSources[0];

            if (!string.Equals(tokenSourcePath, singleBinFullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(singleBinFullPath))
                {
                    return false;
                }

                File.Move(tokenSourcePath, singleBinFullPath);
            }

            string binFileName = Path.GetFileName(singleBinFullPath);
            foreach (int lineIndex in tokenLineIndexes)
            {
                string line = lines[lineIndex];
                string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
                lines[lineIndex] = $"{leadingWhitespace}FILE \"{binFileName}\" BINARY";
            }

            File.WriteAllLines(cueOutputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Log.Information(
                "Repaired literal extractcd track token in single-bin CUE output. Cue={CuePath}; Bin={BinPath}",
                cueOutputPath,
                singleBinFullPath);

            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidOperationException)
        {
            Log.Warning(ex, "Could not repair literal extractcd track token. Cue={CuePath}", cueOutputPath);
            return false;
        }
    }

    private static int FindFirstCueTrackLineIndex(IReadOnlyList<string> lines)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].TrimStart().StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }


    public bool TryValidateDescriptorDependenciesBeforeChdman(
        string inputPath,
        string command,
        out string failureMessageKey)
    {
        failureMessageKey = string.Empty;

        try
        {
            if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(inputPath), ".cue", StringComparison.OrdinalIgnoreCase)
                && !VerifyCueBinDependenciesStrict(inputPath))
            {
                failureMessageKey = InvalidCueBinDependencyMessageKey;
                return false;
            }

            ChdWorkflowProfilePlanner.ValidateConversionInputOrThrow(inputPath, command);
            return true;
        }
        catch (InvalidDataException)
        {
            failureMessageKey = InvalidCueBinDependencyMessageKey;
            return false;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidOperationException
                                  or System.Security.SecurityException)
        {
            failureMessageKey = InvalidCueBinDependencyMessageKey;
            Log.Debug(ex, "Descriptor dependency preflight failed. Input={InputPath}; Command={Command}", inputPath, command);
            return false;
        }
    }

    private static bool VerifyCueBinDependenciesStrict(string cuePath)
    {
        if (string.IsNullOrWhiteSpace(cuePath)
            || !File.Exists(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fullCuePath;
        string? cueDirectory;

        try
        {
            fullCuePath = Path.GetFullPath(cuePath);
            cueDirectory = Path.GetDirectoryName(fullCuePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cueDirectory))
        {
            return false;
        }

        string safeCueDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(cueDirectory));
        bool foundFileStatement = false;

        foreach (string line in File.ReadLines(fullCuePath, Encoding.UTF8))
        {
            if (!TryReadCueFileStatementStrict(line, out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    return false;
                }

                continue;
            }

            foundFileStatement = true;

            if (referencedFileName.IndexOf('\0') >= 0
                || ContainsTrackToken(referencedFileName)
                || Path.IsPathRooted(referencedFileName)
                || referencedFileName.Contains(':', StringComparison.Ordinal)
                || referencedFileName.Contains('/')
                || referencedFileName.Contains('\\')
                || ContainsParentTraversalSegment(referencedFileName))
            {
                return false;
            }

            string candidatePath = Path.GetFullPath(Path.Combine(safeCueDirectory, referencedFileName));
            if (!candidatePath.StartsWith(safeCueDirectory, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(candidatePath)
                || new FileInfo(candidatePath).Length <= 0)
            {
                return false;
            }
        }

        return foundFileStatement;
    }

    private static bool TryReadCueFileStatementStrict(
        string line,
        out string referencedFileName,
        out bool hasFileStatement)
    {
        referencedFileName = string.Empty;
        hasFileStatement = false;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4]))
        {
            return false;
        }

        hasFileStatement = true;

        Match match = Regex.Match(
            trimmed,
            "^FILE\\s+(?:\"(?<quoted>[^\"]*)\"|(?<plain>\\S+))\\s+\\S+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        if (!match.Success)
        {
            return false;
        }

        referencedFileName = match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value.Trim()
            : match.Groups["plain"].Value.Trim();

        return !string.IsNullOrWhiteSpace(referencedFileName);
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
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
