using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.M3u;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services.PostProcessing;

public sealed class PostConversionArtifactService
{
    private static readonly ILogger Logger = Log.ForContext<PostConversionArtifactService>();

    private readonly MultiDiscSetDetector _multiDiscSetDetector;
    private readonly ISbiArtifactCopier _sbiArtifactCopier;
    private readonly IM3uPlaylistGenerator _m3uPlaylistGenerator;

    public PostConversionArtifactService()
        : this(new MultiDiscSetDetector(), new SbiArtifactCopier(), new M3uPlaylistGenerator())
    {
    }

    public PostConversionArtifactService(
        MultiDiscSetDetector multiDiscSetDetector,
        M3uPlaylistGenerator m3uPlaylistGenerator)
        : this(multiDiscSetDetector, new SbiArtifactCopier(), m3uPlaylistGenerator)
    {
    }

    public PostConversionArtifactService(
        MultiDiscSetDetector multiDiscSetDetector,
        ISbiArtifactCopier sbiArtifactCopier,
        IM3uPlaylistGenerator m3uPlaylistGenerator)
    {
        ArgumentNullException.ThrowIfNull(multiDiscSetDetector);
        ArgumentNullException.ThrowIfNull(sbiArtifactCopier);
        ArgumentNullException.ThrowIfNull(m3uPlaylistGenerator);

        _multiDiscSetDetector = multiDiscSetDetector;
        _sbiArtifactCopier = sbiArtifactCopier;
        _m3uPlaylistGenerator = m3uPlaylistGenerator;
    }

    public PostConversionArtifactResult CopyMatchingSbiIfExists(string workingInputPath, string outputChdPath)
    {
        return _sbiArtifactCopier.CopyMatchingSbiIfExists(workingInputPath, outputChdPath);
    }

    public PostConversionArtifactResult GenerateM3uPlaylists(IEnumerable<string> completedChdOutputs, bool overwriteExisting)
    {
        ArgumentNullException.ThrowIfNull(completedChdOutputs);

        string[] safeOutputs =
        [
            .. completedChdOutputs
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        if (safeOutputs.Length == 0)
        {
            Logger.Information("M3U playlist generation skipped because no completed CHD outputs were provided.");
            return PostConversionArtifactResult.Empty;
        }

        string[] expandedOutputs = ExpandWithSiblingChds(safeOutputs);
        if (expandedOutputs.Length < 2)
        {
            Logger.Information(
                "M3U playlist generation skipped because fewer than two CHD outputs were available after sibling scan. CompletedChdOutputs={CompletedChdOutputs}; ExpandedChdOutputs={ExpandedChdOutputs}",
                safeOutputs.Length,
                expandedOutputs.Length);

            return PostConversionArtifactResult.Empty;
        }

        IReadOnlyList<MultiDiscSet> sets = _multiDiscSetDetector.Detect(expandedOutputs);
        if (sets.Count == 0)
        {
            Logger.Information(
                "M3U playlist generation skipped because no multi-disc set was detected. CompletedChdOutputs={CompletedChdOutputs}; ExpandedChdOutputs={ExpandedChdOutputs}",
                safeOutputs.Length,
                expandedOutputs.Length);

            return PostConversionArtifactResult.Empty;
        }

        M3uGenerationResult result = _m3uPlaylistGenerator.Generate(
            sets,
            overwriteExisting);

        Logger.Information(
            "M3U playlist generation completed. CandidateSetCount={CandidateSetCount}; Generated={GeneratedCount}; SkippedExisting={SkippedExistingCount}; Failed={FailedCount}",
            result.CandidateSetCount,
            result.GeneratedCount,
            result.SkippedExistingCount,
            result.Failures.Count);

        return new PostConversionArtifactResult
        {
            M3uGeneratedCount = result.GeneratedCount,
            M3uSkippedExistingCount = result.SkippedExistingCount,
            Failures = result.Failures
        };
    }

    private static string[] ExpandWithSiblingChds(IReadOnlyList<string> completedChdOutputs)
    {
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string output in completedChdOutputs)
        {
            if (!TryNormalizeExistingChd(output, out string normalizedOutput))
            {
                continue;
            }

            outputs.Add(normalizedOutput);

            string? directory = Path.GetDirectoryName(normalizedOutput);
            if (string.IsNullOrWhiteSpace(directory) || HasReparsePointInExistingPathFromVolumeRoot(directory))
            {
                continue;
            }

            try
            {
                foreach (string sibling in Directory.EnumerateFiles(directory, "*.chd", SearchOption.TopDirectoryOnly))
                {
                    if (TryNormalizeExistingChd(sibling, out string normalizedSibling)
                        && !HasReparsePointInExistingPath(normalizedSibling, directory))
                    {
                        outputs.Add(normalizedSibling);
                    }
                }
            }
            catch (Exception ex) when (IsPathOrIoFailure(ex))
            {
                Logger.Debug(ex, "M3U sibling CHD scan skipped. Directory={Directory}", directory);
            }
        }

        return
        [
            .. outputs
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static bool TryNormalizeExistingChd(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath)
                || !string.Equals(Path.GetExtension(fullPath), ".chd", StringComparison.OrdinalIgnoreCase)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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

            if (!IsSamePathOrChild(candidate, root))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsPathOrIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }
}
