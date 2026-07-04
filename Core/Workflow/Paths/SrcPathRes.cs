using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowSourcePathResolver
{
    private const string ChdMediaDetectedReasonKey = "LocStatus_DetectedMediaArabic";
    private const string ChdMediaRawReasonKey = "LocWorkflow_RawMetadataConflictReason";
    private const string ChdContainerOnlyReasonKey = "LocPlatformDetect_ChdContainerOnly";
    public static async Task<PlatformDetectionResult> ApplyChdMediaDetectionAsync(
        IQueueItemStateSink sink,
        string chdPath,
        ChdInfoResult infoResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(infoResult);

        PlatformDetectionResult keywordDetection = await Task.Run(
            () => PlatformDetectionService.Detect(chdPath),
            cancellationToken).ConfigureAwait(false);

        PlatformDetectionResult result;

        if (string.Equals(infoResult.MediaType, "Raw", StringComparison.OrdinalIgnoreCase)
            || string.Equals(infoResult.MediaType, "Raw Disk", StringComparison.OrdinalIgnoreCase))
        {
            result = ChdWorkflowProfilePlanner.TryBuildRawMetadataConflictDetection(
                chdPath,
                keywordDetection,
                out PlatformDetectionResult conflictDetection)
                    ? conflictDetection
                    : PlatformDetectionResult.Create(
                        string.Empty,
                        string.Empty,
                        68,
                        ChdMediaRawReasonKey);
        }
        else if (PlatformDetectionService.IsActionablePlatformName(keywordDetection.PlatformName))
        {
            result = keywordDetection;
        }
        else
        {
            result = PlatformDetectionResult.Create(
                string.Empty,
                string.Empty,
                70,
                PlatformDetectionService.IsMediaOnlyPlatformName(infoResult.MediaType)
                    ? ChdMediaDetectedReasonKey
                    : ChdContainerOnlyReasonKey);
        }

        sink.RecordPlatformDetection(result.PlatformName, result.Reason);
        return result;
    }


}
