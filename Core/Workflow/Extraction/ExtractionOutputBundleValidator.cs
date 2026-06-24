using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Core.Workflow.Extraction;

internal sealed class ExtractionOutputBundleValidator
{
    private const string InvalidOutputKey = "LocWorkflow_ExtractedCueBinInvalid";
    private const string InputFileNotFoundKey = "LocWorkflow_InputFileNotFound";

    private readonly CueSheetReferenceReader _cueReader = new();

    public bool TryValidateExistingFinal(
        ExtractionOutputKind kind,
        string primaryPath,
        out ExtractionOutputBundle bundle,
        out string failureMessageKey) =>
        TryValidateBundle(kind, primaryPath, out bundle, out failureMessageKey);

    public bool TryFinalize(
        ExtractionOutputContract contract,
        out ExtractionOutputBundle finalBundle,
        out string failureMessageKey)
    {
        ArgumentNullException.ThrowIfNull(contract);

        finalBundle = ExtractionOutputBundle.Create(contract.Kind, contract.FinalPrimaryPath, []);
        failureMessageKey = InvalidOutputKey;

        return contract.Kind switch
        {
            ExtractionOutputKind.SingleFile => TryFinalizeSingleFile(contract, out finalBundle, out failureMessageKey),
            ExtractionOutputKind.CueBinBundle => TryFinalizeCueBinBundle(contract, out finalBundle, out failureMessageKey),
            _ => false
        };
    }

    private static bool TryFinalizeSingleFile(
        ExtractionOutputContract contract,
        out ExtractionOutputBundle finalBundle,
        out string failureMessageKey)
    {
        finalBundle = ExtractionOutputBundle.Create(contract.Kind, contract.FinalPrimaryPath, []);
        failureMessageKey = InvalidOutputKey;

        try
        {
            if (!TryGetExistingFileLength(contract.PendingPrimaryPath, out _))
            {
                failureMessageKey = InputFileNotFoundKey;
                return false;
            }

            WorkflowOutputPathContract.PromoteProducedFileToFinalLocation(
                contract.PendingPrimaryPath,
                contract.FinalPrimaryPath);

            if (!TryGetExistingFileLength(contract.FinalPrimaryPath, out long finalLength)
                || finalLength <= 0)
            {
                return false;
            }

            finalBundle = ExtractionOutputBundle.Create(
                contract.Kind,
                contract.FinalPrimaryPath,
                [(contract.FinalPrimaryPath, finalLength)]);

            return true;
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
            failureMessageKey = InvalidOutputKey;
            return false;
        }
    }

    private bool TryFinalizeCueBinBundle(
        ExtractionOutputContract contract,
        out ExtractionOutputBundle finalBundle,
        out string failureMessageKey)
    {
        finalBundle = ExtractionOutputBundle.Create(contract.Kind, contract.FinalPrimaryPath, []);
        failureMessageKey = InvalidOutputKey;
        var promotedTargets = new List<string>();

        try
        {
            if (!TryBuildCueBundlePlan(contract, out CueBundlePlan plan, out failureMessageKey))
            {
                return false;
            }

            if (File.Exists(plan.FinalCuePath)
                || plan.UniqueDependencies.Any(static item => File.Exists(item.FinalPath)))
            {
                failureMessageKey = InvalidOutputKey;
                return false;
            }

            foreach (CueBundleDependency dependency in plan.UniqueDependencies)
            {
                string? targetDirectory = Path.GetDirectoryName(dependency.FinalPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                WorkflowOutputPathContract.PromoteProducedFileToFinalLocation(
                    dependency.PendingPath,
                    dependency.FinalPath);

                promotedTargets.Add(dependency.FinalPath);
            }

            Directory.CreateDirectory(plan.FinalDirectory);
            WorkflowOutputPathContract.PromoteProducedFileToFinalLocation(plan.PendingCuePath, plan.FinalCuePath);
            promotedTargets.Add(plan.FinalCuePath);

            File.WriteAllLines(
                plan.FinalCuePath,
                RewriteCueReferences(plan.SourceLines, plan.Dependencies),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (!TryValidateBundle(ExtractionOutputKind.CueBinBundle, plan.FinalCuePath, out finalBundle, out failureMessageKey))
            {
                CleanupPromotedTargets(promotedTargets);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
            CleanupPromotedTargets(promotedTargets);
            failureMessageKey = InvalidOutputKey;
            Log.Warning(ex, "Extraction output contract failed while finalizing CUE/BIN bundle. Pending={Pending}; Final={Final}", contract.PendingPrimaryPath, contract.FinalPrimaryPath);
            return false;
        }
    }

    private bool TryBuildCueBundlePlan(
        ExtractionOutputContract contract,
        out CueBundlePlan plan,
        out string failureMessageKey)
    {
        plan = default;
        failureMessageKey = InvalidOutputKey;

        string pendingCuePath;
        string finalCuePath;
        string? pendingDirectory;
        string? finalDirectory;

        try
        {
            pendingCuePath = Path.GetFullPath(contract.PendingPrimaryPath);
            finalCuePath = Path.GetFullPath(contract.FinalPrimaryPath);
            pendingDirectory = Path.GetDirectoryName(pendingCuePath);
            finalDirectory = Path.GetDirectoryName(finalCuePath);
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pendingDirectory)
            || string.IsNullOrWhiteSpace(finalDirectory)
            || !string.Equals(Path.GetExtension(pendingCuePath), ".cue", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(finalCuePath), ".cue", StringComparison.OrdinalIgnoreCase)
            || !TryGetExistingFileLength(pendingCuePath, out _))
        {
            return false;
        }

        if (!_cueReader.TryRead(pendingCuePath, out CueSheetReadResult readResult, out failureMessageKey))
        {
            failureMessageKey = string.IsNullOrWhiteSpace(failureMessageKey) ? InvalidOutputKey : failureMessageKey;
            return false;
        }

        var dependencies = new List<CueBundleDependency>();
        foreach (CueSheetFileReference reference in readResult.References)
        {
            if (!TryResolvePendingCueDependency(
                    reference,
                    pendingDirectory,
                    finalDirectory,
                    out CueBundleDependency dependency))
            {
                failureMessageKey = InvalidOutputKey;
                return false;
            }

            dependencies.Add(dependency);
        }

        CueBundleDependency[] uniqueDependencies =
        [
            .. dependencies
                .GroupBy(static item => item.PendingPath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
        ];

        if (!TryRebaseCueBinDependencies(
                pendingCuePath,
                finalCuePath,
                finalDirectory,
                dependencies,
                uniqueDependencies,
                out dependencies,
                out uniqueDependencies))
        {
            return false;
        }

        plan = new CueBundlePlan(
            pendingCuePath,
            finalCuePath,
            finalDirectory,
            readResult.Lines,
            dependencies,
            uniqueDependencies);

        failureMessageKey = string.Empty;
        return true;
    }

    private static bool TryResolvePendingCueDependency(
        CueSheetFileReference reference,
        string pendingDirectory,
        string finalDirectory,
        out CueBundleDependency dependency)
    {
        dependency = default;

        if (!IsSafeCueRelativeReference(reference.Reference))
        {
            return false;
        }

        try
        {
            string pendingPath = Path.GetFullPath(Path.Combine(pendingDirectory, reference.Reference));
            if (!IsSameDirectoryChild(pendingDirectory, pendingPath)
                || !TryGetExistingFileLength(pendingPath, out long length)
                || length <= 0)
            {
                return false;
            }

            string relativeReference = NormalizeCueRelativeReference(Path.GetRelativePath(pendingDirectory, pendingPath));
            if (!IsSafeCueRelativeReference(relativeReference))
            {
                return false;
            }

            string finalPath = Path.GetFullPath(Path.Combine(finalDirectory, relativeReference));
            if (!IsSameDirectoryChild(finalDirectory, finalPath))
            {
                return false;
            }

            dependency = new CueBundleDependency(
                pendingPath,
                finalPath,
                relativeReference,
                reference.LineIndex,
                reference.TrackNumber,
                reference.TrackType,
                reference.IsHighDensityArea);

            return true;
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
            return false;
        }
    }

    private static bool TryRebaseCueBinDependencies(
        string pendingCuePath,
        string finalCuePath,
        string finalDirectory,
        IReadOnlyList<CueBundleDependency> dependencies,
        IReadOnlyList<CueBundleDependency> uniqueDependencies,
        out List<CueBundleDependency> rebasedDependencies,
        out CueBundleDependency[] rebasedUniqueDependencies)
    {
        rebasedDependencies = [.. dependencies];
        rebasedUniqueDependencies = [.. uniqueDependencies];

        if (dependencies.Count == 0
            || uniqueDependencies.Count == 0
            || !uniqueDependencies.All(static item => string.Equals(Path.GetExtension(item.PendingPath), ".bin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string finalBaseName = Path.GetFileNameWithoutExtension(finalCuePath);
        string pendingBaseName = Path.GetFileNameWithoutExtension(pendingCuePath);
        if (string.IsNullOrWhiteSpace(finalBaseName))
        {
            return false;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byPendingPath = new Dictionary<string, CueBundleDependency>(StringComparer.OrdinalIgnoreCase);
        bool singleBinBundle = uniqueDependencies.Count == 1;

        for (int index = 0; index < uniqueDependencies.Count; index++)
        {
            CueBundleDependency source = uniqueDependencies[index];
            if (!TryBuildFinalBinName(
                    finalBaseName,
                    pendingBaseName,
                    source.PendingPath,
                    source.TrackNumber,
                    index + 1,
                    singleBinBundle,
                    usedNames,
                    out string finalBinName))
            {
                return false;
            }

            string finalBinPath = Path.GetFullPath(Path.Combine(finalDirectory, finalBinName));
            if (!IsSameDirectoryChild(finalDirectory, finalBinPath))
            {
                return false;
            }

            byPendingPath[source.PendingPath] = source with
            {
                FinalPath = finalBinPath,
                RelativeReference = finalBinName
            };
        }

        rebasedDependencies = [
            .. dependencies.Select(item =>
                byPendingPath.TryGetValue(item.PendingPath, out CueBundleDependency rebased)
                    ? item with
                    {
                        FinalPath = rebased.FinalPath,
                        RelativeReference = rebased.RelativeReference
                    }
                    : item)
        ];

        rebasedUniqueDependencies = [.. uniqueDependencies.Select(item => byPendingPath[item.PendingPath])];
        return true;
    }

    private static bool TryBuildFinalBinName(
        string finalBaseName,
        string pendingBaseName,
        string pendingBinPath,
        int? trackNumber,
        int ordinal,
        bool singleBinBundle,
        ISet<string> usedNames,
        out string finalBinName)
    {
        finalBinName = string.Empty;

        string suffix = ResolveFinalBinSuffix(
            pendingBaseName,
            Path.GetFileNameWithoutExtension(pendingBinPath),
            trackNumber,
            ordinal,
            singleBinBundle);

        string candidate = finalBaseName + suffix + ".bin";
        if (!IsSafeCueRelativeReference(candidate))
        {
            return false;
        }

        if (usedNames.Add(candidate))
        {
            finalBinName = candidate;
            return true;
        }

        for (int duplicateIndex = 2; duplicateIndex <= 999; duplicateIndex++)
        {
            candidate = finalBaseName + suffix + $" ({duplicateIndex})" + ".bin";
            if (IsSafeCueRelativeReference(candidate) && usedNames.Add(candidate))
            {
                finalBinName = candidate;
                return true;
            }
        }

        return false;
    }

    private static string ResolveFinalBinSuffix(
        string pendingBaseName,
        string pendingBinBaseName,
        int? trackNumber,
        int ordinal,
        bool singleBinBundle)
    {
        if (singleBinBundle)
        {
            return string.Empty;
        }

        int resolvedTrackNumber = trackNumber.GetValueOrDefault();
        if (resolvedTrackNumber > 0)
        {
            return $" (Track {resolvedTrackNumber:D2})";
        }

        if (!string.IsNullOrWhiteSpace(pendingBaseName)
            && !string.IsNullOrWhiteSpace(pendingBinBaseName)
            && pendingBinBaseName.StartsWith(pendingBaseName, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = pendingBinBaseName[pendingBaseName.Length..].Trim();
            if (TryNormalizeTrackSuffix(suffix, out string normalizedTrackSuffix))
            {
                return normalizedTrackSuffix;
            }
        }

        return $" (Track {Math.Max(ordinal, 1):D2})";
    }

    private static bool TryNormalizeTrackSuffix(string suffix, out string normalizedTrackSuffix)
    {
        normalizedTrackSuffix = string.Empty;

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        ReadOnlySpan<char> span = suffix.AsSpan().Trim();
        int digitStart = -1;
        int digitEnd = -1;

        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsDigit(span[i]))
            {
                digitStart = i;
                digitEnd = i;
                while (digitEnd + 1 < span.Length && char.IsDigit(span[digitEnd + 1]))
                {
                    digitEnd++;
                }

                break;
            }
        }

        if (digitStart < 0 || digitEnd < digitStart)
        {
            return false;
        }

        string before = span[..digitStart].ToString();
        string after = span[(digitEnd + 1)..].ToString();
        if (!before.Contains("Track", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(span[digitStart..(digitEnd + 1)], out int trackNumber)
            || trackNumber <= 0
            || after.Any(static ch => !char.IsWhiteSpace(ch) && ch != ')'))
        {
            return false;
        }

        normalizedTrackSuffix = $" (Track {trackNumber:D2})";
        return true;
    }

    private bool TryValidateBundle(
        ExtractionOutputKind kind,
        string primaryPath,
        out ExtractionOutputBundle bundle,
        out string failureMessageKey)
    {
        bundle = ExtractionOutputBundle.Create(kind, primaryPath, []);
        failureMessageKey = InvalidOutputKey;

        if (kind == ExtractionOutputKind.SingleFile)
        {
            if (!TryGetExistingFileLength(primaryPath, out long length) || length <= 0)
            {
                return false;
            }

            bundle = ExtractionOutputBundle.Create(kind, primaryPath, [(primaryPath, length)]);
            failureMessageKey = string.Empty;
            return true;
        }

        return TryValidateCueBinBundle(primaryPath, out bundle, out failureMessageKey);
    }

    private bool TryValidateCueBinBundle(
        string cuePath,
        out ExtractionOutputBundle bundle,
        out string failureMessageKey)
    {
        bundle = ExtractionOutputBundle.Create(ExtractionOutputKind.CueBinBundle, cuePath, []);
        failureMessageKey = InvalidOutputKey;

        string cueFullPath;
        string? cueDirectory;

        try
        {
            cueFullPath = Path.GetFullPath(cuePath);
            cueDirectory = Path.GetDirectoryName(cueFullPath);
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cueDirectory)
            || !TryGetExistingFileLength(cueFullPath, out long cueLength)
            || cueLength <= 0
            || !_cueReader.TryRead(cueFullPath, out CueSheetReadResult result, out failureMessageKey))
        {
            failureMessageKey = string.IsNullOrWhiteSpace(failureMessageKey) ? InvalidOutputKey : failureMessageKey;
            return false;
        }

        var files = new List<(string Path, long Length)> { (cueFullPath, cueLength) };
        foreach (CueSheetFileReference reference in result.References)
        {
            if (!IsSafeCueRelativeReference(reference.Reference))
            {
                failureMessageKey = InvalidOutputKey;
                return false;
            }

            try
            {
                string referencedPath = Path.GetFullPath(Path.Combine(cueDirectory, reference.Reference));
                if (!IsSameDirectoryChild(cueDirectory, referencedPath)
                    || !TryGetExistingFileLength(referencedPath, out long length)
                    || length <= 0)
                {
                    failureMessageKey = InvalidOutputKey;
                    return false;
                }

                files.Add((referencedPath, length));
            }
            catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
            {
                failureMessageKey = InvalidOutputKey;
                return false;
            }
        }

        bundle = ExtractionOutputBundle.Create(ExtractionOutputKind.CueBinBundle, cueFullPath, files);
        failureMessageKey = string.Empty;
        return true;
    }

    private static string[] RewriteCueReferences(
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<CueBundleDependency> dependencies)
    {
        string[] lines = [.. sourceLines];
        Dictionary<int, CueBundleDependency> byLine = dependencies.ToDictionary(static item => item.LineIndex);

        foreach (KeyValuePair<int, CueBundleDependency> item in byLine)
        {
            string line = lines[item.Key];
            string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            lines[item.Key] = $"{leadingWhitespace}FILE \"{item.Value.RelativeReference}\" BINARY";
        }

        return lines;
    }

    private static bool TryGetExistingFileLength(string path, out long length)
    {
        length = 0;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            length = info.Length;
            return length > 0;
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
            return false;
        }
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

    private static bool ContainsParentTraversalSegment(string value)
    {
        string[] parts = value.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(static part => string.Equals(part, "..", StringComparison.Ordinal));
    }

    private static bool IsSameDirectoryChild(string directory, string candidatePath)
    {
        string root = EnsureTrailingDirectorySeparator(Path.GetFullPath(directory));
        string candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingDirectorySeparator(string path) =>
        string.IsNullOrEmpty(path) || Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string NormalizeCueRelativeReference(string value) =>
        string.Join(
            "/",
            value.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
                StringSplitOptions.RemoveEmptyEntries));

    private static void CleanupPromotedTargets(IEnumerable<string> promotedTargets)
    {
        foreach (string path in promotedTargets.OrderByDescending(static item => item.Length))
        {
            TryDeleteFile(path);
            TryDeleteEmptyParentDirectory(path);
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
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
        }
    }

    private static void TryDeleteEmptyParentDirectory(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory)
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (IsExpectedOutputContractFailure(ex))
        {
        }
    }

    private static bool IsExpectedOutputContractFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or System.Security.SecurityException;

    private readonly record struct CueBundleDependency(
        string PendingPath,
        string FinalPath,
        string RelativeReference,
        int LineIndex,
        int? TrackNumber,
        string TrackType,
        bool IsHighDensityArea);

    private readonly record struct CueBundlePlan(
        string PendingCuePath,
        string FinalCuePath,
        string FinalDirectory,
        IReadOnlyList<string> SourceLines,
        IReadOnlyList<CueBundleDependency> Dependencies,
        IReadOnlyList<CueBundleDependency> UniqueDependencies);
}
