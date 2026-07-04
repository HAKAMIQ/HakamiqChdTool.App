using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Core.Workflow;

internal enum WorkflowSourceCleanupMode
{
    VerifiedConversion,
    VerifiedExtraction
}

internal sealed record WorkflowSourceCleanupRequest(
    string SourcePath,
    string OutputPath,
    WorkflowSourceCleanupMode Mode,
    bool IsVerified,
    bool IsEnabled);

internal readonly record struct WorkflowSourceCleanupResult(long DeletedBytes, int DeletedFiles)
{
    public static WorkflowSourceCleanupResult Empty => new(0, 0);
}

internal sealed partial class WorkflowSourceCleanupPipeline
{
    private const int RegexTimeoutMilliseconds = 250;

    private readonly ILogger _log;

    public WorkflowSourceCleanupPipeline(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public WorkflowSourceCleanupResult Run(WorkflowSourceCleanupRequest request)
    {
        if (!request.IsEnabled || !request.IsVerified)
        {
            return WorkflowSourceCleanupResult.Empty;
        }

        if (!TryNormalizeRequest(request, out string sourcePath, out string outputPath))
        {
            return WorkflowSourceCleanupResult.Empty;
        }

        if (!File.Exists(sourcePath) || !File.Exists(outputPath))
        {
            return WorkflowSourceCleanupResult.Empty;
        }

        if (WorkflowPathUtilities.PathsEqual(sourcePath, outputPath))
        {
            _log.Debug("Source cleanup skipped because source and output are the same path. Path={Path}", sourcePath);
            return WorkflowSourceCleanupResult.Empty;
        }

        if (AppPaths.IsPathUnderProcessTempRoot(sourcePath) || AppPaths.IsPathUnderProcessTempRoot(outputPath))
        {
            _log.Debug(
                "Source cleanup skipped because source or output is under process temp root. Source={SourcePath}; Output={OutputPath}",
                sourcePath,
                outputPath);
            return WorkflowSourceCleanupResult.Empty;
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(sourcePath)
            || HasReparsePointInExistingPathFromVolumeRoot(outputPath))
        {
            _log.Warning(
                "Source cleanup skipped because source or output contains a reparse point. Source={SourcePath}; Output={OutputPath}",
                sourcePath,
                outputPath);
            return WorkflowSourceCleanupResult.Empty;
        }

        if (!IsVerifiedOutputReadyForCleanup(request.Mode, outputPath))
        {
            return WorkflowSourceCleanupResult.Empty;
        }

        SourceCleanupCandidate[] candidates = request.Mode switch
        {
            WorkflowSourceCleanupMode.VerifiedConversion => BuildVerifiedConversionCandidates(sourcePath, outputPath),
            WorkflowSourceCleanupMode.VerifiedExtraction => BuildVerifiedExtractionCandidates(sourcePath, outputPath),
            _ => []
        };

        if (candidates.Length == 0)
        {
            return WorkflowSourceCleanupResult.Empty;
        }

        return DeleteCandidates(candidates, request.Mode, sourcePath, outputPath);
    }

    private bool TryNormalizeRequest(
        WorkflowSourceCleanupRequest request,
        out string sourcePath,
        out string outputPath)
    {
        sourcePath = string.Empty;
        outputPath = string.Empty;

        if (string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return false;
        }

        try
        {
            sourcePath = NormalizeFullPath(request.SourcePath);
            outputPath = NormalizeFullPath(request.OutputPath);
            return true;
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            _log.Warning(
                ex,
                "Source cleanup skipped because path normalization failed. Source={SourcePath}; Output={OutputPath}",
                request.SourcePath,
                request.OutputPath);
            return false;
        }
    }

    private bool IsVerifiedOutputReadyForCleanup(WorkflowSourceCleanupMode mode, string outputPath)
    {
        if (!File.Exists(outputPath) || HasReparsePointInExistingPathFromVolumeRoot(outputPath))
        {
            return false;
        }

        if (mode == WorkflowSourceCleanupMode.VerifiedConversion)
        {
            return string.Equals(Path.GetExtension(outputPath), ".chd", StringComparison.OrdinalIgnoreCase);
        }

        string outputExtension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (outputExtension is ".cue" or ".gdi" or ".toc")
        {
            DescriptorReferenceSet outputSet = BuildDescriptorReferenceSet(outputPath, requireExistingFiles: true);
            if (!outputSet.IsComplete || outputSet.Paths.Length == 0)
            {
                _log.Warning(
                    "Source cleanup skipped because extracted descriptor output is incomplete. Output={OutputPath}; Reason={Reason}",
                    outputPath,
                    outputSet.FailureReason ?? "No referenced files were resolved.");
                return false;
            }
        }

        return true;
    }

    private SourceCleanupCandidate[] BuildVerifiedConversionCandidates(string sourcePath, string outputPath)
    {
        if (!string.Equals(Path.GetExtension(outputPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("Conversion source cleanup skipped because verified output is not CHD. Output={OutputPath}", outputPath);
            return [];
        }

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (extension is ".zip" or ".rar" or ".7z" or ".iso" or ".nrg")
        {
            return SingleFileCandidate(sourcePath);
        }

        if (extension is ".cue" or ".gdi" or ".toc")
        {
            return BuildDescriptorSourceSetCandidates(sourcePath);
        }

        if (extension == ".bin")
        {
            return SingleFileCandidate(sourcePath);
        }

        _log.Debug("Conversion source cleanup skipped because source extension is unsupported. Source={SourcePath}", sourcePath);
        return [];
    }

    private SourceCleanupCandidate[] BuildVerifiedExtractionCandidates(string sourcePath, string outputPath)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("Extraction source cleanup skipped because source is not CHD. Source={SourcePath}", sourcePath);
            return [];
        }

        if (string.Equals(Path.GetExtension(outputPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("Extraction source cleanup skipped because output is CHD. Output={OutputPath}", outputPath);
            return [];
        }

        return SingleFileCandidate(sourcePath);
    }

    private SourceCleanupCandidate[] BuildDescriptorSourceSetCandidates(string descriptorPath)
    {
        DescriptorReferenceSet referenceSet = BuildDescriptorReferenceSet(descriptorPath, requireExistingFiles: true);
        if (!referenceSet.IsComplete || referenceSet.Paths.Length == 0)
        {
            _log.Warning(
                "Source cleanup skipped descriptor source set because references could not be resolved safely. Descriptor={Descriptor}; Reason={Reason}",
                descriptorPath,
                referenceSet.FailureReason ?? "No referenced files were resolved.");
            return [];
        }

        List<SourceCleanupCandidate> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string referencedFile in referenceSet.Paths)
        {
            if (seen.Add(referencedFile))
            {
                candidates.Add(new SourceCleanupCandidate(referencedFile, IsDescriptor: false));
            }
        }

        foreach (string sidecarPath in EnumerateDescriptorSidecarPaths(descriptorPath))
        {
            if (File.Exists(sidecarPath)
                && !HasReparsePointInExistingPathFromVolumeRoot(sidecarPath)
                && seen.Add(sidecarPath))
            {
                candidates.Add(new SourceCleanupCandidate(sidecarPath, IsDescriptor: false));
            }
        }

        if (seen.Add(descriptorPath))
        {
            candidates.Add(new SourceCleanupCandidate(descriptorPath, IsDescriptor: true));
        }

        return [.. candidates];
    }

    private DescriptorReferenceSet BuildDescriptorReferenceSet(string descriptorPath, bool requireExistingFiles)
    {
        string fullDescriptorPath;
        string? directory;

        try
        {
            fullDescriptorPath = NormalizeFullPath(descriptorPath);
            directory = Path.GetDirectoryName(fullDescriptorPath);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return DescriptorReferenceSet.Incomplete("Descriptor path could not be normalized.");
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return DescriptorReferenceSet.Incomplete("Descriptor directory does not exist.");
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(fullDescriptorPath)
            || HasReparsePointInExistingPathFromVolumeRoot(directory))
        {
            return DescriptorReferenceSet.Incomplete("Descriptor path contains a reparse point.");
        }

        List<string> paths = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string reference in EnumerateDescriptorReferences(fullDescriptorPath))
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                string trimmedReference = reference.Trim();

                if (Path.IsPathRooted(trimmedReference))
                {
                    return DescriptorReferenceSet.Incomplete($"Descriptor reference is rooted: {trimmedReference}");
                }

                string fullPath;
                try
                {
                    fullPath = NormalizeFullPath(Path.Combine(directory, trimmedReference));
                }
                catch (Exception ex) when (IsIoOrPathFailure(ex))
                {
                    return DescriptorReferenceSet.Incomplete($"Invalid descriptor reference: {trimmedReference}");
                }

                if (!IsSameOrChildPath(directory, fullPath))
                {
                    return DescriptorReferenceSet.Incomplete($"Descriptor reference points outside source directory: {trimmedReference}");
                }

                if (AppPaths.IsPathUnderProcessTempRoot(fullPath))
                {
                    return DescriptorReferenceSet.Incomplete($"Descriptor reference points inside process temp root: {trimmedReference}");
                }

                if (requireExistingFiles && !File.Exists(fullPath))
                {
                    return DescriptorReferenceSet.Incomplete($"Descriptor reference does not exist: {trimmedReference}");
                }

                if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
                {
                    return DescriptorReferenceSet.Incomplete($"Descriptor reference contains a reparse point: {trimmedReference}");
                }

                if (seen.Add(fullPath))
                {
                    paths.Add(fullPath);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return DescriptorReferenceSet.Incomplete("Descriptor parsing timed out.");
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return DescriptorReferenceSet.Incomplete("Descriptor references could not be parsed safely.");
        }

        return DescriptorReferenceSet.Complete(paths);
    }

    private IEnumerable<string> EnumerateDescriptorReferences(string descriptorPath)
    {
        string extension = Path.GetExtension(descriptorPath).ToLowerInvariant();

        return extension switch
        {
            ".cue" => EnumerateCueReferences(descriptorPath),
            ".gdi" => EnumerateGdiReferences(descriptorPath),
            ".toc" => EnumerateTocReferences(descriptorPath),
            _ => []
        };
    }

    private IEnumerable<string> EnumerateCueReferences(string cuePath)
    {
        foreach (string line in ReadDescriptorLines(cuePath))
        {
            if (CueSheetFileStatementReader.TryRead(line, out string path, out _))
            {
                yield return path;
            }
        }
    }

    private IEnumerable<string> EnumerateTocReferences(string tocPath)
    {
        foreach (string line in ReadDescriptorLines(tocPath))
        {
            Match match = TocFileReferenceRegex().Match(line);
            if (match.Success)
            {
                string path = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["plain"].Value;

                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateGdiReferences(string gdiPath)
    {
        foreach (string line in ReadDescriptorLines(gdiPath))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || char.IsDigit(trimmed[0]) is false)
            {
                continue;
            }

            foreach (string token in SplitDescriptorTokens(trimmed))
            {
                string extension;
                try
                {
                    extension = Path.GetExtension(token);
                }
                catch (Exception ex) when (IsIoOrPathFailure(ex))
                {
                    continue;
                }

                if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".raw", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    yield return token;
                    break;
                }
            }
        }
    }

    private IEnumerable<string> ReadDescriptorLines(string descriptorPath)
    {
        IEnumerator<string> enumerator;
        try
        {
            enumerator = File.ReadLines(descriptorPath).GetEnumerator();
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            _log.Warning(ex, "Source cleanup could not read descriptor file. Descriptor={Descriptor}", descriptorPath);
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                string line;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    line = enumerator.Current;
                }
                catch (Exception ex) when (IsIoOrPathFailure(ex))
                {
                    _log.Warning(ex, "Source cleanup stopped reading descriptor file. Descriptor={Descriptor}", descriptorPath);
                    yield break;
                }

                yield return line;
            }
        }
    }

    private static IEnumerable<string> SplitDescriptorTokens(string line)
    {
        foreach (Match match in DescriptorTokenRegex().Matches(line))
        {
            string value = match.Groups["q"].Success
                ? match.Groups["q"].Value
                : match.Groups["u"].Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> EnumerateDescriptorSidecarPaths(string descriptorPath)
    {
        if (Path.GetExtension(descriptorPath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.ChangeExtension(descriptorPath, ".sbi");
        }
    }

    private WorkflowSourceCleanupResult DeleteCandidates(
        SourceCleanupCandidate[] candidates,
        WorkflowSourceCleanupMode mode,
        string sourcePath,
        string outputPath)
    {
        long deletedBytes = 0;
        int deletedFiles = 0;
        bool nonDescriptorFailure = false;

        foreach (SourceCleanupCandidate candidate in candidates.OrderBy(static candidate => candidate.IsDescriptor ? 1 : 0))
        {
            if (candidate.IsDescriptor && nonDescriptorFailure)
            {
                _log.Warning(
                    "Source cleanup skipped descriptor delete because a companion file could not be deleted. Descriptor={Descriptor}",
                    candidate.Path);
                continue;
            }

            if (!TryDeleteCandidate(candidate.Path, mode, sourcePath, outputPath, out long bytes))
            {
                if (!candidate.IsDescriptor)
                {
                    nonDescriptorFailure = true;
                }

                continue;
            }

            deletedBytes += bytes;
            deletedFiles++;
        }

        if (deletedFiles > 0)
        {
            _log.Information(
                "Source cleanup completed. Mode={Mode}; Source={SourcePath}; Output={OutputPath}; DeletedFiles={DeletedFiles}; DeletedBytes={DeletedBytes}",
                mode,
                sourcePath,
                outputPath,
                deletedFiles,
                deletedBytes);
        }

        return new WorkflowSourceCleanupResult(deletedBytes, deletedFiles);
    }

    private bool TryDeleteCandidate(
        string candidatePath,
        WorkflowSourceCleanupMode mode,
        string sourcePath,
        string outputPath,
        out long bytes)
    {
        bytes = 0;

        if (!File.Exists(candidatePath))
        {
            return false;
        }

        if (WorkflowPathUtilities.PathsEqual(candidatePath, outputPath))
        {
            _log.Warning("Source cleanup refused to delete output file. Candidate={Candidate}; Output={OutputPath}", candidatePath, outputPath);
            return false;
        }

        if (AppPaths.IsPathUnderProcessTempRoot(candidatePath))
        {
            _log.Debug("Source cleanup skipped temp-root candidate. Candidate={Candidate}", candidatePath);
            return false;
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(candidatePath))
        {
            _log.Warning("Source cleanup refused to delete reparse-point candidate. Candidate={Candidate}", candidatePath);
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(candidatePath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                _log.Warning("Source cleanup refused to delete directory. Candidate={Candidate}", candidatePath);
                return false;
            }

            bytes = new FileInfo(candidatePath).Length;
            File.Delete(candidatePath);
            return true;
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            _log.Warning(
                ex,
                "Source cleanup failed to delete candidate. Mode={Mode}; Source={SourcePath}; Output={OutputPath}; Candidate={Candidate}",
                mode,
                sourcePath,
                outputPath,
                candidatePath);
            return false;
        }
    }

    private static SourceCleanupCandidate[] SingleFileCandidate(string path) =>
        [new SourceCleanupCandidate(path, IsDescriptor: false)];

    private static bool IsSameOrChildPath(string directory, string candidate)
    {
        string fullDirectory = EnsureDirectorySeparatorSuffix(NormalizeFullPath(directory));
        string fullCandidate = NormalizeFullPath(candidate);

        return fullCandidate.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fullCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                fullDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string root = NormalizeFullPath(rootPath);

            if (!IsSameOrChildPath(root, candidate))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsExistingPathReparsePoint(current))
                {
                    return true;
                }

                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static string NormalizeFullPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsIoOrPathFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    [GeneratedRegex("^\\s*(?:FILE|AUDIOFILE|DATAFILE)\\s+(?:\\\"(?<quoted>[^\\\"]+)\\\"|(?<plain>\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex TocFileReferenceRegex();

    [GeneratedRegex("\\\"(?<q>[^\\\"]+)\\\"|(?<u>\\S+)", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex DescriptorTokenRegex();

    private sealed record SourceCleanupCandidate(string Path, bool IsDescriptor);

    private sealed record DescriptorReferenceSet(bool IsComplete, string[] Paths, string? FailureReason)
    {
        public static DescriptorReferenceSet Complete(List<string> paths) =>
            new(true, [.. paths], null);

        public static DescriptorReferenceSet Incomplete(string reason) =>
            new(false, [], reason);
    }
}