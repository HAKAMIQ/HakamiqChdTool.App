using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Services.StorageAdvisor;

internal enum StorageDeviceKind
{
    Unknown = 0,
    Hdd = 1,
    SataSsd = 2,
    NvmeSsd = 3,
    Removable = 4,
    Network = 5
}

internal enum StoragePathRole
{
    Unknown = 0,
    Source = 1,
    Output = 2,
    PendingWorkspace = 3,
    BinCueRescueWorkspace = 4
}

internal enum StorageAdvisorOperationKind
{
    Unknown = 0,
    StandardConversion = 1,
    BinCueRescue = 2,
    Extraction = 3,
    Verification = 4
}

internal enum StorageAdvisorSeverity
{
    None = 0,
    Info = 1,
    Recommendation = 2,
    Warning = 3,
    Blocking = 4
}

internal enum StorageAdvisorMessageCode
{
    None = 0,

    SourceDeviceUnknown = 1,
    OutputDeviceUnknown = 2,
    PendingWorkspaceDeviceUnknown = 3,

    SourceIsHdd = 10,
    OutputIsHdd = 11,
    SourceAndOutputSameHdd = 12,
    OutputIsFastStorage = 13,

    OutputCrossVolumeIsAllowed = 20,
    LargeOutputShouldBeWrittenDirectly = 21,
    CrossVolumeFinalMoveShouldBeAvoided = 22,

    BinCueRescueRequiresSameVolumeWorkspace = 30,
    CustomPendingWorkspaceDifferentVolumeForBinCueRescue = 31,
    AutoSameVolumeWorkspaceRecommended = 32,
    HardLinkMayFailAcrossVolumes = 33,

    CustomWorkspaceUnsafe = 40,
    WorkspaceFallsBackToAuto = 41,

    UnknownStoragePolicyUsed = 50
}

internal sealed record StorageVolumeIdentity(
    string RootPath,
    string VolumeLabel,
    string FileSystem,
    string SerialNumber)
{
    public static StorageVolumeIdentity Unknown { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

internal sealed record StoragePathAnalysis(
    StoragePathRole Role,
    string OriginalPath,
    string FullPath,
    string DirectoryPath,
    StorageVolumeIdentity Volume,
    StorageDeviceKind DeviceKind,
    bool Exists,
    bool IsDirectory,
    bool IsFile,
    bool IsRoot,
    bool IsReparsePoint,
    bool IsWritableCandidate)
{
    public bool HasKnownVolume => !string.IsNullOrWhiteSpace(Volume.RootPath);

    public bool HasKnownDeviceKind => DeviceKind != StorageDeviceKind.Unknown;
}

internal sealed record StorageAdvisorIssue(
    StorageAdvisorSeverity Severity,
    StorageAdvisorMessageCode MessageCode,
    StoragePathRole RelatedPathRole,
    string TechnicalDetail)
{
    public bool IsBlocking => Severity == StorageAdvisorSeverity.Blocking;

    public bool IsWarningOrHigher =>
        Severity is StorageAdvisorSeverity.Warning or StorageAdvisorSeverity.Blocking;
}

internal sealed record StorageAdvisorRecommendation(
    StorageAdvisorSeverity Severity,
    StorageAdvisorMessageCode MessageCode,
    string TechnicalDetail)
{
    public bool IsWarningOrHigher =>
        Severity is StorageAdvisorSeverity.Warning or StorageAdvisorSeverity.Blocking;
}

internal sealed record StorageAdvisorRequest(
    StorageAdvisorOperationKind OperationKind,
    string? SourcePath,
    string? OutputDirectoryPath,
    string? PendingWorkspaceRoot,
    bool UsesCustomPendingWorkspace)
{
    public bool IsBinCueRescue => OperationKind == StorageAdvisorOperationKind.BinCueRescue;
}

internal sealed record StorageAdvisorResult(
    StorageAdvisorOperationKind OperationKind,
    StoragePathAnalysis? Source,
    StoragePathAnalysis? OutputDirectory,
    StoragePathAnalysis? PendingWorkspaceRoot,
    bool SourceAndOutputSameVolume,
    bool SourceAndPendingWorkspaceSameVolume,
    bool ShouldShowDialog,
    IReadOnlyList<StorageAdvisorIssue> Issues,
    IReadOnlyList<StorageAdvisorRecommendation> Recommendations)
{
    public bool HasBlockingIssue => Issues.Count > 0 && ContainsBlockingIssue(Issues);

    public bool HasWarningOrHigher =>
        Issues.Count > 0 && ContainsWarningOrHigher(Issues)
        || Recommendations.Count > 0 && ContainsWarningOrHigher(Recommendations);

    public static StorageAdvisorResult Empty(StorageAdvisorOperationKind operationKind) => new(
        operationKind,
        null,
        null,
        null,
        false,
        false,
        false,
        Array.Empty<StorageAdvisorIssue>(),
        Array.Empty<StorageAdvisorRecommendation>());

    public static StorageAdvisorResult Create(
        StorageAdvisorOperationKind operationKind,
        StoragePathAnalysis? source,
        StoragePathAnalysis? outputDirectory,
        StoragePathAnalysis? pendingWorkspaceRoot,
        bool sourceAndOutputSameVolume,
        bool sourceAndPendingWorkspaceSameVolume,
        bool shouldShowDialog,
        IReadOnlyList<StorageAdvisorIssue> issues,
        IReadOnlyList<StorageAdvisorRecommendation> recommendations)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(recommendations);

        return new StorageAdvisorResult(
            operationKind,
            source,
            outputDirectory,
            pendingWorkspaceRoot,
            sourceAndOutputSameVolume,
            sourceAndPendingWorkspaceSameVolume,
            shouldShowDialog,
            ToReadOnlyIssueList(issues),
            ToReadOnlyRecommendationList(recommendations));
    }

    private static ReadOnlyCollection<StorageAdvisorIssue> ToReadOnlyIssueList(
        IReadOnlyList<StorageAdvisorIssue> source)
    {
        List<StorageAdvisorIssue> items = new(source.Count);

        foreach (StorageAdvisorIssue item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<StorageAdvisorIssue>(items);
    }

    private static ReadOnlyCollection<StorageAdvisorRecommendation> ToReadOnlyRecommendationList(
        IReadOnlyList<StorageAdvisorRecommendation> source)
    {
        List<StorageAdvisorRecommendation> items = new(source.Count);

        foreach (StorageAdvisorRecommendation item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<StorageAdvisorRecommendation>(items);
    }

    private static bool ContainsBlockingIssue(IReadOnlyList<StorageAdvisorIssue> issues)
    {
        foreach (StorageAdvisorIssue issue in issues)
        {
            if (issue.IsBlocking)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWarningOrHigher(IReadOnlyList<StorageAdvisorIssue> issues)
    {
        foreach (StorageAdvisorIssue issue in issues)
        {
            if (issue.IsWarningOrHigher)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWarningOrHigher(IReadOnlyList<StorageAdvisorRecommendation> recommendations)
    {
        foreach (StorageAdvisorRecommendation recommendation in recommendations)
        {
            if (recommendation.IsWarningOrHigher)
            {
                return true;
            }
        }

        return false;
    }
}