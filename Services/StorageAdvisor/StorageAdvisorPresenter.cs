using HakamiqChdTool.App.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Services.StorageAdvisor;

internal static class StorageAdvisorPresenter
{
    private const string SourceLabelKey = "LocStorageAdvisor_SourceLabel";
    private const string OutputLabelKey = "LocStorageAdvisor_OutputLabel";
    private const string WorkspaceLabelKey = "LocStorageAdvisor_WorkspaceLabel";
    private const string UnknownDeviceKey = "LocStorageAdvisor_Device_Unknown";
    private const string InfoSeverityKey = "LocStorageAdvisor_Severity_Info";
    private const string RecommendationSeverityKey = "LocStorageAdvisor_Severity_Recommendation";
    private const string WarningSeverityKey = "LocStorageAdvisor_Severity_Warning";
    private const string BlockingSeverityKey = "LocStorageAdvisor_Severity_Blocking";

    public static StorageAdvisorPresentation Present(StorageAdvisorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<StorageAdvisorPathPresentation> paths = [];

        AddPath(paths, result.Source);
        AddPath(paths, result.OutputDirectory);
        AddPath(paths, result.PendingWorkspaceRoot);

        List<StorageAdvisorMessagePresentation> messages = [];

        foreach (StorageAdvisorIssue issue in result.Issues)
        {
            messages.Add(PresentIssue(issue));
        }

        foreach (StorageAdvisorRecommendation recommendation in result.Recommendations)
        {
            messages.Add(PresentRecommendation(recommendation));
        }

        return new StorageAdvisorPresentation(
            result.OperationKind,
            result.ShouldShowDialog,
            result.HasBlockingIssue,
            result.HasWarningOrHigher,
            ToReadOnlyPathList(paths),
            ToReadOnlyMessageList(messages));
    }

    private static void AddPath(
        List<StorageAdvisorPathPresentation> paths,
        StoragePathAnalysis? analysis)
    {
        if (analysis is null)
        {
            return;
        }

        paths.Add(new StorageAdvisorPathPresentation(
            analysis.Role,
            ResolveRoleLabel(analysis.Role),
            ResolveDeviceKindText(analysis.DeviceKind),
            analysis.FullPath,
            analysis.DirectoryPath,
            analysis.HasKnownVolume ? analysis.Volume.RootPath : string.Empty,
            analysis.HasKnownVolume ? analysis.Volume.FileSystem : string.Empty));
    }

    private static StorageAdvisorMessagePresentation PresentIssue(StorageAdvisorIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new StorageAdvisorMessagePresentation(
            issue.Severity,
            issue.MessageCode,
            ResolveSeverityText(issue.Severity),
            ResolveMessageText(issue.MessageCode),
            issue.TechnicalDetail,
            issue.RelatedPathRole);
    }

    private static StorageAdvisorMessagePresentation PresentRecommendation(
        StorageAdvisorRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        return new StorageAdvisorMessagePresentation(
            recommendation.Severity,
            recommendation.MessageCode,
            ResolveSeverityText(recommendation.Severity),
            ResolveMessageText(recommendation.MessageCode),
            recommendation.TechnicalDetail,
            StoragePathRole.Unknown);
    }

    private static string ResolveRoleLabel(StoragePathRole role)
    {
        string key = role switch
        {
            StoragePathRole.Source => SourceLabelKey,
            StoragePathRole.Output => OutputLabelKey,
            StoragePathRole.PendingWorkspace or StoragePathRole.BinCueRescueWorkspace => WorkspaceLabelKey,
            _ => WorkspaceLabelKey
        };

        return ArabicUi.ResolveDisplayString(key);
    }

    private static string ResolveDeviceKindText(StorageDeviceKind kind)
    {
        string key = kind switch
        {
            StorageDeviceKind.Hdd => "LocStorageAdvisor_Device_Hdd",
            StorageDeviceKind.SataSsd => "LocStorageAdvisor_Device_SataSsd",
            StorageDeviceKind.NvmeSsd => "LocStorageAdvisor_Device_NvmeSsd",
            StorageDeviceKind.Removable => "LocStorageAdvisor_Device_Removable",
            StorageDeviceKind.Network => "LocStorageAdvisor_Device_Network",
            _ => UnknownDeviceKey
        };

        return ArabicUi.ResolveDisplayString(key);
    }

    private static string ResolveSeverityText(StorageAdvisorSeverity severity)
    {
        string key = severity switch
        {
            StorageAdvisorSeverity.Info => InfoSeverityKey,
            StorageAdvisorSeverity.Recommendation => RecommendationSeverityKey,
            StorageAdvisorSeverity.Warning => WarningSeverityKey,
            StorageAdvisorSeverity.Blocking => BlockingSeverityKey,
            _ => InfoSeverityKey
        };

        return ArabicUi.ResolveDisplayString(key);
    }

    private static string ResolveMessageText(StorageAdvisorMessageCode code)
    {
        string key = code switch
        {
            StorageAdvisorMessageCode.SourceDeviceUnknown => "LocStorageAdvisor_SourceDeviceUnknown",
            StorageAdvisorMessageCode.OutputDeviceUnknown => "LocStorageAdvisor_OutputDeviceUnknown",
            StorageAdvisorMessageCode.PendingWorkspaceDeviceUnknown => "LocStorageAdvisor_PendingWorkspaceDeviceUnknown",

            StorageAdvisorMessageCode.SourceIsHdd => "LocStorageAdvisor_SourceIsHdd",
            StorageAdvisorMessageCode.OutputIsHdd => "LocStorageAdvisor_OutputIsHdd",
            StorageAdvisorMessageCode.SourceAndOutputSameHdd => "LocStorageAdvisor_SourceAndOutputSameHdd",
            StorageAdvisorMessageCode.OutputIsFastStorage => "LocStorageAdvisor_OutputIsFastStorage",

            StorageAdvisorMessageCode.OutputCrossVolumeIsAllowed => "LocStorageAdvisor_OutputCrossVolumeIsAllowed",
            StorageAdvisorMessageCode.LargeOutputShouldBeWrittenDirectly => "LocStorageAdvisor_LargeOutputShouldBeWrittenDirectly",
            StorageAdvisorMessageCode.CrossVolumeFinalMoveShouldBeAvoided => "LocStorageAdvisor_CrossVolumeFinalMoveShouldBeAvoided",

            StorageAdvisorMessageCode.BinCueRescueRequiresSameVolumeWorkspace => "LocStorageAdvisor_BinCueRescueRequiresSameVolumeWorkspace",
            StorageAdvisorMessageCode.CustomPendingWorkspaceDifferentVolumeForBinCueRescue => "LocStorageAdvisor_CustomPendingWorkspaceDifferentVolumeForBinCueRescue",
            StorageAdvisorMessageCode.AutoSameVolumeWorkspaceRecommended => "LocStorageAdvisor_AutoSameVolumeWorkspaceRecommended",
            StorageAdvisorMessageCode.HardLinkMayFailAcrossVolumes => "LocStorageAdvisor_HardLinkMayFailAcrossVolumes",

            StorageAdvisorMessageCode.CustomWorkspaceUnsafe => "LocStorageAdvisor_CustomWorkspaceUnsafe",
            StorageAdvisorMessageCode.WorkspaceFallsBackToAuto => "LocStorageAdvisor_WorkspaceFallsBackToAuto",
            StorageAdvisorMessageCode.UnknownStoragePolicyUsed => "LocStorageAdvisor_UnknownStoragePolicyUsed",

            _ => "LocStorageAdvisor_UnknownStoragePolicyUsed"
        };

        return ArabicUi.ResolveDisplayString(key);
    }

    private static ReadOnlyCollection<StorageAdvisorPathPresentation> ToReadOnlyPathList(
        List<StorageAdvisorPathPresentation> source)
    {
        List<StorageAdvisorPathPresentation> items = new(source.Count);

        foreach (StorageAdvisorPathPresentation item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<StorageAdvisorPathPresentation>(items);
    }

    private static ReadOnlyCollection<StorageAdvisorMessagePresentation> ToReadOnlyMessageList(
        List<StorageAdvisorMessagePresentation> source)
    {
        List<StorageAdvisorMessagePresentation> items = new(source.Count);

        foreach (StorageAdvisorMessagePresentation item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<StorageAdvisorMessagePresentation>(items);
    }
}

internal sealed record StorageAdvisorPresentation(
    StorageAdvisorOperationKind OperationKind,
    bool ShouldShowDialog,
    bool HasBlockingIssue,
    bool HasWarningOrHigher,
    IReadOnlyList<StorageAdvisorPathPresentation> Paths,
    IReadOnlyList<StorageAdvisorMessagePresentation> Messages);

internal sealed record StorageAdvisorPathPresentation(
    StoragePathRole Role,
    string RoleLabel,
    string DeviceKindText,
    string TechnicalPath,
    string TechnicalDirectoryPath,
    string TechnicalVolumeRoot,
    string TechnicalFileSystem);

internal sealed record StorageAdvisorMessagePresentation(
    StorageAdvisorSeverity Severity,
    StorageAdvisorMessageCode MessageCode,
    string SeverityText,
    string MessageText,
    string TechnicalDetail,
    StoragePathRole RelatedPathRole);