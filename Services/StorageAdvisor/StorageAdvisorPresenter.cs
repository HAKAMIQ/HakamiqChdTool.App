using HakamiqChdTool.App.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HakamiqChdTool.App.Services.Storage;

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

    public static StorageAdvisorView Present(StorageAdvisorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<StoragePathView> paths = [];

        AddPath(paths, result.Source);
        AddPath(paths, result.OutputDirectory);
        AddPath(paths, result.PendingWorkspaceRoot);

        List<StorageMessageView> messages = [];

        foreach (StorageAdvisorIssue issue in result.Issues)
        {
            messages.Add(PresentIssue(issue));
        }

        foreach (StorageAdvisorRecommendation recommendation in result.Recommendations)
        {
            messages.Add(PresentRecommendation(recommendation));
        }

        return new StorageAdvisorView(
            result.OperationKind,
            result.ShouldShowDialog,
            result.HasBlockingIssue,
            result.HasWarningOrHigher,
            ToReadOnlyPathList(paths),
            ToReadOnlyMessageList(messages));
    }

    private static void AddPath(
        List<StoragePathView> paths,
        StoragePathAnalysis? analysis)
    {
        if (analysis is null)
        {
            return;
        }

        paths.Add(new StoragePathView(
            analysis.Role,
            ResolveRoleLabel(analysis.Role),
            ResolveDeviceKindText(analysis.DeviceKind),
            analysis.FullPath,
            analysis.DirectoryPath,
            analysis.HasKnownVolume ? analysis.Volume.RootPath : string.Empty,
            analysis.HasKnownVolume ? analysis.Volume.FileSystem : string.Empty));
    }

    private static StorageMessageView PresentIssue(StorageAdvisorIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new StorageMessageView(
            issue.Severity,
            issue.MessageCode,
            ResolveSeverityText(issue.Severity),
            ResolveMessageText(issue.MessageCode),
            issue.TechnicalDetail,
            issue.RelatedPathRole);
    }

    private static StorageMessageView PresentRecommendation(
        StorageAdvisorRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        return new StorageMessageView(
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

    private static ReadOnlyCollection<StoragePathView> ToReadOnlyPathList(
        List<StoragePathView> source)
    {
        List<StoragePathView> items = new(source.Count);

        foreach (StoragePathView item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<StoragePathView>(items);
    }

    private static ReadOnlyCollection<StorageMessageView> ToReadOnlyMessageList(
        List<StorageMessageView> source)
    {
        List<StorageMessageView> items = new(source.Count);

        foreach (StorageMessageView item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<StorageMessageView>(items);
    }
}

internal sealed record StorageAdvisorView(
    StorageAdvisorOperationKind OperationKind,
    bool ShouldShowDialog,
    bool HasBlockingIssue,
    bool HasWarningOrHigher,
    IReadOnlyList<StoragePathView> Paths,
    IReadOnlyList<StorageMessageView> Messages);

internal sealed record StoragePathView(
    StoragePathRole Role,
    string RoleLabel,
    string DeviceKindText,
    string TechnicalPath,
    string TechnicalDirectoryPath,
    string TechnicalVolumeRoot,
    string TechnicalFileSystem);

internal sealed record StorageMessageView(
    StorageAdvisorSeverity Severity,
    StorageAdvisorMessageCode MessageCode,
    string SeverityText,
    string MessageText,
    string TechnicalDetail,
    StoragePathRole RelatedPathRole);
