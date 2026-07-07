using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.M3u;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private Task<PostConversionArtifactResult> GenerateM3uPlaylistsForCompletedChdOutputsAsync(
        IReadOnlyList<string> outputPaths)
    {
        ArgumentNullException.ThrowIfNull(outputPaths);

        if (!_settings.EnableAutoM3uGeneration || outputPaths.Count == 0)
        {
            return Task.FromResult(PostConversionArtifactResult.Empty);
        }

        string[] completedChdOutputs = GetCompletedChdOutputsForM3u(outputPaths);

        if (completedChdOutputs.Length == 0)
        {
            Log.Information(
                "M3U playlist generation skipped because no completed CHD workflow outputs were available. CompletedWorkflowOutputs={CompletedWorkflowOutputs}; CompletedChdOutputs={CompletedChdOutputs}",
                outputPaths.Count,
                completedChdOutputs.Length);

            return Task.FromResult(PostConversionArtifactResult.Empty);
        }

        try
        {
            PostConversionArtifactResult result = _postConversionArtifacts.GenerateM3uPlaylists(
                completedChdOutputs,
                _settings.OverwriteExistingM3uPlaylists);

            if (result.M3uGeneratedCount > 0)
            {
                SetFooterStatus(ArabicUi.Format(
                    MainWindowMessages.Fmt_M3uGeneratedFooter,
                    result.M3uGeneratedCount));
            }

            if (result.FailedArtifactCount > 0)
            {
                Log.Warning(
                    "M3U playlist generation completed with failures. Generated={GeneratedCount}; Failed={FailedCount}; SkippedExisting={SkippedExistingCount}",
                    result.M3uGeneratedCount,
                    result.FailedArtifactCount,
                    result.M3uSkippedExistingCount);
            }

            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Warning(ex, "M3U playlist generation failed after session completion.");

            return Task.FromResult(PostConversionArtifactResult.WithFailure(
                "M3U",
                "LocPostProcessing_M3uGenerationFailed"));
        }
    }

    private static string[] GetCompletedChdOutputsForM3u(IReadOnlyList<string> outputPaths)
    {
        var completedChdOutputs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string outputPath in outputPaths)
        {
            if (!TryNormalizeCompletedChdOutputForM3u(outputPath, out string normalizedPath))
            {
                continue;
            }

            if (seen.Add(normalizedPath))
            {
                completedChdOutputs.Add(normalizedPath);
            }
        }

        return [.. completedChdOutputs];
    }

    private static bool TryNormalizeCompletedChdOutputForM3u(
        string? path,
        out string normalizedPath)
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
            if ((attributes & FileAttributes.Directory) != 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedM3uOutputPathException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedM3uOutputPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;
    }
}