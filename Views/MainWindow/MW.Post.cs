using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

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

        string[] completedChdOutputs =
        [
            .. outputPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Where(path =>
                    string.Equals(Path.GetExtension(path), ".chd", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

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
}
