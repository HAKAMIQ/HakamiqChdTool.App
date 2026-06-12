using System;
using System.Collections.Generic;
using HakamiqChdTool.App.Services.Storage;

namespace HakamiqChdTool.App.Services.StorageAdvisor;

internal sealed class StorageAdvisorService
{
    private readonly StoragePathAnalyzer _pathAnalyzer;

    public StorageAdvisorService()
        : this(new StoragePathAnalyzer())
    {
    }

    public StorageAdvisorService(StoragePathAnalyzer pathAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(pathAnalyzer);
        _pathAnalyzer = pathAnalyzer;
    }

    public StorageAdvisorResult Analyze(
        StorageAdvisorRequest request,
        bool suppressDialog)
    {
        ArgumentNullException.ThrowIfNull(request);

        StoragePathAnalysis? source = AnalyzePath(
            StoragePathRole.Source,
            request.SourcePath);

        StoragePathAnalysis? output = AnalyzePath(
            StoragePathRole.Output,
            request.OutputDirectoryPath);

        StoragePathAnalysis? pendingWorkspace = AnalyzePath(
            request.IsBinCueRescue
                ? StoragePathRole.BinCueRescueWorkspace
                : StoragePathRole.PendingWorkspace,
            request.PendingWorkspaceRoot);

        bool sourceAndOutputSameVolume = AreSameVolume(source, output);
        bool sourceAndPendingWorkspaceSameVolume = AreSameVolume(source, pendingWorkspace);

        List<StorageAdvisorIssue> issues = [];
        List<StorageAdvisorRecommendation> recommendations = [];

        AddPathIssues(source, issues);
        AddPathIssues(output, issues);
        AddPathIssues(pendingWorkspace, issues);

        AddDeviceRecommendations(source, output, recommendations);
        AddOutputPolicyRecommendations(source, output, sourceAndOutputSameVolume, recommendations);

        if (request.IsBinCueRescue)
        {
            AddBinCueRescueRecommendations(
                request,
                source,
                pendingWorkspace,
                sourceAndPendingWorkspaceSameVolume,
                issues,
                recommendations);
        }

        bool shouldShowDialog = !suppressDialog
                                && ShouldShowDialog(
                                    request,
                                    issues,
                                    recommendations);

        return StorageAdvisorResult.Create(
            request.OperationKind,
            source,
            output,
            pendingWorkspace,
            sourceAndOutputSameVolume,
            sourceAndPendingWorkspaceSameVolume,
            shouldShowDialog,
            issues,
            recommendations);
    }

    private StoragePathAnalysis? AnalyzePath(
        StoragePathRole role,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _pathAnalyzer.Analyze(role, path);
    }

    private static void AddPathIssues(
        StoragePathAnalysis? path,
        List<StorageAdvisorIssue> issues)
    {
        if (path is null)
        {
            return;
        }

        if (!path.HasKnownDeviceKind)
        {
            StorageAdvisorMessageCode messageCode = path.Role switch
            {
                StoragePathRole.Source => StorageAdvisorMessageCode.SourceDeviceUnknown,
                StoragePathRole.Output => StorageAdvisorMessageCode.OutputDeviceUnknown,
                StoragePathRole.PendingWorkspace or StoragePathRole.BinCueRescueWorkspace => StorageAdvisorMessageCode.PendingWorkspaceDeviceUnknown,
                _ => StorageAdvisorMessageCode.UnknownStoragePolicyUsed
            };

            issues.Add(new StorageAdvisorIssue(
                StorageAdvisorSeverity.Info,
                messageCode,
                path.Role,
                path.FullPath));
        }

        if (path.Role is StoragePathRole.PendingWorkspace or StoragePathRole.BinCueRescueWorkspace
            && !path.IsWritableCandidate)
        {
            issues.Add(new StorageAdvisorIssue(
                StorageAdvisorSeverity.Warning,
                StorageAdvisorMessageCode.CustomWorkspaceUnsafe,
                path.Role,
                path.FullPath));
        }
    }

    private static void AddDeviceRecommendations(
        StoragePathAnalysis? source,
        StoragePathAnalysis? output,
        List<StorageAdvisorRecommendation> recommendations)
    {
        if (source?.DeviceKind == StorageDeviceKind.Hdd)
        {
            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Recommendation,
                StorageAdvisorMessageCode.SourceIsHdd,
                source.FullPath));
        }

        if (output?.DeviceKind == StorageDeviceKind.Hdd)
        {
            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Warning,
                StorageAdvisorMessageCode.OutputIsHdd,
                output.FullPath));
        }

        if (output?.DeviceKind is StorageDeviceKind.NvmeSsd or StorageDeviceKind.SataSsd)
        {
            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Recommendation,
                StorageAdvisorMessageCode.OutputIsFastStorage,
                output.FullPath));
        }

        if (source?.DeviceKind == StorageDeviceKind.Hdd
            && output?.DeviceKind == StorageDeviceKind.Hdd
            && AreSameVolume(source, output))
        {
            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Warning,
                StorageAdvisorMessageCode.SourceAndOutputSameHdd,
                output.FullPath));
        }
    }

    private static void AddOutputPolicyRecommendations(
        StoragePathAnalysis? source,
        StoragePathAnalysis? output,
        bool sourceAndOutputSameVolume,
        List<StorageAdvisorRecommendation> recommendations)
    {
        if (source is null || output is null)
        {
            return;
        }

        recommendations.Add(new StorageAdvisorRecommendation(
            StorageAdvisorSeverity.Info,
            StorageAdvisorMessageCode.LargeOutputShouldBeWrittenDirectly,
            output.FullPath));

        if (!sourceAndOutputSameVolume)
        {
            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Info,
                StorageAdvisorMessageCode.OutputCrossVolumeIsAllowed,
                output.FullPath));

            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Recommendation,
                StorageAdvisorMessageCode.CrossVolumeFinalMoveShouldBeAvoided,
                output.FullPath));
        }
    }

    private static void AddBinCueRescueRecommendations(
        StorageAdvisorRequest request,
        StoragePathAnalysis? source,
        StoragePathAnalysis? pendingWorkspace,
        bool sourceAndPendingWorkspaceSameVolume,
        List<StorageAdvisorIssue> issues,
        List<StorageAdvisorRecommendation> recommendations)
    {
        recommendations.Add(new StorageAdvisorRecommendation(
            StorageAdvisorSeverity.Warning,
            StorageAdvisorMessageCode.BinCueRescueRequiresSameVolumeWorkspace,
            source?.FullPath ?? string.Empty));

        recommendations.Add(new StorageAdvisorRecommendation(
            StorageAdvisorSeverity.Recommendation,
            StorageAdvisorMessageCode.AutoSameVolumeWorkspaceRecommended,
            source?.DirectoryPath ?? string.Empty));

        if (source is null || pendingWorkspace is null)
        {
            return;
        }

        if (request.UsesCustomPendingWorkspace && !sourceAndPendingWorkspaceSameVolume)
        {
            issues.Add(new StorageAdvisorIssue(
                StorageAdvisorSeverity.Warning,
                StorageAdvisorMessageCode.CustomPendingWorkspaceDifferentVolumeForBinCueRescue,
                StoragePathRole.BinCueRescueWorkspace,
                pendingWorkspace.FullPath));

            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Warning,
                StorageAdvisorMessageCode.HardLinkMayFailAcrossVolumes,
                pendingWorkspace.FullPath));

            recommendations.Add(new StorageAdvisorRecommendation(
                StorageAdvisorSeverity.Recommendation,
                StorageAdvisorMessageCode.WorkspaceFallsBackToAuto,
                source.DirectoryPath));
        }
    }

    private static bool ShouldShowDialog(
        StorageAdvisorRequest request,
        List<StorageAdvisorIssue> issues,
        List<StorageAdvisorRecommendation> recommendations)
    {
        foreach (StorageAdvisorIssue issue in issues)
        {
            if (issue.Severity is StorageAdvisorSeverity.Warning or StorageAdvisorSeverity.Blocking)
            {
                return true;
            }
        }

        foreach (StorageAdvisorRecommendation recommendation in recommendations)
        {
            if (recommendation.Severity is StorageAdvisorSeverity.Warning or StorageAdvisorSeverity.Blocking)
            {
                return true;
            }
        }

        return request.IsBinCueRescue && recommendations.Count > 0;
    }

    private static bool AreSameVolume(
        StoragePathAnalysis? left,
        StoragePathAnalysis? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (!left.HasKnownVolume || !right.HasKnownVolume)
        {
            return false;
        }

        if (string.Equals(left.Volume.RootPath, right.Volume.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.Volume.SerialNumber)
            && !string.IsNullOrWhiteSpace(right.Volume.SerialNumber)
            && string.Equals(left.Volume.SerialNumber, right.Volume.SerialNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Volume.FileSystem, right.Volume.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
